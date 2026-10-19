// -----------------------------------------------------------------------------
// File:     IProviderIndexSync.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Interface for the providers list sync.
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.Interfaces
{
    // This interface exists so the list sync can be mocked in unit tests:
    // ProviderLookupService depends on IProviderIndexSync, not on the class,
    // so a test can decide whether a sync "ran" without calling CQC or the database.
    public interface IProviderIndexSync
    {
        Task<bool> SyncIfExpiredAsync(CancellationToken ct);
    }
}
