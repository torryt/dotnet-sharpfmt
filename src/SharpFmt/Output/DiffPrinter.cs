using System.Text;

namespace SharpFmt.Output;

/// <summary>
/// Produces unified diffs using a Myers diff algorithm for optimal edit scripts.
/// </summary>
public static class DiffPrinter
{
    private const int ContextLines = 3;

    /// <summary>
    /// Generate a unified diff showing the changes between original and formatted text.
    /// Uses the Myers diff algorithm to compute the shortest edit script.
    /// </summary>
    public static string GenerateDiff(string filePath, string original, string formatted)
    {
        var originalLines = SplitLines(original);
        var formattedLines = SplitLines(formatted);

        var editScript = ComputeEditScript(originalLines, formattedLines);

        if (!editScript.Any(e => e.Kind != EditKind.Equal))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        WriteHunks(sb, editScript);

        return sb.ToString();
    }

    /// <summary>
    /// Split text into lines, removing a trailing empty element if text ends with \n.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1] == string.Empty)
        {
            return lines[..^1];
        }

        return lines;
    }

    private enum EditKind { Equal, Delete, Insert }

    private readonly record struct EditEntry(EditKind Kind, string Line, int OrigIndex, int NewIndex);

    /// <summary>
    /// Compute the full edit script (sequence of Equal/Delete/Insert operations)
    /// using the Myers O(ND) diff algorithm.
    /// </summary>
    private static List<EditEntry> ComputeEditScript(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var max = n + m;

        if (max == 0)
            return [];

        var vSize = 2 * max + 1;
        var v = new int[vSize];
        var trace = new List<int[]>();

        // Forward pass: find the shortest edit path
        var found = false;
        for (var d = 0; d <= max && !found; d++)
        {
            trace.Add((int[])v.Clone());

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[k - 1 + max] < v[k + 1 + max]))
                {
                    x = v[k + 1 + max]; // down = insert
                }
                else
                {
                    x = v[k - 1 + max] + 1; // right = delete
                }

                var y = x - k;

                // Follow diagonal (equal lines)
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[k + max] = x;

                if (x >= n && y >= m)
                {
                    found = true;
                    break;
                }
            }
        }

        // Backward pass: reconstruct the path from trace
        return Backtrack(trace, a, b, max);
    }

    /// <summary>
    /// Walk backward through the trace to reconstruct the edit script.
    /// </summary>
    private static List<EditEntry> Backtrack(List<int[]> trace, string[] a, string[] b, int offset)
    {
        var x = a.Length;
        var y = b.Length;
        var edits = new List<EditEntry>();

        for (var d = trace.Count - 1; d > 0; d--)
        {
            var vPrev = trace[d];
            var k = x - y;

            int prevK;
            if (k == -d || (k != d && vPrev[k - 1 + offset] < vPrev[k + 1 + offset]))
            {
                prevK = k + 1;
            }
            else
            {
                prevK = k - 1;
            }

            var prevX = vPrev[prevK + offset];
            var prevY = prevX - prevK;

            // Diagonal (equal lines)
            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                edits.Add(new EditEntry(EditKind.Equal, a[x], x, y));
            }

            // The actual edit
            if (x == prevX && y > prevY)
            {
                // Insert
                y--;
                edits.Add(new EditEntry(EditKind.Insert, b[y], x, y));
            }
            else if (x > prevX && y == prevY)
            {
                // Delete
                x--;
                edits.Add(new EditEntry(EditKind.Delete, a[x], x, y));
            }
        }

        // Handle any remaining diagonal at d=0
        while (x > 0 && y > 0)
        {
            x--;
            y--;
            edits.Add(new EditEntry(EditKind.Equal, a[x], x, y));
        }

        edits.Reverse();
        return edits;
    }

    /// <summary>
    /// Group edit entries into unified diff hunks with context and write them.
    /// </summary>
    private static void WriteHunks(StringBuilder sb, List<EditEntry> edits)
    {
        // Find change regions and group them with context
        var changeIndices = new List<int>();
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Kind != EditKind.Equal)
            {
                changeIndices.Add(i);
            }
        }

        if (changeIndices.Count == 0)
            return;

        // Group changes into hunks: changes separated by more than 2*ContextLines equal lines form separate hunks
        var hunkGroups = new List<(int start, int end)>();
        var groupStart = changeIndices[0];
        var groupEnd = changeIndices[0];

        for (var i = 1; i < changeIndices.Count; i++)
        {
            if (changeIndices[i] - groupEnd <= 2 * ContextLines + 1)
            {
                groupEnd = changeIndices[i];
            }
            else
            {
                hunkGroups.Add((groupStart, groupEnd));
                groupStart = changeIndices[i];
                groupEnd = changeIndices[i];
            }
        }

        hunkGroups.Add((groupStart, groupEnd));

        // Write each hunk
        foreach (var (firstChange, lastChange) in hunkGroups)
        {
            var hunkStart = Math.Max(0, firstChange - ContextLines);
            var hunkEnd = Math.Min(edits.Count - 1, lastChange + ContextLines);

            // Count original and new lines in this hunk
            var origCount = 0;
            var newCount = 0;
            var origStart = -1;
            var newStart = -1;

            for (var i = hunkStart; i <= hunkEnd; i++)
            {
                var e = edits[i];
                switch (e.Kind)
                {
                    case EditKind.Equal:
                        if (origStart == -1) origStart = e.OrigIndex;
                        if (newStart == -1) newStart = e.NewIndex;
                        origCount++;
                        newCount++;
                        break;
                    case EditKind.Delete:
                        if (origStart == -1) origStart = e.OrigIndex;
                        if (newStart == -1) newStart = e.NewIndex;
                        origCount++;
                        break;
                    case EditKind.Insert:
                        if (origStart == -1) origStart = e.OrigIndex;
                        if (newStart == -1) newStart = e.NewIndex;
                        newCount++;
                        break;
                }
            }

            // 1-based line numbers
            sb.AppendLine($"@@ -{origStart + 1},{origCount} +{newStart + 1},{newCount} @@");

            for (var i = hunkStart; i <= hunkEnd; i++)
            {
                var prefix = edits[i].Kind switch
                {
                    EditKind.Equal => ' ',
                    EditKind.Delete => '-',
                    EditKind.Insert => '+',
                    _ => ' ',
                };
                sb.AppendLine($"{prefix}{edits[i].Line}");
            }
        }
    }
}
