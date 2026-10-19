// -----------------------------------------------------------------------------
// File:     ProviderIndexSync.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Keeps the stored providers list in step with CQC.
// -----------------------------------------------------------------------------

using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Interfaces;

namespace SchemeserveDotnetTest.Services
{
    // Keeps dbo.CqcProviderIndex in step with CQC's full providers list.
    // A sync walks every page of CQC's list and replaces the stored copy in one go;
    // stored providers that are no longer listed are deleted as part of the same step.
    public class ProviderIndexSync : IProviderIndexSync
    {
        private const int PageSize = 1000;

        // Only one sync at a time, even if the background job and a request both trigger one.
        private static readonly SemaphoreSlim SyncLock = new SemaphoreSlim(1, 1);

        private readonly IProviderRepository _repository;
        private readonly ICqcRetrievalService _cqc;
        private readonly ILogger<ProviderIndexSync> _log;
        private readonly TimeSpan _ttl;

        public ProviderIndexSync(
            IProviderRepository repository,
            ICqcRetrievalService cqc,
            IConfiguration config,
            ILogger<ProviderIndexSync> log)
        {
            _repository = repository;
            _cqc = cqc;
            _log = log;
            // "One month" is taken as 30 days.
            _ttl = config.GetValue("Sources:Cqc:CacheTtl", TimeSpan.FromDays(30));
        }

        // Syncs if the stored list is missing or older than the TTL.
        // Returns true if a sync ran, false if the stored list was still fresh.
        public async Task<bool> SyncIfExpiredAsync(CancellationToken ct)
        {
            await SyncLock.WaitAsync(ct);
            try
            {
                var lastSync = await _repository.GetLastIndexSyncAsync();
                if (lastSync != null && DateTime.UtcNow - lastSync.Value < _ttl)
                    return false;

                await SyncAsync(ct);
                return true;
            }
            finally
            {
                SyncLock.Release();
            }
        }

        private async Task SyncAsync(CancellationToken ct)
        {
            _log.LogInformation("Syncing the CQC providers list");

            var rows = new List<CqcProviderIndexRow>();
            var page = 1;
            var totalPages = 1;
            var total = 0;

            // Read every page first; the database is only touched once the whole list is in hand.
            while (page <= totalPages)
            {
                var result = await _cqc.GetProvidersPageAsync(page, PageSize, ct);
                if (result == null)
                    throw new CqcUnavailableException("CQC returned an empty providers page " + page);

                foreach (var provider in result.Providers)
                {
                    rows.Add(new CqcProviderIndexRow { ProviderId = provider.ProviderId, ProviderName = provider.ProviderName });
                }

                totalPages = result.TotalPages;
                total = result.Total;
                page++;
            }

            // Only replace the stored list with a complete copy: if the number of providers read doesn't match
            // the total CQC reports, keep the old list (served as STALE) and try again on the next sync.
            if (rows.Count != total)
                throw new CqcUnavailableException("CQC providers list is incomplete: read " + rows.Count + " of " + total);

            await _repository.ReplaceIndexAsync(rows, DateTime.UtcNow);
            _log.LogInformation("CQC providers list synced: {Count} providers", rows.Count);
        }
    }
}
