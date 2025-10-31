#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DatabaseValueSearcher
{
    public class ImportService
    {
        private readonly PerformanceManager performanceManager;

        public ImportService(PerformanceManager performanceManager)
        {
            this.performanceManager = performanceManager;
        }

        /// <summary>
        /// Shows import menu and handles file selection and import
        /// </summary>
        public async Task ImportMenu(string? currentEnvironment = null, string? currentDatabase = null)
        {
            Console.WriteLine();
            DisplayMessages.WriteInfo("SQL FILE IMPORT");
            DisplayMessages.WriteInfo("================");

            // Check for export directory
            var exportPath = Path.Combine(Environment.CurrentDirectory, "Exports");
            if (!Directory.Exists(exportPath))
            {
                DisplayMessages.WriteWarning("No Exports directory found. Please export data first.");
                return;
            }

            // Get all SQL files
            var sqlFiles = Directory.GetFiles(exportPath, "*.sql").OrderByDescending(f => new FileInfo(f).LastWriteTime).ToList();

            if (!sqlFiles.Any())
            {
                DisplayMessages.WriteWarning("No SQL export files found in Exports directory.");
                return;
            }

            Console.WriteLine();
            DisplayMessages.WriteInfo("Available SQL Export Files:");
            for (int i = 0; i < sqlFiles.Count; i++)
            {
                var fileInfo = new FileInfo(sqlFiles[i]);
                var fileName = Path.GetFileName(sqlFiles[i]);
                var fileSize = fileInfo.Length < 1024 * 1024
                    ? $"{fileInfo.Length / 1024:N0} KB"
                    : $"{fileInfo.Length / 1024 / 1024:N1} MB";
                var age = DateTime.Now - fileInfo.LastWriteTime;
                var ageStr = age.TotalHours < 24
                    ? $"{age.TotalHours:F0}h ago"
                    : $"{age.TotalDays:F0}d ago";

                Console.WriteLine($"  {i + 1}. {fileName} ({fileSize}, {ageStr})");
            }

            Console.WriteLine();
            Console.Write($"Select file to import (1-{sqlFiles.Count}) [back]: ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (input.Equals("back", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input))
                return;

            if (!int.TryParse(input, out int selection) || selection < 1 || selection > sqlFiles.Count)
            {
                DisplayMessages.WriteError("Invalid selection.");
                return;
            }

            var selectedFile = sqlFiles[selection - 1];
            await ImportSQLFile(selectedFile, currentEnvironment, currentDatabase);
        }

        /// <summary>
        /// Imports a SQL file into a target database
        /// </summary>
        public async Task ImportSQLFile(string filePath, string? suggestedEnvironment = null, string? suggestedDatabase = null)
        {
            try
            {
                DisplayMessages.WriteInfo($"Analyzing SQL file: {Path.GetFileName(filePath)}");

                // Parse the SQL file header to get metadata
                var metadata = ParseSQLFileMetadata(filePath);

                if (metadata != null)
                {
                    DisplayMessages.WriteInfo("File Metadata:");
                    Console.WriteLine($"  Source: {metadata.SourceEnvironment}.{metadata.SourceDatabase}.{metadata.TableName}");
                    Console.WriteLine($"  Generated: {metadata.GeneratedDate:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"  Total Rows: {metadata.TotalRows:N0}");
                }

                Console.WriteLine();

                // Select target environment
                string targetEnvironment = suggestedEnvironment ?? SelectTargetEnvironment();
                if (targetEnvironment == "back" || targetEnvironment == "quit") return;

                // Select target database
                string targetDatabase = suggestedDatabase ?? await SelectTargetDatabase(targetEnvironment);
                if (targetDatabase == "back" || targetDatabase == "quit") return;

                // Get table name (default to source table name if available)
                string tableName = metadata?.TableName ?? "";
                Console.WriteLine();
                Console.Write($"Target table name [{tableName}]: ");
                string tableInput = Console.ReadLine()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(tableInput))
                {
                    tableName = tableInput;
                }

                if (string.IsNullOrEmpty(tableName))
                {
                    DisplayMessages.WriteError("Table name is required.");
                    return;
                }

                // Import options
                Console.WriteLine();
                DisplayMessages.WriteInfo("Import Options:");
                Console.WriteLine("  1. Create table and import data (default)");
                Console.WriteLine("  2. Import data only (skip CREATE TABLE)");
                Console.WriteLine("  3. Truncate existing data and import");
                Console.WriteLine("  4. Drop table and recreate");
                Console.Write("Select option (1-4) [1]: ");
                string optionInput = Console.ReadLine()?.Trim() ?? "1";

                var importOptions = new ImportOptions
                {
                    CreateTable = optionInput == "1" || optionInput == "4",
                    DropTableFirst = optionInput == "4",
                    TruncateFirst = optionInput == "3",
                    SkipCreateTable = optionInput == "2"
                };

                // Confirm import
                Console.WriteLine();
                DisplayMessages.WriteWarning("IMPORT CONFIRMATION");
                Console.WriteLine($"  File: {Path.GetFileName(filePath)}");
                Console.WriteLine($"  Target: {targetEnvironment}.{targetDatabase}.{tableName}");
                Console.WriteLine($"  Rows: {(metadata?.TotalRows.ToString("N0") ?? "Unknown")}");
                if (importOptions.DropTableFirst)
                    DisplayMessages.WriteColoredInline("  WARNING: ", ConsoleColor.Red);
                if (importOptions.DropTableFirst)
                    Console.WriteLine("Table will be DROPPED and recreated!");
                if (importOptions.TruncateFirst)
                    DisplayMessages.WriteColoredInline("  WARNING: ", ConsoleColor.Yellow);
                if (importOptions.TruncateFirst)
                    Console.WriteLine("Existing data will be TRUNCATED!");

                Console.WriteLine();
                Console.Write("Proceed with import? [y/N]: ");
                string confirm = Console.ReadLine()?.Trim() ?? "";

                if (!confirm.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    DisplayMessages.WriteInfo("Import cancelled.");
                    return;
                }

                // Execute import
                await ExecuteImport(filePath, targetEnvironment, targetDatabase, tableName, importOptions);
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"Import failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes the SQL import operation
        /// </summary>
        private async Task ExecuteImport(string filePath, string environment, string database, string tableName, ImportOptions options)
        {
            var connectionString = DatabaseService.GetFullConnectionString(environment, database);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int rowsImported = 0;
            int batchesExecuted = 0;

            try
            {
                DisplayMessages.WriteInfo("Starting import...");

                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                // Drop table if requested
                if (options.DropTableFirst)
                {
                    DisplayMessages.WriteInfo($"Dropping table {tableName}...");
                    await ExecuteNonQuery(conn, $"DROP TABLE IF EXISTS [{tableName}]");
                }

                // Truncate if requested
                if (options.TruncateFirst)
                {
                    DisplayMessages.WriteInfo($"Truncating table {tableName}...");
                    await ExecuteNonQuery(conn, $"TRUNCATE TABLE [{tableName}]");
                }

                // Read and execute SQL file
                using var reader = new StreamReader(filePath);
                var sqlBatch = new StringBuilder();
                string? line;
                int lineNumber = 0;
                bool inCreateTable = false;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    lineNumber++;

                    // Skip comments
                    if (line.TrimStart().StartsWith("--"))
                    {
                        // Check if we're entering CREATE TABLE section
                        if (line.Contains("Create table script"))
                        {
                            inCreateTable = true;
                        }
                        continue;
                    }

                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Handle CREATE TABLE section
                    if (inCreateTable && options.SkipCreateTable)
                    {
                        // Skip CREATE TABLE if option is set
                        if (line.TrimEnd().EndsWith(";"))
                        {
                            inCreateTable = false;
                        }
                        continue;
                    }

                    sqlBatch.AppendLine(line);

                    // Execute when we hit a semicolon
                    if (line.TrimEnd().EndsWith(";"))
                    {
                        var sql = sqlBatch.ToString();
                        sqlBatch.Clear();

                        try
                        {
                            var result = await ExecuteNonQuery(conn, sql);

                            if (sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                            {
                                rowsImported += result;
                                batchesExecuted++;

                                // Show progress
                                if (batchesExecuted % 10 == 0)
                                {
                                    DisplayMessages.WriteInfo($"Progress: {rowsImported:N0} rows imported ({batchesExecuted} batches)...");
                                }
                            }
                            else if (sql.TrimStart().StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
                            {
                                DisplayMessages.WriteSuccess($"Table {tableName} created successfully.");
                            }
                        }
                        catch (SqlException ex)
                        {
                            DisplayMessages.WriteError($"Error at line {lineNumber}: {ex.Message}");

                            // Ask if user wants to continue
                            Console.Write("Continue with remaining batches? [y/N]: ");
                            string continueInput = Console.ReadLine()?.Trim() ?? "";
                            if (!continueInput.Equals("y", StringComparison.OrdinalIgnoreCase))
                            {
                                throw;
                            }
                        }
                    }
                }

                stopwatch.Stop();

                Console.WriteLine();
                DisplayMessages.WriteSuccess("Import completed successfully!");
                DisplayMessages.WriteInfo("Import Summary:");
                Console.WriteLine($"  Target: {environment}.{database}.{tableName}");
                Console.WriteLine($"  Rows Imported: {rowsImported:N0}");
                Console.WriteLine($"  Batches Executed: {batchesExecuted}");
                Console.WriteLine($"  Time Elapsed: {stopwatch.ElapsedMilliseconds:N0} ms");
                Console.WriteLine($"  Average Speed: {(rowsImported / (stopwatch.ElapsedMilliseconds / 1000.0)):N0} rows/sec");
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"Import failed: {ex.Message}");
                DisplayMessages.WriteInfo($"Rows imported before error: {rowsImported:N0}");
            }
        }

        /// <summary>
        /// Executes a non-query SQL command
        /// </summary>
        private async Task<int> ExecuteNonQuery(SqlConnection conn, string sql)
        {
            using var cmd = new SqlCommand(sql, conn);
            performanceManager.ConfigureCommand(cmd);
            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Parses SQL file metadata from header comments
        /// </summary>
        private ImportFileMetadata? ParseSQLFileMetadata(string filePath)
        {
            try
            {
                var metadata = new ImportFileMetadata();
                using var reader = new StreamReader(filePath);

                for (int i = 0; i < 10; i++) // Read first 10 lines for metadata
                {
                    var line = reader.ReadLine();
                    if (line == null) break;

                    if (line.Contains("-- Source:"))
                    {
                        // Parse: -- Source: Environment.Database.TableName
                        var match = Regex.Match(line, @"Source:\s*(.+?)\.(.+?)\.(.+)");
                        if (match.Success)
                        {
                            metadata.SourceEnvironment = match.Groups[1].Value.Trim();
                            metadata.SourceDatabase = match.Groups[2].Value.Trim();
                            metadata.TableName = match.Groups[3].Value.Trim();
                        }
                    }
                    else if (line.Contains("-- Generated:"))
                    {
                        var match = Regex.Match(line, @"Generated:\s*(.+)");
                        if (match.Success && DateTime.TryParse(match.Groups[1].Value.Trim(), out var date))
                        {
                            metadata.GeneratedDate = date;
                        }
                    }
                    else if (line.Contains("-- Total Rows:"))
                    {
                        var match = Regex.Match(line, @"Total Rows:\s*([\d,]+)");
                        if (match.Success)
                        {
                            var rowsStr = match.Groups[1].Value.Replace(",", "");
                            if (long.TryParse(rowsStr, out var rows))
                            {
                                metadata.TotalRows = rows;
                            }
                        }
                    }
                }

                return metadata.TableName != null ? metadata : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Selects target environment for import
        /// </summary>
        private string SelectTargetEnvironment()
        {
            var environments = new Dictionary<string, string>();
            var envSection = System.Configuration.ConfigurationManager.GetSection("databaseEnvironments") as System.Collections.Specialized.NameValueCollection;

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

            if (environments.Count == 0)
            {
                foreach (System.Configuration.ConnectionStringSettings connStr in System.Configuration.ConfigurationManager.ConnectionStrings)
                {
                    if (connStr.Name != "LocalSqlServer")
                    {
                        environments[connStr.Name] = connStr.Name;
                    }
                }
            }

            DisplayMessages.WriteInfo("Target Environment:");
            var envList = environments.ToList();

            for (int i = 0; i < envList.Count; i++)
            {
                string indicator = envList[i].Key.Equals("Prod", StringComparison.OrdinalIgnoreCase) ? " [PRODUCTION - BE CAREFUL!]" : "";
                Console.Write($"  {i + 1}. {envList[i].Value}");
                if (indicator != "")
                {
                    DisplayMessages.WriteColoredInline(indicator, ConsoleColor.Red);
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine();
                }
            }

            while (true)
            {
                Console.Write($"Select environment (1-{envList.Count}) [back]: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input.Equals("back", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input))
                    return "back";

                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= envList.Count)
                {
                    return envList[selection - 1].Key;
                }

                DisplayMessages.WriteError("Invalid selection.");
            }
        }

        /// <summary>
        /// Selects target database for import
        /// </summary>
        private async Task<string> SelectTargetDatabase(string environment)
        {
            try
            {
                DisplayMessages.WriteInfo("Loading databases...");
                var databaseService = new DatabaseService(performanceManager);
                string baseConnectionString = DatabaseService.GetBaseConnectionString(environment);
                var databases = await databaseService.GetDatabaseListAsync(baseConnectionString);

                if (!databases.Any())
                {
                    DisplayMessages.WriteError("No databases found.");
                    return "back";
                }

                DisplayMessages.WriteInfo("Target Database:");
                for (int i = 0; i < databases.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {databases[i]}");
                }

                while (true)
                {
                    Console.Write($"Select database (1-{databases.Count}) [back]: ");
                    string input = Console.ReadLine()?.Trim() ?? "";

                    if (input.Equals("back", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input))
                        return "back";

                    if (int.TryParse(input, out int selection) && selection >= 1 && selection <= databases.Count)
                    {
                        return databases[selection - 1];
                    }

                    DisplayMessages.WriteError("Invalid selection.");
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteError($"Error loading databases: {ex.Message}");
                return "back";
            }
        }
    }

    /// <summary>
    /// Metadata parsed from SQL export file
    /// </summary>
    public class ImportFileMetadata
    {
        public string? SourceEnvironment { get; set; }
        public string? SourceDatabase { get; set; }
        public string? TableName { get; set; }
        public DateTime GeneratedDate { get; set; }
        public long TotalRows { get; set; }
    }

    /// <summary>
    /// Options for import operation
    /// </summary>
    public class ImportOptions
    {
        public bool CreateTable { get; set; } = true;
        public bool DropTableFirst { get; set; } = false;
        public bool TruncateFirst { get; set; } = false;
        public bool SkipCreateTable { get; set; } = false;
    }
}