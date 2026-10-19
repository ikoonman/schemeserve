// -----------------------------------------------------------------------------
// File:     CacheState.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Values of the X-Cache response header.
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.Enums
{
    // Value of the X-Cache response header, set by CQCController from the DataSource.
    // The names are in capitals on purpose: the name itself is what is written to the header.
    public enum CacheState
    {
        HIT,    // DataSource.Cache
        MISS,   // DataSource.Upstream
        STALE   // DataSource.StaleCache
    }
}
