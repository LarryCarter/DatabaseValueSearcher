#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Configuration;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace DatabaseValueSearcher
{
    public class CacheManager
    {
        private readonly string cacheDirectory;
        private readonly bool enableCaching;
        private readonly int cacheExpiryHours;
        private readonly int maxCacheFileSizeMB;
        private readonly bool compressCache;

        public CacheManager()
        {
            cacheDirectory = ConfigurationManager.AppSettings["CacheDirectory"] ?? "./Cache";
            enableCaching = bool.Parse(ConfigurationManager.AppSettings["EnableCaching"] ?? "true");
            cacheExpiryHours = int.Parse(ConfigurationManager.AppSettings["CacheExpiryHours"] ?? "24");
            maxCacheFileSizeMB = int.Parse(ConfigurationManager.AppSettings["MaxCacheFileSizeMB"] ?? "100");
            compressCache = bool.Parse(ConfigurationManager.AppSettings["CompressCache"] ?? "true");

            if (enableCaching && !Directory.Exists(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }
        }

        public string GetCacheKey(string environment, string database, string tableName)
        {
            return $"{environment}_{database}_{tableName}".Replace(" ", "_");
        }

        public string GetMetadataPath(string cacheKey)
        {
            return Path.Combine(cacheDirectory, $"{cacheKey}_metadata.json");
        }

        public string GetDataPath(string cacheKey, int pageNumber)
        {
            var extension = compressCache ? ".json.gz" : ".json";
            return Path.Combine(cacheDirectory, $"{cacheKey}_page_{pageNumber:D6}{extension}");
        }

        public bool IsCacheValid(string cacheKey)
        {
            if (!enableCaching) return false;

            var metadataPath = GetMetadataPath(cacheKey);
            if (!File.Exists(metadataPath)) return false;

            try
            {
                var metadataJson = File.ReadAllText(metadataPath);
                var metadata = JsonSerializer.Deserialize<CachedTableData>(metadataJson);
                if (metadata == null) return false;

                var ageHours = (DateTime.Now - metadata.CachedAt).TotalHours;
                return ageHours < cacheExpiryHours;
            }
            catch
            {
                return false;
            }
        }

        public CachedTableData? LoadMetadata(string cacheKey)
        {
            if (!enableCaching || !IsCacheValid(cacheKey)) return null;

            try
            {
                var metadataPath = GetMetadataPath(cacheKey);
                var metadataJson = File.ReadAllText(metadataPath);
                return JsonSerializer.Deserialize<CachedTableData>(metadataJson);
            }
            catch
            {
                return null;
            }
        }

        public void SaveMetadata(string cacheKey, CachedTableData metadata)
        {
            if (!enableCaching) return;

            try
            {
                var metadataPath = GetMetadataPath(cacheKey);
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(metadataPath, json);
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Failed to save metadata: {ex.Message}");
            }
        }

        public DataPage? LoadPage(string cacheKey, int pageNumber)
        {
            if (!enableCaching) return null;

            try
            {
                var dataPath = GetDataPath(cacheKey, pageNumber);
                if (!File.Exists(dataPath)) return null;

                string json;
                if (compressCache)
                {
                    using var fileStream = File.OpenRead(dataPath);
                    using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzipStream);
                    json = reader.ReadToEnd();
                }
                else
                {
                    json = File.ReadAllText(dataPath);
                }

                return JsonSerializer.Deserialize<DataPage>(json);
            }
            catch
            {
                return null;
            }
        }

        public void SavePage(string cacheKey, int pageNumber, DataPage page)
        {
            if (!enableCaching) return;

            try
            {
                var dataPath = GetDataPath(cacheKey, pageNumber);
                var json = JsonSerializer.Serialize(page, new JsonSerializerOptions { WriteIndented = false });

                if (compressCache)
                {
                    using var fileStream = File.Create(dataPath);
                    using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(gzipStream);
                    writer.Write(json);
                }
                else
                {
                    File.WriteAllText(dataPath, json);
                }

                // Check file size
                var fileInfo = new FileInfo(dataPath);
                if (fileInfo.Length > maxCacheFileSizeMB * 1024 * 1024)
                {
                    DisplayMessages.WriteWarning($"Cache file {dataPath} exceeds maximum size limit");
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Failed to save page: {ex.Message}");
            }
        }

        public void ClearCache(string? cacheKey = null)
        {
            if (!enableCaching) return;

            try
            {
                if (cacheKey == null)
                {
                    // Clear all cache
                    if (Directory.Exists(cacheDirectory))
                    {
                        Directory.Delete(cacheDirectory, true);
                        Directory.CreateDirectory(cacheDirectory);
                    }
                }
                else
                {
                    // Clear specific cache
                    var files = Directory.GetFiles(cacheDirectory, $"{cacheKey}*");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Failed to clear cache: {ex.Message}");
            }
        }

        public long GetCacheSize()
        {
            if (!enableCaching || !Directory.Exists(cacheDirectory)) return 0;

            try
            {
                return Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets all cached page numbers for a specific table
        /// </summary>
        public List<int> GetCachedPageNumbers(string cacheKey)
        {
            var pageNumbers = new List<int>();

            if (!enableCaching || !Directory.Exists(cacheDirectory))
                return pageNumbers;

            try
            {
                var extension = compressCache ? ".json.gz" : ".json";
                var pattern = $"{cacheKey}_page_*{extension}";
                var files = Directory.GetFiles(cacheDirectory, pattern);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (compressCache) // Remove .json part if compressed
                        fileName = Path.GetFileNameWithoutExtension(fileName);

                    // Extract page number from filename like "cachekey_page_000001"
                    var parts = fileName.Split('_');
                    if (parts.Length >= 3 && int.TryParse(parts[^1], out int pageNum))
                    {
                        pageNumbers.Add(pageNum);
                    }
                }

                pageNumbers.Sort();
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Failed to get cached page numbers: {ex.Message}");
            }

            return pageNumbers;
        }

        /// <summary>
        /// Gets cache statistics for a specific table
        /// </summary>
        public CacheStatistics GetCacheStatistics(string cacheKey)
        {
            var stats = new CacheStatistics
            {
                CacheKey = cacheKey,
                HasMetadata = false,
                CachedPages = 0,
                TotalCacheSize = 0,
                LastModified = DateTime.MinValue
            };

            if (!enableCaching || !Directory.Exists(cacheDirectory))
                return stats;

            try
            {
                // Check metadata
                var metadataPath = GetMetadataPath(cacheKey);
                if (File.Exists(metadataPath))
                {
                    stats.HasMetadata = true;
                    var metadataInfo = new FileInfo(metadataPath);
                    stats.LastModified = metadataInfo.LastWriteTime;
                    stats.TotalCacheSize += metadataInfo.Length;
                }

                // Count pages and size
                var extension = compressCache ? ".json.gz" : ".json";
                var pattern = $"{cacheKey}_page_*{extension}";
                var pageFiles = Directory.GetFiles(cacheDirectory, pattern);

                stats.CachedPages = pageFiles.Length;

                foreach (var file in pageFiles)
                {
                    var fileInfo = new FileInfo(file);
                    stats.TotalCacheSize += fileInfo.Length;

                    if (fileInfo.LastWriteTime > stats.LastModified)
                        stats.LastModified = fileInfo.LastWriteTime;
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Failed to get cache statistics: {ex.Message}");
            }

            return stats;
        }

        /// <summary>
        /// Gets all cached tables across all environments
        /// </summary>
        public List<CacheStatistics> GetAllCachedTables()
        {
            var cachedTables = new List<CacheStatistics>();

            if (!enableCaching || !Directory.Exists(cacheDirectory))
                return cachedTables;

            try
            {
                var metadataFiles = Directory.GetFiles(cacheDirectory, "*_metadata.json");
                var processedKeys = new HashSet<string>();

                foreach (var metadataFile in metadataFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(metadataFile);
                    var cacheKey = fileName.Replace("_metadata", "");

                    if (!processedKeys.Contains(cacheKey))
                    {
                        processedKeys.Add(cacheKey);
                        var stats = GetCacheStatistics(cacheKey);

                        // Try to load metadata to get table info
                        try
                        {
                            var metadata = LoadMetadata(cacheKey);
                            if (metadata != null)
                            {
                                stats.Environment = metadata.Environment;
                                stats.Database = metadata.Database;
                                stats.TableName = metadata.TableName;
                                stats.TotalRows = metadata.TotalRows;
                                stats.CachedAt = metadata.CachedAt;
                                stats.IsComplete = metadata.IsComplete;
                            }
                        }
                        catch
                        {
                            // If we can't load metadata, still include in list
                        }

                        cachedTables.Add(stats);
                    }
                }
            }
            catch (Exception ex)
            {
                DisplayMessages.WriteWarning($"Failed to get cached tables list: {ex.Message}");
            }

            return cachedTables.OrderBy(t => t.Environment)
                              .ThenBy(t => t.Database)
                              .ThenBy(t => t.TableName)
                              .ToList();
        }

        /// <summary>
        /// Validates cache integrity for a specific table
        /// </summary>
        public CacheValidationResult ValidateCache(string cacheKey)
        {
            var result = new CacheValidationResult
            {
                CacheKey = cacheKey,
                IsValid = false,
                Issues = new List<string>()
            };

            if (!enableCaching)
            {
                result.Issues.Add("Caching is disabled");
                return result;
            }

            try
            {
                // Check if metadata exists
                var metadata = LoadMetadata(cacheKey);
                if (metadata == null)
                {
                    result.Issues.Add("Metadata file missing or invalid");
                    return result;
                }

                // Check if cache is expired
                if (!IsCacheValid(cacheKey))
                {
                    result.Issues.Add($"Cache expired (age: {(DateTime.Now - metadata.CachedAt).TotalHours:F1} hours)");
                }

                // Check page integrity
                var expectedPages = (int)Math.Ceiling((double)metadata.TotalRows / metadata.PageSize);
                var cachedPages = GetCachedPageNumbers(cacheKey);

                result.ExpectedPages = expectedPages;
                result.ActualPages = cachedPages.Count;

                if (cachedPages.Count == 0)
                {
                    result.Issues.Add("No data pages found");
                    return result;
                }

                // Check for missing pages
                var missingPages = new List<int>();
                for (int i = 1; i <= expectedPages; i++)
                {
                    if (!cachedPages.Contains(i))
                    {
                        missingPages.Add(i);
                    }
                }

                if (missingPages.Any())
                {
                    result.Issues.Add($"Missing pages: {string.Join(", ", missingPages.Take(10))}" +
                                     (missingPages.Count > 10 ? $" (and {missingPages.Count - 10} more)" : ""));
                }

                // Try to load first and last pages to verify readability
                try
                {
                    var firstPage = LoadPage(cacheKey, 1);
                    if (firstPage == null)
                    {
                        result.Issues.Add("Cannot read first page");
                    }
                }
                catch (Exception ex)
                {
                    result.Issues.Add($"Error reading first page: {ex.Message}");
                }

                if (cachedPages.Any())
                {
                    try
                    {
                        var lastPage = LoadPage(cacheKey, cachedPages.Max());
                        if (lastPage == null)
                        {
                            result.Issues.Add("Cannot read last page");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Issues.Add($"Error reading last page: {ex.Message}");
                    }
                }

                result.IsValid = !result.Issues.Any();

                if (result.IsValid)
                {
                    result.Issues.Add("Cache validation passed");
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add($"Validation error: {ex.Message}");
            }

            return result;
        }
    }

    /// <summary>
    /// Cache statistics for a specific table
    /// </summary>
    public class CacheStatistics
    {
        public string CacheKey { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public bool HasMetadata { get; set; }
        public int CachedPages { get; set; }
        public long TotalCacheSize { get; set; }
        public DateTime LastModified { get; set; }
        public long TotalRows { get; set; }
        public DateTime CachedAt { get; set; }
        public bool IsComplete { get; set; }

        public string SizeDisplay => TotalCacheSize < 1024 * 1024
            ? $"{TotalCacheSize / 1024:N0} KB"
            : $"{TotalCacheSize / 1024 / 1024:N1} MB";
    }

    /// <summary>
    /// Cache validation result
    /// </summary>
    public class CacheValidationResult
    {
        public string CacheKey { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
        public int ExpectedPages { get; set; }
        public int ActualPages { get; set; }
    }
}