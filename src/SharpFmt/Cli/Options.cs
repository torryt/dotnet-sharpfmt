namespace SharpFmt.Cli;

/// <summary>
/// Parsed CLI options for sharpfmt.
/// </summary>
public sealed class SharpFmtOptions
{
    /// <summary>
    /// The base commit to diff against. Defaults to "HEAD".
    /// </summary>
    public string Commit { get; init; } = "HEAD";

    /// <summary>
    /// Whether to format staged changes (--staged / --cached).
    /// </summary>
    public bool Staged { get; init; }

    /// <summary>
    /// Check mode: exit 1 if any files would change, don't modify files.
    /// </summary>
    public bool Check { get; init; }

    /// <summary>
    /// Print a unified diff instead of modifying files.
    /// </summary>
    public bool Diff { get; init; }

    /// <summary>
    /// Don't expand changed lines to enclosing syntax blocks.
    /// </summary>
    public bool NoExpand { get; init; }

    /// <summary>
    /// Glob patterns to include (default: **/*.cs).
    /// </summary>
    public string[] Include { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Glob patterns to exclude.
    /// </summary>
    public string[] Exclude { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Number of parallel workers.
    /// </summary>
    public int Jobs { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;

    /// <summary>
    /// Explicit file paths to format (for the 'format' subcommand).
    /// </summary>
    public string[] Files { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explicit line ranges for the 'format' subcommand (e.g., "10:25").
    /// </summary>
    public string[] Lines { get; init; } = Array.Empty<string>();
}

public enum Verbosity
{
    Quiet,
    Normal,
    Verbose,
}
