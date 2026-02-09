using System.CommandLine;
using SharpFmt.Cli;
using SharpFmt.Formatting;
using SharpFmt.Git;

namespace SharpFmt;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var commitArgument = new Argument<string?>(
            name: "commit",
            getDefaultValue: () => null,
            description: "Commit to diff against (default: HEAD)");

        var stagedOption = new Option<bool>(
            aliases: new[] { "--staged", "--cached" },
            description: "Format changes in the staging area");

        var checkOption = new Option<bool>(
            name: "--check",
            description: "Exit with code 1 if any files would change (CI mode)");

        var diffOption = new Option<bool>(
            name: "--diff",
            description: "Print a unified diff instead of modifying files");

        var noExpandOption = new Option<bool>(
            name: "--no-expand",
            description: "Format only the exact changed lines, not enclosing syntax blocks");

        var includeOption = new Option<string[]>(
            name: "--include",
            getDefaultValue: () => Array.Empty<string>(),
            description: "Only format files matching these globs (default: **/*.cs)");

        var excludeOption = new Option<string[]>(
            name: "--exclude",
            getDefaultValue: () => Array.Empty<string>(),
            description: "Exclude files matching these globs");

        var jobsOption = new Option<int>(
            aliases: new[] { "-j", "--jobs" },
            getDefaultValue: () => Environment.ProcessorCount,
            description: "Number of parallel workers");

        var verbosityOption = new Option<Verbosity>(
            aliases: new[] { "-v", "--verbosity" },
            getDefaultValue: () => Verbosity.Normal,
            description: "Verbosity level: quiet, normal, verbose");

        var rootCommand = new RootCommand(
            "sharpfmt - Fast C# formatter for changed lines. Like git-clang-format for C#.")
        {
            commitArgument,
            stagedOption,
            checkOption,
            diffOption,
            noExpandOption,
            includeOption,
            excludeOption,
            jobsOption,
            verbosityOption,
        };

        rootCommand.SetHandler(async (context) =>
        {
            var options = new SharpFmtOptions
            {
                Commit = context.ParseResult.GetValueForArgument(commitArgument) ?? "HEAD",
                Staged = context.ParseResult.GetValueForOption(stagedOption),
                Check = context.ParseResult.GetValueForOption(checkOption),
                Diff = context.ParseResult.GetValueForOption(diffOption),
                NoExpand = context.ParseResult.GetValueForOption(noExpandOption),
                Include = context.ParseResult.GetValueForOption(includeOption) ?? Array.Empty<string>(),
                Exclude = context.ParseResult.GetValueForOption(excludeOption) ?? Array.Empty<string>(),
                Jobs = context.ParseResult.GetValueForOption(jobsOption),
                Verbosity = context.ParseResult.GetValueForOption(verbosityOption),
            };

            context.ExitCode = await FormattingPipeline.RunAsync(
                options, context.GetCancellationToken());
        });

        // 'format' subcommand for explicit file formatting
        var formatFilesArgument = new Argument<string[]>(
            name: "files",
            description: "Files to format");

        var linesOption = new Option<string[]>(
            name: "--lines",
            getDefaultValue: () => Array.Empty<string>(),
            description: "Line ranges to format (e.g., 10:25)");

        var formatCommand = new Command("format", "Format specific files with optional line ranges")
        {
            formatFilesArgument,
            linesOption,
            checkOption,
            diffOption,
            verbosityOption,
        };

        formatCommand.SetHandler((context) =>
        {
            var options = new SharpFmtOptions
            {
                Files = context.ParseResult.GetValueForArgument(formatFilesArgument),
                Lines = context.ParseResult.GetValueForOption(linesOption) ?? Array.Empty<string>(),
                Check = context.ParseResult.GetValueForOption(checkOption),
                Diff = context.ParseResult.GetValueForOption(diffOption),
                Verbosity = context.ParseResult.GetValueForOption(verbosityOption),
            };

            context.ExitCode = FormattingPipeline.RunDirect(options);
        });

        rootCommand.AddCommand(formatCommand);

        return await rootCommand.InvokeAsync(args);
    }
}
