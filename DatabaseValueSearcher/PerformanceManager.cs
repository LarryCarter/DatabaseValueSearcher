#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using Microsoft.Data.SqlClient;
using System;
using System.Configuration;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class PerformanceManager
    {
        private readonly int queryDelayMs;
        private readonly int maxConcurrentConnections;
        private readonly bool useReadUncommitted;
        private readonly int commandTimeoutSeconds;
        private readonly SemaphoreSlim connectionSemaphore;
        private DateTime lastQueryTime = DateTime.MinValue;

        public PerformanceManager()
        {
            queryDelayMs = int.Parse(ConfigurationManager.AppSettings["QueryDelayMs"] ?? "100");
            maxConcurrentConnections = int.Parse(ConfigurationManager.AppSettings["MaxConcurrentConnections"] ?? "2");
            useReadUncommitted = bool.Parse(ConfigurationManager.AppSettings["UseReadUncommitted"] ?? "true");
            commandTimeoutSeconds = int.Parse(ConfigurationManager.AppSettings["CommandTimeoutSeconds"] ?? "300");
            connectionSemaphore = new SemaphoreSlim(maxConcurrentConnections, maxConcurrentConnections);
        }

        public async Task<T> ExecuteWithThrottling<T>(Func<Task<T>> operation)
        {
            await connectionSemaphore.WaitAsync();
            try
            {
                // Implement delay between queries to prevent DoS-like behavior
                var timeSinceLastQuery = DateTime.Now - lastQueryTime;
                if (timeSinceLastQuery.TotalMilliseconds < queryDelayMs)
                {
                    var delayNeeded = queryDelayMs - (int)timeSinceLastQuery.TotalMilliseconds;
                    await Task.Delay(delayNeeded);
                }

                lastQueryTime = DateTime.Now;
                return await operation();
            }
            finally
            {
                connectionSemaphore.Release();
            }
        }

        public void ConfigureConnection(SqlConnection connection)
        {
            if (useReadUncommitted)
            {
                // Use READ UNCOMMITTED to avoid locks
                using var cmd = new SqlCommand("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED", connection);
                cmd.ExecuteNonQuery();
            }
        }

        public void ConfigureCommand(SqlCommand command)
        {
            command.CommandTimeout = commandTimeoutSeconds;
        }
    }
}