// -----------------------------------------------------------------------------
// File:     ProviderLookupService.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Cache rules: database first, CQC second, stale copy as fallback.
// -----------------------------------------------------------------------------

using System.Data.Common;
using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Enums;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Interfaces;
using SchemeserveDotnetTest.Logging;

namespace SchemeserveDotnetTest.Services
{
    // Database first, CQC second.
    //
    // Providers list: served page by page from dbo.CqcProviderIndex, which ProviderIndexSync keeps
    // in step with CQC's full list (re-synced when older than the TTL).
    //
    // Single provider:
    //   1. Only trust a stored copy if the id is in the providers list and the copy is younger than the TTL.
    //   2. Otherwise fetch it from CQC (or, if CQC is down, fall back to the expired copy).
    //   3. Store what CQC returned, then return it. If CQC no longer knows the id, remove it locally.
    public class ProviderLookupService
    {
        private readonly IProviderRepository _repository;
        private readonly ICqcRetrievalService _cqc;
        private readonly IProviderIndexSync _indexSync;
        private readonly ILogger<ProviderLookupService> _log;
        private readonly TimeSpan _ttl;
        private readonly int _defaultPerPage;

        public ProviderLookupService(
            IProviderRepository repository,
            ICqcRetrievalService cqc,
            IProviderIndexSync indexSync,
            IConfiguration config,
            ILogger<ProviderLookupService> log)
        {
            _repository = repository;
            _cqc = cqc;
            _indexSync = indexSync;
            _log = log;
            // "One month" is taken as 30 days.
            _ttl = config.GetValue("Sources:Cqc:CacheTtl", TimeSpan.FromDays(30));
            // Page size when only "page" is given: the configured maximum, 1000 (CQC's own page size) if not set.
            _defaultPerPage = config.GetValue("Sources:Cqc:MaxPerPage", 1000);
        }

        public async Task<CacheResult<CqcProvider>?> GetProviderByIdAsync(string id, CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            // 1. Database first, but only for ids that are still in CQC's providers list.
            var isListed = await _repository.IsInIndexAsync(id);
            var stored = isListed ? await _repository.GetProviderAsync(id) : null;

            if (stored != null && now - stored.FetchedAtUtc < _ttl)
                return new CacheResult<CqcProvider>(stored.ToProvider(), DataSource.Cache, stored.FetchedAtUtc);

            // 2. Not listed, missing or expired: ask CQC.
            CqcProvider? provider;
            try
            {
                provider = await _cqc.GetProviderByIdAsync(id, ct);
            }
            catch (CqcUnavailableException ex) when (stored != null)
            {
                try
                {
                    _log.LogWarning(ex, "CQC unavailable, serving expired copy of provider {Id}", id);
                }
                catch (Exception)
                {
                    // Logging itself failed: write to the Windows event log instead, and still serve the copy.
                    EventLogFallback.Write("CQC unavailable, serving expired copy of provider " + id, ex);
                }
                return new CacheResult<CqcProvider>(stored.ToProvider(), DataSource.StaleCache, stored.FetchedAtUtc);
            }
            catch (CqcUnavailableException ex)
            {
                // Nothing stored to fall back on: log it and let the controller answer 503.
                try
                {
                    _log.LogWarning(ex, "CQC unavailable and no stored copy of provider {Id}", id);
                }
                catch (Exception)
                {
                    // Logging itself failed: write to the Windows event log instead.
                    EventLogFallback.Write("CQC unavailable and no stored copy of provider " + id, ex);
                }
                throw;
            }

            if (provider == null)
            {
                // CQC doesn't know this id (any more): make sure nothing stale is kept for it.
                try
                {
                    await _repository.DeleteProviderAsync(id);
                }
                catch (DbException ex)
                {
                    _log.LogError(ex, "Failed to remove provider {Id}", id);
                }
                return null;
            }

            // 3. Store it. A failed save is logged but doesn't fail the request.
            try
            {
                await _repository.SaveProviderAsync(CqcProviderRow.FromProvider(provider, now));

                // A provider CQC added after the last list sync: add it to the list now.
                if (!isListed)
                    await _repository.AddToIndexAsync(new CqcProviderIndexRow { ProviderId = provider.ProviderId, ProviderName = provider.Name ?? "" });
            }
            catch (DbException ex)
            {
                _log.LogError(ex, "Failed to store provider {Id}", id);
            }

            return new CacheResult<CqcProvider>(provider, DataSource.Upstream, now);
        }

        // The providers list in the same shape CQC returns, with page links pointing at this API.
        // With no page/perPage it returns the full list as a single page; otherwise just the requested page.
        public async Task<CacheResult<CqcProvidersPage>?> GetProvidersAsync(int? requestedPage, int? requestedPerPage, CancellationToken ct)
        {
            // Normally the background job has already synced; this only does work if the list
            // has never been synced or has expired.
            var source = DataSource.Cache;
            try
            {
                if (await _indexSync.SyncIfExpiredAsync(ct))
                    source = DataSource.Upstream;
            }
            catch (CqcUnavailableException ex)
            {
                try
                {
                    _log.LogWarning(ex, "CQC unavailable, serving the stored providers list");
                }
                catch (Exception)
                {
                    // Logging itself failed: write to the Windows event log instead, and still serve the list.
                    EventLogFallback.Write("CQC unavailable, serving the stored providers list", ex);
                }
                source = DataSource.StaleCache;
            }

            var lastSync = await _repository.GetLastIndexSyncAsync();
            if (lastSync == null)
                return null; // never synced and CQC unavailable: nothing to serve

            var total = await _repository.GetIndexCountAsync();
            var isFullList = requestedPage == null && requestedPerPage == null;

            CqcProvidersPage result;
            List<CqcProviderIndexRow> rows;

            if (isFullList)
            {
                // Everything as one page; there are no other pages to link to.
                rows = await _repository.GetIndexPageAsync(1, Math.Max(total, 1));
                result = new CqcProvidersPage
                {
                    Total = total,
                    Page = 1,
                    PerPage = total,
                    TotalPages = 1,
                    Uri = "/v1/providers"
                };
            }
            else
            {
                var page = requestedPage ?? 1;
                var perPage = requestedPerPage ?? _defaultPerPage;
                var totalPages = (total + perPage - 1) / perPage; // round up

                rows = await _repository.GetIndexPageAsync(page, perPage);
                result = new CqcProvidersPage
                {
                    Total = total,
                    Page = page,
                    PerPage = perPage,
                    TotalPages = totalPages,
                    Uri = PageUri(page, perPage),
                    FirstPageUri = PageUri(1, perPage),
                    LastPageUri = PageUri(Math.Max(totalPages, 1), perPage),
                    PreviousPageUri = page > 1 ? PageUri(page - 1, perPage) : null,
                    NextPageUri = page < totalPages ? PageUri(page + 1, perPage) : null
                };
            }

            foreach (var row in rows)
            {
                result.Providers.Add(new CqcProviderSummary { ProviderId = row.ProviderId, ProviderName = row.ProviderName });
            }

            return new CacheResult<CqcProvidersPage>(result, source, lastSync.Value);
        }

        private static string PageUri(int page, int perPage)
        {
            return "/v1/providers?page=" + page + "&perPage=" + perPage;
        }
    }
}
