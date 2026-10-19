// -----------------------------------------------------------------------------
// File:     IProviderRepository.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Interface for the provider cache data access.
// -----------------------------------------------------------------------------

using SchemeserveDotnetTest.Data;

namespace SchemeserveDotnetTest.Interfaces
{
    // Data access for the provider cache (implemented by ProviderRepository with Dapper).
    // Behind an interface so the services can be unit tested with a mock instead of a real database.
    public interface IProviderRepository
    {
        // Individual providers (the "children").
        Task<CqcProviderRow?> GetProviderAsync(string providerId);
        Task SaveProviderAsync(CqcProviderRow row);
        Task DeleteProviderAsync(string providerId);

        // The full providers list (the "index").
        Task<List<CqcProviderIndexRow>> GetIndexPageAsync(int page, int perPage);
        Task<int> GetIndexCountAsync();
        Task<bool> IsInIndexAsync(string providerId);
        Task AddToIndexAsync(CqcProviderIndexRow row);
        Task ReplaceIndexAsync(List<CqcProviderIndexRow> rows, DateTime syncedAtUtc);
        Task<DateTime?> GetLastIndexSyncAsync();
    }
}
