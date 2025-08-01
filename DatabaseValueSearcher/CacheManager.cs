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
                    // Replace the incorrect 'GzipStream' with the correct 'GZipStream' in the following line:
                    using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
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
    }
}