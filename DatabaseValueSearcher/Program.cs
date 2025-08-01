#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;

namespace DatabaseValueSearcher
{

    partial class Program
    {
        private static Dictionary<string, string> environments = new Dictionary<string, string>();
        private static CacheManager cacheManager = new CacheManager();
        private static PerformanceManager performanceManager = new PerformanceManager();
        private static SearchSession? currentSession;

        static async Task Main(string[] args)
        {
            try
            {
                // Enable console colors
                if (!Console.IsOutputRedirected)
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                }

                LoadEnvironments();

                if (args.Length == 0)
                {
                    await RunInteractiveMode();
                }
                else
                {
                    await RunCommandLineMode(args);
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"FATAL ERROR: {ex.Message}");
                if (ex.InnerException != null)
                {
                    DisplayMessages.WriteError($"Details: {ex.InnerException.Message}");
                }
                DisplayMessages.WriteWarning("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }
        }

        

        static void LoadEnvironments()
        {
            try
            {
                var envSection = ConfigurationManager.GetSection("databaseEnvironments") as System.Collections.Specialized.NameValueCollection;
                if (envSection != null)
                {
                    foreach (string key in envSection.AllKeys)
                    {
                        if (key != null)
                        {
                            environments[key] = envSection[key] ?? key;
                        }
                    }
                }

                // Fallback to connection strings if no environment section
                if (environments.Count == 0)
                {
                    foreach (ConnectionStringSettings connStr in ConfigurationManager.ConnectionStrings)
                    {
                        if (connStr.Name != "LocalSqlServer") // Skip default .NET connection
                        {
                            environments[connStr.Name] = connStr.Name;
                        }
                    }
                }

                if (environments.Count == 0)
                {
                    DisplayMessages.WriteWarning("No environments configured. Please check your App.config file.");
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"Error loading environments: {ex.Message}");
            }
        }

        static async Task RunInteractiveMode()
        {
            DisplayMessages.WriteInfo("============================================");
            DisplayMessages.WriteInfo("DATABASE VALUE SEARCHER - INTERACTIVE MODE");
            DisplayMessages.WriteInfo("============================================");
            Console.WriteLine();
            DisplayMessages.WriteHighlight("Navigation: Type 'back' to go back, 'quit' to exit, 'cache' for cache options");
            ShowCacheStatus();
            Console.WriteLine();

            while (true) // Main loop to allow complete restart
            {
                // Check if we have an active session
                if (currentSession != null)
                {
                    DisplayMessages.WriteInfo($"Active Session: {currentSession.Environment} > {currentSession.Database} > {currentSession.TableName}");
                    DisplayMessages.WriteHighlight("Options: 'search' for new search, 'refresh' to reload data, 'clear' to start over");
                    Console.WriteLine();

                    Console.Write("Choose action [search/refresh/clear/cache/quit]: ");
                    string action = Console.ReadLine()?.Trim() ?? "";

                    switch (action.ToLower())
                    {
                        case "search":
                            await PerformCachedSearch();
                            continue;
                        case "refresh":
                            await RefreshCurrentTable();
                            continue;
                        case "clear":
                            currentSession = null;
                            break;
                        case "cache":
                            await HandleCacheCommands();
                            continue;
                        case "quit":
                            return;
                        default:
                            DisplayMessages.WriteError("Invalid option. Please try again.");
                            continue;
                    }
                }

                // New session flow
                string environment = "";
                string database = "";
                string tableOrView = "";

                // Step 1: Select Environment
                while (string.IsNullOrEmpty(environment))
                {
                    environment = SelectEnvironment();
                    if (environment == "quit") return;
                    if (environment == "cache")
                    {
                        await HandleCacheCommands();
                        environment = "";
                        continue;
                    }
                }

                // Step 2: Select Database
                while (string.IsNullOrEmpty(database))
                {
                    database = await SelectDatabase(environment);
                    if (database == "quit") return;
                    if (database == "back")
                    {
                        environment = "";
                        break;
                    }
                    if (database == "cache")
                    {
                        await HandleCacheCommands();
                        continue;
                    }
                }
                if (string.IsNullOrEmpty(environment)) continue;

                // Step 3: Select Table or View
                while (string.IsNullOrEmpty(tableOrView))
                {
                    tableOrView = await SelectTableOrView(environment, database);
                    if (tableOrView == "quit") return;
                    if (tableOrView == "back")
                    {
                        database = "";
                        break;
                    }
                    if (tableOrView == "cache")
                    {
                        await HandleCacheCommands();
                        continue;
                    }
                }
                if (string.IsNullOrEmpty(database)) continue;

                // Create new session
                currentSession = new SearchSession
                {
                    Environment = environment,
                    Database = database,
                    TableName = tableOrView
                };

                // Step 4: Initialize cache for this table
                await InitializeTableCache(currentSession);

                // Step 5: Perform first search
                await PerformCachedSearch();
            }
        }

        static async Task RunCommandLineMode(string[] args)
        {
            if (args.Length < 4)
            {
                DisplayMessages.ShowUsageDisplay();
                return;
            }

            string environment = args[0];
            string database = args[1];
            string tableName = args[2];
            string searchValue = args[3];
            bool useRegex = args.Length > 4 && args[4].Equals("--regex", StringComparison.OrdinalIgnoreCase);

            DisplayMessages.WriteInfo("============================================");
            DisplayMessages.WriteInfo("DATABASE VALUE SEARCHER - COMMAND LINE MODE");
            DisplayMessages.WriteInfo("============================================");
            Console.WriteLine($"Environment: {environment}");
            Console.WriteLine($"Database: {database}");
            Console.WriteLine($"Table: {tableName}");
            Console.WriteLine($"Search Value: {searchValue}");
            if (useRegex) DisplayMessages.WriteHighlight("Search Type: Regular Expression");
            DisplayMessages.WriteInfo("============================================");
            Console.WriteLine();

            currentSession = new SearchSession
            {
                Environment = environment,
                Database = database,
                TableName = tableName
            };

            await InitializeTableCache(currentSession);
            await SearchCachedTable(currentSession, searchValue, useRegex);
        }

        static async Task HandleCacheCommands()
        {
            Console.WriteLine();
            DisplayMessages.WriteInfo("Cache Management Options:");
            Console.WriteLine("  1. Show cache status");
            Console.WriteLine("  2. Clear all cache");
            Console.WriteLine("  3. Clear current table cache");
            Console.WriteLine("  4. Cache statistics");
            Console.WriteLine();

            Console.Write("Select option (1-4) [back]: ");
            string input = Console.ReadLine()?.Trim() ?? "";

            switch (input)
            {
                case "1":
                    ShowCacheStatus();
                    break;
                case "2":
                    cacheManager.ClearCache();
                    DisplayMessages.WriteSuccess("All cache cleared.");
                    break;
                case "3":
                    if (currentSession != null)
                    {
                        var cacheKey = cacheManager.GetCacheKey(currentSession.Environment, currentSession.Database, currentSession.TableName);
                        cacheManager.ClearCache(cacheKey);
                        DisplayMessages.WriteSuccess($"Cache cleared for {currentSession.TableName}.");
                    }
                    else
                    {
                        DisplayMessages.WriteWarning("No active session to clear cache for.");
                    }
                    break;
                case "4":
                    ShowCacheStatistics();
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

        static void ShowCacheStatus()
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

        static void ShowCacheStatistics()
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

            Console.WriteLine();
            DisplayMessages.WriteInfo("CACHE STATISTICS:");
            Console.WriteLine($"  Total Files: {files.Length}");
            Console.WriteLine($"  Metadata Files: {metadataFiles}");
            Console.WriteLine($"  Data Page Files: {pageFiles}");
            Console.WriteLine($"  Total Size: {totalSize / 1024 / 1024:F2} MB");
            Console.WriteLine($"  Cache Directory: {cacheDirectory}");
        }

        static async Task InitializeTableCache(SearchSession session)
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
            var connectionString = GetFullConnectionString(session.Environment, session.Database);

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
                metadata.Columns = await GetStringColumnsAsync(conn, session.TableName);
                metadata.PrimaryKeys = await GetPrimaryKeyColumnsAsync(conn, session.TableName);

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

        static async Task RefreshCurrentTable()
        {
            if (currentSession == null) return;

            DisplayMessages.WriteInfo("Refreshing table data...");
            var cacheKey = cacheManager.GetCacheKey(currentSession.Environment, currentSession.Database, currentSession.TableName);
            cacheManager.ClearCache(cacheKey);
            await InitializeTableCache(currentSession);
            DisplayMessages.WriteSuccess("Table data refreshed.");
        }

        static async Task PerformCachedSearch()
        {
            if (currentSession?.CachedData == null)
            {
                DisplayMessages.WriteError("No cached data available. Please initialize table first.");
                return;
            }

            string searchValue = GetSearchValue();
            if (searchValue == "quit" || searchValue == "back") return;

            bool useRegex = searchValue.StartsWith("REGEX:");
            if (useRegex) searchValue = searchValue.Substring(6);

            await SearchCachedTable(currentSession, searchValue, useRegex);
        }

        static async Task SearchCachedTable(SearchSession session, string searchValue, bool useRegex)
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

                // Search this page in memory
                var pageMatches = SearchPageInMemory(page, session.CachedData.Columns, session.CachedData.PrimaryKeys, searchValue, useRegex);
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

            // Display results
            DisplaySearchResults(session, allMatches, searchValue, useRegex, stopwatch.ElapsedMilliseconds, pagesProcessed);
        }

        static async Task<DataPage?> LoadOrFetchPage(SearchSession session, int pageNumber)
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

        static async Task<DataPage?> FetchPageFromDatabase(SearchSession session, int pageNumber)
        {
            if (session.CachedData == null) return null;

            var connectionString = GetFullConnectionString(session.Environment, session.Database);
            var cacheKey = cacheManager.GetCacheKey(session.Environment, session.Database, session.TableName);

            return await performanceManager.ExecuteWithThrottling(async () =>
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                var offset = (pageNumber - 1) * session.CachedData.PageSize;
                var allColumns = new List<string>(session.CachedData.Columns.Select(c => c.Name));
                allColumns.AddRange(session.CachedData.PrimaryKeys.Where(pk => !allColumns.Contains(pk)));

                var sql = $@"
                    SELECT {string.Join(", ", allColumns.Select(c => $"[{c}]"))}
                    FROM [{session.TableName}]
                    ORDER BY {string.Join(", ", session.CachedData.PrimaryKeys.Select(pk => $"[{pk}]"))}
                    OFFSET {offset} ROWS
                    FETCH NEXT {session.CachedData.PageSize} ROWS ONLY";

                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);

                var page = new DataPage
                {
                    PageNumber = pageNumber,
                    Rows = new List<Dictionary<string, object?>>()
                };

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[columnName] = value;
                    }
                    page.Rows.Add(row);
                }

                page.IsLastPage = page.Rows.Count < session.CachedData.PageSize;

                // Cache the page
                cacheManager.SavePage(cacheKey, pageNumber, page);

                return page;
            });
        }

        static List<MatchingRecord> SearchPageInMemory(DataPage page, List<ColumnInfo> columns, List<string> primaryKeys, string searchValue, bool useRegex)
        {
            var matches = new List<MatchingRecord>();
            Regex? regex = null;

            if (useRegex)
            {
                try
                {
                    regex = new Regex(searchValue, RegexOptions.IgnoreCase);
                }
                catch
                {
                    return matches; // Invalid regex
                }
            }

            foreach (var row in page.Rows)
            {
                foreach (var column in columns)
                {
                    if (!row.TryGetValue(column.Name, out var value) || value == null) continue;

                    var stringValue = value.ToString() ?? "";
                    bool isMatch = false;

                    if (useRegex && regex != null)
                    {
                        isMatch = regex.IsMatch(stringValue);
                    }
                    else
                    {
                        // SQL LIKE behavior
                        isMatch = IsLikeMatch(stringValue, searchValue);
                    }

                    if (isMatch)
                    {
                        var match = new MatchingRecord
                        {
                            ColumnName = column.Name,
                            MatchingValue = stringValue
                        };

                        // Get primary key values
                        foreach (var pkColumn in primaryKeys)
                        {
                            if (row.TryGetValue(pkColumn, out var pkValue))
                            {
                                match.PrimaryKeyValues[pkColumn] = pkValue?.ToString() ?? "<NULL>";
                            }
                        }

                        matches.Add(match);
                    }
                }
            }

            return matches;
        }

        static bool IsLikeMatch(string text, string pattern)
        {
            // Convert SQL LIKE pattern to regex
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\%", ".*")
                .Replace("\\_", ".") + "$";

            try
            {
                return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        static void DisplaySearchResults(SearchSession session, List<MatchingRecord> allMatches, string searchValue, bool useRegex, long searchTimeMs, int pagesProcessed)
        {
            DisplayMessages.WriteInfo("============================================");
            DisplayMessages.WriteInfo("SEARCH RESULTS:");
            DisplayMessages.WriteInfo("============================================");

            if (session.CachedData == null) return;

            Console.WriteLine($"  Environment: {environments.GetValueOrDefault(session.Environment, session.Environment)}");
            Console.WriteLine($"  Database: {session.Database}");
            Console.WriteLine($"  Table: {session.TableName}");
            Console.WriteLine($"  Search Value: {searchValue}");
            Console.WriteLine($"  Search Type: {(useRegex ? "Regular Expression" : "LIKE Pattern")}");
            Console.WriteLine($"  Pages Processed: {pagesProcessed:N0}");
            Console.WriteLine($"  Search Method: Cached Data");

            var columnGroups = allMatches.GroupBy(m => m.ColumnName).ToList();

            DisplayMessages.WriteColoredInline("  Columns with Matches: ", ConsoleColor.Green);
            Console.WriteLine(columnGroups.Count.ToString());

            DisplayMessages.WriteColoredInline("  Total Matches: ", ConsoleColor.Yellow);
            Console.WriteLine($"{allMatches.Count:N0}");

            Console.WriteLine($"  Search Time: {searchTimeMs:N0} ms");

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
                        Console.WriteLine($"  Type: {columnInfo.DataType}({GetLengthDisplay(columnInfo.MaxLength)}) {(columnInfo.IsNullable ? "NULL" : "NOT NULL")}");
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

        static async Task<List<ColumnInfo>> GetStringColumnsAsync(SqlConnection conn, string tableName)
        {
            var columns = new List<ColumnInfo>();

            string sql = @"
                SELECT COLUMN_NAME, DATA_TYPE, 
                       ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) as MaxLength,
                       IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @TableName
                  AND DATA_TYPE IN ('varchar', 'nvarchar', 'char', 'nchar', 'text', 'ntext')
                ORDER BY ORDINAL_POSITION";

            using var cmd = new SqlCommand(sql, conn);
            performanceManager.ConfigureCommand(cmd);
            cmd.Parameters.AddWithValue("@TableName", tableName);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var column = new ColumnInfo
                {
                    Name = reader["COLUMN_NAME"]?.ToString() ?? string.Empty,
                    DataType = reader["DATA_TYPE"]?.ToString() ?? string.Empty,
                    MaxLength = Convert.ToInt32(reader["MaxLength"] ?? 0),
                    IsNullable = (reader["IS_NULLABLE"]?.ToString() ?? "NO") == "YES"
                };
                columns.Add(column);
            }

            return columns;
        }

        static async Task<List<string>> GetPrimaryKeyColumnsAsync(SqlConnection conn, string tableName)
        {
            var primaryKeys = new List<string>();

            string sql = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1
                  AND TABLE_NAME = @TableName
                ORDER BY ORDINAL_POSITION";

            try
            {
                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);
                cmd.Parameters.AddWithValue("@TableName", tableName);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string columnName = reader["COLUMN_NAME"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(columnName))
                    {
                        primaryKeys.Add(columnName);
                    }
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Could not retrieve primary key information: {ex.Message}");
            }

            return primaryKeys;
        }

        static string SelectEnvironment()
        {
            DisplayMessages.WriteInfo("Available Environments:");
            var envList = environments.ToList();

            for (int i = 0; i < envList.Count; i++)
            {
                string indicator = envList[i].Key.Equals("Prod", StringComparison.OrdinalIgnoreCase) ? " [PRODUCTION]" : "";
                if (indicator != "")
                {
                    Console.Write($"  {i + 1}. {envList[i].Value}");
                    DisplayMessages.WriteColoredInline(indicator, ConsoleColor.Red);
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"  {i + 1}. {envList[i].Value}");
                }
            }

            while (true)
            {
                Console.Write($"Select environment (1-{envList.Count}) [cache/quit]: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                if (input.Equals("cache", StringComparison.OrdinalIgnoreCase)) return "cache";

                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= envList.Count)
                {
                    string selectedEnv = envList[selection - 1].Key;
                    DisplayMessages.WriteSuccess($"Selected: {environments[selectedEnv]}");
                    Console.WriteLine();
                    return selectedEnv;
                }

                DisplayMessages.WriteError("Invalid selection. Please try again.");
            }
        }

        static async Task<string> SelectDatabase(string environment)
        {
            try
            {
                string baseConnectionString = GetBaseConnectionString(environment);
                var databases = await GetDatabaseListAsync(baseConnectionString);

                if (databases.Count == 0)
                {
                    DisplayMessages.WriteError("No databases found or unable to access database list.");
                    DisplayMessages.WriteWarning("Press any key to go back...");
                    Console.ReadKey();
                    return "back";
                }

                DisplayMessages.WriteInfo("Available Databases:");
                for (int i = 0; i < databases.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {databases[i]}");
                }

                while (true)
                {
                    Console.Write($"Select database (1-{databases.Count}) [back/cache/quit]: ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";
                    if (input.Equals("cache", StringComparison.OrdinalIgnoreCase)) return "cache";

                    if (int.TryParse(input, out int selection) && selection >= 1 && selection <= databases.Count)
                    {
                        string selectedDb = databases[selection - 1];
                        DisplayMessages.WriteSuccess($"Selected: {selectedDb}");
                        Console.WriteLine();
                        return selectedDb;
                    }

                    DisplayMessages.WriteError("Invalid selection. Please try again.");
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"Error retrieving database list: {ex.Message}");
                DisplayMessages.WriteWarning("Press any key to go back...");
                Console.ReadKey();
                return "back";
            }
        }

        static async Task<List<string>> GetDatabaseListAsync(string baseConnectionString)
        {
            var databases = new List<string>();

            await performanceManager.ExecuteWithThrottling(async () =>
            {
                using var conn = new SqlConnection(baseConnectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                string sql = @"
                    SELECT name 
                    FROM sys.databases 
                    WHERE state = 0 
                      AND name NOT IN ('master', 'tempdb', 'model', 'msdb')
                    ORDER BY name";

                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string dbName = reader["name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(dbName))
                    {
                        databases.Add(dbName);
                    }
                }
                return true;
            });

            return databases;
        }

        static async Task<string> SelectTableOrView(string environment, string database)
        {
            try
            {
                string connectionString = GetFullConnectionString(environment, database);
                var tablesAndViews = await GetTablesAndViewsAsync(connectionString);

                if (tablesAndViews.Count == 0)
                {
                    DisplayMessages.WriteError("No tables or views found in the database.");
                    DisplayMessages.WriteWarning("Press any key to go back...");
                    Console.ReadKey();
                    return "back";
                }

                DisplayTablesAndViews(tablesAndViews);

                while (true)
                {
                    Console.Write($"Select table/view (1-{tablesAndViews.Count}) [f=filter, back/cache/quit]: ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";
                    if (input.Equals("cache", StringComparison.OrdinalIgnoreCase)) return "cache";

                    // Handle filter option
                    if (input.Equals("f", StringComparison.OrdinalIgnoreCase))
                    {
                        string filtered = FilterTablesAndViews(tablesAndViews);
                        if (!string.IsNullOrEmpty(filtered) && filtered != "back" && filtered != "quit")
                        {
                            return filtered;
                        }
                        if (filtered == "quit") return "quit";
                        if (filtered == "back") continue; // Show full list again
                    }

                    if (int.TryParse(input, out int selection) && selection >= 1 && selection <= tablesAndViews.Count)
                    {
                        var selected = tablesAndViews[selection - 1];
                        DisplayMessages.WriteSuccess($"Selected: {selected.Name} ({(selected.Type == "T" ? "Table" : "View")})");
                        Console.WriteLine();
                        return selected.Name;
                    }

                    DisplayMessages.WriteError("Invalid selection. Please try again.");
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"Error retrieving tables and views: {ex.Message}");
                DisplayMessages.WriteWarning("Press any key to go back...");
                Console.ReadKey();
                return "back";
            }
        }

        static void DisplayTablesAndViews(List<TableViewInfo> tablesAndViews)
        {
            DisplayMessages.WriteInfo("Available Tables and Views:");
            DisplayMessages.WriteHighlight("(T = Table, V = View)");
            Console.WriteLine();

            // Group by type for better display
            var tables = tablesAndViews.Where(tv => tv.Type == "T").OrderBy(tv => tv.Name).ToList();
            var views = tablesAndViews.Where(tv => tv.Type == "V").OrderBy(tv => tv.Name).ToList();

            int counter = 1;

            if (tables.Count > 0)
            {
                DisplayMessages.WriteColoredInline("TABLES:", ConsoleColor.Green);
                Console.WriteLine();
                foreach (var table in tables)
                {
                    Console.Write($"  {counter}. ");
                    DisplayMessages.WriteColoredInline("[T]", ConsoleColor.Green);
                    Console.WriteLine($" {table.Name} ({table.RowCount:N0} rows)");
                    counter++;
                }
                Console.WriteLine();
            }

            if (views.Count > 0)
            {
                DisplayMessages.WriteColoredInline("VIEWS:", ConsoleColor.Blue);
                Console.WriteLine();
                foreach (var view in views)
                {
                    Console.Write($"  {counter}. ");
                    DisplayMessages.WriteColoredInline("[V]", ConsoleColor.Blue);
                    Console.WriteLine($" {view.Name}");
                    counter++;
                }
                Console.WriteLine();
            }
        }

        static string FilterTablesAndViews(List<TableViewInfo> allItems)
        {
            while (true)
            {
                Console.Write("Enter filter text (partial name) [back/quit]: ");
                string filter = Console.ReadLine()?.Trim() ?? "";

                if (filter.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                if (filter.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";

                if (string.IsNullOrEmpty(filter))
                {
                    DisplayMessages.WriteWarning("No filter provided. Please try again.");
                    continue;
                }

                var filtered = allItems.Where(item =>
                    item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                if (filtered.Count == 0)
                {
                    DisplayMessages.WriteError($"No tables or views found matching '{filter}'. Try again.");
                    continue;
                }

                DisplayMessages.WriteInfo($"Filtered Results (containing '{filter}'):");
                for (int i = 0; i < filtered.Count; i++)
                {
                    var item = filtered[i];
                    string rowInfo = item.Type == "T" ? $" ({item.RowCount:N0} rows)" : "";
                    Console.Write($"  {i + 1}. ");
                    DisplayMessages.WriteColoredInline($"[{item.Type}]", item.Type == "T" ? ConsoleColor.Green : ConsoleColor.Blue);
                    Console.WriteLine($" {item.Name}{rowInfo}");
                }

                while (true)
                {
                    Console.Write($"Select from filtered list (1-{filtered.Count}) [back/quit]: ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) break; // Back to filter input

                    if (int.TryParse(input, out int selection) && selection >= 1 && selection <= filtered.Count)
                    {
                        var selected = filtered[selection - 1];
                        DisplayMessages.WriteSuccess($"Selected: {selected.Name} ({(selected.Type == "T" ? "Table" : "View")})");
                        Console.WriteLine();
                        return selected.Name;
                    }

                    DisplayMessages.WriteError("Invalid selection. Please try again.");
                }
            }
        }

        static async Task<List<TableViewInfo>> GetTablesAndViewsAsync(string connectionString)
        {
            var items = new List<TableViewInfo>();

            await performanceManager.ExecuteWithThrottling(async () =>
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                // Simplified query that should work on all SQL Server versions
                string sql = @"
                    SELECT 
                        t.TABLE_NAME as Name,
                        t.TABLE_TYPE,
                        CASE 
                            WHEN t.TABLE_TYPE = 'BASE TABLE' THEN 'T'
                            WHEN t.TABLE_TYPE = 'VIEW' THEN 'V'
                            ELSE 'O'
                        END as Type
                    FROM INFORMATION_SCHEMA.TABLES t
                    WHERE t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                      AND t.TABLE_SCHEMA = 'dbo'
                    ORDER BY t.TABLE_TYPE, t.TABLE_NAME";

                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var item = new TableViewInfo
                    {
                        Name = reader["Name"]?.ToString() ?? "",
                        Type = reader["Type"]?.ToString() ?? "O",
                        RowCount = 0 // We'll get row count separately for tables only
                    };
                    items.Add(item);
                }

                return true;
            });

            // Get row counts for tables only (separate queries to avoid complex joins)
            foreach (var item in items.Where(i => i.Type == "T"))
            {
                try
                {
                    await performanceManager.ExecuteWithThrottling(async () =>
                    {
                        using var conn = new SqlConnection(connectionString);
                        await conn.OpenAsync();
                        performanceManager.ConfigureConnection(conn);

                        string countSql = $"SELECT COUNT(*) FROM [{item.Name}]";
                        using var cmd = new SqlCommand(countSql, conn);
                        performanceManager.ConfigureCommand(cmd);

                        var result = await cmd.ExecuteScalarAsync();
                        item.RowCount = Convert.ToInt64(result ?? 0);
                        return true;
                    });
                }
                catch
                {
                    // If we can't get row count, just leave it as 0
                    item.RowCount = 0;
                }
            }

            return items;
        }

        static string GetSearchValue()
        {
            DisplayMessages.SearchValuesDisplay();

            while (true)
            {
                Console.Write("Search type [1=LIKE, 2=REGEX] [back/quit]: ");
                string typeInput = Console.ReadLine()?.Trim() ?? "";

                if (typeInput.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                if (typeInput.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";

                bool useRegex = false;
                if (typeInput == "2" || typeInput.Equals("regex", StringComparison.OrdinalIgnoreCase))
                {
                    useRegex = true;
                }
                else if (typeInput != "1" && !string.IsNullOrEmpty(typeInput) && !typeInput.Equals("like", StringComparison.OrdinalIgnoreCase))
                {
                    DisplayMessages.WriteError("Invalid selection. Please choose 1 for LIKE or 2 for REGEX.");
                    continue;
                }

                while (true)
                {
                    Console.Write($"Search value ({(useRegex ? "REGEX" : "LIKE")} pattern) [back/quit]: ");
                    string searchValue = Console.ReadLine()?.Trim() ?? "";

                    if (searchValue.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (searchValue.Equals("back", StringComparison.OrdinalIgnoreCase)) break;

                    if (string.IsNullOrEmpty(searchValue))
                    {
                        DisplayMessages.WriteError("Search value is required. Please try again.");
                        continue;
                    }

                    if (useRegex)
                    {
                        try
                        {
                            var regex = new Regex(searchValue, RegexOptions.IgnoreCase);
                            DisplayMessages.WriteSuccess($"Using regular expression: {searchValue}");
                            return $"REGEX:{searchValue}";
                        }
                        catch (ArgumentException ex)
                        {
                            DisplayMessages.WriteError($"Invalid regular expression: {ex.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        DisplayMessages.WriteSuccess($"Using LIKE pattern: {searchValue}");
                        return searchValue;
                    }
                }
            }
        }

        

        static string GetLengthDisplay(int maxLength)
        {
            if (maxLength == -1) return "MAX";
            if (maxLength == 0) return "?";
            return maxLength.ToString();
        }

        static string GetBaseConnectionString(string environment)
        {
            var connectionString = ConfigurationManager.ConnectionStrings[environment];
            if (connectionString == null)
            {
                throw new ArgumentException($"Environment '{environment}' not found in configuration.");
            }
            return connectionString.ConnectionString;
        }

        static string GetFullConnectionString(string environment, string database)
        {
            string baseConnectionString = GetBaseConnectionString(environment);

            var builder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = database
            };

            return builder.ConnectionString;
        }
    }
}