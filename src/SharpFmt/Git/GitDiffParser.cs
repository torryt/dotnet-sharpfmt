using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SharpFmt.Git;

public static partial class GitDiffParser
{
    // Matches @@ -a,b +c,d @@ hunk headers (the +side tells us new file lines)
    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkHeaderRegex();

    // Matches +++ b/path lines
    [GeneratedRegex(@"^\+\+\+ [^/]+/(.*)")]
    private static partial Regex FileHeaderRegex();

    /// <summary>
    /// Run git diff and parse the output to extract changed line ranges per file.
    /// </summary>
    public static async Task<IReadOnlyList<FileDiff>> GetChangedRangesAsync(
        string repoRoot,
        string? commit = null,
        bool staged = false,
        CancellationToken ct = default)
    {
        var args = BuildGitArgs(commit, staged);
        var output = await RunGitAsync(repoRoot, args, ct);
        return ParseUnifiedDiff(output);
    }

    internal static string[] BuildGitArgs(string? commit, bool staged)
    {
        // Default to HEAD if no commit specified
        var baseCommit = commit ?? "HEAD";

        var args = new List<string> { "diff-index", "-p", "-U0" };

        if (staged)
        {
            args.Add("--cached");
        }

        args.Add(baseCommit);
        args.Add("--");

        return args.ToArray();
    }

    internal static IReadOnlyList<FileDiff> ParseUnifiedDiff(string diffOutput)
    {
        var results = new Dictionary<string, List<DiffRange>>();
        string? currentFile = null;

        foreach (var line in diffOutput.Split('\n'))
        {
            var fileMatch = FileHeaderRegex().Match(line);
            if (fileMatch.Success)
            {
                currentFile = fileMatch.Groups[1].Value.TrimEnd('\r', '\n', '\t');
                if (!results.ContainsKey(currentFile))
                {
                    results[currentFile] = new List<DiffRange>();
                }
                continue;
            }

            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success && currentFile != null)
            {
                var startLine = int.Parse(hunkMatch.Groups[1].Value);
                var lineCount = hunkMatch.Groups[2].Success
                    ? int.Parse(hunkMatch.Groups[2].Value)
                    : 1;

                // Skip pure deletions (lineCount == 0 means lines were only removed)
                if (lineCount == 0 || startLine == 0)
                {
                    continue;
                }

                results[currentFile].Add(new DiffRange(startLine, lineCount));
            }
        }

        return results
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new FileDiff(kv.Key, kv.Value))
            .ToList();
    }

    private static async Task<string> RunGitAsync(
        string workingDirectory,
        string[] args,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed with exit code {process.ExitCode}: {error}");
        }

        return output;
    }
}
