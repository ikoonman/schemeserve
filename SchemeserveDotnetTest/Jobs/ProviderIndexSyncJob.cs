// -----------------------------------------------------------------------------
// File:     ProviderIndexSyncJob.cs
// Author:   Dawid Swanepoel
// Created:  2026-10-14
// Purpose:  Background job that re-syncs the providers list daily if expired.
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using SchemeserveDotnetTest.Interfaces;

namespace SchemeserveDotnetTest.Jobs
{
    // Runs in the background for the lifetime of the app: on startup and then once a day it
    // checks whether the stored providers list has expired, and re-syncs it from CQC if so.
    // Excluded from code coverage: a timer loop that only runs inside the running app; the sync it calls is unit tested.
    [ExcludeFromCodeCoverage]
    public class ProviderIndexSyncJob : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ProviderIndexSyncJob> _log;

        public ProviderIndexSyncJob(IServiceScopeFactory scopeFactory, ILogger<ProviderIndexSyncJob> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // ProviderIndexSync is a scoped service, so each run gets its own scope.
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var sync = scope.ServiceProvider.GetRequiredService<IProviderIndexSync>();
                        await sync.SyncIfExpiredAsync(stoppingToken);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Keep the job alive; the next check will try again.
                    _log.LogError(ex, "Providers list sync failed");
                }

                try
                {
                    await Task.Delay(CheckInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // App is shutting down.
                }
            }
        }
    }
}
