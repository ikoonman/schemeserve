// -----------------------------------------------------------------------------
// File:     CqcProviderRowTests.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Unit tests for CqcProviderRow conversions.
// -----------------------------------------------------------------------------

using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.DTO;

namespace SchemeserveDotnetTest.Tests
{
    // Tests the hand-written conversion between the API shape (CqcProvider) and the database row (CqcProviderRow):
    // CqcProviderRow.FromProvider and CqcProviderRow.ToProvider.
    public class CqcProviderRowTests
    {
        // Tests: converting a provider to a row and back gives the same values, including the
        // two fields that are stored differently (LocationIds as JSON, LastInspection.Date as one column).
        [Fact]
        public void FromProviderThenToProvider_KeepsTheValues()
        {
            // Arrange
            var provider = new CqcProvider
            {
                ProviderId = "1-101",
                Name = "Test provider",
                LocationIds = new List<string> { "1-201", "1-202" },
                PostalCode = "AB1 2CD",
                OnspdLatitude = 51.5,
                LastInspection = new InspectionResponse { Date = "2025-01-31" }
            };
            var fetchedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

            // Act
            var row = CqcProviderRow.FromProvider(provider, fetchedAt);
            var back = row.ToProvider();

            // Assert
            Assert.Equal("[\"1-201\",\"1-202\"]", row.LocationIdsJson);
            Assert.Equal("2025-01-31", row.LastInspectionDate);
            Assert.Equal(fetchedAt, row.FetchedAtUtc);

            Assert.Equal("1-101", back.ProviderId);
            Assert.Equal("Test provider", back.Name);
            Assert.Equal(new List<string> { "1-201", "1-202" }, back.LocationIds);
            Assert.Equal("AB1 2CD", back.PostalCode);
            Assert.Equal(51.5, back.OnspdLatitude);
            Assert.Equal("2025-01-31", back.LastInspection!.Date);
        }

        // Tests: a provider with no location ids and no inspection keeps those as null both ways,
        // rather than becoming an empty list or an empty inspection object.
        [Fact]
        public void FromProviderThenToProvider_MissingOptionalFields_StayNull()
        {
            // Arrange
            var provider = new CqcProvider { ProviderId = "1-101" };

            // Act
            var back = CqcProviderRow.FromProvider(provider, DateTime.UtcNow).ToProvider();

            // Assert
            Assert.Null(back.LocationIds);
            Assert.Null(back.LastInspection);
        }
    }
}
