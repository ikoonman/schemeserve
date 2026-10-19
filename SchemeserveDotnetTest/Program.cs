// -----------------------------------------------------------------------------
// File:     Program.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Entry point: registers services, creates the database, starts the API.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using SchemeserveDotnetTest.Data;
using SchemeserveDotnetTest.Factories;
using SchemeserveDotnetTest.Interfaces;
using SchemeserveDotnetTest.Jobs;
using SchemeserveDotnetTest.Logging;
using SchemeserveDotnetTest.Services;

namespace SchemeserveDotnetTest
{
    // Entry point of the API: registers the services, creates the database and tables,
    // then starts the web server.
    // Excluded from code coverage: it needs the running app, so unit tests can't reach it.
    [ExcludeFromCodeCoverage]
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Created inside the try below; still null if startup fails before the app is built.
            WebApplication? app = null;

            // Global catch-all: any error that stops the API (during startup or while running) and isn't
            // handled elsewhere is logged here before the process exits. Errors inside requests are handled
            // separately by UseExceptionHandler below, and the background job catches its own errors.
            try
            {
                var builder = WebApplication.CreateBuilder(args);

                // Add services to the container.
                builder.Services.AddControllers();

                // Any exception nothing else catches becomes a 500 with a standard "problem details" JSON body
                // (instead of an empty response or a stack trace). The error is still logged.
                builder.Services.AddProblemDetails();
                builder.Services.AddOpenApi();

                builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
                builder.Services.AddScoped<IProviderRepository, ProviderRepository>();
                builder.Services.AddScoped<ProviderLookupService>();
                builder.Services.AddScoped<IProviderIndexSync, ProviderIndexSync>();

                // Background job that re-syncs the full providers list from CQC when it expires.
                builder.Services.AddHostedService<ProviderIndexSyncJob>();

                // Typed HttpClient for the CQC API: base address and subscription key come from appsettings.json.
                // Settings are grouped per external source under "Sources" (only CQC for now), so another
                // provider could be added later with its own section.
                // Registered against ICqcRetrievalService so the service can be swapped for a mock in tests.
                builder.Services.AddHttpClient<ICqcRetrievalService, CqcRetrievalService>(client =>
                {
                    var cqc = builder.Configuration.GetSection("Sources:Cqc");
                    client.BaseAddress = new Uri(cqc["BaseUrl"]!);
                    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", cqc["SubscriptionKey"]);
                });

                app = builder.Build();

                // Create the database and tables if they don't exist yet.
                var connectionString = builder.Configuration.GetConnectionString("CqcCache")!;

                try
                {
                    await SchemaBootstrapper.EnsureDatabaseAsync(connectionString);
                    await SchemaBootstrapper.EnsureTableAsync<CqcProviderRow>(connectionString, "CqcProviders", "ProviderId");
                    await SchemaBootstrapper.EnsureTableAsync<CqcProviderIndexRow>(connectionString, "CqcProviderIndex", "ProviderId");
                    await SchemaBootstrapper.EnsureTableAsync<CqcSyncStateRow>(connectionString, "CqcSyncState", "Name");
                }
                catch (SqlException ex)
                {
                    // The API can't work without its database, so stop here with a clear hint instead of a raw stack trace.
                    app.Logger.LogCritical(ex,
                        "Could not create the database. Check that SQL Server LocalDB is installed and running " +
                        "(run 'sqllocaldb info' in a terminal) and that ConnectionStrings:CqcCache in appsettings.json is correct.");
                    Environment.ExitCode = 1;
                    return;
                }

                // Configure the HTTP request pipeline.
                // Turns unhandled exceptions into the 500 problem details response registered above.
                app.UseExceptionHandler();

                if (app.Environment.IsDevelopment())
                {
                    app.MapOpenApi();
                }

                app.UseHttpsRedirection();
                app.UseAuthorization();
                app.MapControllers();

                app.Run();
            }
            catch (Exception ex)
            {
                ReportFatalError(app, ex);
                Environment.ExitCode = 1;
            }
        }

        // Logs an error that stopped the API. Uses the app's logger when there is one (console, and on Windows
        // the event log too). If the app wasn't built yet, or logging itself fails, it writes straight to the
        // Windows event log instead (see EventLogFallback).
        private static void ReportFatalError(WebApplication? app, Exception ex)
        {
            if (app != null)
            {
                try
                {
                    app.Logger.LogCritical(ex, "The API stopped because of an unexpected error");
                    return;
                }
                catch (Exception)
                {
                    // Logging failed: use the event log below.
                }
            }

            EventLogFallback.Write("The API stopped because of an unexpected error", ex);
        }
    }
}
