#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int MaxLength { get; set; }
        public bool IsNullable { get; set; }
    }
}