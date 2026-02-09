namespace SharpFmt.Git;

/// <summary>
/// Represents a range of changed lines in a file from a git diff.
/// </summary>
/// <param name="StartLine">1-based start line number.</param>
/// <param name="LineCount">Number of consecutive changed lines.</param>
public readonly record struct DiffRange(int StartLine, int LineCount)
{
    /// <summary>
    /// 1-based end line (inclusive).
    /// </summary>
    public int EndLine => StartLine + LineCount - 1;
}

/// <summary>
/// All changed line ranges for a single file.
/// </summary>
/// <param name="FilePath">Path to the changed file (relative to repo root).</param>
/// <param name="Ranges">The changed line ranges.</param>
public readonly record struct FileDiff(string FilePath, IReadOnlyList<DiffRange> Ranges);
