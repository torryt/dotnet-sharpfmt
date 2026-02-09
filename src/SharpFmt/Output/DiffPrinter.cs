namespace SharpFmt.Output;

/// <summary>
/// Produces a simple unified diff between original and formatted text.
/// </summary>
public static class DiffPrinter
{
    /// <summary>
    /// Generate a unified-diff-style output showing the changes.
    /// This is a simplified diff — not a full unified diff algorithm,
    /// but sufficient for showing formatting changes.
    /// </summary>
    public static string GenerateDiff(string filePath, string original, string formatted)
    {
        var originalLines = original.Split('\n');
        var formattedLines = formatted.Split('\n');

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        // Simple line-by-line comparison with context
        var i = 0;
        var j = 0;
        while (i < originalLines.Length || j < formattedLines.Length)
        {
            if (i < originalLines.Length && j < formattedLines.Length
                && originalLines[i] == formattedLines[j])
            {
                i++;
                j++;
                continue;
            }

            // Find the extent of the changed hunk
            var hunkStartOrig = i;
            var hunkStartFmt = j;

            // Advance until we find matching lines again
            var (newI, newJ) = FindNextMatch(originalLines, formattedLines, i, j);
            
            // Print hunk header
            var origCount = newI - hunkStartOrig;
            var fmtCount = newJ - hunkStartFmt;
            sb.AppendLine($"@@ -{hunkStartOrig + 1},{origCount} +{hunkStartFmt + 1},{fmtCount} @@");

            for (var k = hunkStartOrig; k < newI; k++)
            {
                sb.AppendLine($"-{originalLines[k]}");
            }

            for (var k = hunkStartFmt; k < newJ; k++)
            {
                sb.AppendLine($"+{formattedLines[k]}");
            }

            i = newI;
            j = newJ;
        }

        return sb.ToString();
    }

    private static (int i, int j) FindNextMatch(
        string[] original,
        string[] formatted,
        int i,
        int j)
    {
        // Look ahead for the next matching line (simple greedy approach)
        const int maxLookAhead = 100;

        for (var look = 1; look < maxLookAhead; look++)
        {
            // Try advancing original
            if (i + look < original.Length && j < formatted.Length)
            {
                if (original[i + look] == formatted[j])
                {
                    return (i + look, j);
                }
            }

            // Try advancing formatted
            if (i < original.Length && j + look < formatted.Length)
            {
                if (original[i] == formatted[j + look])
                {
                    return (i, j + look);
                }
            }

            // Try advancing both
            if (i + look < original.Length && j + look < formatted.Length)
            {
                if (original[i + look] == formatted[j + look])
                {
                    return (i + look, j + look);
                }
            }
        }

        // No match found — consume everything remaining
        return (original.Length, formatted.Length);
    }
}
