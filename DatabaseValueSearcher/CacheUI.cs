#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DatabaseValueSearcher
{
    public class CacheUI
    {
        private readonly CacheManager cacheManager;
        private readonly Dictionary<string, List<string>> databaseListCache;
        private readonly Dictionary<string, List<TableViewInfo>> tableListCache;
        private readonly Dictionary<string, DateTime> cacheTimestamps;

        public CacheUI(
            CacheManager cacheManager,
            Dictionary<string, List<string>> databaseListCache,
            Dictionary<string, List<TableViewInfo>> tableListCache,
            Dictionary<string, DateTime> cacheTimestamps)
        {
            this.cacheManager = cacheManager;
            this.databaseListCache = databaseListCache;
            this.tableListCache = tableListCache;
            this.cacheTimestamps = cacheTimestamps;
        }

        /// <summary>
        /// Main cache management menu
        /// </summary>
        public async Task HandleCacheCommands(SearchSession? currentSession)
        {
            Console.WriteLine();
            DisplayMessages.WriteInfo("Cache Management Options:");
            Console.WriteLine("  1. Show cache status");
            Console.WriteLine("  2. Show all cached tables");
            Console.WriteLine("  3. Validate cache integrity");
            Console.WriteLine("  4. Clear all cache");
            Console.WriteLine("  5. Clear current table cache");
            Console.WriteLine("  6. Clear specific table cache");
            Console.WriteLine("  7. Export cached table to SQL");
            Console.WriteLine("  8. Cache statistics");
            Console.WriteLine();

            Console.Write("Select option (1-8) [back]: ");
            string input = Console.ReadLine()?.Trim() ?? "";

            switch (input)
            {
                case "1":
                    ShowCacheStatus();
                    break;
                case "2":
                    ShowAllCachedTables();
                    break;
                case "3":
                    await ValidateCacheIntegrity(currentSession);
                    break;
                case "4":
                    await ClearAllCache(currentSession);
                    break;
                case "5":
                    ClearCurrentTableCache(currentSession);
                    break;
                case "6":
                    await ClearSpecificTableCache(currentSession);
                    break;
                case "7":
                    DisplayMessages.WriteInfo("Export functionality handled by ExportService - use 'export' command in main menu.");
                    break;
                case "8":
                    ShowDetailedCacheStatistics();
                    break;
                case "back":
                case "":
                    break;
                default:
                    DisplayMessages.WriteError("Invalid option.");
                    break;
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Shows basic cache status information
        /// </summary>
        public void ShowCacheStatus()
        {
            bool enableCaching = bool.Parse(ConfigurationManager.AppSettings["EnableCaching"] ?? "true");
            if (!enableCaching)
            {
                DisplayMessages.WriteWarning("Caching is disabled.");
                return;
            }

            var cacheSize = cacheManager.GetCacheSize();
            DisplayMessages.WriteInfo($"Cache Status: Enabled | Size: {cacheSize / 1024 / 1024:F1} MB");
        }

        /// <summary>
        /// Shows all cached tables grouped by environment and database
        /// </summary>
        public void ShowAllCachedTables()
        {
            var cachedTables = cacheManager.GetAllCachedTables();

            if (!cachedTables.Any())
            {
                DisplayMessages.WriteWarning("No cached tables found.");
                return;
            }

            Console.WriteLine();
            DisplayMessages.WriteInfo("ALL CACHED TABLES:");
            DisplayMessages.WriteInfo("================================================================");

            var totalSize = cachedTables.Sum(t => t.TotalCacheSize);
            Console.WriteLine($"Total cached tables: {cachedTables.Count}");
            Console.WriteLine($"Total cache size: {(totalSize / 1024 / 1024):F1} MB");
            Console.WriteLine();

            var grouped = cachedTables.GroupBy(t => $"{t.Environment}.{t.Database}");

            foreach (var group in grouped)
            {
                DisplayMessages.WriteColoredInline($"{group.Key}:", ConsoleColor.Cyan);
                Console.WriteLine();

                foreach (var table in group)
                {
                    string status = "";
                    if (table.CachedAt != DateTime.MinValue)
                    {
                        var age = DateTime.Now - table.CachedAt;
                        if (age.TotalHours < 1)
                            status = $"({age.TotalMinutes:F0}m old)";
                        else if (age.TotalDays < 1)
                            status = $"({age.TotalHours:F0}h old)";
                        else
                            status = $"({age.TotalDays:F0}d old)";
                    }

                    var completeness = table.IsComplete ? "Complete" : "Partial";

                    Console.Write("  ");
                    DisplayMessages.WriteColoredInline($"[{table.TableName}]", ConsoleColor.Green);
                    Console.WriteLine($" {table.TotalRows:N0} rows, {table.CachedPages} pages, {table.SizeDisplay} {status} - {completeness}");
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Validates cache integrity for selected tables
        /// </summary>
        public async Task ValidateCacheIntegrity(SearchSession? currentSession)
        {
            Console.WriteLine();

            if (currentSession != null)
            {
                Console.Write("Validate current table cache? [Y/n]: ");
                string response = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(response) || response.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    var cacheKey = cacheManager.GetCacheKey(currentSession.Environment, currentSession.Database, currentSession.TableName);
                    await ValidateSpecificCache(cacheKey, $"{currentSession.Environment}.{currentSession.Database}.{currentSession.TableName}");
                    return;
                }
            }

            // Show all cached tables for validation
            var cachedTables = cacheManager.GetAllCachedTables();

            if (!cachedTables.Any())
            {
                DisplayMessages.WriteWarning("No cached tables found to validate.");
                return;
            }

            Console.WriteLine("Select table to validate:");
            for (int i = 0; i < cachedTables.Count; i++)
            {
                var table = cachedTables[i];
                Console.WriteLine($"  {i + 1}. {table.Environment}.{table.Database}.{table.TableName}");
            }

            Console.Write($"Select table (1-{cachedTables.Count}) [back]: ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (input.Equals("back", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input))
                return;

            if (int.TryParse(input, out int selection) && selection >= 1 && selection <= cachedTables.Count)
            {
                var selectedTable = cachedTables[selection - 1];
                await ValidateSpecificCache(selectedTable.CacheKey, $"{selectedTable.Environment}.{selectedTable.Database}.{selectedTable.TableName}");
            }
            else
            {
                DisplayMessages.WriteError("Invalid selection.");
            }
        }

        /// <summary>
        /// Validates a specific cache and displays results
        /// </summary>
        public async Task ValidateSpecificCache(string cacheKey, string displayName)
        {
            DisplayMessages.WriteInfo($"Validating cache for {displayName}...");

            var result = cacheManager.ValidateCache(cacheKey);

            Console.WriteLine();
            DisplayMessages.WriteInfo("CACHE VALIDATION RESULTS:");
            DisplayMessages.WriteInfo("----------------------------------------");
            Console.WriteLine($"Table: {displayName}");
            Console.WriteLine($"Cache Key: {cacheKey}");

            if (result.IsValid)
            {
                DisplayMessages.WriteColoredInline("Status: ", ConsoleColor.White);
                DisplayMessages.WriteColoredInline("VALID", ConsoleColor.Green);
                Console.WriteLine();
            }
            else
            {
                DisplayMessages.WriteColoredInline("Status: ", ConsoleColor.White);
                DisplayMessages.WriteColoredInline("INVALID", ConsoleColor.Red);
                Console.WriteLine();
            }

            if (result.ExpectedPages > 0)
            {
                Console.WriteLine($"Expected Pages: {result.ExpectedPages}");
                Console.WriteLine($"Actual Pages: {result.ActualPages}");

                if (result.ActualPages == result.ExpectedPages)
                {
                    DisplayMessages.WriteColoredInline("Page Coverage: ", ConsoleColor.White);
                    DisplayMessages.WriteColoredInline("100%", ConsoleColor.Green);
                    Console.WriteLine();
                }
                else
                {
                    var coverage = result.ExpectedPages > 0 ? (double)result.ActualPages / result.ExpectedPages * 100 : 0;
                    DisplayMessages.WriteColoredInline("Page Coverage: ", ConsoleColor.White);
                    DisplayMessages.WriteColoredInline($"{coverage:F1}%", coverage > 90 ? ConsoleColor.Yellow : ConsoleColor.Red);
                    Console.WriteLine();
                }
            }

            Console.WriteLine();
            DisplayMessages.WriteInfo("Issues Found:");
            foreach (var issue in result.Issues)
            {
                if (issue.Contains("passed"))
                {
                    DisplayMessages.WriteColoredInline("  ✓ ", ConsoleColor.Green);
                    Console.WriteLine(issue);
                }
                else
                {
                    DisplayMessages.WriteColoredInline("  ✗ ", ConsoleColor.Red);
                    Console.WriteLine(issue);
                }
            }
        }

        /// <summary>
        /// Clears all cache with user confirmation
        /// </summary>
        public async Task ClearAllCache(SearchSession? currentSession)
        {
            Console.WriteLine();
            DisplayMessages.WriteWarning("This will clear ALL cached data for ALL tables.");
            Console.Write("Are you sure? [y/N]: ");
            string response = Console.ReadLine()?.Trim() ?? "";

            if (response.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                cacheManager.ClearCache();

                // Also clear our in-memory caches
                databaseListCache.Clear();
                tableListCache.Clear();
                cacheTimestamps.Clear();

                DisplayMessages.WriteSuccess("All cache cleared successfully.");

                // Clear current session cache reference
                if (currentSession != null)
                {
                    currentSession.CachedData = null;
                }
            }
            else
            {
                DisplayMessages.WriteInfo("Cache clear cancelled.");
            }
        }

        /// <summary>
        /// Clears cache for the current table session
        /// </summary>
        public void ClearCurrentTableCache(SearchSession? currentSession)
        {
            if (currentSession != null)
            {
                var cacheKey = cacheManager.GetCacheKey(currentSession.Environment, currentSession.Database, currentSession.TableName);
                cacheManager.ClearCache(cacheKey);
                currentSession.CachedData = null;
                DisplayMessages.WriteSuccess($"Cache cleared for {currentSession.TableName}.");
            }
            else
            {
                DisplayMessages.WriteWarning("No active session to clear cache for.");
            }
        }

        /// <summary>
        /// Clears cache for a specific table selected by user
        /// </summary>
        public async Task ClearSpecificTableCache(SearchSession? currentSession)
        {
            var cachedTables = cacheManager.GetAllCachedTables();

            if (!cachedTables.Any())
            {
                DisplayMessages.WriteWarning("No cached tables found.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Select table cache to clear:");
            for (int i = 0; i < cachedTables.Count; i++)
            {
                var table = cachedTables[i];
                Console.WriteLine($"  {i + 1}. {table.Environment}.{table.Database}.{table.TableName} ({table.SizeDisplay})");
            }

            Console.Write($"Select table (1-{cachedTables.Count}) [back]: ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (input.Equals("back", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input))
                return;

            if (int.TryParse(input, out int selection) && selection >= 1 && selection <= cachedTables.Count)
            {
                var selectedTable = cachedTables[selection - 1];

                Console.WriteLine();
                DisplayMessages.WriteWarning($"This will clear cache for {selectedTable.Environment}.{selectedTable.Database}.{selectedTable.TableName}");
                Console.Write("Are you sure? [y/N]: ");
                string response = Console.ReadLine()?.Trim() ?? "";

                if (response.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    cacheManager.ClearCache(selectedTable.CacheKey);
                    DisplayMessages.WriteSuccess($"Cache cleared for {selectedTable.TableName}.");

                    // Clear current session if it matches
                    if (currentSession != null &&
                        currentSession.Environment == selectedTable.Environment &&
                        currentSession.Database == selectedTable.Database &&
                        currentSession.TableName == selectedTable.TableName)
                    {
                        currentSession.CachedData = null;
                    }
                }
                else
                {
                    DisplayMessages.WriteInfo("Cache clear cancelled.");
                }
            }
            else
            {
                DisplayMessages.WriteError("Invalid selection.");
            }
        }

        /// <summary>
        /// Shows detailed cache statistics and metrics
        /// </summary>
        public void ShowDetailedCacheStatistics()
        {
            var cacheDirectory = ConfigurationManager.AppSettings["CacheDirectory"] ?? "./Cache";
            if (!Directory.Exists(cacheDirectory))
            {
                DisplayMessages.WriteWarning("No cache directory found.");
                return;
            }

            var files = Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => new FileInfo(f).Length);
            var metadataFiles = files.Count(f => f.Contains("_metadata.json"));
            var pageFiles = files.Count(f => f.Contains("_page_"));
            var compressedFiles = files.Count(f => f.EndsWith(".gz"));

            var cachedTables = cacheManager.GetAllCachedTables();
            var completelyCache = cachedTables.Count(t => t.IsComplete);
            var totalRows = cachedTables.Sum(t => t.TotalRows);

            Console.WriteLine();
            DisplayMessages.WriteInfo("DETAILED CACHE STATISTICS:");
            DisplayMessages.WriteInfo("================================================================");
            Console.WriteLine($"Cache Directory: {cacheDirectory}");
            Console.WriteLine($"Total Files: {files.Length:N0}");
            Console.WriteLine($"  - Metadata Files: {metadataFiles:N0}");
            Console.WriteLine($"  - Data Page Files: {pageFiles:N0}");
            Console.WriteLine($"  - Compressed Files: {compressedFiles:N0}");
            Console.WriteLine($"Total Size: {totalSize / 1024 / 1024:F2} MB");
            Console.WriteLine();
            Console.WriteLine($"Cached Tables: {cachedTables.Count:N0}");
            Console.WriteLine($"  - Completely Cached: {completelyCache:N0}");
            Console.WriteLine($"  - Partially Cached: {cachedTables.Count - completelyCache:N0}");
            Console.WriteLine($"Total Cached Rows: {totalRows:N0}");

            if (cachedTables.Any())
            {
                var oldestCache = cachedTables.Where(t => t.CachedAt != DateTime.MinValue).OrderBy(t => t.CachedAt).FirstOrDefault();
                var newestCache = cachedTables.Where(t => t.CachedAt != DateTime.MinValue).OrderByDescending(t => t.CachedAt).FirstOrDefault();

                if (oldestCache != null)
                {
                    Console.WriteLine($"Oldest Cache: {oldestCache.TableName} ({oldestCache.CachedAt:yyyy-MM-dd HH:mm})");
                }
                if (newestCache != null)
                {
                    Console.WriteLine($"Newest Cache: {newestCache.TableName} ({newestCache.CachedAt:yyyy-MM-dd HH:mm})");
                }
            }

            Console.WriteLine();

            // Show cache efficiency
            if (pageFiles > 0)
            {
                var avgPageSize = totalSize / pageFiles;
                Console.WriteLine($"Average Page Size: {avgPageSize / 1024:F1} KB");

                var compressionRatio = compressedFiles > 0 ? (double)compressedFiles / pageFiles * 100 : 0;
                Console.WriteLine($"Compression Ratio: {compressionRatio:F1}%");
            }
        }
    }
}