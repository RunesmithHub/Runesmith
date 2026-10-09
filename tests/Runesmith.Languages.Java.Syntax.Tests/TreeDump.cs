using System.Reflection;
using System.Text;

namespace Runesmith.Languages.Java.Syntax.Tests;

/// <summary>Writes a tree as text, with every node's kind, span and simple properties, to compare trees.</summary>
internal static class TreeDump
{
    public static string Of(JavaSyntaxTree tree)
    {
        var builder = new StringBuilder();
        Write(builder, tree.Root, 0);
        builder.AppendLine("tokens");
        foreach (var token in tree.Tokens)
            builder.Append(token.Kind).Append(token.Span).Append(' ');
        builder.AppendLine().AppendLine("comments");
        foreach (var comment in tree.Comments)
            builder.Append(comment.Kind).Append(comment.Span).Append(' ');
        builder.AppendLine().AppendLine("diagnostics");
        foreach (var diagnostic in tree.Diagnostics)
            builder.AppendLine(diagnostic.ToString());
        return builder.ToString();
    }

    public static string Of(SyntaxNode node)
    {
        var builder = new StringBuilder();
        Write(builder, node, 0);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, SyntaxNode node, int depth)
    {
        builder.Append(' ', depth * 2).Append(node.GetType().Name).Append(' ').Append(node.Span);
        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var type = property.PropertyType;
            if (property.GetIndexParameters().Length > 0 || !(type.IsEnum || type == typeof(string) || type == typeof(bool) || type == typeof(int)))
                continue;
            if (property.Name is "Start" or "End" or "Count")
                continue;
            builder.Append(' ').Append(property.Name).Append('=').Append(property.GetValue(node));
        }

        builder.AppendLine();
        foreach (var child in node.ChildNodes())
            Write(builder, child, depth + 1);
    }
}
