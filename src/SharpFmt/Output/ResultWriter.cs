using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpFmt.Formatting;

namespace SharpFmt.Output;

/// <summary>
/// Handles writing format results based on the output mode.
/// </summary>
public static class ResultWriter
{
    /// <summary>
    /// Process format results: write files, print diff, or check.
    /// Returns exit code (0 = success/clean, 1 = check found changes, 2 = error).
    /// </summary>
    public static int ProcessResults(
        IReadOnlyList<FormatResult> results,
        bool checkMode,
        bool diffMode,
        bool verbose)
    {
        var changedFiles = results.Where(r => r.HasChanges).ToList();

        if (changedFiles.Count == 0)
        {
            if (verbose)
            {
                Console.Error.WriteLine("All files are correctly formatted.");
            }

            return 0;
        }

        if (checkMode)
        {
            Console.Error.WriteLine($"{changedFiles.Count} file(s) would be reformatted:");
            foreach (var result in changedFiles)
            {
                Console.Error.WriteLine($"  {GetRelativePath(result.FilePath)}");
            }

            return 1;
        }

        if (diffMode)
        {
            foreach (var result in changedFiles)
            {
                var diff = DiffPrinter.GenerateDiff(
                    GetRelativePath(result.FilePath),
                    result.OriginalText,
                    result.FormattedText);
                Console.Write(diff);
            }

            return changedFiles.Count > 0 ? 1 : 0;
        }

        // Default: write in place
        foreach (var result in changedFiles)
        {
            File.WriteAllText(result.FilePath, result.FormattedText);
            if (verbose)
            {
                Console.Error.WriteLine($"  Formatted: {GetRelativePath(result.FilePath)}");
            }
        }

        Console.Error.WriteLine($"Formatted {changedFiles.Count} file(s).");
        return 0;
    }

    private static string GetRelativePath(string fullPath)
    {
        try
        {
            return Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}
