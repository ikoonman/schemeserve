// -----------------------------------------------------------------------------
// File:     CqcUnavailableException.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-15
// Purpose:  Thrown when CQC can't give a usable answer.
// -----------------------------------------------------------------------------

namespace SchemeserveDotnetTest.Exceptions
{
    // Thrown by CqcRetrievalService and ProviderIndexSync when CQC can't give a usable answer:
    // it can't be reached, returns an error status, times out, or sends a body that can't be read or doesn't
    // match what was asked for. The original error is kept as the InnerException.
    // ProviderLookupService catches this to serve an expired copy (STALE); with no copy, CQCController returns 503.
    public class CqcUnavailableException : Exception
    {
        public CqcUnavailableException(string message)
            : base(message)
        {
            // Logging intentionally left out.
        }

        public CqcUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
            // Logging intentionally left out.
        }
    }
}
