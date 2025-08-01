#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class SearchSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string Environment { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public CachedTableData? CachedData { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int LastSearchedPage { get; set; } = 0;
    }
}