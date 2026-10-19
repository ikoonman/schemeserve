// -----------------------------------------------------------------------------
// File:     ProviderRepository.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Dapper data access for providers and the providers list.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Dapper;
using SchemeserveDotnetTest.Interfaces;

namespace SchemeserveDotnetTest.Data
{
    // Dapper data access. Dapper maps columns to properties (and properties to @parameters) by name.
    // Excluded from code coverage: needs a real SQL Server, so it is covered by integration tests, not unit tests.
    [ExcludeFromCodeCoverage]
    public class ProviderRepository : IProviderRepository
    {
        private const string IndexSyncName = "ProviderIndex";

        // "Upsert" for one provider: insert it if it isn't stored yet, otherwise update the stored row.
        // MERGE (standard SQL Server since 2008) compares the target table (t) with a one-row source (s)
        // built from @ProviderId:
        //   WHEN MATCHED     -> a row with that ProviderId exists, so UPDATE it with the new values.
        //   WHEN NOT MATCHED -> no such row, so INSERT a new one.
        // WITH (HOLDLOCK) locks the key while MERGE runs, so two requests saving the same new provider
        // at the same time can't both decide "not matched" and insert it twice.
        // The @Name-style parameters are filled by Dapper from the CqcProviderRow properties of the same name.
        private const string SaveProviderSql = @"
            MERGE dbo.CqcProviders WITH (HOLDLOCK) AS t
            USING (SELECT @ProviderId AS ProviderId) AS s ON t.ProviderId = s.ProviderId
            WHEN MATCHED THEN UPDATE SET
                LocationIdsJson = @LocationIdsJson, OrganisationType = @OrganisationType,
                OwnershipType = @OwnershipType, [Type] = @Type, [Name] = @Name,
                BrandId = @BrandId, BrandName = @BrandName, RegistrationStatus = @RegistrationStatus,
                RegistrationDate = @RegistrationDate, CompaniesHouseNumber = @CompaniesHouseNumber,
                CharityNumber = @CharityNumber, Website = @Website,
                PostalAddressLine1 = @PostalAddressLine1, PostalAddressLine2 = @PostalAddressLine2,
                PostalAddressTownCity = @PostalAddressTownCity, PostalAddressCounty = @PostalAddressCounty,
                Region = @Region, PostalCode = @PostalCode, Uprn = @Uprn,
                OnspdLatitude = @OnspdLatitude, OnspdLongitude = @OnspdLongitude,
                MainPhoneNumber = @MainPhoneNumber, InspectionDirectorate = @InspectionDirectorate,
                Constituency = @Constituency, LocalAuthority = @LocalAuthority,
                LastInspectionDate = @LastInspectionDate, FetchedAtUtc = @FetchedAtUtc
            WHEN NOT MATCHED THEN INSERT (
                ProviderId, LocationIdsJson, OrganisationType, OwnershipType, [Type], [Name],
                BrandId, BrandName, RegistrationStatus, RegistrationDate, CompaniesHouseNumber,
                CharityNumber, Website, PostalAddressLine1, PostalAddressLine2, PostalAddressTownCity,
                PostalAddressCounty, Region, PostalCode, Uprn, OnspdLatitude, OnspdLongitude,
                MainPhoneNumber, InspectionDirectorate, Constituency, LocalAuthority,
                LastInspectionDate, FetchedAtUtc)
            VALUES (
                @ProviderId, @LocationIdsJson, @OrganisationType, @OwnershipType, @Type, @Name,
                @BrandId, @BrandName, @RegistrationStatus, @RegistrationDate, @CompaniesHouseNumber,
                @CharityNumber, @Website, @PostalAddressLine1, @PostalAddressLine2, @PostalAddressTownCity,
                @PostalAddressCounty, @Region, @PostalCode, @Uprn, @OnspdLatitude, @OnspdLongitude,
                @MainPhoneNumber, @InspectionDirectorate, @Constituency, @LocalAuthority,
                @LastInspectionDate, @FetchedAtUtc);";

        private readonly IDbConnectionFactory _db;

        public ProviderRepository(IDbConnectionFactory db)
        {
            _db = db;
        }

        public async Task<CqcProviderRow?> GetProviderAsync(string providerId)
        {
            using (var connection = _db.Open())
            {
                return await connection.QuerySingleOrDefaultAsync<CqcProviderRow>(
                    "SELECT * FROM dbo.CqcProviders WHERE ProviderId = @providerId",
                    new { providerId });
            }
        }

        public async Task SaveProviderAsync(CqcProviderRow row)
        {
            using (var connection = _db.Open())
            {
                await connection.ExecuteAsync(SaveProviderSql, row);
            }
        }

        // Removes a provider CQC no longer knows, from both the children and the index.
        public async Task DeleteProviderAsync(string providerId)
        {
            using (var connection = _db.Open())
            {
                await connection.ExecuteAsync(
                    "DELETE FROM dbo.CqcProviders WHERE ProviderId = @providerId; " +
                    "DELETE FROM dbo.CqcProviderIndex WHERE ProviderId = @providerId;",
                    new { providerId });
            }
        }

        // One page of the stored providers list, ordered by id (page numbers start at 1).
        public async Task<List<CqcProviderIndexRow>> GetIndexPageAsync(int page, int perPage)
        {
            using (var connection = _db.Open())
            {
                var rows = await connection.QueryAsync<CqcProviderIndexRow>(@"
                    SELECT ProviderId, ProviderName FROM dbo.CqcProviderIndex
                    ORDER BY ProviderId
                    OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
                    new { skip = (page - 1) * perPage, take = perPage });
                return rows.ToList();
            }
        }

        public async Task<int> GetIndexCountAsync()
        {
            using (var connection = _db.Open())
            {
                return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.CqcProviderIndex");
            }
        }

        public async Task<bool> IsInIndexAsync(string providerId)
        {
            using (var connection = _db.Open())
            {
                var count = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM dbo.CqcProviderIndex WHERE ProviderId = @providerId",
                    new { providerId });
                return count > 0;
            }
        }

        // Adds a provider CQC knows but the last sync didn't include (added since).
        public async Task AddToIndexAsync(CqcProviderIndexRow row)
        {
            using (var connection = _db.Open())
            {
                await connection.ExecuteAsync(
                    "IF NOT EXISTS (SELECT 1 FROM dbo.CqcProviderIndex WHERE ProviderId = @ProviderId) " +
                    "INSERT INTO dbo.CqcProviderIndex (ProviderId, ProviderName) VALUES (@ProviderId, @ProviderName)",
                    row);
            }
        }

        // Replaces the whole index with a fresh copy from CQC, in one transaction:
        //   1. clear the index and insert the new list
        //   2. delete stored providers ("children") that are no longer in the list
        //   3. expire the surviving children, so the next request for each one re-fetches it from CQC
        //   4. record when this sync happened
        // If anything fails, the transaction rolls back and the previous copy is kept.
        public async Task ReplaceIndexAsync(List<CqcProviderIndexRow> rows, DateTime syncedAtUtc)
        {
            using (var connection = _db.Open())
            using (var transaction = connection.BeginTransaction())
            {
                await connection.ExecuteAsync("DELETE FROM dbo.CqcProviderIndex", null, transaction);

                await connection.ExecuteAsync(
                    "INSERT INTO dbo.CqcProviderIndex (ProviderId, ProviderName) VALUES (@ProviderId, @ProviderName)",
                    rows, transaction);

                await connection.ExecuteAsync(
                    "DELETE FROM dbo.CqcProviders WHERE ProviderId NOT IN (SELECT ProviderId FROM dbo.CqcProviderIndex)",
                    null, transaction);

                // Backdating FetchedAtUtc makes every row older than the TTL. The rows are kept (not deleted)
                // so an expired copy can still be served if CQC is unreachable at the time of the request.
                // The date is written in the SQL itself: a C# DateTime parameter is sent as SQL's older DATETIME
                // type, which can't hold dates before 1753 (so DateTime.MinValue would fail).
                await connection.ExecuteAsync(
                    "UPDATE dbo.CqcProviders SET FetchedAtUtc = '1900-01-01'",
                    null, transaction);

                await connection.ExecuteAsync(@"
                    MERGE dbo.CqcSyncState WITH (HOLDLOCK) AS t
                    USING (SELECT @Name AS Name) AS s ON t.Name = s.Name
                    WHEN MATCHED THEN UPDATE SET LastSyncedAtUtc = @LastSyncedAtUtc
                    WHEN NOT MATCHED THEN INSERT (Name, LastSyncedAtUtc) VALUES (@Name, @LastSyncedAtUtc);",
                    new CqcSyncStateRow { Name = IndexSyncName, LastSyncedAtUtc = syncedAtUtc },
                    transaction);

                transaction.Commit();
            }
        }

        // Null when the providers list has never been synced.
        public async Task<DateTime?> GetLastIndexSyncAsync()
        {
            using (var connection = _db.Open())
            {
                return await connection.QuerySingleOrDefaultAsync<DateTime?>(
                    "SELECT LastSyncedAtUtc FROM dbo.CqcSyncState WHERE Name = @name",
                    new { name = IndexSyncName });
            }
        }
    }
}
