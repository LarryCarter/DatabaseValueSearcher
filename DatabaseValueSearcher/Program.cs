﻿#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace DatabaseValueSearcher
{
    partial class Program
    {
        // Core dependencies
        private static Dictionary<string, string> environments = new Dictionary<string, string>();
        private static CacheManager cacheManager = new CacheManager();
        private static PerformanceManager performanceManager = new PerformanceManager();
        private static SearchSession? currentSession;

        // In-memory caches for database and table lists
        private static Dictionary<string, List<string>> databaseListCache = new Dictionary<string, List<string>>();
        private static Dictionary<string, List<TableViewInfo>> tableListCache = new Dictionary<string, List<TableViewInfo>>();
        private static Dictionary<string, DateTime> cacheTimestamps = new Dictionary<string, DateTime>();

        // Service instances
        private static DatabaseService? databaseService;
        private static NavigationService? navigationService;
        private static CacheUI? cacheUI;
        private static ExportService? exportService;
        private static SessionManager? sessionManager;

        static async Task Main(string[] args)
        {
            try
            {
                // Enable console colors
                if (!Console.IsOutputRedirected)
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                }

                // Initialize core components
                LoadEnvironments();
                InitializeServices();

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

        /// <summary>
        /// Loads environment configuration from App.config
        /// </summary>
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

        /// <summary>
        /// Initializes all service dependencies
        /// </summary>
        static void InitializeServices()
        {
            databaseService = new DatabaseService(performanceManager);
            navigationService = new NavigationService(databaseService, environments, databaseListCache, tableListCache, cacheTimestamps);
            cacheUI = new CacheUI(cacheManager, databaseListCache, tableListCache, cacheTimestamps);
            exportService = new ExportService(cacheManager);
            sessionManager = new SessionManager(cacheManager, performanceManager, databaseService);
        }

        /// <summary>
        /// Runs the interactive mode with enhanced navigation
        /// </summary>
        static async Task RunInteractiveMode()
        {
            DisplayMessages.WriteInfo("============================================");
            DisplayMessages.WriteInfo("DATABASE VALUE SEARCHER - INTERACTIVE MODE");
            DisplayMessages.WriteInfo("============================================");
            Console.WriteLine();
            DisplayMessages.WriteHighlight("Navigation: Type 'back' to go back, 'quit' to exit, 'cache' for cache options, 'export' to export cache data");
            cacheUI!.ShowCacheStatus();
            Console.WriteLine();

            while (true) // Main loop to allow complete restart
            {
                // Check if we have an active session
                if (currentSession != null)
                {
                    await HandleActiveSession();
                    continue;
                }

                // New session flow
                string environment = "";
                string database = "";
                string tableOrView = "";

                // Step 1: Select Environment
                while (string.IsNullOrEmpty(environment))
                {
                    environment = navigationService!.SelectEnvironment();
                    if (environment == "quit") return;
                    if (environment == "cache")
                    {
                        await cacheUI!.HandleCacheCommands(currentSession);
                        environment = "";
                        continue;
                    }
                }

                // Step 2: Select Database
                while (string.IsNullOrEmpty(database))
                {
                    database = await navigationService!.SelectDatabase(environment);
                    if (database == "quit") return;
                    if (database == "back")
                    {
                        environment = "";
                        break;
                    }
                    if (database == "cache")
                    {
                        await cacheUI!.HandleCacheCommands(currentSession);
                        continue;
                    }
                }
                if (string.IsNullOrEmpty(environment)) continue;

                // Step 3: Select Table or View
                while (string.IsNullOrEmpty(tableOrView))
                {
                    tableOrView = await navigationService!.SelectTableOrView(environment, database);
                    if (tableOrView == "quit") return;
                    if (tableOrView == "back")
                    {
                        database = "";
                        break;
                    }
                    if (tableOrView == "cache")
                    {
                        await cacheUI!.HandleCacheCommands(currentSession);
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
                await sessionManager!.InitializeTableCache(currentSession);

                // Step 5: Perform first search
                await sessionManager.PerformCachedSearch(currentSession, () => navigationService!.GetSearchValue());
            }
        }

        /// <summary>
        /// Runs command line mode for scripted execution
        /// </summary>
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

            await sessionManager!.InitializeTableCache(currentSession);
            await sessionManager.SearchCachedTable(currentSession, searchValue, useRegex);
        }

        /// <summary>
        /// Handles the active session menu with all available options
        /// </summary>
        static async Task HandleActiveSession()
        {
            if (currentSession == null) return;

            DisplayMessages.WriteInfo($"Active Session: {currentSession.Environment} > {currentSession.Database} > {currentSession.TableName}");
            DisplayMessages.WriteHighlight("Options:");
            Console.WriteLine("  'search' - New search on current table");
            Console.WriteLine("  'refresh' - Reload current table data");
            Console.WriteLine("  'table' - Select different table (same database)");
            Console.WriteLine("  'database' - Select different database");
            Console.WriteLine("  'clear' - Start completely over");
            Console.WriteLine("  'export' - Export current table cache to SQL");
            Console.WriteLine("  'cache' - Cache management");
            Console.WriteLine("  'stats' - Show session statistics");
            Console.WriteLine("  'quit' - Exit application");
            Console.WriteLine();

            Console.Write("Choose action: ");
            string action = Console.ReadLine()?.Trim() ?? "";

            switch (action.ToLower())
            {
                case "search":
                    await sessionManager!.PerformCachedSearch(currentSession, () => navigationService!.GetSearchValue());
                    break;
                case "refresh":
                    await sessionManager!.RefreshCurrentTable(currentSession);
                    break;
                case "table":
                    await SelectNewTable();
                    break;
                case "database":
                    await SelectNewDatabase();
                    break;
                case "clear":
                    currentSession = null;
                    break;
                case "export":
                    await exportService!.ExportCachedTableMenu(currentSession, (session, pageNum) => sessionManager!.LoadOrFetchPage(session, pageNum));
                    break;
                case "cache":
                    await cacheUI!.HandleCacheCommands(currentSession);
                    break;
                case "stats":
                    ShowSessionStatistics();
                    break;
                case "quit":
                    Environment.Exit(0);
                    break;
                default:
                    DisplayMessages.WriteError("Invalid option. Please try again.");
                    break;
            }
        }

        /// <summary>
        /// Allows user to select a new table in the same database
        /// </summary>
        static async Task SelectNewTable()
        {
            if (currentSession == null) return;

            string tableOrView = await navigationService!.SelectTableOrView(currentSession.Environment, currentSession.Database);
            if (tableOrView == "quit")
            {
                Environment.Exit(0);
            }
            else if (tableOrView != "back" && tableOrView != "cache")
            {
                // Update session with new table
                currentSession.TableName = tableOrView;
                currentSession.CachedData = null; // Clear old cache data
                currentSession.CreatedAt = DateTime.Now;
                currentSession.LastSearchedPage = 0;

                await sessionManager!.InitializeTableCache(currentSession);
                await sessionManager.PerformCachedSearch(currentSession, () => navigationService!.GetSearchValue());
            }
        }

        /// <summary>
        /// Allows user to select a new database and table
        /// </summary>
        static async Task SelectNewDatabase()
        {
            if (currentSession == null) return;

            string database = await navigationService!.SelectDatabase(currentSession.Environment);
            if (database == "quit")
            {
                Environment.Exit(0);
            }
            else if (database != "back" && database != "cache")
            {
                // Update session with new database
                currentSession.Database = database;
                currentSession.TableName = "";
                currentSession.CachedData = null;
                currentSession.CreatedAt = DateTime.Now;
                currentSession.LastSearchedPage = 0;

                // Now select table
                await SelectNewTable();
            }
        }

        /// <summary>
        /// Shows detailed session statistics
        /// </summary>
        static void ShowSessionStatistics()
        {
            if (currentSession == null)
            {
                DisplayMessages.WriteWarning("No active session.");
                return;
            }

            var stats = sessionManager!.GetSessionStatistics(currentSession);

            Console.WriteLine();
            DisplayMessages.WriteInfo("SESSION STATISTICS:");
            DisplayMessages.WriteInfo("==========================================");
            Console.WriteLine($"Session ID: {stats.SessionId}");
            Console.WriteLine($"Environment: {stats.Environment}");
            Console.WriteLine($"Database: {stats.Database}");
            Console.WriteLine($"Table: {stats.TableName}");
            Console.WriteLine($"Created: {stats.CreatedAt:yyyy-MM-dd HH:mm:ss} ({stats.Age.TotalMinutes:F0} minutes ago)");

            if (stats.IsInitialized)
            {
                Console.WriteLine($"Cached: {stats.CachedAt:yyyy-MM-dd HH:mm:ss} ({stats.CacheAge.TotalMinutes:F0} minutes ago)");
                Console.WriteLine($"Total Rows: {stats.TotalRows:N0}");
                Console.WriteLine($"Columns: {stats.ColumnCount} searchable string columns");
                Console.WriteLine($"Primary Keys: {stats.PrimaryKeyCount}");
                Console.WriteLine($"Page Size: {stats.PageSize:N0} rows");
                Console.WriteLine($"Cache Status: {stats.CachedPages}/{stats.TotalPages} pages ({stats.CompletionPercentage:F1}%)");
                Console.WriteLine($"Cache Size: {stats.CacheSizeDisplay}");

                if (stats.IsComplete)
                {
                    DisplayMessages.WriteColoredInline("Completeness: ", ConsoleColor.White);
                    DisplayMessages.WriteColoredInline("COMPLETE", ConsoleColor.Green);
                    Console.WriteLine();
                }
                else
                {
                    DisplayMessages.WriteColoredInline("Completeness: ", ConsoleColor.White);
                    DisplayMessages.WriteColoredInline("PARTIAL", ConsoleColor.Yellow);
                    Console.WriteLine();
                }
            }
            else
            {
                DisplayMessages.WriteColoredInline("Status: ", ConsoleColor.White);
                DisplayMessages.WriteColoredInline("NOT INITIALIZED", ConsoleColor.Red);
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Helper method for length display formatting
        /// </summary>
        static string GetLengthDisplay(int maxLength)
        {
            if (maxLength == -1) return "MAX";
            if (maxLength == 0) return "?";
            return maxLength.ToString();
        }
    }
}