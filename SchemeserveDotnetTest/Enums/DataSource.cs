// -----------------------------------------------------------------------------
// File:     DataSource.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Where a lookup result came from.
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.Enums
{
    // Where a CacheResult's data came from (set by ProviderLookupService).
    public enum DataSource
    {
        Cache,      // served from the database
        Upstream,   // fetched from CQC (and stored)
        StaleCache  // expired database copy, served because CQC could not be reached
    }
}
