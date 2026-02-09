using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpFmt.Git;

namespace SharpFmt.Formatting;

/// <summary>
/// Expands line ranges to the smallest enclosing syntactically meaningful block.
/// </summary>
public static class RangeExpander
{
    /// <summary>
    /// Convert diff line ranges to TextSpans that cover the smallest enclosing syntax nodes.
    /// </summary>
    public static IReadOnlyList<TextSpan> ExpandToSyntaxSpans(
        SyntaxNode root,
        SourceText sourceText,
        IReadOnlyList<DiffRange> ranges)
    {
        var spans = new List<TextSpan>();

        foreach (var range in ranges)
        {
            var span = LineRangeToTextSpan(sourceText, range);
            var expandedSpan = ExpandToEnclosingNode(root, span);
            spans.Add(expandedSpan);
        }

        return MergeOverlappingSpans(spans);
    }

    /// <summary>
    /// Convert a line range to a TextSpan.
    /// </summary>
    internal static TextSpan LineRangeToTextSpan(SourceText sourceText, DiffRange range)
    {
        // Lines in DiffRange are 1-based, SourceText lines are 0-based
        var startLine = Math.Max(0, range.StartLine - 1);
        var endLine = Math.Min(sourceText.Lines.Count - 1, range.EndLine - 1);

        var start = sourceText.Lines[startLine].Start;
        var end = sourceText.Lines[endLine].End;

        return TextSpan.FromBounds(start, end);
    }

    /// <summary>
    /// Find the smallest enclosing syntax node that represents a complete
    /// statement, member, or type declaration.
    /// </summary>
    internal static TextSpan ExpandToEnclosingNode(SyntaxNode root, TextSpan span)
    {
        // Find the node that most tightly contains this span
        var node = root.FindNode(span, findInsideTrivia: false, getInnermostNodeForTie: true);

        // Walk up to find the smallest syntactically meaningful container
        while (node != null)
        {
            if (IsFormattingBoundary(node))
            {
                return node.FullSpan;
            }

            node = node.Parent;
        }

        // Fallback: return the original span
        return span;
    }

    /// <summary>
    /// Determines whether a node is a good boundary for formatting.
    /// We want the smallest unit that can be independently formatted.
    /// </summary>
    private static bool IsFormattingBoundary(SyntaxNode node)
    {
        return node is StatementSyntax
            or MemberDeclarationSyntax
            or AccessorDeclarationSyntax
            or TypeDeclarationSyntax
            or NamespaceDeclarationSyntax
            or FileScopedNamespaceDeclarationSyntax
            or UsingDirectiveSyntax
            or AttributeListSyntax;
    }

    /// <summary>
    /// Merge overlapping or adjacent spans into non-overlapping spans.
    /// </summary>
    internal static IReadOnlyList<TextSpan> MergeOverlappingSpans(List<TextSpan> spans)
    {
        if (spans.Count <= 1)
        {
            return spans;
        }

        var sorted = spans.OrderBy(s => s.Start).ToList();
        var merged = new List<TextSpan> { sorted[0] };

        for (var i = 1; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var last = merged[^1];

            if (current.Start <= last.End)
            {
                // Overlapping or adjacent — merge
                merged[^1] = TextSpan.FromBounds(
                    last.Start,
                    Math.Max(last.End, current.End));
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
