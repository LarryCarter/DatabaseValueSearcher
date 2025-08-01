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
            if (pattern.EndsWith("%") && !pattern.StartsWith("%") && !pattern.Contains("_"))
            {
                var searchValue = pattern.Substring(0, pattern.Length - 1);
                return text.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase);
            }

            // Pattern: "%Value" (ends with)
            if (pattern.StartsWith("%") && !pattern.EndsWith("%") && !pattern.Contains("_"))
            {
                var searchValue = pattern.Substring(1);
                return text.EndsWith(searchValue, StringComparison.OrdinalIgnoreCase);
            }

            // Pattern: "%Value%" (contains)
            if (pattern.StartsWith("%") && pattern.EndsWith("%") && !pattern.Contains("_"))
            {
                var searchValue = pattern.Substring(1, pattern.Length - 2);
                return text.Contains(searchValue, StringComparison.OrdinalIgnoreCase);
            }

            // Pattern: "Value" (exact match)
            if (!pattern.Contains("%") && !pattern.Contains("_"))
            {
                return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }

            // For complex patterns with _ wildcards or mixed patterns,
            // fall back to character-by-character matching
            return IsLikeMatchCharByChar(text, pattern);
        }

        /// <summary>
        /// Character-by-character LIKE pattern matching for complex patterns
        /// </summary>
        private static bool IsLikeMatchCharByChar(string text, string pattern)
        {
            int textIndex = 0;
            int patternIndex = 0;

            while (patternIndex < pattern.Length)
            {
                char patternChar = pattern[patternIndex];

                if (patternChar == '%')
                {
                    // Skip consecutive % characters
                    while (patternIndex < pattern.Length && pattern[patternIndex] == '%')
                        patternIndex++;

                    // If % is at the end, we match
                    if (patternIndex >= pattern.Length)
                        return true;

                    // Find the next non-wildcard character
                    char nextChar = pattern[patternIndex];

                    // Try to find this character in the remaining text
                    while (textIndex < text.Length)
                    {
                        if (char.ToLowerInvariant(text[textIndex]) == char.ToLowerInvariant(nextChar))
                        {
                            // Try matching from this position
                            if (IsLikeMatchCharByChar(text.Substring(textIndex), pattern.Substring(patternIndex)))
                                return true;
                        }
                        textIndex++;
                    }
                    return false;
                }
                else if (patternChar == '_')
                {
                    // _ matches exactly one character
                    if (textIndex >= text.Length)
                        return false;
                    textIndex++;
                    patternIndex++;
                }
                else
                {
                    // Literal character match
                    if (textIndex >= text.Length)
                        return false;

                    if (char.ToLowerInvariant(text[textIndex]) != char.ToLowerInvariant(patternChar))
                        return false;

                    textIndex++;
                    patternIndex++;
                }
            }

            // We've consumed the entire pattern
            // We match if we've also consumed all the text
            return textIndex >= text.Length;
        }

        /// <summary>
        /// Test method to validate LIKE pattern matching behavior
        /// </summary>
        public static void TestLikePatterns()
        {
            var testCases = new[]
            {
                // Pattern, Text, Expected Result
                ("A%", "AD_Managers", true),
                ("A%", "BD_Managers", false),
                ("%Manager%", "AD_Managers", true),
                ("%Manager%", "AD_Users", false),
                ("A_M%", "AD_Managers", true),
                ("A_M%", "A_Managers", false),
                ("%D_A%", "AD_Administrators", true),
                ("%D_A%", "BD_Users", false),
                ("Test", "Test", true),
                ("Test", "test", true),
                ("Test", "Testing", false),
            };

            Console.WriteLine("LIKE Pattern Test Results:");
            Console.WriteLine("==========================");

            foreach (var (pattern, text, expected) in testCases)
            {
                var actual = IsLikeMatch(text, pattern);
                var status = actual == expected ? "PASS" : "FAIL";
                Console.WriteLine($"{status}: IsLikeMatch(\"{text}\", \"{pattern}\") = {actual} (expected {expected})");
            }
        }
    }
}