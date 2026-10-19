// -----------------------------------------------------------------------------
// File:     EventLogFallback.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-18
// Purpose:  Last-resort error reporting when normal logging fails.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SchemeserveDotnetTest.Logging
{
    // Used only when the normal logger (ILogger) can't be used or throws itself.
    // Writes the error straight to the Windows Application event log, under the ".NET Runtime" source.
    // That source always exists, so no admin rights are needed to register one.
    // On other operating systems, or if the event log can't be written either, the error goes to the console.
    // Excluded from code coverage: it writes to the Windows event log, which unit tests shouldn't touch.
    [ExcludeFromCodeCoverage]
    public static class EventLogFallback
    {
        private const string EventSource = ".NET Runtime";

        // The event log rejects messages longer than about 31,800 characters.
        private const int MaxMessageLength = 31000;

        public static void Write(string message, Exception ex)
        {
            var text = "SchemeserveDotnetTest: " + message + Environment.NewLine + ex;
            if (text.Length > MaxMessageLength)
                text = text.Substring(0, MaxMessageLength);

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    EventLog.WriteEntry(EventSource, text, EventLogEntryType.Error);
                    return;
                }
            }
            catch (Exception)
            {
                // The event log can't be written either: use the console below.
            }

            Console.Error.WriteLine(text);
        }
    }
}
