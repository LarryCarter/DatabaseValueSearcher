#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DatabaseValueSearcher
{
    public class NavigationService
    {
        private readonly DatabaseService databaseService;
        private readonly Dictionary<string, string> environments;
        private readonly Dictionary<string, List<string>> databaseListCache;
        private readonly Dictionary<string, List<TableViewInfo>> tableListCache;
        private readonly Dictionary<string, DateTime> cacheTimestamps;

        public NavigationService(
            DatabaseService databaseService,
            Dictionary<string, string> environments,
            Dictionary<string, List<string>> databaseListCache,
            Dictionary<string, List<TableViewInfo>> tableListCache,
            Dictionary<string, DateTime> cacheTimestamps)
        {
            this.databaseService = databaseService;
            this.environments = environments;
            this.databaseListCache = databaseListCache;
            this.tableListCache = tableListCache;
            this.cacheTimestamps = cacheTimestamps;
        }

        /// <summary>
        /// Displays environment selection menu and gets user choice
        /// </summary>
        public string SelectEnvironment()
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

        /// <summary>
        /// Displays database selection menu with caching support
        /// </summary>
        public async Task<string> SelectDatabase(string environment)
        {
            try
            {
                // Check if we have cached database list and if it's still valid
                string cacheKey = $"databases_{environment}";
                bool useCache = databaseListCache.ContainsKey(cacheKey) &&
                               cacheTimestamps.ContainsKey(cacheKey) &&
                               (DateTime.Now - cacheTimestamps[cacheKey]).TotalHours < 24;

                List<string> databases;

                if (useCache)
                {
                    databases = databaseListCache[cacheKey];
                    DisplayMessages.WriteInfo($"Using cached database list (cached {cacheTimestamps[cacheKey]:yyyy-MM-dd HH:mm})");
                }
                else
                {
                    DisplayMessages.WriteInfo("Loading database list...");
                    string baseConnectionString = DatabaseService.GetBaseConnectionString(environment);
                    databases = await databaseService.GetDatabaseListAsync(baseConnectionString);

                    // Cache the results
                    databaseListCache[cacheKey] = databases;
                    cacheTimestamps[cacheKey] = DateTime.Now;
                }

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

                if (databaseListCache.ContainsKey(cacheKey))
                {
                    Console.WriteLine();
                    DisplayMessages.WriteHighlight("Options: [r=refresh list, back/cache/quit]");
                }

                while (true)
                {
                    Console.Write($"Select database (1-{databases.Count}): ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";
                    if (input.Equals("cache", StringComparison.OrdinalIgnoreCase)) return "cache";

                    if (input.Equals("r", StringComparison.OrdinalIgnoreCase) ||
                        input.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                    {
                        // Clear cache and reload
                        if (databaseListCache.ContainsKey(cacheKey)) databaseListCache.Remove(cacheKey);
                        if (cacheTimestamps.ContainsKey(cacheKey)) cacheTimestamps.Remove(cacheKey);
                        return await SelectDatabase(environment); // Recursive call to reload
                    }

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

        // (Duplicate method removed)

        /// <summary>
        /// Handles table selection for pre-caching with options for cache mode
        /// </summary>
        public async Task<string> SelectTableForPreCache(List<TableViewInfo> tablesAndViews, string environment, string database)
        {
            Console.WriteLine();
            DisplayMessages.WriteInfo("PRE-CACHE TABLE DATA");
            DisplayMessages.WriteInfo("Select table to pre-cache (download data in advance):");

            for (int i = 0; i < tablesAndViews.Count; i++)
            {
                var table = tablesAndViews[i];
                string rowInfo = table.Type == "T" ? $" ({table.RowCount:N0} rows)" : "";
                Console.Write($"  {i + 1}. ");
                DisplayMessages.WriteColoredInline($"[{table.Type}]", table.Type == "T" ? ConsoleColor.Green : ConsoleColor.Blue);
                Console.WriteLine($" {table.Name}{rowInfo}");
            }

            while (true)
            {
                Console.WriteLine();
                Console.Write($"Select table to pre-cache (1-{tablesAndViews.Count}) [back/quit]: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";

                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= tablesAndViews.Count)
                {
                    var selectedTable = tablesAndViews[selection - 1];

                    // Show pre-cache options
                    string cacheMode = await GetPreCacheMode(selectedTable);
                    if (cacheMode == "quit") return "quit";
                    if (cacheMode == "back") continue;

                    if (!string.IsNullOrEmpty(cacheMode))
                    {
                        // Signal to start pre-caching
                        return $"PRECACHE:{cacheMode}:{selectedTable.Name}";
                    }
                }
                else
                {
                    DisplayMessages.WriteError("Invalid selection. Please try again.");
                }
            }
        }

        /// <summary>
        /// Gets the pre-cache mode from user (full or partial)
        /// </summary>
        public async Task<string> GetPreCacheMode(TableViewInfo table)
        {
            Console.WriteLine();
            DisplayMessages.WriteInfo($"PRE-CACHE OPTIONS FOR: {table.Name}");

            if (table.Type == "T" && table.RowCount > 0)
            {
                DisplayMessages.WriteHighlight($"Table has {table.RowCount:N0} rows");

                // Estimate cache time and size
                var estimatedPages = (int)Math.Ceiling((double)table.RowCount / 10000); // Assuming 10k page size
                var estimatedTimeMinutes = estimatedPages * 0.1; // Rough estimate: 0.1 minutes per page

                Console.WriteLine($"Estimated cache time: {estimatedTimeMinutes:F1} minutes");
                Console.WriteLine($"Estimated pages: {estimatedPages:N0}");
            }

            Console.WriteLine();
            DisplayMessages.WriteHighlight("Cache Options:");
            Console.WriteLine("  1. Full Cache - Download all table data (recommended for searches)");
            Console.WriteLine("  2. Metadata Only - Just table structure and column info");
            Console.WriteLine("  3. Sample Cache - Download first few pages only (quick preview)");
            Console.WriteLine();

            while (true)
            {
                Console.Write("Select cache mode (1-3) [back/quit]: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";

                switch (input)
                {
                    case "1":
                        // Confirm full cache for large tables
                        if (table.Type == "T" && table.RowCount > 100000)
                        {
                            Console.WriteLine();
                            DisplayMessages.WriteWarning($"This table has {table.RowCount:N0} rows. Full caching may take several minutes.");
                            Console.Write("Continue with full cache? [y/N]: ");
                            string confirm = Console.ReadLine()?.Trim() ?? "";
                            if (!confirm.Equals("y", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        DisplayMessages.WriteSuccess("Full cache mode selected.");
                        return "FULL";

                    case "2":
                        DisplayMessages.WriteSuccess("Metadata only mode selected.");
                        return "METADATA";

                    case "3":
                        DisplayMessages.WriteSuccess("Sample cache mode selected.");
                        return "SAMPLE";

                    default:
                        DisplayMessages.WriteError("Invalid selection. Please choose 1, 2, or 3.");
                        break;
                }
            }
        }

        /// <summary>
        /// Displays table/view selection menu with caching and filtering
        /// </summary>
        public async Task<string> SelectTableOrView(string environment, string database)
        {
            try
            {
                // Check if we have cached table list
                string cacheKey = $"tables_{environment}_{database}";
                bool useCache = tableListCache.ContainsKey(cacheKey) &&
                               cacheTimestamps.ContainsKey(cacheKey) &&
                               (DateTime.Now - cacheTimestamps[cacheKey]).TotalHours < 24;

                List<TableViewInfo> tablesAndViews;

                if (useCache)
                {
                    tablesAndViews = tableListCache[cacheKey];
                    DisplayMessages.WriteInfo($"Using cached table list (cached {cacheTimestamps[cacheKey]:yyyy-MM-dd HH:mm})");
                }
                else
                {
                    DisplayMessages.WriteInfo("Loading table and view list...");
                    string connectionString = DatabaseService.GetFullConnectionString(environment, database);
                    tablesAndViews = await databaseService.GetTablesAndViewsAsync(connectionString);

                    // Cache the results
                    tableListCache[cacheKey] = tablesAndViews;
                    cacheTimestamps[cacheKey] = DateTime.Now;
                }

                if (tablesAndViews.Count == 0)
                {
                    DisplayMessages.WriteError("No tables or views found in the database.");
                    DisplayMessages.WriteWarning("Press any key to go back...");
                    Console.ReadKey();
                    return "back";
                }

                DisplayTablesAndViews(tablesAndViews);

                if (tableListCache.ContainsKey(cacheKey))
                {
                    Console.WriteLine();
                    DisplayMessages.WriteHighlight("Options: [f=filter, r=refresh list, back/cache/quit]");
                }

                while (true)
                {
                    Console.Write($"Select table/view (1-{tablesAndViews.Count}): ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) return "quit";
                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase)) return "back";
                    if (input.Equals("cache", StringComparison.OrdinalIgnoreCase)) return "cache";

                    if (input.Equals("r", StringComparison.OrdinalIgnoreCase) ||
                        input.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                    {
                        // Clear cache and reload
                        if (tableListCache.ContainsKey(cacheKey)) tableListCache.Remove(cacheKey);
                        if (cacheTimestamps.ContainsKey(cacheKey)) cacheTimestamps.Remove(cacheKey);
                        return await SelectTableOrView(environment, database); // Recursive call to reload
                    }

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

        /// <summary>
        /// Displays formatted list of tables and views
        /// </summary>
        public void DisplayTablesAndViews(List<TableViewInfo> tablesAndViews)
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

        /// <summary>
        /// Provides interactive filtering for tables and views
        /// </summary>
        public string FilterTablesAndViews(List<TableViewInfo> allItems)
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

        /// <summary>
        /// Gets search value and type from user input
        /// </summary>
        public string GetSearchValue()
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
    }
}