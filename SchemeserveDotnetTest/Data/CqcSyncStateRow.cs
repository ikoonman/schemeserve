// -----------------------------------------------------------------------------
// File:     CqcSyncStateRow.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Row of dbo.CqcSyncState (when a sync last completed).
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
namespace SchemeserveDotnetTest.Data
{
    // One row of dbo.CqcSyncState: when a sync (e.g. the providers list) last completed.
    // Excluded from code coverage: only used by ProviderRepository (SQL Server).
    [ExcludeFromCodeCoverage]
    public class CqcSyncStateRow
    {
        public string Name { get; set; } = "";
        public DateTime LastSyncedAtUtc { get; set; }
    }
}
