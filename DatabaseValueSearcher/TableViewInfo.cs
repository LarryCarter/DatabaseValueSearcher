#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class TableViewInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // T = Table, V = View
        public long RowCount { get; set; }
    }
}