// -----------------------------------------------------------------------------
// File:     CQCController.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  GET v1/providers and v1/providers/{id} endpoints.
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SchemeserveDotnetTest.DTO;
using SchemeserveDotnetTest.Enums;
using SchemeserveDotnetTest.Exceptions;
using SchemeserveDotnetTest.Services;

namespace SchemeserveDotnetTest.Controllers
{
    [Route("v1/providers")]
    [ApiController]
    public class CQCController : ControllerBase
    {
        private readonly ProviderLookupService _lookup;

        // Largest page size a caller may ask for: Sources:Cqc:MaxPerPage in appsettings.json (1000, CQC's own
        // page size, if not set). The same setting is the default page size in ProviderLookupService.
        private readonly int _maxPerPage;

        // One place to build error responses: a standard "problem details" JSON body with the given status code.
        private ObjectResult ProblemResponse(int statusCode, string title, string detail)
        {
            return Problem(statusCode: statusCode, title: title, detail: detail);
        }

        public CQCController(ProviderLookupService lookup, IConfiguration config)
        {
            _lookup = lookup;
            _maxPerPage = config.GetValue("Sources:Cqc:MaxPerPage", 1000);
        }

        // No parameters: the full list. With page and/or perPage: CQC-style pages (perPage defaults to Sources:Cqc:MaxPerPage).
        // Both are optional query-string values. Because of [ApiController], ASP.NET answers 400 by itself,
        // before this method runs, when a value is not a whole number (e.g. perPage=abc) or is below 1 ([Range]).
        // The upper limit for perPage comes from configuration, so it is checked here in code: attributes like
        // [Range] only accept fixed values, not settings.
        [HttpGet]
        public async Task<IActionResult> GetProviders(
            CancellationToken ct,
            [FromQuery, Range(1, int.MaxValue)] int? page = null,
            [FromQuery, Range(1, int.MaxValue)] int? perPage = null)
        {
            if (perPage > _maxPerPage)
            {
                return ProblemResponse(StatusCodes.Status400BadRequest, "Invalid perPage",
                    "perPage may not be more than " + _maxPerPage + ".");
            }

            var result = await _lookup.GetProvidersAsync(page, perPage, ct);
            if (result == null)
            {
                // The list has never been synced and CQC can't be reached right now.
                return ProblemResponse(StatusCodes.Status503ServiceUnavailable, "CQC unavailable",
                    "The providers list has not been loaded yet and CQC can't be reached. Try again later.");
            }

            AddCacheHeaders(result.Source, result.FetchedAtUtc);
            return Ok(result.Data);
        }

        [HttpGet("{id:regex(^1-[[0-9]]+$)}")] // CQC provider ids look like 1-123456789
        public async Task<IActionResult> GetProviderById(string id, CancellationToken ct)
        {
            CacheResult<CqcProvider>? result;
            try
            {
                result = await _lookup.GetProviderByIdAsync(id, ct);
            }
            catch (CqcUnavailableException)
            {
                // CQC is down (or sent something unusable) and there is no stored copy to fall back on.
                // ProviderLookupService has already logged the details.
                return ProblemResponse(StatusCodes.Status503ServiceUnavailable, "CQC unavailable",
                    "Provider " + id + " is not stored locally and CQC can't be reached. Try again later.");
            }

            if (result == null)
            {
                //Call to API was successful, but there was an issue with the upstream
                return ProblemResponse(StatusCodes.Status404NotFound, "Provider not found", "CQC has no provider with id " + id + ".");
            }

            AddCacheHeaders(result.Source, result.FetchedAtUtc);
            return Ok(result.Data);
        }

        // Response body as per spec. Origin returned in headers
        private void AddCacheHeaders(DataSource source, DateTime fetchedAtUtc)
        {
            CacheState cacheState;
            switch (source)
            {
                case DataSource.Cache: //in db cache, non-expired
                    cacheState = CacheState.HIT;
                    break;
                case DataSource.StaleCache: //CQC off-line, expired copy in db
                    cacheState = CacheState.STALE;
                    break;
                default:
                    cacheState = CacheState.MISS; //retrieved upstream CQC and stored locally
                    break;
            }

            Response.Headers["X-Cache"] = cacheState.ToString();
            Response.Headers["X-Fetched-At"] = fetchedAtUtc.ToString("o");
        }
    }
}