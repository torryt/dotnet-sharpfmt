namespace SharpFmt.Formatting;

/// <summary>
/// Result of formatting a single file.
/// </summary>
/// <param name="FilePath">Absolute path to the file.</param>
/// <param name="OriginalText">The original file content.</param>
/// <param name="FormattedText">The formatted file content.</param>
public readonly record struct FormatResult(
    string FilePath,
    string OriginalText,
    string FormattedText)
{
    /// <summary>
    /// Whether the file content changed as a result of formatting.
    /// </summary>
    public bool HasChanges => OriginalText != FormattedText;
}
