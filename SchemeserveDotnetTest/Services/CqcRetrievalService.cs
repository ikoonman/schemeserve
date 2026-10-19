// -----------------------------------------------------------------------------
// File:     CqcRetrievalService.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Calls the CQC API.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Interfaces;

namespace SchemeserveDotnetTest.Services
{
    // Calls the CQC API. The HttpClient is configured in Program.cs (base address + subscription key).
    public class CqcRetrievalService : ICqcRetrievalService
    {
        private readonly HttpClient _http;

        public CqcRetrievalService(HttpClient http)
        {
            _http = http;
        }

        // One page of CQC's providers list.
        // Any failure (network, error status, timeout, unreadable JSON) is thrown as CqcUnavailableException.
        public async Task<CqcProvidersPage?> GetProvidersPageAsync(int page, int perPage, CancellationToken ct)
        {
            try
            {
                return await _http.GetFromJsonAsync<CqcProvidersPage>("providers?page=" + page + "&perPage=" + perPage, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new CqcUnavailableException("CQC could not return providers page " + page, ex);
            }
            catch (JsonException ex)
            {
                throw new CqcUnavailableException("CQC returned an unreadable providers page " + page, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // The HttpClient timed out. (If ct was cancelled, the caller went away: let that through as is.)
                throw new CqcUnavailableException("CQC did not answer in time for providers page " + page, ex);
            }
        }

        // Returns null when CQC doesn't know the id (404).
        // Any other failure is thrown as CqcUnavailableException, including a provider with a different id.
        public async Task<CqcProvider?> GetProviderByIdAsync(string id, CancellationToken ct)
        {
            CqcProvider? provider;
            try
            {
                using (var response = await _http.GetAsync("providers/" + Uri.EscapeDataString(id), ct))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return null;

                    response.EnsureSuccessStatusCode();
                    provider = await response.Content.ReadFromJsonAsync<CqcProvider>(ct);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new CqcUnavailableException("CQC could not return provider " + id, ex);
            }
            catch (JsonException ex)
            {
                throw new CqcUnavailableException("CQC returned an unreadable body for provider " + id, ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // The HttpClient timed out. (If ct was cancelled, the caller went away: let that through as is.)
                throw new CqcUnavailableException("CQC did not answer in time for provider " + id, ex);
            }

            // Don't store a body that isn't the provider that was asked for (empty, or a different id).
            if (provider == null || provider.ProviderId != id)
                throw new CqcUnavailableException("CQC returned a different or empty provider for " + id);

            return provider;
        }
    }
}
