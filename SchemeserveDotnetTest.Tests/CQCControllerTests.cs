// -----------------------------------------------------------------------------
// File:     CQCControllerTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Unit tests for CQCController status codes and headers.
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchemeserveDotnetTest.Controllers;
using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Interfaces;
using SchemeserveDotnetTest.Services;

namespace SchemeserveDotnetTest.Tests
{
    // Tests the HTTP side of CQCController: status codes, response bodies and the cache headers.
    // The controller uses a real ProviderLookupService built on mocks, so no database or CQC is involved.
    public class CQCControllerTests
    {
        private readonly Mock<IProviderRepository> _repository = new Mock<IProviderRepository>();
        private readonly Mock<ICqcRetrievalService> _cqc = new Mock<ICqcRetrievalService>();
        private readonly Mock<IProviderIndexSync> _indexSync = new Mock<IProviderIndexSync>();

        // A small page-size limit (50 instead of the default 1000) proves the controller reads it from configuration.
        private const int MaxPerPage = 50;

        private CQCController CreateController()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "Sources:Cqc:MaxPerPage", MaxPerPage.ToString() } })
                .Build();
            var lookup = new ProviderLookupService(_repository.Object, _cqc.Object, _indexSync.Object, config,
                NullLogger<ProviderLookupService>.Instance);

            // The controller needs an HttpContext to write response headers, and MVC's services
            // so that Problem(...) can build its ProblemDetails body.
            var services = new ServiceCollection();
            services.AddControllers();
            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = services.BuildServiceProvider();

            var controller = new CQCController(lookup, config);
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        // Tests: when there is no providers list to serve (never synced, CQC down), the endpoint returns 503
        // with a ProblemDetails body titled "CQC unavailable".
        [Fact]
        public async Task GetProviders_NoListAvailable_Returns503Problem()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync((DateTime?)null);

            // Act
            var response = await CreateController().GetProviders(CancellationToken.None);

            // Assert
            var result = Assert.IsType<ObjectResult>(response);
            Assert.Equal(503, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal("CQC unavailable", problem.Title);
        }

        // Tests: when CQC can't be reached and the provider isn't stored at all, the endpoint returns 503
        // with a ProblemDetails body titled "CQC unavailable" (instead of an unhandled 500).
        [Fact]
        public async Task GetProviderById_CqcDownWithNoCopy_Returns503Problem()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync("1-101")).ReturnsAsync(false);
            _cqc.Setup(c => c.GetProviderByIdAsync("1-101", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));

            // Act
            var response = await CreateController().GetProviderById("1-101", CancellationToken.None);

            // Assert
            var result = Assert.IsType<ObjectResult>(response);
            Assert.Equal(503, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal("CQC unavailable", problem.Title);
        }

        // Tests: an id CQC doesn't know returns a 404 with a ProblemDetails body titled "Provider not found".
        [Fact]
        public async Task GetProviderById_UnknownId_Returns404Problem()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync("1-999")).ReturnsAsync(false);
            _cqc.Setup(c => c.GetProviderByIdAsync("1-999", It.IsAny<CancellationToken>())).ReturnsAsync((CqcProvider?)null);

            // Act
            var response = await CreateController().GetProviderById("1-999", CancellationToken.None);

            // Assert
            var result = Assert.IsType<ObjectResult>(response);
            Assert.Equal(404, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal("Provider not found", problem.Title);
        }

        // Tests: a provider served from the database returns 200 with the provider as the body,
        // X-Cache set to HIT and X-Fetched-At set to when it was stored (ISO 8601, UTC).
        [Fact]
        public async Task GetProviderById_StoredProvider_Returns200WithCacheHeaders()
        {
            // Arrange
            var storedAt = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);
            _repository.Setup(r => r.IsInIndexAsync("1-101")).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync("1-101"))
                .ReturnsAsync(new CqcProviderRow { ProviderId = "1-101", Name = "Stored", FetchedAtUtc = storedAt });
            var controller = CreateController();

            // Act
            var response = await controller.GetProviderById("1-101", CancellationToken.None);

            // Assert
            var result = Assert.IsType<OkObjectResult>(response);
            var provider = Assert.IsType<CqcProvider>(result.Value);
            Assert.Equal("Stored", provider.Name);
            Assert.Equal("HIT", controller.Response.Headers["X-Cache"].ToString());
            Assert.Equal("2026-10-14T09:30:00.0000000Z", controller.Response.Headers["X-Fetched-At"].ToString());
        }

        // Tests: a provider fetched from CQC returns 200 with X-Cache set to MISS,
        // and X-Fetched-At set to the time of this request (it was fetched just now).
        [Fact]
        public async Task GetProviderById_FetchedFromCqc_SetsCacheHeaderToMiss()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync("1-101")).ReturnsAsync(false);
            _cqc.Setup(c => c.GetProviderByIdAsync("1-101", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CqcProvider { ProviderId = "1-101", Name = "From CQC" });
            var controller = CreateController();

            // Act
            var response = await controller.GetProviderById("1-101", CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(response);
            Assert.Equal("MISS", controller.Response.Headers["X-Cache"].ToString());
            var fetchedAt = DateTime.Parse(controller.Response.Headers["X-Fetched-At"].ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.True(DateTime.UtcNow - fetchedAt < TimeSpan.FromMinutes(1));
        }
        // Tests: when CQC can't be reached and only an expired copy is stored, the provider is still returned
        // with 200, X-Cache is set to STALE, and X-Fetched-At shows when that old copy was stored.
        [Fact]
        public async Task GetProviderById_CqcDownWithExpiredCopy_SetsCacheHeaderToStale()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync("1-101")).ReturnsAsync(true);
            _repository.Setup(r => r.GetProviderAsync("1-101"))
                .ReturnsAsync(new CqcProviderRow { ProviderId = "1-101", Name = "Expired", FetchedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc) });
            _cqc.Setup(c => c.GetProviderByIdAsync("1-101", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CqcUnavailableException("CQC is down"));
            var controller = CreateController();

            // Act
            var response = await controller.GetProviderById("1-101", CancellationToken.None);

            // Assert
            var result = Assert.IsType<OkObjectResult>(response);
            var provider = Assert.IsType<CqcProvider>(result.Value);
            Assert.Equal("Expired", provider.Name);
            Assert.Equal("STALE", controller.Response.Headers["X-Cache"].ToString());
            Assert.Equal("2026-08-01T00:00:00.0000000Z", controller.Response.Headers["X-Fetched-At"].ToString());
        }
        // Tests: the providers list sets the same two headers: X-Cache is HIT when no sync was needed,
        // and X-Fetched-At is when the list was last synced from CQC.
        [Fact]
        public async Task GetProviders_StoredList_SetsCacheHeaders()
        {
            // Arrange
            var syncedAt = new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc);
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync(syncedAt);
            _repository.Setup(r => r.GetIndexCountAsync()).ReturnsAsync(1);
            _repository.Setup(r => r.GetIndexPageAsync(It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new List<CqcProviderIndexRow> { new CqcProviderIndexRow { ProviderId = "1-1", ProviderName = "First" } });
            var controller = CreateController();

            // Act
            var response = await controller.GetProviders(CancellationToken.None);

            // Assert
            Assert.IsType<OkObjectResult>(response);
            Assert.Equal("HIT", controller.Response.Headers["X-Cache"].ToString());
            Assert.Equal("2026-10-01T06:00:00.0000000Z", controller.Response.Headers["X-Fetched-At"].ToString());
        }

        // Tests: a perPage larger than Sources:Cqc:MaxPerPage returns 400 with a ProblemDetails body titled
        // "Invalid perPage", and the providers list is not read at all.
        [Fact]
        public async Task GetProviders_PerPageOverConfiguredMax_Returns400Problem()
        {
            // Act
            var response = await CreateController().GetProviders(CancellationToken.None, page: 1, perPage: MaxPerPage + 1);

            // Assert
            var result = Assert.IsType<ObjectResult>(response);
            Assert.Equal(400, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal("Invalid perPage", problem.Title);
            _repository.Verify(r => r.GetIndexPageAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        // Tests: a perPage exactly at Sources:Cqc:MaxPerPage is allowed and returns 200.
        [Fact]
        public async Task GetProviders_PerPageAtConfiguredMax_Returns200()
        {
            // Arrange
            _indexSync.Setup(s => s.SyncIfExpiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _repository.Setup(r => r.GetLastIndexSyncAsync()).ReturnsAsync(DateTime.UtcNow);
            _repository.Setup(r => r.GetIndexCountAsync()).ReturnsAsync(1);
            _repository.Setup(r => r.GetIndexPageAsync(1, MaxPerPage))
                .ReturnsAsync(new List<CqcProviderIndexRow> { new CqcProviderIndexRow { ProviderId = "1-1", ProviderName = "First" } });

            // Act
            var response = await CreateController().GetProviders(CancellationToken.None, page: 1, perPage: MaxPerPage);

            // Assert
            Assert.IsType<OkObjectResult>(response);
        }

        // Tests: a 404 response carries no cache headers (there is no data, so nothing was cached).
        [Fact]
        public async Task GetProviderById_UnknownId_HasNoCacheHeaders()
        {
            // Arrange
            _repository.Setup(r => r.IsInIndexAsync("1-999")).ReturnsAsync(false);
            _cqc.Setup(c => c.GetProviderByIdAsync("1-999", It.IsAny<CancellationToken>())).ReturnsAsync((CqcProvider?)null);
            var controller = CreateController();

            // Act
            await controller.GetProviderById("1-999", CancellationToken.None);

            // Assert
            Assert.False(controller.Response.Headers.ContainsKey("X-Cache"));
            Assert.False(controller.Response.Headers.ContainsKey("X-Fetched-At"));
        }
    }
}
