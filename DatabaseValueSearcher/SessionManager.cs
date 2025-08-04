#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace DatabaseValueSearcher
{
    public class SessionManager
    {
        private readonly CacheManager cacheManager;
        private readonly PerformanceManager performanceManager;
        private readonly DatabaseService databaseService;

        public SessionManager(
            CacheManager cacheManager,
            PerformanceManager performanceManager,
            DatabaseService databaseService)
        {
            this.cacheManager = cacheManager;
            this.performanceManager = performanceManager;
            this.databaseService = databaseService;
        }

        /// <summary>
        /// Initializes cache for a table session, loading from cache or database
        /// </summary>
        public async Task InitializeTableCache(SearchSession session)
        {
            var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);
            var cachedData = cacheManager.LoadMetadata(cacheKey);

            if (cachedData != null && cacheManager.IsCacheValid(cacheKey))
            {
                DisplayMessages.WriteSuccess($"Using cached data for {session.TableName} (cached {cachedData.CachedAt:yyyy-MM-dd HH:mm})");
                session.CachedData = cachedData;
                return;
            }

            DisplayMessages.WriteInfo($"Initializing cache for {session.TableName}...");

            // Get fresh metadata from database
            var connectionString = DatabaseService.GetFullConnectionString(session.Environment, session.Database);

            await performanceManager.ExecuteWithThrottling(async () =>
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                var metadata = new CachedTableData
                {
                    Environment = session.Environment,
                    Database = session.Database,
                    TableName = session.TableName,
                    CachedAt = DateTime.Now,
                    PageSize = int.Parse(ConfigurationManager.AppSettings["PageSize"] ?? "10000")
                };

                // Get table structure
                metadata.Columns = await databaseService.GetStringColumnsAsync(conn, session.TableName);
                metadata.PrimaryKeys = await databaseService.GetPrimaryKeyColumnsAsync(conn, session.TableName);

                // Get total row count
                var countSql = $"SELECT COUNT(*) FROM [{session.TableName}]";
                using var countCmd = new SqlCommand(countSql, conn);
                performanceManager.ConfigureCommand(countCmd);
                metadata.TotalRows = Convert.ToInt64(await countCmd.ExecuteScalarAsync());

                session.CachedData = metadata;
                cacheManager.SaveMetadata(cacheKey, metadata);

                DisplayMessages.WriteSuccess($"Table metadata cached. Total rows: {metadata.TotalRows:N0}, Columns: {metadata.Columns.Count}");
                return true;
            });
        }

        /// <summary>
        /// Refreshes the current table by clearing and reinitializing cache
        /// </summary>
        public async Task RefreshCurrentTable(SearchSession session)
        {
            DisplayMessages.WriteInfo("Refreshing table data...");
            var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);
            cacheManager.ClearCache(cacheKey);
            await InitializeTableCache(session);
            DisplayMessages.WriteSuccess("Table data refreshed.");
        }

        /// <summary>
        /// Loads a page from cache or fetches from database if not cached
        /// </summary>
        public async Task<DataPage?> LoadOrFetchPage(SearchSession session, int pageNumber)
        {
            var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);

            // Try to load from cache first
            var cachedPage = cacheManager.LoadPage(cacheKey, pageNumber);
            if (cachedPage != null)
            {
                return cachedPage;
            }

            // Fetch from database if not cached
            return await FetchPageFromDatabase(session, pageNumber);
        }

        /// <summary>
        /// Fetches a specific page from the database and caches it
        /// </summary>
        /// <summary>
        /// Fetches a specific page from the database and caches it
        /// </summary>
        public async Task<DataPage?> FetchPageFromDatabase(SearchSession session, int pageNumber)
        {
            if (session.CachedData == null) return null;

            var connectionString = DatabaseService.GetFullConnectionString(session.Environment, session.Database);
            var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);

            return await performanceManager.ExecuteWithThrottling(async () =>
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                var offset = (pageNumber - 1) * session.CachedData.PageSize;
                var allColumns = new List<string>(session.CachedData.Columns.Select(c => c.Name));
                allColumns.AddRange(session.CachedData.PrimaryKeys.Where(pk => !allColumns.Contains(pk)));

                string sql;

                // Check if we have primary keys for proper ordering
                if (session.CachedData.PrimaryKeys.Any())
                {
                    // Use OFFSET/FETCH with primary key ordering (SQL Server 2012+)
                    var orderByColumns = string.Join(", ", session.CachedData.PrimaryKeys.Select(pk => $"[{pk}]"));
                    sql = $@"
                SELECT {string.Join(", ", allColumns.Select(c => $"[{c}]"))}
                FROM [{session.TableName}]
                ORDER BY {orderByColumns}
                OFFSET {offset} ROWS
                FETCH NEXT {session.CachedData.PageSize} ROWS ONLY";
                }
                else
                {
                    // Fallback for tables without primary keys - use ROW_NUMBER() window function
                    sql = $@"
                WITH PagedData AS (
                    SELECT {string.Join(", ", allColumns.Select(c => $"[{c}]"))},
                           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) as RowNum
                    FROM [{session.TableName}]
                )
                SELECT {string.Join(", ", allColumns.Select(c => $"[{c}]"))}
                FROM PagedData
                WHERE RowNum > {offset} AND RowNum <= {offset + session.CachedData.PageSize}";
                }

                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);

                var page = new DataPage
                {
                    PageNumber = pageNumber,
                    Rows = new List<Dictionary<string, object?>>()
                };

                try
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object?>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var columnName = reader.GetName(i);
                            // Skip the RowNum column if it exists (from fallback query)
                            if (columnName == "RowNum") continue;

                            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            row[columnName] = value;
                        }
                        page.Rows.Add(row);
                    }

                    page.IsLastPage = page.Rows.Count < session.CachedData.PageSize;

                    // Cache the page
                    cacheManager.SavePage(cacheKey, pageNumber, page);

                    return page;
                }
                catch (SqlException ex)
                {
                    DisplayMessages.WriteError($"SQL Error on page {pageNumber}: {ex.Message}");

                    // Log the SQL that failed for debugging
                    DisplayMessages.WriteWarning($"Failed SQL: {sql}");

                    // Return empty page rather than crashing
                    return new DataPage
                    {
                        PageNumber = pageNumber,
                        Rows = new List<Dictionary<string, object?>>(),
                        IsLastPage = true
                    };
                }
            });
        }

        /// <summary>
        /// Performs a cached search across all pages of a table
        /// </summary>
        public async Task SearchCachedTable(SearchSession session, string searchValue, bool useRegex)
        {
            if (session.CachedData == null)
            {
                DisplayMessages.WriteError("No cached data available.");
                return;
            }

            DisplayMessages.WriteInfo($"Searching {session.TableName} with cached data...");
            DisplayMessages.WriteInfo($"Search Type: {(useRegex ? "Regular Expression" : "LIKE Pattern")}");
            DisplayMessages.WriteInfo($"Primary Keys: {string.Join(", ", session.CachedData.PrimaryKeys)}");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();
            var allMatches = new List<MatchingRecord>();
            var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);

            // Calculate total pages
            var totalPages = (int)Math.Ceiling((double)session.CachedData.TotalRows / session.CachedData.PageSize);
            var pagesProcessed = 0;
            var matchesFound = 0;

            DisplayMessages.WriteInfo($"Processing {totalPages} pages ({session.CachedData.PageSize:N0} rows per page)...");

            // Process each page
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                // Load page from cache or database
                var page = await LoadOrFetchPage(session, pageNum);
                if (page == null || !page.Rows.Any()) continue;

                // Search this page in memory using the Match class
                var pageMatches = Match.SearchPageInMemory(page, session.CachedData.Columns, session.CachedData.PrimaryKeys, searchValue, useRegex);
                allMatches.AddRange(pageMatches);
                matchesFound += pageMatches.Count;
                pagesProcessed++;

                // Show progress for large tables
                if (totalPages > 10 && pageNum % Math.Max(1, totalPages / 10) == 0)
                {
                    var progress = (double)pageNum / totalPages * 100;
                    DisplayMessages.WriteInfo($"Progress: {progress:F1}% ({pageNum}/{totalPages} pages) - {matchesFound:N0} matches found");
                }

                // Memory management
                var gcPages = int.Parse(ConfigurationManager.AppSettings["GarbageCollectAfterPages"] ?? "50");
                if (pageNum % gcPages == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            stopwatch.Stop();

            // Return results for display (Program.cs will handle the display)
            var searchResults = new SearchResults
            {
                Session = session,
                Matches = allMatches,
                SearchValue = searchValue,
                UseRegex = useRegex,
                SearchTimeMs = stopwatch.ElapsedMilliseconds,
                PagesProcessed = pagesProcessed
            };

            DisplaySearchResults(searchResults);
        }

        /// <summary>
        /// Performs a cached search and gets user input for search parameters
        /// </summary>
        public async Task PerformCachedSearch(SearchSession session, Func<string> getSearchValueFunc)
        {
            if (session.CachedData == null)
            {
                DisplayMessages.WriteError("No cached data available. Please initialize table first.");
                return;
            }

            string searchValue = getSearchValueFunc();
            if (searchValue == "quit" || searchValue == "back") return;

            bool useRegex = searchValue.StartsWith("REGEX:");
            if (useRegex) searchValue = searchValue.Substring(6);

            await SearchCachedTable(session, searchValue, useRegex);
        }

        /// <summary>
        /// Displays search results with formatted output
        /// </summary>
        private void DisplaySearchResults(SearchResults results)
        {
            var session = results.Session;
            var allMatches = results.Matches;

            DisplayMessages.WriteInfo("============================================");
            DisplayMessages.WriteInfo("SEARCH RESULTS:");
            DisplayMessages.WriteInfo("============================================");

            if (session.CachedData == null) return;

            Console.WriteLine($"  Environment: {session.Environment}");
            Console.WriteLine($"  Database: {session.Database}");
            Console.WriteLine($"  Table: {session.TableName}");
            Console.WriteLine($"  Search Value: {results.SearchValue}");
            Console.WriteLine($"  Search Type: {(results.UseRegex ? "Regular Expression" : "LIKE Pattern")}");
            Console.WriteLine($"  Pages Processed: {results.PagesProcessed:N0}");
            Console.WriteLine($"  Search Method: Cached Data");

            var columnGroups = allMatches.GroupBy(m => m.ColumnName).ToList();

            DisplayMessages.WriteColoredInline("  Columns with Matches: ", ConsoleColor.Green);
            Console.WriteLine(columnGroups.Count.ToString());

            DisplayMessages.WriteColoredInline("  Total Matches: ", ConsoleColor.Yellow);
            Console.WriteLine($"{allMatches.Count:N0}");

            Console.WriteLine($"  Search Time: {results.SearchTimeMs:N0} ms");

            if (columnGroups.Any())
            {
                Console.WriteLine();
                DisplayMessages.WriteInfo("COLUMN RESULTS:");
                DisplayMessages.WriteInfo("----------------------------------------");

                foreach (var group in columnGroups.OrderByDescending(g => g.Count()))
                {
                    DisplayMessages.WriteColoredInline("✓ Column: ", ConsoleColor.Green);
                    DisplayMessages.WriteColoredInline(group.Key, ConsoleColor.White);
                    Console.WriteLine();

                    var columnInfo = session.CachedData.Columns.FirstOrDefault(c => c.Name == group.Key);
                    if (columnInfo != null)
                    {
                        var lengthDisplay = columnInfo.MaxLength == -1 ? "MAX" :
                                          columnInfo.MaxLength == 0 ? "?" :
                                          columnInfo.MaxLength.ToString();
                        Console.WriteLine($"  Type: {columnInfo.DataType}({lengthDisplay}) {(columnInfo.IsNullable ? "NULL" : "NOT NULL")}");
                    }

                    DisplayMessages.WriteColoredInline("  Matches: ", ConsoleColor.Cyan);
                    DisplayMessages.WriteColoredInline($"{group.Count():N0}", ConsoleColor.Yellow);
                    Console.WriteLine();

                    // Show sample matches
                    DisplayMessages.WriteColoredInline("  Sample Matches:", ConsoleColor.Magenta);
                    Console.WriteLine();

                    int maxSamples = int.Parse(ConfigurationManager.AppSettings["DefaultMaxSamples"] ?? "3");
                    var samples = group.Take(maxSamples).ToList();

                    foreach (var match in samples)
                    {
                        DisplayMessages.WriteColoredInline("    • ", ConsoleColor.Gray);
                        DisplayMessages.WriteColoredInline($"{match.ColumnName}: ", ConsoleColor.Cyan);

                        var displayValue = match.MatchingValue;
                        int maxLength = int.Parse(ConfigurationManager.AppSettings["MaxDisplayLength"] ?? "50");
                        if (displayValue.Length > maxLength)
                        {
                            displayValue = displayValue.Substring(0, maxLength - 3) + "...";
                        }

                        DisplayMessages.WriteColoredInline($"'{displayValue}'", ConsoleColor.Yellow);

                        if (match.PrimaryKeyValues.Any())
                        {
                            DisplayMessages.WriteColoredInline(" | Keys: ", ConsoleColor.Magenta);
                            var keyPairs = match.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}");
                            Console.Write(string.Join(", ", keyPairs));
                        }
                        Console.WriteLine();
                    }

                    if (group.Count() > maxSamples)
                    {
                        DisplayMessages.WriteColoredInline($"    ... and {group.Count() - maxSamples:N0} more matches", ConsoleColor.Gray);
                        Console.WriteLine();
                    }
                    Console.WriteLine();
                }

                // Unique records summary
                var uniqueRecords = allMatches
                    .GroupBy(r => string.Join("|", r.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}")))
                    .ToList();

                Console.WriteLine();
                DisplayMessages.WriteInfo("UNIQUE RECORDS SUMMARY:");
                DisplayMessages.WriteInfo("----------------------------------------");

                var recordsToShow = uniqueRecords.Take(10).ToList();
                foreach (var recordGroup in recordsToShow)
                {
                    var record = recordGroup.First();
                    DisplayMessages.WriteColoredInline("  Record: ", ConsoleColor.Green);

                    if (record.PrimaryKeyValues.Any())
                    {
                        var keyPairs = record.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}");
                        Console.WriteLine(string.Join(", ", keyPairs));

                        DisplayMessages.WriteColoredInline("    Found in: ", ConsoleColor.Cyan);
                        var columns = recordGroup.Select(r => r.ColumnName).Distinct();
                        Console.WriteLine(string.Join(", ", columns));
                    }
                    else
                    {
                        Console.WriteLine("(No primary key available)");
                    }
                }

                if (uniqueRecords.Count > 10)
                {
                    DisplayMessages.WriteColoredInline($"  ... and {uniqueRecords.Count - 10:N0} more unique records", ConsoleColor.Gray);
                    Console.WriteLine();
                }

                Console.WriteLine();
                DisplayMessages.WriteColoredInline("  Total Unique Records: ", ConsoleColor.Magenta);
                Console.WriteLine($"{uniqueRecords.Count:N0}");
            }
            else
            {
                Console.WriteLine();
                DisplayMessages.WriteWarning("No matches found in any column.");
                DisplayMessages.WriteHighlight("Tips:");
                Console.WriteLine("- Try using wildcards: %search_term%");
                Console.WriteLine("- Check your search term spelling");
                Console.WriteLine("- Consider using regular expressions for complex patterns");
            }

            DisplayMessages.WriteInfo("============================================");
        }

        /// <summary>
        /// Gets session statistics and information
        /// </summary>
        public SessionStatistics GetSessionStatistics(SearchSession session)
        {
            var stats = new SessionStatistics
            {
                SessionId = session.SessionId,
                Environment = session.Environment,
                Database = session.Database,
                TableName = session.TableName,
                CreatedAt = session.CreatedAt,
                IsInitialized = session.CachedData != null
            };

            if (session.CachedData != null)
            {
                stats.TotalRows = session.CachedData.TotalRows;
                stats.ColumnCount = session.CachedData.Columns.Count;
                stats.PrimaryKeyCount = session.CachedData.PrimaryKeys.Count;
                stats.PageSize = session.CachedData.PageSize;
                stats.CachedAt = session.CachedData.CachedAt;

                var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);
                var cacheStats = cacheManager.GetCacheStatistics(cacheKey);
                stats.CachedPages = cacheStats.CachedPages;
                stats.TotalPages = (int)Math.Ceiling((double)session.CachedData.TotalRows / session.CachedData.PageSize);
                stats.CacheSize = cacheStats.TotalCacheSize;
                stats.IsComplete = cacheStats.CachedPages == stats.TotalPages;
            }

            return stats;
        }
    }

    /// <summary>
    /// Search results container
    /// </summary>
    public class SearchResults
    {
        public SearchSession Session { get; set; } = new SearchSession();
        public List<MatchingRecord> Matches { get; set; } = new List<MatchingRecord>();
        public string SearchValue { get; set; } = string.Empty;
        public bool UseRegex { get; set; }
        public long SearchTimeMs { get; set; }
        public int PagesProcessed { get; set; }
    }

    /// <summary>
    /// Session statistics and information
    /// </summary>
    public class SessionStatistics
    {
        public string SessionId { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime CachedAt { get; set; }
        public bool IsInitialized { get; set; }
        public long TotalRows { get; set; }
        public int ColumnCount { get; set; }
        public int PrimaryKeyCount { get; set; }
        public int PageSize { get; set; }
        public int CachedPages { get; set; }
        public int TotalPages { get; set; }
        public long CacheSize { get; set; }
        public bool IsComplete { get; set; }

        public string CacheSizeDisplay => CacheSize < 1024 * 1024
            ? $"{CacheSize / 1024:N0} KB"
            : $"{CacheSize / 1024 / 1024:N1} MB";

        public double CompletionPercentage => TotalPages > 0 ? (double)CachedPages / TotalPages * 100 : 0;

        public TimeSpan Age => DateTime.Now - CreatedAt;
        public TimeSpan CacheAge => DateTime.Now - CachedAt;
    }
}