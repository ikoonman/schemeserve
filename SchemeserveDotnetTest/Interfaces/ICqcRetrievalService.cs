// -----------------------------------------------------------------------------
// File:     ICqcRetrievalService.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Interface for calling the CQC API.
// -----------------------------------------------------------------------------

using SchemeserveDotnetTest.DTO;

namespace SchemeserveDotnetTest.Interfaces
{
    // This interface exists so the CQC calls can be mocked in unit tests:
    // ProviderLookupService and ProviderIndexSync depend on ICqcRetrievalService, not on the class,
    // so a test can hand them a fake that never touches the network.
    public interface ICqcRetrievalService
    {
        Task<CqcProvidersPage?> GetProvidersPageAsync(int page, int perPage, CancellationToken ct);
        Task<CqcProvider?> GetProviderByIdAsync(string id, CancellationToken ct);
    }
}
