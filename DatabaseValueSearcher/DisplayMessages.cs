#nullable enable
#pragma warning disable IDE0063 // Use simple 'using' statement
namespace DatabaseValueSearcher
{
    public class DisplayMessages
    {
        #region Console Color Helpers
        public static void WriteSuccess(string message) => WriteColored(message, ConsoleColor.Green);
        public static void WriteInfo(string message) => WriteColored(message, ConsoleColor.Cyan);
        public static void WriteWarning(string message) => WriteColored(message, ConsoleColor.Yellow);
        public static void WriteError(string message) => WriteColored(message, ConsoleColor.Red);
        public static void WriteHighlight(string message) => WriteColored(message, ConsoleColor.Magenta);

        public static void WriteColored(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        public static void WriteColoredInline(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ForegroundColor = originalColor;
        }
        #endregion

        public static void SearchValuesDisplay()
        {
            WriteInfo("Enter search value:");
            Console.WriteLine();
            WriteHighlight("Search Type Options:");
            Console.WriteLine("  1. LIKE Pattern (default) - Uses SQL LIKE with wildcards");
            Console.WriteLine("  2. Regular Expression - Uses .NET regex patterns");
            Console.WriteLine();

            WriteHighlight("LIKE Pattern Examples:");
            Console.WriteLine("  john          - Exact match for 'john'");
            Console.WriteLine("  %john         - Ends with 'john'");
            Console.WriteLine("  john%         - Starts with 'john'");
            Console.WriteLine("  %john%        - Contains 'john' anywhere");
            Console.WriteLine("  j_hn          - 'j' + any single character + 'hn'");
            Console.WriteLine();

            WriteHighlight("Regular Expression Examples:");
            Console.WriteLine("  ^john$        - Exact match for 'john'");
            Console.WriteLine("  john.*        - Starts with 'john'");
            Console.WriteLine("  .*john.*      - Contains 'john' anywhere");
            Console.WriteLine("  \\d{3}-\\d{3}-\\d{4} - Phone number pattern");
            Console.WriteLine();
        }

        public static void ShowUsageDisplay()
        {
            WriteInfo("Database Value Searcher - Usage with Performance Optimization:");
            Console.WriteLine("");
            WriteHighlight("Interactive Mode (Recommended):");
            Console.WriteLine("  DatabaseValueSearcher.exe");
            Console.WriteLine("  - Smart caching reduces database load");
            Console.WriteLine("  - Session management for repeated searches");
            Console.WriteLine("  - Pagination prevents DoS-like behavior");
            Console.WriteLine("");
            WriteHighlight("Command Line Mode:");
            Console.WriteLine("  DatabaseValueSearcher.exe <environment> <database> <table_name> <search_value> [--regex]");
            Console.WriteLine("");
            WriteInfo("Examples:");
            Console.WriteLine("  DatabaseValueSearcher.exe");
            Console.WriteLine("  DatabaseValueSearcher.exe Local MyDB Users \"%john%\"");
            Console.WriteLine("  DatabaseValueSearcher.exe QA TestDB Orders \"\\d{5}\" --regex");
        }
    }
}