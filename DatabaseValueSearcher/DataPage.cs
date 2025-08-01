#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class DataPage
    {
        public int PageNumber { get; set; }
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();
        public bool IsLastPage { get; set; }
    }
}