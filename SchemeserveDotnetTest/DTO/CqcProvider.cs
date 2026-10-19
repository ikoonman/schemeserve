// -----------------------------------------------------------------------------
// File:     CqcProvider.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Single provider, in the shape the API returns.
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.DTO
{
    // The provider fields the connector returns (everything else CQC sends is dropped).
    public class CqcProvider
    {
        public string ProviderId { get; set; } = "";
        public List<string>? LocationIds { get; set; }
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
        public InspectionResponse? LastInspection { get; set; }
    }

    public class InspectionResponse
    {
        public string? Date { get; set; }
    }
}
