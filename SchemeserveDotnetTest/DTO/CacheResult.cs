// -----------------------------------------------------------------------------
// File:     CacheResult.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Lookup result: the data plus where it came from.
// -----------------------------------------------------------------------------

using SchemeserveDotnetTest.Enums;

namespace SchemeserveDotnetTest.DTO
{
    // Returned by ProviderLookupService: the data plus where it came from.
    public class CacheResult<T>
    {
        public CacheResult(T data, DataSource source, DateTime fetchedAtUtc)
        {
            Data = data;
            Source = source;
            FetchedAtUtc = fetchedAtUtc;
        }

        public T Data { get; }
        public DataSource Source { get; }
        public DateTime FetchedAtUtc { get; }
    }
}
