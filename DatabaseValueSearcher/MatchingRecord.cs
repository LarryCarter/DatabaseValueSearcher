#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class MatchingRecord
    {
        public string ColumnName { get; set; } = string.Empty;
        public string MatchingValue { get; set; } = string.Empty;
        public Dictionary<string, string> PrimaryKeyValues { get; set; } = new Dictionary<string, string>();
        public int RowIndex { get; set; }
    }
}