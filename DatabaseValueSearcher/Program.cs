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

namespace DatabaseValueSearcher
{
    class Program
    {
        private static Dictionary<string, string> environments = new Dictionary<string, string>();

        static void Main(string[] args)
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
                    RunInteractiveMode();
                }
                else
                {
                    RunCommandLineMode(args);
                }
            }
            catch (Exception ex)
            {
                WriteError($"FATAL ERROR: {ex.Message}");
                if (ex.InnerException != null)
                {
                    WriteError($"Details: {ex.InnerException.Message}");
                }
                WriteWarning("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }
        }

        #region Console Color Helpers
        static void WriteSuccess(string message) => WriteColored(message, ConsoleColor.Green);
        static void WriteInfo(string message) => WriteColored(message, ConsoleColor.Cyan);
        static void WriteWarning(string message) => WriteColored(message, ConsoleColor.Yellow);
        static void WriteError(string message) => WriteColored(message, ConsoleColor.Red);
        static void WriteHighlight(string message) => WriteColored(message, ConsoleColor.Magenta);

        static void WriteColored(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        static void WriteColoredInline(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ForegroundColor = originalColor;
        }
        #endregion

        static void LoadEnvironments()
        {
            try
            {
                var envSection = ConfigurationManager.GetSection("databaseEnvironments") as System.Collections.Specialized.NameValueCollection;
                if (envSection != null)
                {
                    foreach (string key in envSection.AllKeys)
                    {
                        environments[key] = envSection[key] ?? key;
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
                    WriteWarning("No environments configured. Please check your App.config file.");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Error loading environments: {ex.Message}");
            }
        }

        static void RunInteractiveMode()
        {
            WriteInfo("============================================");
            WriteInfo("DATABASE VALUE SEARCHER - INTERACTIVE MODE");
            WriteInfo("============================================");
            Console.WriteLine();
            WriteHighlight("Navigation: Type 'back' to go back, 'quit' to exit");
            Console.WriteLine();

            while (true) // Main loop to allow complete restart
            {
                string environment = "";
                string database = "";
                string tableOrView = "";
                string searchValue = "";

                // Step 1: Select Environment
                while (string.IsNullOrEmpty(environment))
                {
                    environment = SelectEnvironment();
                    if (environment == "quit") return;
                }

                // Step 2: Select Database
                while (string.IsNullOrEmpty(database))
                {
                    database = SelectDatabase(environment);
                    if (database == "quit") return;
                    if (database == "back")
                    {
                        environment = "";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(environment)) continue; // Start over from environment

                // Step 3: Select Table or View
                while (string.IsNullOrEmpty(tableOrView))
                {
                    tableOrView = SelectTableOrView(environment, database);
                    if (tableOrView == "quit") return;
                    if (tableOrView == "back")
                    {
                        database = "";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(database)) continue; // Go back to database selection

                // Step 4: Get search parameters
                while (string.IsNullOrEmpty(searchValue))
                {
                    searchValue = GetSearchValue();
                    if (searchValue == "quit") return;
                    if (searchValue == "back")
                    {
                        tableOrView = "";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(tableOrView)) continue; // Go back to table selection

                // Step 5: Confirm if production
                if (environment.Equals("Prod", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ConfirmProductionAccess(database, tableOrView))
                    {
                        WriteWarning("Operation cancelled. Starting over...");
                        Console.WriteLine();
                        continue; // Start over from the beginning
                    }
                }

                // Step 6: Perform search
                Console.WriteLine();
                SearchTable(environment, database, tableOrView, searchValue);

                // Ask if user wants to search again
                Console.WriteLine();
                WriteHighlight("Would you like to perform another search? (y/n): ");
                string again = Console.ReadLine()?.Trim() ?? "";
                if (!again.Equals("y", StringComparison.OrdinalIgnoreCase) &&
                    !again.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                Console.WriteLine();
            }
        }

        static void RunCommandLineMode(string[] args)
        {
            if (args.Length < 4)
            {
                ShowUsage();
                return;
            }

            string environment = args[0];
            string database = args[1];
            string tableName = args[2];
            string searchValue = args[3];
            bool useRegex = args.Length > 4 && args[4].Equals("--regex", StringComparison.OrdinalIgnoreCase);

            WriteInfo("============================================");
            WriteInfo("DATABASE VALUE SEARCHER - COMMAND LINE MODE");
            WriteInfo("============================================");
            Console.WriteLine($"Environment: {environment}");
            Console.WriteLine($"Database: {database}");
            Console.WriteLine($"Table: {tableName}");
            Console.WriteLine($"Search Value: {searchValue}");
            if (useRegex) WriteHighlight("Search Type: Regular Expression");
            WriteInfo("============================================");
            Console.WriteLine();

            SearchTable(environment, database, tableName, searchValue, useRegex);
        }

        static string SelectEnvironment()
        {
            WriteInfo("Available Environments:");
            var envList = environments.ToList();

            for (int i = 0; i < envList.Count; i++)
            {
                string indicator = envList[i].Key.Equals("Prod", StringComparison.OrdinalIgnoreCase) ? " [PRODUCTION]" : "";
                if (indicator != "")
                {
                    Console.Write($"  {i + 1}. {envList[i].Value}");
                    WriteColoredInline(indicator, ConsoleColor.Red);
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"  {i + 1}. {envList[i].Value}");
                }
            }

            while (true)
            {
                Console.Write($"Select environment (1-{envList.Count}) [back/quit]: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                if (input.Equals("back", StringComparison.OrdinalIgnoreCase))
                {
                    WriteWarning("Already at the first step.");
                    continue;
                }

                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= envList.Count)
                {
                    string selectedEnv = envList[selection - 1].Key;
                    WriteSuccess($"Selected: {environments[selectedEnv]}");
                    Console.WriteLine();
                    return selectedEnv;
                }

                WriteError("Invalid selection. Please try again.");
            }
        }

        static string SelectDatabase(string environment)
        {
            try
            {
                string baseConnectionString = GetBaseConnectionString(environment);
                var databases = GetDatabaseList(baseConnectionString);

                if (databases.Count == 0)
                {
                    WriteError("No databases found or unable to access database list.");
                    WriteWarning("Press any key to go back...");
                    Console.ReadKey();
                    return "back";
                }

                WriteInfo("Available Databases:");
                for (int i = 0; i < databases.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {databases[i]}");
                }

                while (true)
                {
                    Console.Write($"Select database (1-{databases.Count}) [back/quit]: ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";

                    if (int.TryParse(input, out int selection) && selection >= 1 && selection <= databases.Count)
                    {
                        string selectedDb = databases[selection - 1];
                        WriteSuccess($"Selected: {selectedDb}");
                        Console.WriteLine();
                        return selectedDb;
                    }

                    WriteError("Invalid selection. Please try again.");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Error retrieving database list: {ex.Message}");
                WriteWarning("Press any key to go back...");
                Console.ReadKey();
                return "back";
            }
        }

        static List<string> GetDatabaseList(string baseConnectionString)
        {
            var databases = new List<string>();

            using (var conn = new SqlConnection(baseConnectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT name 
                    FROM sys.databases 
                    WHERE state = 0 
                      AND name NOT IN ('master', 'tempdb', 'model', 'msdb')
                    ORDER BY name";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string dbName = reader["name"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(dbName))
                            {
                                databases.Add(dbName);
                            }
                        }
                    }
                }
            }

            return databases;
        }

        static string SelectTableOrView(string environment, string database)
        {
            try
            {
                string connectionString = GetFullConnectionString(environment, database);
                var tablesAndViews = GetTablesAndViews(connectionString);

                if (tablesAndViews.Count == 0)
                {
                    WriteError("No tables or views found in the database.");
                    WriteWarning("Press any key to go back...");
                    Console.ReadKey();
                    return "back";
                }

                DisplayTablesAndViews(tablesAndViews);

                while (true)
                {
                    Console.Write($"Select table/view (1-{tablesAndViews.Count}) [f=filter, back/quit]: ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";

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
                        WriteSuccess($"Selected: {selected.Name} ({(selected.Type == "T" ? "Table" : "View")})");
                        Console.WriteLine();
                        return selected.Name;
                    }

                    WriteError("Invalid selection. Please try again.");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Error retrieving tables and views: {ex.Message}");
                WriteWarning("Press any key to go back...");
                Console.ReadKey();
                return "back";
            }
        }

        static void DisplayTablesAndViews(List<TableViewInfo> tablesAndViews)
        {
            WriteInfo("Available Tables and Views:");
            WriteHighlight("(T = Table, V = View)");
            Console.WriteLine();

            // Group by type for better display
            var tables = tablesAndViews.Where(tv => tv.Type == "T").OrderBy(tv => tv.Name).ToList();
            var views = tablesAndViews.Where(tv => tv.Type == "V").OrderBy(tv => tv.Name).ToList();

            int counter = 1;

            if (tables.Count > 0)
            {
                WriteColoredInline("TABLES:", ConsoleColor.Green);
                Console.WriteLine();
                foreach (var table in tables)
                {
                    Console.Write($"  {counter}. ");
                    WriteColoredInline("[T]", ConsoleColor.Green);
                    Console.WriteLine($" {table.Name} ({table.RowCount:N0} rows)");
                    counter++;
                }
                Console.WriteLine();
            }

            if (views.Count > 0)
            {
                WriteColoredInline("VIEWS:", ConsoleColor.Blue);
                Console.WriteLine();
                foreach (var view in views)
                {
                    Console.Write($"  {counter}. ");
                    WriteColoredInline("[V]", ConsoleColor.Blue);
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
                    WriteWarning("No filter provided. Please try again.");
                    continue;
                }

                var filtered = allItems.Where(item =>
                    item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                if (filtered.Count == 0)
                {
                    WriteError($"No tables or views found matching '{filter}'. Try again.");
                    continue;
                }

                WriteInfo($"Filtered Results (containing '{filter}'):");
                for (int i = 0; i < filtered.Count; i++)
                {
                    var item = filtered[i];
                    string rowInfo = item.Type == "T" ? $" ({item.RowCount:N0} rows)" : "";
                    Console.Write($"  {i + 1}. ");
                    WriteColoredInline($"[{item.Type}]", item.Type == "T" ? ConsoleColor.Green : ConsoleColor.Blue);
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
                        WriteSuccess($"Selected: {selected.Name} ({(selected.Type == "T" ? "Table" : "View")})");
                        Console.WriteLine();
                        return selected.Name;
                    }

                    WriteError("Invalid selection. Please try again.");
                }
            }
        }

        static List<TableViewInfo> GetTablesAndViews(string connectionString)
        {
            var items = new List<TableViewInfo>();

            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

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

                using (var cmd = new SqlCommand(sql, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new TableViewInfo
                            {
                                Name = reader["Name"]?.ToString() ?? "",
                                Type = reader["Type"]?.ToString() ?? "O",
                                RowCount = 0 // We'll get row count separately for tables only
                            };
                            items.Add(item);
                        }
                    }
                }

                // Get row counts for tables only (separate query to avoid complex joins)
                foreach (var item in items.Where(i => i.Type == "T"))
                {
                    try
                    {
                        string countSql = $"SELECT COUNT(*) FROM [{item.Name}]";
                        using (var cmd = new SqlCommand(countSql, conn))
                        {
                            var result = cmd.ExecuteScalar();
                            item.RowCount = Convert.ToInt64(result ?? 0);
                        }
                    }
                    catch
                    {
                        // If we can't get row count, just leave it as 0
                        item.RowCount = 0;
                    }
                }
            }

            return items;
        }

        static string GetSearchValue()
        {
            WriteInfo("Enter search value:");
            Console.WriteLine();
            WriteHighlight("Search Type Options:");
            Console.WriteLine("  1. LIKE Pattern (default) - Uses SQL LIKE with wildcards");
            Console.WriteLine("  2. Regular Expression - Uses .NET regex patterns");
            Console.WriteLine();

            WriteHighlight("LIKE Pattern Examples:");
            Console.WriteLine("  john          - Exact match for 'john'");
            Console.WriteLine("  %john         - Ends with 'john' (e.g., 'mjohn', 'datajohn')");
            Console.WriteLine("  john%         - Starts with 'john' (e.g., 'johnson', 'johnny')");
            Console.WriteLine("  %john%        - Contains 'john' anywhere (e.g., 'johnson', 'mjohnson')");
            Console.WriteLine("  j_hn          - 'j' + any single character + 'hn' (e.g., 'john', 'jahn')");
            Console.WriteLine("  %@%.com       - Contains '@' and ends with '.com' (emails)");
            Console.WriteLine();

            WriteHighlight("Regular Expression Examples:");
            Console.WriteLine("  ^john$        - Exact match for 'john'");
            Console.WriteLine("  john.*        - Starts with 'john'");
            Console.WriteLine("  .*john        - Ends with 'john'");
            Console.WriteLine("  .*john.*      - Contains 'john' anywhere");
            Console.WriteLine("  j.hn          - 'j' + any single character + 'hn'");
            Console.WriteLine("  \\d{3}-\\d{3}-\\d{4} - Phone number pattern (123-456-7890)");
            Console.WriteLine("  [a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,} - Email pattern");
            Console.WriteLine();

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
                    WriteError("Invalid selection. Please choose 1 for LIKE or 2 for REGEX.");
                    continue;
                }

                while (true)
                {
                    Console.Write($"Search value ({(useRegex ? "REGEX" : "LIKE")} pattern) [back/quit]: ");
                    string searchValue = Console.ReadLine()?.Trim() ?? "";

                    if (searchValue.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (searchValue.Equals("back", StringComparison.OrdinalIgnoreCase)) break; // Back to search type selection

                    if (string.IsNullOrEmpty(searchValue))
                    {
                        WriteError("Search value is required. Please try again.");
                        continue;
                    }

                    // Validate regex pattern if using regex
                    if (useRegex)
                    {
                        try
                        {
                            var regex = new Regex(searchValue, RegexOptions.IgnoreCase);
                            WriteSuccess($"Using regular expression: {searchValue}");
                            return $"REGEX:{searchValue}";
                        }
                        catch (ArgumentException ex)
                        {
                            WriteError($"Invalid regular expression: {ex.Message}");
                            WriteWarning("Please correct the regex pattern or try again.");
                            continue;
                        }
                    }
                    else
                    {
                        // Show what pattern will be used for LIKE
                        Console.WriteLine();
                        if (searchValue.Contains('%') || searchValue.Contains('_'))
                        {
                            WriteSuccess($"Using LIKE pattern: {searchValue}");
                        }
                        else
                        {
                            WriteSuccess($"Using exact match: {searchValue}");
                            WriteHighlight($"Tip: Add % wildcards for broader matching (e.g., %{searchValue}%)");
                        }
                        return searchValue;
                    }
                }
            }
        }

        static bool ConfirmProductionAccess(string database, string tableName)
        {
            string requireConfirmation = ConfigurationManager.AppSettings["RequireConfirmationForProd"] ?? "true";
            if (!bool.Parse(requireConfirmation))
            {
                return true;
            }

            Console.WriteLine();
            WriteError("⚠️  WARNING: You are about to access PRODUCTION environment!");
            Console.WriteLine($"   Database: {database}");
            Console.WriteLine($"   Table: {tableName}");
            Console.WriteLine();
            Console.Write("Are you sure you want to continue? (type 'YES' to confirm): ");

            string confirmation = Console.ReadLine()?.Trim() ?? "";
            return confirmation.Equals("YES", StringComparison.Ordinal);
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

            // Add database to connection string
            var builder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = database
            };

            return builder.ConnectionString;
        }

        static void SearchTable(string environment, string database, string tableName, string searchValue, bool useRegexFromCmdLine = false)
        {
            // Parse search value for regex
            bool useRegex = useRegexFromCmdLine || searchValue.StartsWith("REGEX:");
            if (useRegex && searchValue.StartsWith("REGEX:"))
            {
                searchValue = searchValue.Substring(6); // Remove "REGEX:" prefix
            }

            try
            {
                string connectionString = GetFullConnectionString(environment, database);

                using (var conn = new SqlConnection(connectionString))
                {
                    try
                    {
                        conn.Open();
                        WriteSuccess($"Connected to {environments.GetValueOrDefault(environment, environment)} - {database}");
                    }
                    catch (Exception ex)
                    {
                        WriteError($"Failed to connect to database: {ex.Message}");
                        WriteWarning("Connection failed. Please check your configuration.");
                        return; // Don't exit, just return to allow recovery
                    }

                    // Check if table exists
                    if (!TableExists(conn, tableName))
                    {
                        WriteError($"Table '{tableName}' does not exist in database '{database}'!");
                        WriteWarning("Table not found. Please verify the table name.");
                        return; // Don't exit, just return
                    }

                    // Get primary key columns
                    var primaryKeys = GetPrimaryKeyColumns(conn, tableName);

                    // Get string columns
                    var columns = GetStringColumns(conn, tableName);

                    if (columns.Count == 0)
                    {
                        WriteWarning($"No searchable string columns found in table '{tableName}'.");
                        WriteInfo("This table may only contain non-string columns (numbers, dates, etc.)");
                        return; // Don't exit, just return
                    }

                    WriteInfo($"Found {columns.Count} searchable columns. Searching...");
                    if (primaryKeys.Count > 0)
                    {
                        WriteInfo($"Primary Key(s): {string.Join(", ", primaryKeys)}");
                    }
                    else
                    {
                        WriteWarning("No primary key found - results will show available unique identifiers");
                    }

                    if (useRegex)
                    {
                        WriteHighlight($"Using Regular Expression: {searchValue}");
                    }
                    Console.WriteLine();

                    int totalMatches = 0;
                    int columnsWithMatches = 0;
                    var totalTime = Stopwatch.StartNew();
                    var allMatchingRecords = new List<MatchingRecord>();

                    // Search each column
                    foreach (var column in columns)
                    {
                        var columnTime = Stopwatch.StartNew();

                        int matchCount = 0;
                        List<MatchingRecord> columnMatches = new List<MatchingRecord>();

                        try
                        {
                            if (useRegex)
                            {
                                columnMatches = SearchColumnRegexWithKeys(conn, tableName, column.Name, searchValue, primaryKeys);
                            }
                            else
                            {
                                columnMatches = SearchColumnWithKeys(conn, tableName, column.Name, searchValue, primaryKeys);
                            }
                            matchCount = columnMatches.Count;
                        }
                        catch (Exception ex)
                        {
                            WriteError($"Error searching column {column.Name}: {ex.Message}");
                            continue; // Skip this column and continue with others
                        }

                        columnTime.Stop();

                        if (matchCount > 0)
                        {
                            columnsWithMatches++;
                            totalMatches += matchCount;
                            allMatchingRecords.AddRange(columnMatches);

                            WriteColoredInline("✓ Column: ", ConsoleColor.Green);
                            WriteColoredInline(column.Name, ConsoleColor.White);
                            Console.WriteLine();

                            Console.WriteLine($"  Type: {column.DataType}({GetLengthDisplay(column.MaxLength)}) {(column.IsNullable ? "NULL" : "NOT NULL")}");

                            WriteColoredInline("  Matches: ", ConsoleColor.Cyan);
                            WriteColoredInline($"{matchCount:N0}", ConsoleColor.Yellow);
                            Console.WriteLine();

                            bool showTiming = bool.Parse(ConfigurationManager.AppSettings["ShowTimingDetails"] ?? "true");
                            if (showTiming)
                            {
                                Console.WriteLine($"  Search Time: {columnTime.ElapsedMilliseconds} ms");
                            }

                            // Show sample matching records with primary keys
                            WriteColoredInline("  Sample Matches:", ConsoleColor.Magenta);
                            Console.WriteLine();

                            int maxSamples = int.Parse(ConfigurationManager.AppSettings["DefaultMaxSamples"] ?? "3");
                            var sampleMatches = columnMatches.Take(maxSamples).ToList();

                            foreach (var match in sampleMatches)
                            {
                                WriteColoredInline("    • ", ConsoleColor.Gray);
                                WriteColoredInline($"{column.Name}: ", ConsoleColor.Cyan);
                                WriteColoredInline($"'{match.MatchingValue}'", ConsoleColor.Yellow);

                                if (match.PrimaryKeyValues.Count > 0)
                                {
                                    WriteColoredInline(" | ", ConsoleColor.Gray);
                                    WriteColoredInline("Keys: ", ConsoleColor.Magenta);
                                    Console.Write(string.Join(", ", match.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}")));
                                }
                                Console.WriteLine();
                            }

                            if (columnMatches.Count > maxSamples)
                            {
                                WriteColoredInline($"    ... and {columnMatches.Count - maxSamples} more matches", ConsoleColor.Gray);
                                Console.WriteLine();
                            }
                            Console.WriteLine();
                        }
                    }

                    totalTime.Stop();

                    // Enhanced Summary with record details
                    WriteInfo("============================================");
                    WriteInfo("SEARCH SUMMARY:");
                    WriteInfo("============================================");
                    Console.WriteLine($"  Environment: {environments.GetValueOrDefault(environment, environment)}");
                    Console.WriteLine($"  Database: {database}");
                    Console.WriteLine($"  Table: {tableName}");
                    Console.WriteLine($"  Search Value: {searchValue}");
                    Console.WriteLine($"  Search Type: {(useRegex ? "Regular Expression" : "LIKE Pattern")}");

                    WriteColoredInline("  Columns Searched: ", ConsoleColor.Cyan);
                    Console.WriteLine(columns.Count.ToString());

                    WriteColoredInline("  Columns with Matches: ", ConsoleColor.Green);
                    Console.WriteLine(columnsWithMatches.ToString());

                    WriteColoredInline("  Total Matches: ", ConsoleColor.Yellow);
                    Console.WriteLine($"{totalMatches:N0}");

                    Console.WriteLine($"  Total Search Time: {totalTime.ElapsedMilliseconds} ms");
                    Console.WriteLine($"  Average Time per Column: {(columns.Count > 0 ? totalTime.ElapsedMilliseconds / columns.Count : 0)} ms");

                    // Show detailed record summary if we have matches
                    if (allMatchingRecords.Count > 0)
                    {
                        Console.WriteLine();
                        WriteInfo("MATCHING RECORDS SUMMARY:");
                        WriteInfo("----------------------------------------");

                        // Group by unique primary key combinations to avoid duplicates
                        var uniqueRecords = allMatchingRecords
                            .GroupBy(r => string.Join("|", r.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}")))
                            .Select(g => g.First())
                            .Take(10) // Show max 10 unique records in summary
                            .ToList();

                        foreach (var record in uniqueRecords)
                        {
                            WriteColoredInline("  Record: ", ConsoleColor.Green);
                            if (record.PrimaryKeyValues.Count > 0)
                            {
                                Console.WriteLine(string.Join(", ", record.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}")));
                                WriteColoredInline("    Found in: ", ConsoleColor.Cyan);
                                WriteColoredInline($"{record.ColumnName}", ConsoleColor.White);
                                WriteColoredInline($" = '{record.MatchingValue}'", ConsoleColor.Yellow);
                                Console.WriteLine();
                            }
                            else
                            {
                                WriteColoredInline($"{record.ColumnName}", ConsoleColor.White);
                                WriteColoredInline($" = '{record.MatchingValue}'", ConsoleColor.Yellow);
                                Console.WriteLine(" (No primary key available)");
                            }
                        }

                        var totalUniqueRecords = allMatchingRecords
                            .GroupBy(r => string.Join("|", r.PrimaryKeyValues.Select(kv => $"{kv.Key}={kv.Value}")))
                            .Count();

                        if (totalUniqueRecords > 10)
                        {
                            WriteColoredInline($"  ... and {totalUniqueRecords - 10} more unique records", ConsoleColor.Gray);
                            Console.WriteLine();
                        }

                        Console.WriteLine();
                        WriteColoredInline("  Unique Records Found: ", ConsoleColor.Magenta);
                        Console.WriteLine(totalUniqueRecords.ToString());
                    }

                    WriteInfo("============================================");

                    if (columnsWithMatches == 0)
                    {
                        Console.WriteLine();
                        WriteWarning("No matches found in any column.");
                        WriteHighlight("Tips:");
                        Console.WriteLine("- Try using wildcards: %search_term%");
                        Console.WriteLine("- Check your search term spelling");
                        Console.WriteLine("- Verify the table contains the expected data");
                        if (!useRegex)
                        {
                            Console.WriteLine("- Consider using regular expressions for more complex patterns");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError($"Unexpected error during search: {ex.Message}");
                if (ex.InnerException != null)
                {
                    WriteError($"Details: {ex.InnerException.Message}");
                }
                WriteWarning("Search failed, but you can try again with different parameters.");
            }
        }

        static void ShowUsage()
        {
            WriteInfo("Database Value Searcher - Usage:");
            Console.WriteLine("");
            WriteHighlight("Interactive Mode (Recommended):");
            Console.WriteLine("  DatabaseValueSearcher.exe");
            Console.WriteLine("  - Guides you through environment, database, and table selection");
            Console.WriteLine("  - Shows available tables and views with row counts");
            Console.WriteLine("  - Provides search pattern examples and tips");
            Console.WriteLine("  - Supports navigation (back/quit commands)");
            Console.WriteLine("");
            WriteHighlight("Command Line Mode:");
            Console.WriteLine("  DatabaseValueSearcher.exe <environment> <database> <table_name> <search_value> [--regex]");
            Console.WriteLine("");
            WriteInfo("Available Environments:");
            foreach (var env in environments)
            {
                Console.WriteLine($"  {env.Key} - {env.Value}");
            }
            Console.WriteLine("");
            WriteHighlight("Search Pattern Examples:");
            WriteInfo("LIKE Patterns:");
            Console.WriteLine("  %john%        - Contains 'john' anywhere");
            Console.WriteLine("  john%         - Starts with 'john'");
            Console.WriteLine("  %john         - Ends with 'john'");
            Console.WriteLine("  john          - Exact match");
            Console.WriteLine("  j_hn          - 'j' + any single character + 'hn'");
            Console.WriteLine("");
            WriteInfo("Regular Expression Patterns:");
            Console.WriteLine("  ^john$        - Exact match for 'john'");
            Console.WriteLine("  john.*        - Starts with 'john'");
            Console.WriteLine("  .*john.*      - Contains 'john' anywhere");
            Console.WriteLine("  \\d{3}-\\d{3}-\\d{4} - Phone number pattern");
            Console.WriteLine("  [a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,} - Email pattern");
            Console.WriteLine("");
            WriteHighlight("Examples:");
            Console.WriteLine("  DatabaseValueSearcher.exe");
            Console.WriteLine("  DatabaseValueSearcher.exe Local MyDatabase Users \"%john%\"");
            Console.WriteLine("  DatabaseValueSearcher.exe QA OrdersDB OrderView \"\\d{5}\" --regex");
        }

        static List<string> GetPrimaryKeyColumns(SqlConnection conn, string tableName)
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
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string columnName = reader["COLUMN_NAME"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(columnName))
                            {
                                primaryKeys.Add(columnName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteWarning($"Could not retrieve primary key information: {ex.Message}");
            }

            return primaryKeys;
        }

        static List<MatchingRecord> SearchColumnWithKeys(SqlConnection conn, string tableName, string columnName, string searchValue, List<string> primaryKeys)
        {
            var matches = new List<MatchingRecord>();

            // Build SELECT statement with primary keys
            var selectColumns = new List<string> { $"[{columnName}]" };
            selectColumns.AddRange(primaryKeys.Select(pk => $"[{pk}]"));

            string sql = $"SELECT {string.Join(", ", selectColumns.Distinct())} FROM [{tableName}] WHERE [{columnName}] LIKE @SearchValue";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@SearchValue", searchValue);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var match = new MatchingRecord
                        {
                            ColumnName = columnName,
                            MatchingValue = reader[columnName]?.ToString() ?? "<NULL>"
                        };

                        // Get primary key values
                        foreach (string pkColumn in primaryKeys)
                        {
                            var pkValue = reader[pkColumn];
                            match.PrimaryKeyValues[pkColumn] = pkValue?.ToString() ?? "<NULL>";
                        }

                        matches.Add(match);
                    }
                }
            }

            return matches;
        }

        static List<MatchingRecord> SearchColumnRegexWithKeys(SqlConnection conn, string tableName, string columnName, string regexPattern, List<string> primaryKeys)
        {
            var matches = new List<MatchingRecord>();

            try
            {
                var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

                // Build SELECT statement with primary keys
                var selectColumns = new List<string> { $"[{columnName}]" };
                selectColumns.AddRange(primaryKeys.Select(pk => $"[{pk}]"));

                string sql = $"SELECT {string.Join(", ", selectColumns.Distinct())} FROM [{tableName}] WHERE [{columnName}] IS NOT NULL";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var value = reader[columnName];
                            if (value != null && value != DBNull.Value)
                            {
                                string stringValue = value.ToString() ?? "";
                                if (regex.IsMatch(stringValue))
                                {
                                    var match = new MatchingRecord
                                    {
                                        ColumnName = columnName,
                                        MatchingValue = stringValue
                                    };

                                    // Get primary key values
                                    foreach (string pkColumn in primaryKeys)
                                    {
                                        var pkValue = reader[pkColumn];
                                        match.PrimaryKeyValues[pkColumn] = pkValue?.ToString() ?? "<NULL>";
                                    }

                                    matches.Add(match);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError($"Error in regex search with keys: {ex.Message}");
            }

            return matches;
        }
        {
            if (maxLength == -1) return "MAX";
            if (maxLength == 0) return "?";
            return maxLength.ToString();
        }

        static bool TableExists(SqlConnection conn, string tableName)
        {
            string sql = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @TableName 
                  AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                return count > 0;
            }
        }

        static List<ColumnInfo> GetStringColumns(SqlConnection conn, string tableName)
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

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
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
                }
            }

            return columns;
        }

        static int SearchColumn(SqlConnection conn, string tableName, string columnName, string searchValue)
        {
            string sql = $"SELECT COUNT(*) FROM [{tableName}] WHERE [{columnName}] LIKE @SearchValue";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@SearchValue", searchValue);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        static int SearchColumnRegex(SqlConnection conn, string tableName, string columnName, string regexPattern)
        {
            // For regex, we need to pull data and filter in .NET since SQL Server doesn't have built-in regex
            string sql = $"SELECT [{columnName}] FROM [{tableName}] WHERE [{columnName}] IS NOT NULL";
            int matchCount = 0;

            try
            {
                var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

                using (var cmd = new SqlCommand(sql, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var value = reader[0];
                            if (value != null && value != DBNull.Value)
                            {
                                string stringValue = value.ToString() ?? "";
                                if (regex.IsMatch(stringValue))
                                {
                                    matchCount++;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError($"Error in regex search: {ex.Message}");
            }

            return matchCount;
        }

        static List<string> GetSampleData(SqlConnection conn, string tableName, string columnName, string searchValue, int maxSamples)
        {
            var samples = new List<string>();

            string sql = $"SELECT TOP ({maxSamples}) [{columnName}] FROM [{tableName}] WHERE [{columnName}] LIKE @SearchValue ORDER BY [{columnName}]";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@SearchValue", searchValue);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var value = reader[0];
                        string displayValue = (value == null || value == DBNull.Value) ? "<NULL>" : (value.ToString() ?? "<NULL>");

                        // Truncate long values for display
                        int maxLength = int.Parse(ConfigurationManager.AppSettings["MaxDisplayLength"] ?? "50");
                        if (displayValue.Length > maxLength)
                        {
                            displayValue = displayValue.Substring(0, maxLength - 3) + "...";
                        }
                        samples.Add(displayValue);
                    }
                }
            }

            return samples;
        }

        static List<string> GetSampleDataRegex(SqlConnection conn, string tableName, string columnName, string regexPattern, int maxSamples)
        {
            var samples = new List<string>();

            try
            {
                var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                string sql = $"SELECT [{columnName}] FROM [{tableName}] WHERE [{columnName}] IS NOT NULL ORDER BY [{columnName}]";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read() && samples.Count < maxSamples)
                        {
                            var value = reader[0];
                            if (value != null && value != DBNull.Value)
                            {
                                string stringValue = value.ToString() ?? "";
                                if (regex.IsMatch(stringValue))
                                {
                                    // Truncate long values for display
                                    int maxLength = int.Parse(ConfigurationManager.AppSettings["MaxDisplayLength"] ?? "50");
                                    if (stringValue.Length > maxLength)
                                    {
                                        stringValue = stringValue.Substring(0, maxLength - 3) + "...";
                                    }
                                    samples.Add(stringValue);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError($"Error getting regex sample data: {ex.Message}");
            }

            return samples;
        }
    }

    public class MatchingRecord
{
    public string ColumnName { get; set; } = string.Empty;
    public string MatchingValue { get; set; } = string.Empty;
    public Dictionary<string, string> PrimaryKeyValues { get; set; } = new Dictionary<string, string>();
}

public class TableViewInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // T = Table, V = View
    public long RowCount { get; set; }
}

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public bool IsNullable { get; set; }
}
}