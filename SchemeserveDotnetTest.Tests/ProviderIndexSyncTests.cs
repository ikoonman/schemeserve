// -----------------------------------------------------------------------------
// File:     ProviderIndexSyncTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Unit tests for ProviderIndexSync.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Interfaces;
using SchemeserveDotnetTest.Services;

namespace SchemeserveDotnetTest.Tests
{
    // Tests when ProviderIndexSync re-syncs the full providers list, and that it reads every CQC page.
    // The database and CQC are mocks.
    public class ProviderIndexSyncTests
    {
        private readonly Mock<IProviderRepository> _repository = new Mock<IProviderRepository>();
        private readonly Mock<ICqcRetrievalService> _cqc = new Mock<ICqcRetrievalService>();

        private ProviderIndexSync CreateSync()
        {
            // Empty configuration: the default 30 day TTL is used.
            var config = new ConfigurationBuilder().Build();
            return new ProviderIndexSync(_repository.Object, _cqc.Object, config, NullLogger<ProviderIndexSync>.Instance);
        }

        // One provider per page, so the total number of providers equals the number of pages.
        private static CqcProvidersPage Page(int totalPages, string providerId)
        {
            var page = new CqcProvidersPage { TotalPages = totalPages, Total = totalPages };
            page.Providers.Add(new CqcProviderSummary { ProviderId = providerId, ProviderName = "Provider " + providerId });
            return page;
        }

        // Tests: a list synced less than 30 days ago is left alone; CQC is not called and nothing is replaced.
        [Fact]
        public async Task SyncIfExpired_ListIsFresh_DoesNotSync()
        {
            // Arrange
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync(DateTime.UtcNow.AddDays(-1));

            // Act
            var synced = await CreateSync().SyncIfExpiredAsync(CancellationToken.None);

            // Assert
            Assert.False(synced);
            _cqc.Verify(c => c.GetProvidersPageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            _repository.Verify(r => r.ReplaceIndexAsync(It.IsAny<List<CqcProviderIndexRow>>(), It.IsAny<DateTime>()), Times.Never);
        }

        // Tests: a list that has never been synced is fetched from CQC page by page (here 2 pages of 1000),
        // and all providers from every page are written to the database in one ReplaceIndexAsync call.
        [Fact]
        public async Task SyncIfExpired_NeverSynced_ReadsEveryPageAndReplacesList()
        {
            // Arrange
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync((DateTime?)null);
            _cqc.Setup(c => c.GetProvidersPageAsync(1, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(Page(2, "1-1"));
            _cqc.Setup(c => c.GetProvidersPageAsync(2, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(Page(2, "1-2"));

            // Act
            var synced = await CreateSync().SyncIfExpiredAsync(CancellationToken.None);

            // Assert
            Assert.True(synced);
            _repository.Verify(r => r.ReplaceIndexAsync(
                It.Is<List<CqcProviderIndexRow>>(rows => rows.Count == 2 && rows[0].ProviderId == "1-1" && rows[1].ProviderId == "1-2"),
                It.IsAny<DateTime>()), Times.Once);
        }

        // Tests: a list older than 30 days is re-synced.
        [Fact]
        public async Task SyncIfExpired_ListOlderThan30Days_Syncs()
        {
            // Arrange
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync(DateTime.UtcNow.AddDays(-31));
            _cqc.Setup(c => c.GetProvidersPageAsync(1, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(Page(1, "1-1"));

            // Act
            var synced = await CreateSync().SyncIfExpiredAsync(CancellationToken.None);

            // Assert
            Assert.True(synced);
            _repository.Verify(r => r.ReplaceIndexAsync(It.IsAny<List<CqcProviderIndexRow>>(), It.IsAny<DateTime>()), Times.Once);
        }

        // Tests: if CQC fails part-way through, the stored list is not touched (nothing is replaced)
        // and the error reaches the caller.
        [Fact]
        public async Task SyncIfExpired_CqcFails_KeepsStoredList()
        {
            // Arrange
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync((DateTime?)null);
            _cqc.Setup(c => c.GetProvidersPageAsync(1, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(Page(2, "1-1"));
            _cqc.Setup(c => c.GetProvidersPageAsync(2, 1000, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(() => CreateSync().SyncIfExpiredAsync(CancellationToken.None));

            // Assert
            _repository.Verify(r => r.ReplaceIndexAsync(It.IsAny<List<CqcProviderIndexRow>>(), It.IsAny<DateTime>()), Times.Never);
        }

        // Tests: if CQC reports more providers than were actually read (an incomplete list),
        // the stored list is not replaced and CqcUnavailableException is thrown.
        [Fact]
        public async Task SyncIfExpired_ListIncomplete_KeepsStoredList()
        {
            // Arrange
            var incomplete = Page(1, "1-1");
            incomplete.Total = 5;
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync((DateTime?)null);
            _cqc.Setup(c => c.GetProvidersPageAsync(1, 1000, It.IsAny<CancellationToken>())).ReturnsAsync(incomplete);

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(() => CreateSync().SyncIfExpiredAsync(CancellationToken.None));

            // Assert
            _repository.Verify(r => r.ReplaceIndexAsync(It.IsAny<List<CqcProviderIndexRow>>(), It.IsAny<DateTime>()), Times.Never);
        }
    }
}
