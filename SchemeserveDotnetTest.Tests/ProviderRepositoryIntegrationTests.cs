// -----------------------------------------------------------------------------
// File:     ProviderRepositoryIntegrationTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Integration tests for ProviderRepository (skipped by default).
// -----------------------------------------------------------------------------

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.Factories;

namespace SchemeserveDotnetTest.Tests
{
    // Integration tests for ProviderRepository: the real Dapper SQL against a real SQL Server database.
    //
    // SKIPPED BY DEFAULT. They need SQL Server LocalDB (installed with Visual Studio).
    // To run them, set SkipReason below to null, then run the tests as usual (see README, "Integration tests").
    //
    // They never touch the app's own CqcCache database. They create and use a separate database,
    // CqcCache_IntegrationTests, and empty its tables before every test, so each test starts clean
    // and doesn't depend on the API having been run or on any data from CQC.
    public class ProviderRepositoryIntegrationTests
    {
        // The one switch for all integration tests: a reason = skipped, null = run.
        private const string? SkipReason = "Integration test: needs SQL Server LocalDB. Set SkipReason to null in ProviderRepositoryIntegrationTests.cs to run.";
        //private const string? SkipReason = null; 

        private const string ConnectionString =
            @"Server=(localdb)\MSSQLLocalDB;Database=CqcCache_IntegrationTests;Trusted_Connection=True;TrustServerCertificate=True";

        // Creates the test database and tables (same SchemaBootstrapper as the app), empties the tables,
        // and returns a ProviderRepository connected to it.
        private static async Task<ProviderRepository> CreateRepositoryAsync()
        {
            await SchemaBootstrapper.EnsureDatabaseAsync(ConnectionString);
            await SchemaBootstrapper.EnsureTableAsync<CqcProviderRow>(ConnectionString, "CqcProviders", "ProviderId");
            await SchemaBootstrapper.EnsureTableAsync<CqcProviderIndexRow>(ConnectionString, "CqcProviderIndex", "ProviderId");
            await SchemaBootstrapper.EnsureTableAsync<CqcSyncStateRow>(ConnectionString, "CqcSyncState", "Name");

            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.ExecuteAsync("DELETE FROM dbo.CqcProviders; DELETE FROM dbo.CqcProviderIndex; DELETE FROM dbo.CqcSyncState;");
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:CqcCache", ConnectionString } })
                .Build();
            return new ProviderRepository(new SqlConnectionFactory(config));
        }

        private static CqcProviderRow Provider(string id, string name, DateTime fetchedAtUtc)
        {
            return new CqcProviderRow
            {
                ProviderId = id,
                Name = name,
                LocationIdsJson = "[\"1-201\"]",
                OnspdLatitude = 51.5,
                FetchedAtUtc = fetchedAtUtc
            };
        }

        private static CqcProviderIndexRow Listed(string id)
        {
            return new CqcProviderIndexRow { ProviderId = id, ProviderName = "Provider " + id };
        }

        // Tests: a provider saved with SaveProviderAsync (MERGE insert) is read back by GetProviderAsync
        // with the same values, including the FetchedAtUtc date.
        [Fact(Skip = SkipReason)]
        public async Task SaveProvider_ThenGetProvider_ReturnsSameValues()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();
            var fetchedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

            // Act
            await repository.SaveProviderAsync(Provider("1-101", "Test provider", fetchedAt));
            var stored = await repository.GetProviderAsync("1-101");

            // Assert
            Assert.NotNull(stored);
            Assert.Equal("Test provider", stored.Name);
            Assert.Equal("[\"1-201\"]", stored.LocationIdsJson);
            Assert.Equal(51.5, stored.OnspdLatitude);
            Assert.Equal(fetchedAt, stored.FetchedAtUtc);
        }

        // Tests: saving the same provider id twice updates the existing row (MERGE update) instead of
        // failing on the primary key or creating a second row.
        [Fact(Skip = SkipReason)]
        public async Task SaveProvider_SameIdTwice_UpdatesTheRow()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();
            await repository.SaveProviderAsync(Provider("1-101", "Old name", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

            // Act
            await repository.SaveProviderAsync(Provider("1-101", "New name", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            var stored = await repository.GetProviderAsync("1-101");

            // Assert
            Assert.NotNull(stored);
            Assert.Equal("New name", stored.Name);
            Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), stored.FetchedAtUtc);
        }

        // Tests: DeleteProviderAsync removes the provider from both the stored providers and the providers list.
        [Fact(Skip = SkipReason)]
        public async Task DeleteProvider_RemovesItFromProvidersAndList()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();
            await repository.SaveProviderAsync(Provider("1-101", "Test provider", DateTime.UtcNow));
            await repository.AddToIndexAsync(Listed("1-101"));

            // Act
            await repository.DeleteProviderAsync("1-101");

            // Assert
            Assert.Null(await repository.GetProviderAsync("1-101"));
            Assert.False(await repository.IsInIndexAsync("1-101"));
        }

        // Tests: AddToIndexAsync adds an id to the providers list only once, even if called twice,
        // and IsInIndexAsync reports which ids are listed.
        [Fact(Skip = SkipReason)]
        public async Task AddToIndex_CalledTwice_AddsTheIdOnce()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();

            // Act
            await repository.AddToIndexAsync(Listed("1-101"));
            await repository.AddToIndexAsync(Listed("1-101"));

            // Assert
            Assert.Equal(1, await repository.GetIndexCountAsync());
            Assert.True(await repository.IsInIndexAsync("1-101"));
            Assert.False(await repository.IsInIndexAsync("1-999"));
        }

        // Tests: GetIndexPageAsync returns the requested page (OFFSET/FETCH) ordered by provider id,
        // and GetIndexCountAsync returns the total.
        [Fact(Skip = SkipReason)]
        public async Task GetIndexPage_ReturnsRequestedPageInIdOrder()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();
            var rows = new List<CqcProviderIndexRow> { Listed("1-5"), Listed("1-1"), Listed("1-4"), Listed("1-2"), Listed("1-3") };
            await repository.ReplaceIndexAsync(rows, DateTime.UtcNow);

            // Act
            var page2 = await repository.GetIndexPageAsync(2, 2);

            // Assert
            Assert.Equal(5, await repository.GetIndexCountAsync());
            Assert.Equal(2, page2.Count);
            Assert.Equal("1-3", page2[0].ProviderId);
            Assert.Equal("1-4", page2[1].ProviderId);
        }

        // Tests: before any sync, GetLastIndexSyncAsync returns null (the "never synced" case).
        [Fact(Skip = SkipReason)]
        public async Task GetLastIndexSync_NeverSynced_ReturnsNull()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();

            // Act
            var lastSync = await repository.GetLastIndexSyncAsync();

            // Assert
            Assert.Null(lastSync);
        }

        // Tests: ReplaceIndexAsync, the monthly list refresh, in one go:
        //   - replaces the providers list with the new one,
        //   - deletes stored providers that are no longer in the list,
        //   - expires the stored providers that are still listed (FetchedAtUtc set to 1900-01-01),
        //   - records the sync time, which GetLastIndexSyncAsync then returns.
        // (The expiry step is where a SqlDateTime overflow bug was found when running the app.)
        [Fact(Skip = SkipReason)]
        public async Task ReplaceIndex_DeletesUnlistedExpiresListedAndRecordsSyncTime()
        {
            // Arrange
            var repository = await CreateRepositoryAsync();
            await repository.SaveProviderAsync(Provider("1-101", "Still listed", DateTime.UtcNow));
            await repository.SaveProviderAsync(Provider("1-102", "No longer listed", DateTime.UtcNow));
            var syncedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

            // Act
            await repository.ReplaceIndexAsync(new List<CqcProviderIndexRow> { Listed("1-101"), Listed("1-103") }, syncedAt);

            // Assert
            Assert.Equal(2, await repository.GetIndexCountAsync());
            Assert.True(await repository.IsInIndexAsync("1-103"));

            var stillListed = await repository.GetProviderAsync("1-101");
            Assert.NotNull(stillListed);
            Assert.Equal(new DateTime(1900, 1, 1), stillListed.FetchedAtUtc);

            Assert.Null(await repository.GetProviderAsync("1-102"));

            Assert.Equal(syncedAt, await repository.GetLastIndexSyncAsync());
        }
    }
}
