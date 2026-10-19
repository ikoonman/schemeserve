// -----------------------------------------------------------------------------
// File:     CqcProviderIndexRow.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Row of dbo.CqcProviderIndex (the full providers list).
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.Data
{
    // One row of dbo.CqcProviderIndex: the full CQC providers list, one row per provider.
    public class CqcProviderIndexRow
    {
        public string ProviderId { get; set; } = "";
        public string ProviderName { get; set; } = "";
    }
}
