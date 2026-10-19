// -----------------------------------------------------------------------------
// File:     CqcRetrievalServiceTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Unit tests for CqcRetrievalService.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Services;

namespace SchemeserveDotnetTest.Tests
{
    // Tests how CqcRetrievalService reads CQC's HTTP responses.
    // A fake HttpMessageHandler stands in for the network and returns a canned response.
    public class CqcRetrievalServiceTests
    {
        // Returns the given status code and JSON body for every request, and remembers the last request URL.
        private class FakeHttpHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _json;

            public FakeHttpHandler(HttpStatusCode statusCode, string json)
            {
                _statusCode = statusCode;
                _json = json;
            }

            public Uri? LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequestUri = request.RequestUri;
                var response = new HttpResponseMessage(_statusCode);
                response.Content = new StringContent(_json, Encoding.UTF8, "application/json");
                return Task.FromResult(response);
            }
        }

        private static CqcRetrievalService CreateService(FakeHttpHandler handler)
        {
            var http = new HttpClient(handler);
            http.BaseAddress = new Uri("https://cqc.example/public/v1/");
            return new CqcRetrievalService(http);
        }

        // Tests: a 200 from CQC is turned into a CqcProvider, and the request goes to providers/{id}.
        [Fact]
        public async Task GetProviderById_Cqc200_ReturnsProvider()
        {
            // Arrange
            var handler = new FakeHttpHandler(HttpStatusCode.OK, "{\"providerId\":\"1-101\",\"name\":\"Test provider\"}");

            // Act
            var provider = await CreateService(handler).GetProviderByIdAsync("1-101", CancellationToken.None);

            // Assert
            Assert.NotNull(provider);
            Assert.Equal("1-101", provider.ProviderId);
            Assert.Equal("Test provider", provider.Name);
            Assert.Equal("https://cqc.example/public/v1/providers/1-101", handler.LastRequestUri!.ToString());
        }

        // Tests: a 404 from CQC means "no such provider", so the service returns null instead of throwing.
        [Fact]
        public async Task GetProviderById_Cqc404_ReturnsNull()
        {
            // Arrange
            var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "{}");

            // Act
            var provider = await CreateService(handler).GetProviderByIdAsync("1-101", CancellationToken.None);

            // Assert
            Assert.Null(provider);
        }

        // Tests: any other error from CQC (here a 500) throws CqcUnavailableException,
        // which is what the lookup service catches to serve a stale copy.
        [Fact]
        public async Task GetProviderById_Cqc500_ThrowsCqcUnavailable()
        {
            // Arrange
            var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, "{}");

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(
                () => CreateService(handler).GetProviderByIdAsync("1-101", CancellationToken.None));
        }

        // Tests: a 200 whose body isn't valid JSON throws CqcUnavailableException (not a raw JsonException),
        // so a malformed payload is handled the same way as CQC being down.
        [Fact]
        public async Task GetProviderById_MalformedJson_ThrowsCqcUnavailable()
        {
            // Arrange
            var handler = new FakeHttpHandler(HttpStatusCode.OK, "{\"providerId\": not json");

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(
                () => CreateService(handler).GetProviderByIdAsync("1-101", CancellationToken.None));
        }

        // Tests: a 200 for a different provider id than the one asked for throws CqcUnavailableException,
        // so the wrong provider is never stored under the requested id.
        [Fact]
        public async Task GetProviderById_DifferentIdReturned_ThrowsCqcUnavailable()
        {
            // Arrange
            var handler = new FakeHttpHandler(HttpStatusCode.OK, "{\"providerId\":\"1-999\",\"name\":\"Someone else\"}");

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(
                () => CreateService(handler).GetProviderByIdAsync("1-101", CancellationToken.None));
        }

        // Tests: a providers page whose body isn't valid JSON throws CqcUnavailableException.
        [Fact]
        public async Task GetProvidersPage_MalformedJson_ThrowsCqcUnavailable()
        {
            // Arrange
            var handler = new FakeHttpHandler(HttpStatusCode.OK, "[1, 2");

            // Act + Assert
            await Assert.ThrowsAsync<CqcUnavailableException>(
                () => CreateService(handler).GetProvidersPageAsync(1, 1000, CancellationToken.None));
        }

        // Tests: a providers page is read from CQC with the page and perPage passed in the query string.
        [Fact]
        public async Task GetProvidersPage_Cqc200_ReturnsPage()
        {
            // Arrange
            var json = "{\"total\":1,\"page\":2,\"perPage\":1000,\"totalPages\":2," +
                       "\"providers\":[{\"providerId\":\"1-101\",\"providerName\":\"Test provider\"}]}";
            var handler = new FakeHttpHandler(HttpStatusCode.OK, json);

            // Act
            var page = await CreateService(handler).GetProvidersPageAsync(2, 1000, CancellationToken.None);

            // Assert
            Assert.NotNull(page);
            Assert.Equal(2, page.TotalPages);
            Assert.Single(page.Providers);
            Assert.Equal("Test provider", page.Providers[0].ProviderName);
            Assert.Equal("https://cqc.example/public/v1/providers?page=2&perPage=1000", handler.LastRequestUri!.ToString());
        }
    }
}
