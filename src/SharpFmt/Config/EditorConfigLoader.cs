using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpFmt.Config;

/// <summary>
/// Loads .editorconfig files using Roslyn's AnalyzerConfig system for
/// byte-for-byte compatibility with dotnet format / csc interpretation.
/// </summary>
public sealed partial class EditorConfigLoader
{
    private readonly AnalyzerConfigSet _configSet;

    private EditorConfigLoader(AnalyzerConfigSet configSet)
    {
        _configSet = configSet;
    }

    /// <summary>
    /// Walk from <paramref name="directory"/> upward, collecting all .editorconfig files
    /// until one with root=true is found (or filesystem root is reached).
    /// </summary>
    public static EditorConfigLoader Create(string directory)
    {
        var configs = new List<AnalyzerConfig>();
        var dir = Path.GetFullPath(directory);

        while (!string.IsNullOrEmpty(dir))
        {
            var editorConfigPath = Path.Combine(dir, ".editorconfig");
            if (File.Exists(editorConfigPath))
            {
                var text = File.ReadAllText(editorConfigPath);
                var config = AnalyzerConfig.Parse(text, editorConfigPath);
                configs.Add(config);

                // AnalyzerConfig.IsRoot is internal, so we detect root = true
                // by scanning the raw text (before any section headers).
                if (IsRootConfig(text))
                {
                    break;
                }
            }

            dir = Path.GetDirectoryName(dir);
        }

        var configSet = AnalyzerConfigSet.Create(configs);
        return new EditorConfigLoader(configSet);
    }

    /// <summary>
    /// Get the resolved .editorconfig options for a specific source file path.
    /// </summary>
    public AnalyzerConfigOptionsResult GetOptionsForSourcePath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return _configSet.GetOptionsForSourcePath(fullPath);
    }

    /// <summary>
    /// Checks whether the .editorconfig text declares <c>root = true</c> in the
    /// global (pre-section) preamble. Per the editorconfig spec, <c>root</c> must
    /// appear before the first section header <c>[...]</c>.
    /// </summary>
    internal static bool IsRootConfig(string text)
    {
        // Read line by line; stop at the first section header.
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("["))
                break; // reached a section header, root must appear before this

            if (RootPattern().IsMatch(trimmed.ToString()))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"^\s*root\s*=\s*true\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RootPattern();
}
