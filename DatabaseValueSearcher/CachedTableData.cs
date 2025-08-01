#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class CachedTableData
    {
        public string Environment { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        public List<string> PrimaryKeys { get; set; } = new List<string>();
        public DateTime CachedAt { get; set; }
        public long TotalRows { get; set; }
        public int PageSize { get; set; }
        public bool IsComplete { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}