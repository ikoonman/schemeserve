// -----------------------------------------------------------------------------
// File:     ProviderLookupServiceTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Unit tests for ProviderLookupService cache rules.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Enums;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Interfaces;
using SchemeserveDotnetTest.Services;

namespace SchemeserveDotnetTest.Tests
{
    // Tests the cache rules in ProviderLookupService.
    // The database (IProviderRepository), CQC (ICqcRetrievalService) and the list sync (IProviderIndexSync)
    // are all mocks, so each test controls exactly what is "stored" and what CQC "returns".
    public class ProviderLookupServiceTests
    {
        private const string ProviderId = "1-101";

        private readonly Mock<IProviderRepository> _repository = new Mock<IProviderRepository>();
        private readonly Mock<ICqcRetrievalService> _cqc = new Mock<ICqcRetrievalService>();
        private readonly Mock<IProviderIndexSync> _indexSync = new Mock<IProviderIndexSync>();

        private ProviderLookupService CreateService()
        {
            // Empty configuration: the service falls back to its defaults (30 day TTL, 1000 per page).
            var config = new ConfigurationBuilder().Build();
            return new ProviderLookupService(_repository.Object, _cqc.Object, _indexSync.Object, config,
                NullLogger<ProviderLookupService>.Instance);
        }

        private static CqcProviderRow StoredRow(DateTime fetchedAtUtc)
        {
            return new CqcProviderRow { ProviderId = ProviderId, Name = "Stored name", FetchedAtUtc = fetchedAtUtc };
        }

        // ---------- GET v1/providers/{id} ----------

        // Tests: a provider that is in the list and was stored less than 30 days ago
        // is returned from the database (HIT), and CQC is not called at all.
        [Fact]
        public async Task GetProviderById_ListedAndFresh_ReturnsStoredCopyWithoutCallingCqc()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync(ProviderId)).ReturnsAsync(StoredRow(DateTime.UtcNow.AddDays(-1)));

            // Act
            var result = await CreateService().GetProviderByIdAsync(ProviderId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.Cache, result.Source);
            Assert.Equal("Stored name", result.Data.Name);
            _cqc.Verify(c => c.GetProviderByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // Tests: a stored copy older than 30 days is ignored; the provider is fetched from CQC (MISS)
        // and the fresh copy is saved to the database.
        [Fact]
        public async Task GetProviderById_ListedButExpired_FetchesFromCqcAndSaves()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync(ProviderId)).ReturnsAsync(StoredRow(DateTime.UtcNow.AddDays(-31)));
            _cqc.Setup(c => c.GetProviderByIdAsync(ProviderId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CqcProvider { ProviderId = ProviderId, Name = "Fresh name" });

            // Act
            var result = await CreateService().GetProviderByIdAsync(ProviderId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.Upstream, result.Source);
            Assert.Equal("Fresh name", result.Data.Name);
            _repository.Verify(r => r.SaveProviderAsync(It.Is<CqcProviderRow>(row => row.Name == "Fresh name")), Times.Once);
        }

        // Tests: an id that is not in the stored providers list is never read from the database.
        // It is fetched from CQC, saved, and added to the list (a provider CQC added since the last sync).
        [Fact]
        public async Task GetProviderById_NotInList_FetchesFromCqcAndAddsToList()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(false);
            _cqc.Setup(c => c.GetProviderByIdAsync(ProviderId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CqcProvider { ProviderId = ProviderId, Name = "New provider" });

            // Act
            var result = await CreateService().GetProviderByIdAsync(ProviderId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.Upstream, result.Source);
            _repository.Verify(r => r.GetProviderAsync(It.IsAny<string>()), Times.Never);
            _repository.Verify(r => r.SaveProviderAsync(It.IsAny<CqcProviderRow>()), Times.Once);
            _repository.Verify(r => r.AddToIndexAsync(It.Is<CqcProviderIndexRow>(row => row.ProviderId == ProviderId)), Times.Once);
        }

        // Tests: when CQC answers 404 (the service returns null), the local copy is deleted
        // and the lookup returns null, which the controller turns into a 404.
        [Fact]
        public async Task GetProviderById_CqcReturnsNotFound_DeletesLocalCopyAndReturnsNull()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync(ProviderId)).ReturnsAsync(StoredRow(DateTime.UtcNow.AddDays(-31)));
            _cqc.Setup(c => c.GetProviderByIdAsync(ProviderId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((CqcProvider?)null);

            // Act
            var result = await CreateService().GetProviderByIdAsync(ProviderId, CancellationToken.None);

            // Assert
            Assert.Null(result);
            _repository.Verify(r => r.DeleteProviderAsync(ProviderId), Times.Once);
        }

        // Tests: when CQC cannot be reached and an expired copy exists, the expired copy is returned (STALE)
        // instead of an error.
        [Fact]
        public async Task GetProviderById_CqcDownWithExpiredCopy_ReturnsStaleCopy()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync(ProviderId)).ReturnsAsync(StoredRow(DateTime.UtcNow.AddDays(-31)));
            _cqc.Setup(c => c.GetProviderByIdAsync(ProviderId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));

            // Act
            var result = await CreateService().GetProviderByIdAsync(ProviderId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.StaleCache, result.Source);
            Assert.Equal("Stored name", result.Data.Name);
        }

        // Tests: if the logger itself throws while logging the CQC problem, the request still succeeds
        // with the expired copy (STALE). The failed log goes to the event log fallback instead of crashing the request.
        [Fact]
        public async Task GetProviderById_CqcDownAndLoggingFails_StillReturnsStaleCopy()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync(ProviderId)).ReturnsAsync(StoredRow(DateTime.UtcNow.AddDays(-31)));
            _cqc.Setup(c => c.GetProviderByIdAsync(ProviderId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));
            var brokenLogger = new Mock<ILogger<ProviderLookupService>>();
            brokenLogger.Setup(l => l.Log(
                    It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()))
                .Throws(new InvalidOperationException("Logging is broken"));
            var service = new ProviderLookupService(_repository.Object, _cqc.Object, _indexSync.Object,
                new ConfigurationBuilder().Build(), brokenLogger.Object);

            // Act
            var result = await service.GetProviderByIdAsync(ProviderId, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.StaleCache, result.Source);
        }

        // Tests: when CQC cannot be reached and nothing is stored, the error is not hidden;
        // the exception reaches the caller.
        [Fact]
        public async Task GetProviderById_CqcDownWithNoCopy_Throws()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync(ProviderId)).ReturnsAsync(false);
            _cqc.Setup(c => c.GetProviderByIdAsync(ProviderId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(
                () => CreateService().GetProviderByIdAsync(ProviderId, CancellationToken.None));
        }

        // ---------- GET v1/providers ----------

        private void SetupStoredList(int total, DateTime lastSyncUtc)
        {
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync(lastSyncUtc);
            _repository.Setup(r => r.GetIndexCountAsync()).ReturnsAsync(total);
            _repository.Setup(r => r.GetIndexPageAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<CqcProviderIndexRow>
                {
                    new CqcProviderIndexRow { ProviderId = "1-1", ProviderName = "First" },
                    new CqcProviderIndexRow { ProviderId = "1-2", ProviderName = "Second" }
                });
        }

        // Tests: with no page or perPage, the whole stored list comes back as one page
        // (page 1 of 1, perPage = total), served from the database (HIT) because no sync was needed.
        [Fact]
        public async Task GetProviders_NoPaging_ReturnsFullListAsOnePage()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            SetupStoredList(2, DateTime.UtcNow.AddDays(-1));

            // Act
            var result = await CreateService().GetProvidersAsync(null, null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.Cache, result.Source);
            Assert.Equal(2, result.Data.Total);
            Assert.Equal(1, result.Data.Page);
            Assert.Equal(2, result.Data.PerPage);
            Assert.Equal(1, result.Data.TotalPages);
            Assert.Equal(2, result.Data.Providers.Count);
            Assert.Null(result.Data.NextPageUri);
            _repository.Verify(r => r.GetIndexPageAsync(1, 2), Times.Once);
        }

        // Tests: with page and perPage, the requested page is read from the database and the paging
        // fields and links are worked out correctly (25 providers, 10 per page, page 2 of 3).
        [Fact]
        public async Task GetProviders_WithPaging_ReturnsRequestedPageWithLinks()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            SetupStoredList(25, DateTime.UtcNow.AddDays(-1));

            // Act
            var result = await CreateService().GetProvidersAsync(2, 10, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Data.Page);
            Assert.Equal(10, result.Data.PerPage);
            Assert.Equal(3, result.Data.TotalPages);
            Assert.Equal("/v1/providers?page=2&perPage=10", result.Data.Uri);
            Assert.Equal("/v1/providers?page=1&perPage=10", result.Data.PreviousPageUri);
            Assert.Equal("/v1/providers?page=3&perPage=10", result.Data.NextPageUri);
            Assert.Equal("/v1/providers?page=3&perPage=10", result.Data.LastPageUri);
            _repository.Verify(r => r.GetIndexPageAsync(2, 10), Times.Once);
        }

        // Tests: with only page given (e.g. ?page=3), perPage falls back to the default of 1000
        // (Sources:Cqc:MaxPerPage, not set here), so page 3 of 1000-per-page is read.
        [Fact]
        public async Task GetProviders_OnlyPageGiven_UsesDefaultPerPage()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            SetupStoredList(2500, DateTime.UtcNow.AddDays(-1));

            // Act
            var result = await CreateService().GetProvidersAsync(3, null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Data.Page);
            Assert.Equal(1000, result.Data.PerPage);
            Assert.Equal(3, result.Data.TotalPages);
            Assert.Equal("/v1/providers?page=3&perPage=1000", result.Data.Uri);
            _repository.Verify(r => r.GetIndexPageAsync(3, 1000), Times.Once);
        }

        // Tests: with only perPage given (e.g. ?perPage=400, or ?perPage=400&page= where the empty page counts
        // as not given), page falls back to 1.
        [Fact]
        public async Task GetProviders_OnlyPerPageGiven_StartsAtPageOne()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            SetupStoredList(2500, DateTime.UtcNow.AddDays(-1));

            // Act
            var result = await CreateService().GetProvidersAsync(null, 400, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.Data.Page);
            Assert.Equal(400, result.Data.PerPage);
            Assert.Equal(7, result.Data.TotalPages);
            Assert.Null(result.Data.PreviousPageUri);
            Assert.Equal("/v1/providers?page=2&perPage=400", result.Data.NextPageUri);
            _repository.Verify(r => r.GetIndexPageAsync(1, 400), Times.Once);
        }

        // Tests: when the list sync ran during this request, the result is marked as fetched from CQC (MISS).
        [Fact]
        public async Task GetProviders_SyncRan_SourceIsUpstream()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            SetupStoredList(2, DateTime.UtcNow);

            // Act
            var result = await CreateService().GetProvidersAsync(null, null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.Upstream, result.Source);
        }

        // Tests: when the list has expired but CQC cannot be reached, the old stored list is still served (STALE).
        [Fact]
        public async Task GetProviders_CqcDown_ServesStoredListAsStale()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));
            SetupStoredList(2, DateTime.UtcNow.AddDays(-40));

            // Act
            var result = await CreateService().GetProvidersAsync(null, null, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(DataSource.StaleCache, result.Source);
            Assert.Equal(2, result.Data.Providers.Count);
        }

        // Tests: when the list has never been synced and CQC cannot be reached, there is nothing to serve,
        // so the lookup returns null (the controller turns this into a 503).
        [Fact]
        public async Task GetProviders_NeverSyncedAndCqcDown_ReturnsNull()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync((DateTime?)null);

            // Act
            var result = await CreateService().GetProvidersAsync(null, null, CancellationToken.None);

            // Assert
            Assert.Null(result);
        }
    }
}
