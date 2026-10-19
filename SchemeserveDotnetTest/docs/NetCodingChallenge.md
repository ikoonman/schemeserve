# Backend Tech Test

This is similar to scenarios that we sometimes use within SchemeServe 

Within SchemeServe we often build connector services between the core application and external API's. We need an internal API creating that will form one of these connectors 

## The Connector API

The external API we will be integrating with will be CQC provider API. It's documentation can be found here: [https://api-portal.service.cqc.org.uk/api-details#api=syndication&operation=get-changes-within-timeframe](https://api-portal.service.cqc.org.uk/api-details#api=syndication&operation=get-changes-within-timeframe)

Authentication is required to access this API but is not the focus of this test. So to authenticate please simply include this key value pair in the headers: `Ocp-Apim-Subscription-Key` with value `65907e17e06440f6b212ded670f54cbb`

We need an internal API (the Connector) to lookup providers and individual providers:
- GET: `https://api.service.cqc.org.uk/public/v1/providers`
- GET: `https://api.service.cqc.org.uk/public/v1/providers/{id}`

Behaviour requirements
- records are to be stored to avoid excessive external API calls
   - records should have a timeout of one month from being stored
   - at minimum: the individual requested records are to be individually stored
- when a request is made it should first check if the record exists in the store
  - if the record exists in the store then the record is returned
  - if it is not found, then the external API is called
- when the external Api is called; if a matching record is discovered in the external API, it should be stored and then returned

The Connector will need to return the below values (everything else that the external API returns can be dropped):
``` json
{
"providerId": "1-345678912", 
"locationIds": [ 
 "1-987654321", 
 "1-876543212" 
], 
"organisationType": "Provider", 
"ownershipType": "NHS Body",  
"type": "NHS Healthcare Organisation", 
"name": "Sample Teaching Hospitals NHS Foundation Trust",  
"brandId": "ABC123", 
"brandName": "Sample Hill",  
"registrationStatus": "Registered", 
"registrationDate": "2012-04-01", 
"companiesHouseNumber": "12345678",  
"charityNumber": "123456", 
"website": "www.samplehospitals.nhs.uk", 
"postalAddressLine1": "Trust Headquarters, Example Hospital", 
"postalAddressLine2": "Example Road",
"postalAddressTownCity": "Blackpool", 
"postalAddressCounty": "Lancashire", 
"region": "North West", 
"postalCode": "FY3 8RN", 
"uprn": "123456789012", 
"onspdLatitude": 53.123456, 
"onspdLongitude": -2.123456, 
"mainPhoneNumber": "01253301234", 
"inspectionDirectorate": "Hospitals", 
"constituency": "Blackpool North and Cleveleys", 
"localAuthority": "Blackpool", 
"lastInspection": { 
 "date": "2021-11-01" 
},
```

# Test Constraints

1. The design on the API, database is completely up to you.
2. The API must be created in .net 10 (or higher).
3. Where possible unit tests should be written to cover functionality, we currently use XUnit however you can use anything you like.
4. We currently use the dapper .net database object mapper, however you can use anything you like.
5. Please don't spend any longer than 2 hours on the test. What you create in these two hours is what we would like to review.
6. Finally, please share the application back with a link to a GitHub repo to allow us to easily look through it.