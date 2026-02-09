using Microsoft.Extensions.FileSystemGlobbing;
using SharpFmt.Cli;
using SharpFmt.Config;
using SharpFmt.Git;
using SharpFmt.Output;

namespace SharpFmt.Formatting;

/// <summary>
/// Orchestrates the full formatting pipeline:
/// git diff → filter → expand ranges → format → output results.
/// </summary>
public sealed class FormattingPipeline
{
    /// <summary>
    /// Run the formatting pipeline with git diff integration.
    /// Returns exit code.
    /// </summary>
    public static async Task<int> RunAsync(SharpFmtOptions options, CancellationToken ct = default)
    {
        var repoRoot = GetGitRepoRoot();
        var verbose = options.Verbosity == Verbosity.Verbose;

        if (verbose)
        {
            Console.Error.WriteLine($"Repository root: {repoRoot}");
            Console.Error.WriteLine($"Diffing against: {options.Commit}");
        }

        // 1. Get changed ranges from git
        IReadOnlyList<FileDiff> diffs;
        try
        {
            diffs = await GitDiffParser.GetChangedRangesAsync(
                repoRoot, options.Commit, options.Staged, ct);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        if (diffs.Count == 0)
        {
            if (options.Verbosity != Verbosity.Quiet)
            {
                Console.Error.WriteLine("No modified C# files to format.");
            }

            return 0;
        }

        // 2. Filter to .cs files and apply include/exclude globs
        var filteredDiffs = FilterFiles(diffs, options.Include, options.Exclude);

        if (filteredDiffs.Count == 0)
        {
            if (options.Verbosity != Verbosity.Quiet)
            {
                Console.Error.WriteLine("No matching C# files to format.");
            }

            return 0;
        }

        if (verbose)
        {
            Console.Error.WriteLine($"Files to format: {filteredDiffs.Count}");
            foreach (var diff in filteredDiffs)
            {
                Console.Error.WriteLine($"  {diff.FilePath} ({diff.Ranges.Count} changed region(s))");
            }
        }

        // 3. Format files (in parallel)
        var configLoader = EditorConfigLoader.Create(repoRoot);
        var formatter = new FileFormatter(configLoader);
        var results = new FormatResult[filteredDiffs.Count];

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Jobs,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, filteredDiffs.Count),
            parallelOptions,
            (index, token) =>
            {
                var diff = filteredDiffs[index];
                var fullPath = Path.Combine(repoRoot, diff.FilePath);

                if (!File.Exists(fullPath))
                {
                    if (verbose)
                    {
                        Console.Error.WriteLine($"  Skipping (not found): {diff.FilePath}");
                    }

                    results[index] = new FormatResult(fullPath, string.Empty, string.Empty);
                    return ValueTask.CompletedTask;
                }

                results[index] = formatter.Format(fullPath, diff.Ranges as IReadOnlyList<DiffRange>, options.NoExpand);

                return ValueTask.CompletedTask;
            });

        // 4. Output results
        return ResultWriter.ProcessResults(
            results.Where(r => !string.IsNullOrEmpty(r.OriginalText)).ToList(),
            options.Check,
            options.Diff,
            verbose);
    }

    /// <summary>
    /// Run formatting on explicit files with optional line ranges (the 'format' subcommand).
    /// </summary>
    public static int RunDirect(SharpFmtOptions options)
    {
        var verbose = options.Verbosity == Verbosity.Verbose;

        if (options.Files.Length == 0)
        {
            Console.Error.WriteLine("Error: No files specified.");
            return 2;
        }

        // Parse --lines arguments into DiffRanges
        var ranges = new List<DiffRange>();
        foreach (var lineSpec in options.Lines)
        {
            var parts = lineSpec.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var start)
                && int.TryParse(parts[1], out var end))
            {
                ranges.Add(new DiffRange(start, end - start + 1));
            }
            else
            {
                Console.Error.WriteLine($"Error: Invalid line range '{lineSpec}'. Expected format: START:END");
                return 2;
            }
        }

        var cwd = Directory.GetCurrentDirectory();
        var configLoader = EditorConfigLoader.Create(cwd);
        var formatter = new FileFormatter(configLoader);
        var results = new List<FormatResult>();

        foreach (var file in options.Files)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"Error: File not found: {file}");
                return 2;
            }

            var result = formatter.Format(file, ranges.Count > 0 ? ranges : null, options.NoExpand);
            results.Add(result);
        }

        return ResultWriter.ProcessResults(results, options.Check, options.Diff, verbose);
    }

    private static IReadOnlyList<FileDiff> FilterFiles(
        IReadOnlyList<FileDiff> diffs,
        string[] include,
        string[] exclude)
    {
        var filtered = diffs
            .Where(d => d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (include.Length > 0)
        {
            var matcher = new Matcher();
            foreach (var pattern in include)
            {
                matcher.AddInclude(pattern);
            }

            filtered = filtered
                .Where(d => matcher.Match(d.FilePath).HasMatches)
                .ToList();
        }

        if (exclude.Length > 0)
        {
            var matcher = new Matcher();
            foreach (var pattern in exclude)
            {
                matcher.AddInclude(pattern);
            }

            filtered = filtered
                .Where(d => !matcher.Match(d.FilePath).HasMatches)
                .ToList();
        }

        return filtered;
    }

    private static string GetGitRepoRoot()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse --show-toplevel",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Not a git repository. Use 'sharpfmt format <files>' for non-git usage.");
        }

        return output;
    }
}
