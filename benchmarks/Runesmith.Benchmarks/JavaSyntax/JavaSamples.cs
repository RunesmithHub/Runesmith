using System.Text;

namespace Runesmith.Benchmarks.JavaSyntax;

/// <summary>Builds realistic Java source of any size, with the constructs of every release.</summary>
internal static class JavaSamples
{
    private const string Header = """
        package com.example.generated;

        import java.util.*;
        import java.util.function.Function;
        import static java.util.Objects.requireNonNull;

        """;

    private const string Type = """"
        /**
         * A generated service, number {0}.
         *
         * @param <T> the element type
         */
        @SuppressWarnings("unchecked")
        public final class Service{0}<T extends Comparable<? super T>> implements Iterable<T> {{
            private static final long LIMIT = 0x7fff_ffffL;
            private final List<T> items = new ArrayList<>();
            private final Map<String, List<Integer>> index = new HashMap<>();
            private int count;

            public Service{0}(Collection<? extends T> source) {{
                super();
                requireNonNull(source, "source");
                for (T item : source) {{
                    items.add(item);
                }}
                count = items.size();
            }}

            /// Finds the first item matching a key, in Markdown.
            public Optional<T> find(String key, int limit) {{
                if (key == null || key.isEmpty()) {{
                    return Optional.empty();
                }}
                var matches = new ArrayList<T>();
                for (int i = 0; i < items.size() && i < limit; i++) {{
                    T item = items.get(i);
                    if (item.toString().startsWith(key) && !matches.contains(item)) {{
                        matches.add(item);
                    }} else if (i % 3 == 0) {{
                        count += i << 2 >> 1 >>> 1;
                    }}
                }}
                matches.sort(Comparator.naturalOrder());
                return matches.stream().filter(m -> m.compareTo(items.get(0)) >= 0).findFirst();
            }}

            public String describe(Object value) {{
                return switch (value) {{
                    case Integer n when n > 10 -> "big " + n;
                    case Integer n -> "small " + n;
                    case String s -> {{
                        String text = s.strip();
                        yield text.isEmpty() ? "empty" : '"' + text + '"';
                    }}
                    case int[] array -> "array of " + array.length;
                    case null, default -> "other";
                }};
            }}

            public static <K, V> Map<V, List<K>> invert(Map<K, V> map, Function<? super V, ? extends V> mapper) {{
                Map<V, List<K>> result = new TreeMap<>();
                map.forEach((key, value) -> result.computeIfAbsent(mapper.apply(value), unused -> new ArrayList<>()).add(key));
                try (var scanner = new Scanner("a b c")) {{
                    while (scanner.hasNext()) {{
                        String word = scanner.next();
                        assert word != null : "no word";
                    }}
                }} catch (IllegalStateException | NoSuchElementException e) {{
                    throw new RuntimeException(e.getMessage(), e);
                }} finally {{
                    synchronized (result) {{
                        result.size();
                    }}
                }}
                return result;
            }}

            @Override
            public Iterator<T> iterator() {{
                return new Iterator<>() {{
                    private int position;

                    @Override
                    public boolean hasNext() {{
                        return position < count;
                    }}

                    @Override
                    public T next() {{
                        return (T) items.get(position++);
                    }}
                }};
            }}

            record Point{0}(int x, int y) {{
                Point{0} {{
                    if (x < 0) throw new IllegalArgumentException("x");
                }}

                double length() {{
                    String block = """
                        Point: %d, %d
                        """.formatted(x, y);
                    return Math.sqrt((double) x * x + y * y) + block.length() * 0.0;
                }}
            }}

            sealed interface Shape{0} permits Circle{0}, Square{0} {{ }}

            static final class Circle{0} implements Shape{0} {{ }}

            static non-sealed class Square{0} implements Shape{0} {{ }}

            enum Color{0} {{ RED, GREEN, BLUE; Color{0} next() {{ return values()[(ordinal() + 1) % values().length]; }} }}
        }}

        """";

    private static readonly CompositeFormat TypeFormat = CompositeFormat.Parse(Type);

    /// <summary>Gets a file of about <paramref name="lines"/> lines.</summary>
    public static string File(int lines)
    {
        var typeLines = Type.Count('\n');
        var builder = new StringBuilder(Header);
        for (var i = 0; builder.Length == Header.Length || CountLines(builder) + typeLines <= lines; i++)
            builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, TypeFormat, i);
        return builder.ToString();
    }

    private static int CountLines(StringBuilder builder)
    {
        var lines = 0;
        foreach (var chunk in builder.GetChunks())
            lines += chunk.Span.Count('\n');
        return lines;
    }
}
