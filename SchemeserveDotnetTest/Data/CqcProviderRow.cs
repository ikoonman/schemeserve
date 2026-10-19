// -----------------------------------------------------------------------------
// File:     CqcProviderRow.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Row of dbo.CqcProviders, plus conversion to and from CqcProvider.
// -----------------------------------------------------------------------------

using System.Text.Json;
using SchemeserveDotnetTest.DTO;

namespace SchemeserveDotnetTest.Data
{
    // One row of dbo.CqcProviders. The table is generated from this class (see SchemaBootstrapper).
    public class CqcProviderRow
    {
        public string ProviderId { get; set; } = "";
        public string? LocationIdsJson { get; set; }
        public string? OrganisationType { get; set; }
        public string? OwnershipType { get; set; }
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? BrandId { get; set; }
        public string? BrandName { get; set; }
        public string? RegistrationStatus { get; set; }
        public string? RegistrationDate { get; set; }
        public string? CompaniesHouseNumber { get; set; }
        public string? CharityNumber { get; set; }
        public string? Website { get; set; }
        public string? PostalAddressLine1 { get; set; }
        public string? PostalAddressLine2 { get; set; }
        public string? PostalAddressTownCity { get; set; }
        public string? PostalAddressCounty { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Uprn { get; set; }
        public double? OnspdLatitude { get; set; }
        public double? OnspdLongitude { get; set; }
        public string? MainPhoneNumber { get; set; }
        public string? InspectionDirectorate { get; set; }
        public string? Constituency { get; set; }
        public string? LocalAuthority { get; set; }
        public string? LastInspectionDate { get; set; }
        public DateTime FetchedAtUtc { get; set; }

        // ---- Conversion to and from the API shape (CqcProvider) ----
        // LocationIds is stored as a JSON string; LastInspection.Date as a single column.
        // Kept here, in the data layer, so the DTO doesn't depend on database types.
        // (Riok.Mapperly could generate these two methods at compile time; they are hand-written here.)

        // A database row for a provider fetched from CQC, stamped with when it was fetched.
        public static CqcProviderRow FromProvider(CqcProvider provider, DateTime fetchedAtUtc)
        {
            return new CqcProviderRow
            {
                ProviderId = provider.ProviderId,
                LocationIdsJson = provider.LocationIds == null ? null : JsonSerializer.Serialize(provider.LocationIds),
                OrganisationType = provider.OrganisationType,
                OwnershipType = provider.OwnershipType,
                Type = provider.Type,
                Name = provider.Name,
                BrandId = provider.BrandId,
                BrandName = provider.BrandName,
                RegistrationStatus = provider.RegistrationStatus,
                RegistrationDate = provider.RegistrationDate,
                CompaniesHouseNumber = provider.CompaniesHouseNumber,
                CharityNumber = provider.CharityNumber,
                Website = provider.Website,
                PostalAddressLine1 = provider.PostalAddressLine1,
                PostalAddressLine2 = provider.PostalAddressLine2,
                PostalAddressTownCity = provider.PostalAddressTownCity,
                PostalAddressCounty = provider.PostalAddressCounty,
                Region = provider.Region,
                PostalCode = provider.PostalCode,
                Uprn = provider.Uprn,
                OnspdLatitude = provider.OnspdLatitude,
                OnspdLongitude = provider.OnspdLongitude,
                MainPhoneNumber = provider.MainPhoneNumber,
                InspectionDirectorate = provider.InspectionDirectorate,
                Constituency = provider.Constituency,
                LocalAuthority = provider.LocalAuthority,
                LastInspectionDate = provider.LastInspection?.Date,
                FetchedAtUtc = fetchedAtUtc
            };
        }

        // This row as the provider the API returns.
        public CqcProvider ToProvider()
        {
            return new CqcProvider
            {
                ProviderId = this.ProviderId,
                LocationIds = this.LocationIdsJson == null ? null : JsonSerializer.Deserialize<List<string>>(this.LocationIdsJson),
                OrganisationType = this.OrganisationType,
                OwnershipType = this.OwnershipType,
                Type = this.Type,
                Name = this.Name,
                BrandId = this.BrandId,
                BrandName = this.BrandName,
                RegistrationStatus = this.RegistrationStatus,
                RegistrationDate = this.RegistrationDate,
                CompaniesHouseNumber = this.CompaniesHouseNumber,
                CharityNumber = this.CharityNumber,
                Website = this.Website,
                PostalAddressLine1 = this.PostalAddressLine1,
                PostalAddressLine2 = this.PostalAddressLine2,
                PostalAddressTownCity = this.PostalAddressTownCity,
                PostalAddressCounty = this.PostalAddressCounty,
                Region = this.Region,
                PostalCode = this.PostalCode,
                Uprn = this.Uprn,
                OnspdLatitude = this.OnspdLatitude,
                OnspdLongitude = this.OnspdLongitude,
                MainPhoneNumber = this.MainPhoneNumber,
                InspectionDirectorate = this.InspectionDirectorate,
                Constituency = this.Constituency,
                LocalAuthority = this.LocalAuthority,
                LastInspection = this.LastInspectionDate == null ? null : new InspectionResponse { Date = this.LastInspectionDate }
            };
        }
    }
}
