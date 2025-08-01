#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseValueSearcher
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public class LoggingService
    {
        private readonly string logDirectory;
        private readonly string logFileName;
        private readonly LogLevel minimumLogLevel;
        private readonly bool enableConsoleLogging;
        private readonly bool enableFileLogging;
        private readonly int maxLogFileSizeMB;
        private readonly int maxLogFiles;
        private readonly object lockObject = new object();

        public LoggingService()
        {
            logDirectory = ConfigurationManager.AppSettings["LogDirectory"] ?? "./Logs";
            var baseFileName = ConfigurationManager.AppSettings["LogFileName"] ?? "DatabaseValueSearcher";
            logFileName = $"{baseFileName}_{DateTime.Now:yyyyMMdd}.log";

            Enum.TryParse(ConfigurationManager.AppSettings["MinimumLogLevel"] ?? "Info", out minimumLogLevel);
            enableConsoleLogging = bool.Parse(ConfigurationManager.AppSettings["EnableConsoleLogging"] ?? "true");
            enableFileLogging = bool.Parse(ConfigurationManager.AppSettings["EnableFileLogging"] ?? "true");
            maxLogFileSizeMB = int.Parse(ConfigurationManager.AppSettings["MaxLogFileSizeMB"] ?? "10");
            maxLogFiles = int.Parse(ConfigurationManager.AppSettings["MaxLogFiles"] ?? "5");

            if (enableFileLogging && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
        }

        public void LogDebug(string message, Exception? exception = null)
        {
            Log(LogLevel.Debug, message, exception);
        }

        public void LogInfo(string message, Exception? exception = null)
        {
            Log(LogLevel.Info, message, exception);
        }

        public void LogWarning(string message, Exception? exception = null)
        {
            Log(LogLevel.Warning, message, exception);
        }

        public void LogError(string message, Exception? exception = null)
        {
            Log(LogLevel.Error, message, exception);
        }

        public void LogCritical(string message, Exception? exception = null)
        {
            Log(LogLevel.Critical, message, exception);
        }

        public void LogPerformance(string operation, long milliseconds, string? details = null)
        {
            var message = $"PERFORMANCE: {operation} completed in {milliseconds}ms";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" - {details}";
            }
            Log(LogLevel.Info, message);
        }

        public void LogDatabaseOperation(string operation, string environment, string database, string? table = null, long? milliseconds = null)
        {
            var message = $"DATABASE: {operation} - {environment}.{database}";
            if (!string.IsNullOrEmpty(table))
            {
                message += $".{table}";
            }
            if (milliseconds.HasValue)
            {
                message += $" ({milliseconds}ms)";
            }
            Log(LogLevel.Info, message);
        }

        public void LogCacheOperation(string operation, string cacheKey, string? details = null)
        {
            var message = $"CACHE: {operation} - {cacheKey}";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" - {details}";
            }
            Log(LogLevel.Debug, message);
        }

        public void LogSearchOperation(string searchType, string pattern, int matchCount, long milliseconds, int pagesProcessed)
        {
            var message = $"SEARCH: {searchType} pattern '{pattern}' found {matchCount} matches in {pagesProcessed} pages ({milliseconds}ms)";
            Log(LogLevel.Info, message);
        }

        public void LogExportOperation(string tableName, int rowCount, long fileSizeBytes, long milliseconds)
        {
            var fileSizeMB = fileSizeBytes / 1024.0 / 1024.0;
            var message = $"EXPORT: {tableName} - {rowCount:N0} rows exported to {fileSizeMB:F1}MB file ({milliseconds}ms)";
            Log(LogLevel.Info, message);
        }

        public void LogUserAction(string action, string? details = null)
        {
            var message = $"USER: {action}";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" - {details}";
            }
            Log(LogLevel.Debug, message);
        }

        private void Log(LogLevel level, string message, Exception? exception = null)
        {
            if (level < minimumLogLevel) return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr = level.ToString().ToUpper().PadRight(8);
            var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            var logEntry = $"[{timestamp}] [{levelStr}] [T:{threadId:D2}] {message}";

            if (exception != null)
            {
                logEntry += $"{Environment.NewLine}Exception: {exception.GetType().Name}: {exception.Message}";
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    logEntry += $"{Environment.NewLine}StackTrace: {exception.StackTrace}";
                }
                if (exception.InnerException != null)
                {
                    logEntry += $"{Environment.NewLine}InnerException: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}";
                }
            }

            // Console logging
            if (enableConsoleLogging)
            {
                LogToConsole(level, logEntry);
            }

            // File logging
            if (enableFileLogging)
            {
                LogToFile(logEntry);
            }
        }

        private void LogToConsole(LogLevel level, string logEntry)
        {
            var originalColor = Console.ForegroundColor;

            Console.ForegroundColor = level switch
            {
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.Magenta,
                _ => ConsoleColor.White
            };

            Console.WriteLine(logEntry);
            Console.ForegroundColor = originalColor;
        }

        private void LogToFile(string logEntry)
        {
            lock (lockObject)
            {
                try
                {
                    var logFilePath = Path.Combine(logDirectory, logFileName);

                    // Check if log rotation is needed
                    if (File.Exists(logFilePath))
                    {
                        var fileInfo = new FileInfo(logFilePath);
                        if (fileInfo.Length > maxLogFileSizeMB * 1024 * 1024)
                        {
                            RotateLogFiles();
                        }
                    }

                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // Fallback to console if file logging fails
                    Console.WriteLine($"LOGGING ERROR: Failed to write to log file: {ex.Message}");
                    Console.WriteLine(logEntry);
                }
            }
        }

        private void RotateLogFiles()
        {
            try
            {
                var baseLogPath = Path.Combine(logDirectory, Path.GetFileNameWithoutExtension(logFileName));
                var extension = Path.GetExtension(logFileName);
                var currentLogPath = Path.Combine(logDirectory, logFileName);

                // Move existing numbered log files up
                for (int i = maxLogFiles - 1; i >= 1; i--)
                {
                    var oldFile = $"{baseLogPath}.{i}{extension}";
                    var newFile = $"{baseLogPath}.{i + 1}{extension}";

                    if (File.Exists(oldFile))
                    {
                        if (File.Exists(newFile))
                        {
                            File.Delete(newFile);
                        }
                        File.Move(oldFile, newFile);
                    }
                }

                // Move current log to .1
                if (File.Exists(currentLogPath))
                {
                    var archivedFile = $"{baseLogPath}.1{extension}";
                    if (File.Exists(archivedFile))
                    {
                        File.Delete(archivedFile);
                    }
                    File.Move(currentLogPath, archivedFile);
                }

                // Clean up old files beyond maxLogFiles
                for (int i = maxLogFiles + 1; i <= maxLogFiles + 5; i++)
                {
                    var oldFile = $"{baseLogPath}.{i}{extension}";
                    if (File.Exists(oldFile))
                    {
                        File.Delete(oldFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LOGGING ERROR: Failed to rotate log files: {ex.Message}");
            }
        }

        public LogStatistics GetLogStatistics()
        {
            var stats = new LogStatistics
            {
                LogDirectory = logDirectory,
                CurrentLogFile = logFileName,
                MinimumLogLevel = minimumLogLevel,
                ConsoleLoggingEnabled = enableConsoleLogging,
                FileLoggingEnabled = enableFileLogging
            };

            if (enableFileLogging && Directory.Exists(logDirectory))
            {
                try
                {
                    var logFiles = Directory.GetFiles(logDirectory, "*.log");
                    stats.LogFileCount = logFiles.Length;

                    long totalSize = 0;
                    foreach (var file in logFiles)
                    {
                        totalSize += new FileInfo(file).Length;
                    }
                    stats.TotalLogSizeBytes = totalSize;

                    var currentLogPath = Path.Combine(logDirectory, logFileName);
                    if (File.Exists(currentLogPath))
                    {
                        stats.CurrentLogSizeBytes = new FileInfo(currentLogPath).Length;
                    }
                }
                catch (Exception ex)
                {
                    LogError("Failed to get log statistics", ex);
                }
            }

            return stats;
        }

        public void CleanupOldLogs(int daysToKeep = 30)
        {
            if (!enableFileLogging || !Directory.Exists(logDirectory)) return;

            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logFiles = Directory.GetFiles(logDirectory, "*.log");

                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(file);
                        LogInfo($"Deleted old log file: {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to cleanup old log files", ex);
            }
        }
    }

    public class LogStatistics
    {
        public string LogDirectory { get; set; } = string.Empty;
        public string CurrentLogFile { get; set; } = string.Empty;
        public LogLevel MinimumLogLevel { get; set; }
        public bool ConsoleLoggingEnabled { get; set; }
        public bool FileLoggingEnabled { get; set; }
        public int LogFileCount { get; set; }
        public long TotalLogSizeBytes { get; set; }
        public long CurrentLogSizeBytes { get; set; }

        public string TotalLogSizeDisplay => TotalLogSizeBytes < 1024 * 1024
            ? $"{TotalLogSizeBytes / 1024:N0} KB"
            : $"{TotalLogSizeBytes / 1024 / 1024:N1} MB";

        public string CurrentLogSizeDisplay => CurrentLogSizeBytes < 1024 * 1024
            ? $"{CurrentLogSizeBytes / 1024:N0} KB"
            : $"{CurrentLogSizeBytes / 1024 / 1024:N1} MB";
    }
}