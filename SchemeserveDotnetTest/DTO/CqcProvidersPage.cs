// -----------------------------------------------------------------------------
// File:     CqcProvidersPage.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  One page of the providers list, in CQC's shape.
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.DTO
{
    // Response of CQC's GET /providers.
    public class CqcProvidersPage
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PerPage { get; set; }
        public int TotalPages { get; set; }

        public string? FirstPageUri { get; set; }
        public string? PreviousPageUri { get; set; }
        public string? NextPageUri { get; set; }
        public string? LastPageUri { get; set; }
        public string? Uri { get; set; }

        public List<CqcProviderSummary> Providers { get; set; } = new List<CqcProviderSummary>();
    }

    public class CqcProviderSummary
    {
        public string ProviderId { get; set; } = "";
        public string ProviderName { get; set; } = "";
    }
}
