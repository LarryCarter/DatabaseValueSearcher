using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace DatabaseValueSearcher
{
    public class DatabaseService
    {
        private readonly PerformanceManager performanceManager;

        public DatabaseService(PerformanceManager performanceManager)
        {
            this.performanceManager = performanceManager;
        }

        /// <summary>
        /// Gets string columns from a table's schema
        /// </summary>
        public async Task<List<ColumnInfo>> GetStringColumnsAsync(SqlConnection conn, string schemaName, string tableName)
        {
            var columns = new List<ColumnInfo>();

            string sql = @"
                SELECT COLUMN_NAME, DATA_TYPE, 
                       ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) as MaxLength,
                       IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @SchemaName
                  AND TABLE_NAME = @TableName
                  AND DATA_TYPE IN ('varchar', 'nvarchar', 'char', 'nchar', 'text', 'ntext')
                ORDER BY ORDINAL_POSITION";

            using var cmd = new SqlCommand(sql, conn);
            performanceManager.ConfigureCommand(cmd);
            cmd.Parameters.AddWithValue("@SchemaName", schemaName);
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

        /// <summary>
        /// Gets primary key columns for a table
        /// </summary>
        public async Task<List<string>> GetPrimaryKeyColumnsAsync(SqlConnection conn, string schemaName, string tableName)
        {
            var primaryKeys = new List<string>();

            string sql = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1
                  AND TABLE_SCHEMA = @SchemaName
                  AND TABLE_NAME = @TableName
                ORDER BY ORDINAL_POSITION";

            try
            {
                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);
                cmd.Parameters.AddWithValue("@SchemaName", schemaName);
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

        /// <summary>
        /// Gets list of databases from SQL Server instance
        /// </summary>
        public async Task<List<string>> GetDatabaseListAsync(string baseConnectionString)
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

        /// <summary>
        /// Gets tables and views from a database with row counts - ALL SCHEMAS
        /// </summary>
        public async Task<List<TableViewInfo>> GetTablesAndViewsAsync(string connectionString)
        {
            var items = new List<TableViewInfo>();

            await performanceManager.ExecuteWithThrottling(async () =>
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                performanceManager.ConfigureConnection(conn);

                // Updated query to include ALL schemas, not just 'dbo'
                string sql = @"
                    SELECT 
                        t.TABLE_SCHEMA as SchemaName,
                        t.TABLE_NAME as Name,
                        t.TABLE_TYPE,
                        CASE 
                            WHEN t.TABLE_TYPE = 'BASE TABLE' THEN 'T'
                            WHEN t.TABLE_TYPE = 'VIEW' THEN 'V'
                            ELSE 'O'
                        END as Type
                    FROM INFORMATION_SCHEMA.TABLES t
                    WHERE t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                    ORDER BY t.TABLE_SCHEMA, t.TABLE_TYPE, t.TABLE_NAME";

                using var cmd = new SqlCommand(sql, conn);
                performanceManager.ConfigureCommand(cmd);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var item = new TableViewInfo
                    {
                        SchemaName = reader["SchemaName"]?.ToString() ?? "dbo",
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

                        string countSql = $"SELECT COUNT(*) FROM [{item.SchemaName}].[{item.Name}]";
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

        /// <summary>
        /// Gets base connection string for an environment
        /// </summary>
        public static string GetBaseConnectionString(string environment)
        {
            var connectionString = ConfigurationManager.ConnectionStrings[environment];
            if (connectionString == null)
            {
                throw new ArgumentException($"Environment '{environment}' not found in configuration.");
            }
            return connectionString.ConnectionString;
        }

        /// <summary>
        /// Gets full connection string with specific database
        /// </summary>
        public static string GetFullConnectionString(string environment, string database)
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
