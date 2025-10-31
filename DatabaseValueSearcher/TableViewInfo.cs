using System;
using System.Linq;

namespace DatabaseValueSearcher
{
    public class TableViewInfo
    {
        public string SchemaName { get; set; } = "dbo";
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // T = Table, V = View
        public long RowCount { get; set; }

        /// <summary>
        /// Gets the fully qualified name including schema
        /// </summary>
        public string FullName => $"{SchemaName}.{Name}";

        /// <summary>
        /// Gets display name with schema prefix if not dbo
        /// </summary>
        public string DisplayName => SchemaName == "dbo" ? Name : $"{SchemaName}.{Name}";
    }
}