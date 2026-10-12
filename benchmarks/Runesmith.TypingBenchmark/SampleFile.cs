using System.Text;

namespace Runesmith.TypingBenchmark;

/// <summary>The file the benchmark opens, in its own sample language: 200 classes of fields, properties, documented methods, loops and a
/// region, 10,008 lines in all, which Runesmith highlights with the grammar <see cref="SampleLanguage"/> brings.</summary>
internal static class SampleFile
{
    /// <summary>The number of classes.</summary>
    public const int ClassCount = 200;

    /// <summary>The class whose <c>Add</c> method the benchmark types into, in the middle of the file.</summary>
    public const int TypingClass = 100;

    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text;

        namespace Benchmark.Sample;

        // The typing benchmark's sample file.

        """;

    /// <summary>Gets the file's text.</summary>
    public static string Create()
    {
        var text = new StringBuilder(Header);
        for (var number = 1; number <= ClassCount; number++)
            text.Append(Class(number));
        return text.ToString();
    }

    /// <summary>Gets the line, counted from zero, after which the benchmark types: the last line of <see cref="TypingClass"/>'s <c>Add</c>.</summary>
    public static int TypingLine => LineOf(TypingClass, "        count++;");

    /// <summary>Gets the first line of a class that starts with the given text, counted from zero in the whole file.</summary>
    public static int LineOf(int classNumber, string lineStart)
    {
        var lines = Class(classNumber).Split('\n');
        var index = Array.FindIndex(lines, line => line.StartsWith(lineStart, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"No line of the class starts with \"{lineStart}\".", nameof(lineStart));

        return Lines(Header) + (classNumber - 1) * Lines(Class(1)) + index;
    }

    private static int Lines(string text) => text.Count(c => c == '\n');

    private static string Class(int number) => $$"""
        /// <summary>A sample type, number {{number}}.</summary>
        public sealed class Widget{{number}} : IComparable<Widget{{number}}>
        {
            private readonly List<string> items = [];
            private readonly Dictionary<string, int> lookup = new(StringComparer.Ordinal);
            private int count;

            /// <summary>Gets or sets the name shown for the widget.</summary>
            public string Name { get; set; } = "Widget{{number}}";

            public int Count => count;

            /// <summary>Adds a value and counts it.</summary>
            public void Add(string value)
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("A value is needed.", nameof(value));

                items.Add(value);
                lookup[value] = count;
                count++;
            }

            public string Describe(int limit)
            {
                var builder = new StringBuilder(Name);
                foreach (var item in items.Take(limit))
                {
                    builder.Append(item).Append(", ");
                }

                return builder.ToString();
            }

            #region Totals
            public int Total(IEnumerable<int> values)
            {
                var total = 0;
                for (var i = 0; i < count; i++)
                {
                    total += i % 3 == 0 ? values.Sum() : -i;
                }

                return total;
            }
            #endregion

            public int CompareTo(Widget{{number}}? other) => string.CompareOrdinal(Name, other?.Name);
        }


        """;
}
