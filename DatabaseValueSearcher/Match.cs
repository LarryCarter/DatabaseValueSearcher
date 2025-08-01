#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DatabaseValueSearcher
{
    public static class Match
    {
        /// <summary>
        /// Searches a page of data for matching records using either LIKE or regex patterns
        /// </summary>
        public static List<MatchingRecord> SearchPageInMemory(DataPage page, List<ColumnInfo> columns, List<string> primaryKeys, string searchValue, bool useRegex)
        {
            var matches = new List<MatchingRecord>();
            Regex? regex = null;

            if (useRegex)
            {
                try
                {
                    regex = new Regex(searchValue, RegexOptions.IgnoreCase);
                }
                catch
                {
                    return matches; // Invalid regex
                }
            }

            foreach (var row in page.Rows)
            {
                foreach (var column in columns)
                {
                    if (!row.TryGetValue(column.Name, out var value) || value == null) continue;

                    var stringValue = value.ToString() ?? "";
                    bool isMatch = false;

                    if (useRegex && regex != null)
                    {
                        isMatch = regex.IsMatch(stringValue);
                    }
                    else
                    {
                        // SQL LIKE behavior using LINQ-style operations
                        isMatch = IsLikeMatch(stringValue, searchValue);
                    }

                    if (isMatch)
                    {
                        var match = new MatchingRecord
                        {
                            ColumnName = column.Name,
                            MatchingValue = stringValue
                        };

                        // Get primary key values
                        foreach (var pkColumn in primaryKeys)
                        {
                            if (row.TryGetValue(pkColumn, out var pkValue))
                            {
                                match.PrimaryKeyValues[pkColumn] = pkValue?.ToString() ?? "<NULL>";
                            }
                        }

                        matches.Add(match);
                    }
                }
            }

            return matches;
        }

        /// <summary>
        /// Mimics SQL LIKE pattern matching using LINQ-style string operations
        /// </summary>
        public static bool IsLikeMatch(string text, string pattern)
        {
            // Handle simple cases first
            if (string.IsNullOrEmpty(pattern))
                return string.IsNullOrEmpty(text);

            if (pattern == "%")
                return true; // % matches everything

            // Use LINQ-style string operations to mimic SQL LIKE behavior
            // Handle the most common LIKE patterns using string methods

            // Pattern: "Value%" (starts with)
            if (pattern.EndsWith("%") && !pattern.StartsWith("%"))
            {
                var searchValue = pattern.Substring(0, pattern.Length - 1);
                return text.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase);
            }

            // Pattern: "%Value" (ends with)
            if (pattern.StartsWith("%") && !pattern.EndsWith("%"))
            {
                var searchValue = pattern.Substring(1);
                return text.EndsWith(searchValue, StringComparison.OrdinalIgnoreCase);
            }

            // Pattern: "%Value%" (contains)
            if (pattern.StartsWith("%") && pattern.EndsWith("%"))
            {
                var searchValue = pattern.Substring(1, pattern.Length - 2);
                return text.Contains(searchValue, StringComparison.OrdinalIgnoreCase);
            }

            // Pattern: "Value" (exact match)
            if (!pattern.Contains("%"))
            {
                return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }

            // For any other patterns, just do a contains search
            // This covers most real-world usage without complex wildcard handling
            return text.Contains(pattern.Replace("%", ""), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Test method to validate LIKE pattern matching behavior including multiple patterns
        /// </summary>
        public static void TestLikePatterns()
        {
            var testCases = new[]
            {
                // Single pattern tests
                ("A%", "AD_Managers", true),
                ("A%", "BD_Managers", false),
                ("%Manager%", "AD_Managers", true),
                ("%Manager%", "AD_Users", false),
                ("%D_A%", "AD_Administrators", true),
                ("%D_A%", "BD_Users", false),
                ("Test", "Test", true),
                ("Test", "test", true),
                ("Test", "Testing", false),
                ("%test%", "This is a test", true),
                ("prefix%", "prefix_something", true),
            };

            Console.WriteLine("LIKE Pattern Test Results:");
            Console.WriteLine("==========================");

            foreach (var (pattern, text, expected) in testCases)
            {
                var actual = IsLikeMatch(text, pattern);
                var status = actual == expected ? "PASS" : "FAIL";
                Console.WriteLine($"{status}: IsLikeMatch(\"{text}\", \"{pattern}\") = {actual} (expected {expected})");
            }

            Console.WriteLine();
            Console.WriteLine("Multiple Pattern Examples:");
            Console.WriteLine("=========================");
            Console.WriteLine("Search: 'Group%, %D' - Will find rows that contain BOTH 'Group' at start AND 'D' anywhere");
            Console.WriteLine("Search: '%Admin%, %User%' - Will find rows that contain BOTH 'Admin' AND 'User' anywhere");
            Console.WriteLine("Search: 'A%, %Manager%' - Will find rows that start with 'A' AND contain 'Manager'");
        }

        /// <summary>
        /// Parses and validates multiple search patterns
        /// </summary>
        public static List<string> ParseSearchPatterns(string searchValue)
        {
            return searchValue.Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        /// <summary>
        /// Gets a description of how multiple patterns will be processed
        /// </summary>
        public static string GetSearchDescription(string searchValue, bool useRegex)
        {
            var patterns = ParseSearchPatterns(searchValue);

            if (patterns.Count == 1)
            {
                return $"Single {(useRegex ? "regex" : "LIKE")} pattern: '{patterns[0]}'";
            }

            var patternType = useRegex ? "regex patterns" : "LIKE patterns";
            return $"Multiple {patternType} (AND logic): {string.Join(" AND ", patterns.Select(p => $"'{p}'"))}";
        }
    }
}