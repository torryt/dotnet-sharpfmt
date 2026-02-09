using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using SharpFmt.Config;
using SharpFmt.Git;

namespace SharpFmt.Formatting;

/// <summary>
/// Formats a single C# file, optionally restricted to specific line ranges.
/// </summary>
public sealed class FileFormatter
{
    private readonly EditorConfigLoader _configLoader;

    public FileFormatter(EditorConfigLoader configLoader)
    {
        _configLoader = configLoader;
    }

    /// <summary>
    /// Format a file, restricting formatting to the given diff ranges.
    /// If ranges is null or empty, formats the entire file.
    /// When noExpand is true, line ranges are converted to TextSpans directly
    /// without expanding to enclosing syntax nodes.
    /// </summary>
    public FormatResult Format(string filePath, IReadOnlyList<DiffRange>? ranges = null, bool noExpand = false)
    {
        var fullPath = Path.GetFullPath(filePath);
        var originalText = File.ReadAllText(fullPath);

        var sourceText = SourceText.From(originalText);
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: fullPath);
        var root = tree.GetRoot();

        // Determine spans to format
        IReadOnlyList<TextSpan> spans;
        if (ranges is { Count: > 0 })
        {
            if (noExpand)
            {
                // Convert line ranges to TextSpans without syntax expansion
                spans = ranges
                    .Select(r => RangeExpander.LineRangeToTextSpan(sourceText, r))
                    .ToList();
            }
            else
            {
                spans = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);
            }
        }
        else
        {
            // Format entire file
            spans = [root.FullSpan];
        }

        // Create a workspace with .editorconfig options applied
        using var workspace = new AdhocWorkspace();
        ApplyEditorConfigOptions(workspace, fullPath);

        // Format the specified spans
        var formattedRoot = Formatter.Format(root, spans, workspace, cancellationToken: default);
        var formattedText = formattedRoot.ToFullString();

        return new FormatResult(fullPath, originalText, formattedText);
    }

    /// <summary>
    /// Maps .editorconfig key-value pairs to Roslyn workspace options so that
    /// <see cref="Formatter.Format(SyntaxNode, System.Collections.Generic.IEnumerable{TextSpan}, Workspace, OptionSet?, CancellationToken)"/>
    /// produces output consistent with the project's .editorconfig.
    /// </summary>
    private void ApplyEditorConfigOptions(AdhocWorkspace workspace, string filePath)
    {
        var configResult = _configLoader.GetOptionsForSourcePath(filePath);
        var options = configResult.AnalyzerOptions;

        if (options.IsEmpty)
            return;

#pragma warning disable CS0618 // Workspace.Options setter is marked obsolete but is the simplest API for AdhocWorkspace
        var optionSet = workspace.Options;

        // === Generic (language-neutral) formatting options ===
        optionSet = MapIndentStyle(optionSet, options);
        optionSet = MapIntPerLanguageOption(optionSet, options, "indent_size", FormattingOptions.IndentationSize);
        optionSet = MapIntPerLanguageOption(optionSet, options, "tab_width", FormattingOptions.TabSize);

        // === C#-specific spacing options ===
        optionSet = MapBoolOption(optionSet, options, "csharp_space_after_cast", CSharpFormattingOptions.SpaceAfterCast);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_after_comma", CSharpFormattingOptions.SpaceAfterComma);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_after_dot", CSharpFormattingOptions.SpaceAfterDot);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_after_semicolon_in_for_statement", CSharpFormattingOptions.SpaceAfterSemicolonsInForStatement);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_before_comma", CSharpFormattingOptions.SpaceBeforeComma);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_before_dot", CSharpFormattingOptions.SpaceBeforeDot);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_before_semicolon_in_for_statement", CSharpFormattingOptions.SpaceBeforeSemicolonsInForStatement);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_after_keywords_in_control_flow_statements", CSharpFormattingOptions.SpaceAfterControlFlowStatementKeyword);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_method_declaration_parameter_list_parentheses", CSharpFormattingOptions.SpaceWithinMethodDeclarationParenthesis);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_method_call_parameter_list_parentheses", CSharpFormattingOptions.SpaceWithinMethodCallParentheses);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_method_declaration_empty_parameter_list_parentheses", CSharpFormattingOptions.SpaceBetweenEmptyMethodDeclarationParentheses);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_method_call_empty_parameter_list_parentheses", CSharpFormattingOptions.SpaceBetweenEmptyMethodCallParentheses);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_method_declaration_name_and_open_parenthesis", CSharpFormattingOptions.SpacingAfterMethodDeclarationName);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_method_call_name_and_opening_parenthesis", CSharpFormattingOptions.SpaceAfterMethodCallName);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_within_expression_parentheses", CSharpFormattingOptions.SpaceWithinExpressionParentheses);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_within_cast_parentheses", CSharpFormattingOptions.SpaceWithinCastParentheses);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_within_other_parentheses", CSharpFormattingOptions.SpaceWithinOtherParentheses);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_within_square_brackets", CSharpFormattingOptions.SpaceWithinSquareBrackets);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_before_open_square_brackets", CSharpFormattingOptions.SpaceBeforeOpenSquareBracket);
        optionSet = MapBoolOption(optionSet, options, "csharp_space_between_empty_square_brackets", CSharpFormattingOptions.SpaceBetweenEmptySquareBrackets);

        // === C#-specific newline options ===
        optionSet = MapNewLineBeforeOpenBrace(optionSet, options);
        optionSet = MapBoolOption(optionSet, options, "csharp_new_line_before_else", CSharpFormattingOptions.NewLineForElse);
        optionSet = MapBoolOption(optionSet, options, "csharp_new_line_before_catch", CSharpFormattingOptions.NewLineForCatch);
        optionSet = MapBoolOption(optionSet, options, "csharp_new_line_before_finally", CSharpFormattingOptions.NewLineForFinally);
        optionSet = MapBoolOption(optionSet, options, "csharp_new_line_before_members_in_object_initializers", CSharpFormattingOptions.NewLineForMembersInObjectInit);
        optionSet = MapBoolOption(optionSet, options, "csharp_new_line_before_members_in_anonymous_types", CSharpFormattingOptions.NewLineForMembersInAnonymousTypes);
        optionSet = MapBoolOption(optionSet, options, "csharp_new_line_between_query_expression_clauses", CSharpFormattingOptions.NewLineForClausesInQuery);

        // === C#-specific indentation options ===
        optionSet = MapBoolOption(optionSet, options, "csharp_indent_block_contents", CSharpFormattingOptions.IndentBlock);
        optionSet = MapBoolOption(optionSet, options, "csharp_indent_braces", CSharpFormattingOptions.IndentBraces);
        optionSet = MapBoolOption(optionSet, options, "csharp_indent_case_contents", CSharpFormattingOptions.IndentSwitchCaseSection);
        optionSet = MapBoolOption(optionSet, options, "csharp_indent_case_contents_when_block", CSharpFormattingOptions.IndentSwitchCaseSectionWhenBlock);
        optionSet = MapBoolOption(optionSet, options, "csharp_indent_switch_labels", CSharpFormattingOptions.IndentSwitchSection);

        // === C#-specific wrapping options ===
        optionSet = MapBoolOption(optionSet, options, "csharp_preserve_single_line_statements", CSharpFormattingOptions.WrappingPreserveSingleLine);
        optionSet = MapBoolOption(optionSet, options, "csharp_preserve_single_line_blocks", CSharpFormattingOptions.WrappingKeepStatementsOnSingleLine);

        workspace.Options = optionSet;
#pragma warning restore CS0618
    }

    /// <summary>
    /// Maps indent_style to <see cref="FormattingOptions.UseTabs"/>.
    /// </summary>
    private static OptionSet MapIndentStyle(OptionSet optionSet, ImmutableDictionary<string, string> options)
    {
        if (options.TryGetValue("indent_style", out var value))
        {
            var useTabs = value.Equals("tab", StringComparison.OrdinalIgnoreCase);
            optionSet = optionSet.WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, useTabs);
        }

        return optionSet;
    }

    /// <summary>
    /// Maps a per-language integer option (e.g. indent_size, tab_width).
    /// </summary>
    private static OptionSet MapIntPerLanguageOption(
        OptionSet optionSet,
        ImmutableDictionary<string, string> options,
        string editorConfigKey,
        PerLanguageOption<int> roslynOption)
    {
        if (options.TryGetValue(editorConfigKey, out var value) &&
            int.TryParse(value, out var intValue) && intValue > 0)
        {
            optionSet = optionSet.WithChangedOption(roslynOption, LanguageNames.CSharp, intValue);
        }

        return optionSet;
    }

    /// <summary>
    /// Maps a boolean .editorconfig key to a Roslyn <see cref="Option{T}"/>.
    /// </summary>
    private static OptionSet MapBoolOption(
        OptionSet optionSet,
        ImmutableDictionary<string, string> options,
        string editorConfigKey,
        Option<bool> roslynOption)
    {
        if (options.TryGetValue(editorConfigKey, out var value))
        {
            var boolValue = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            optionSet = optionSet.WithChangedOption(roslynOption, boolValue);
        }

        return optionSet;
    }

    /// <summary>
    /// Maps <c>csharp_new_line_before_open_brace</c> to the Roslyn per-context newline options.
    /// Accepts <c>all</c>, <c>none</c>, or a comma-separated list of contexts:
    /// <c>types</c>, <c>methods</c>, <c>properties</c>, <c>accessors</c>,
    /// <c>control_blocks</c>, <c>anonymous_methods</c>, <c>anonymous_types</c>,
    /// <c>object_collection_array_initializers</c>, <c>lambdas</c>, <c>local_functions</c>.
    /// </summary>
    private static OptionSet MapNewLineBeforeOpenBrace(OptionSet optionSet, ImmutableDictionary<string, string> options)
    {
        if (!options.TryGetValue("csharp_new_line_before_open_brace", out var value))
            return optionSet;

        var trimmed = value.Trim().ToLowerInvariant();

        if (trimmed == "none")
        {
            return SetAllBraceNewlines(optionSet, false);
        }

        if (trimmed == "all")
        {
            return SetAllBraceNewlines(optionSet, true);
        }

        // Start with all off, then enable the requested contexts
        optionSet = SetAllBraceNewlines(optionSet, false);
        var contexts = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var context in contexts)
        {
            optionSet = context switch
            {
                "types" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInTypes, true),
                "methods" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInMethods, true),
                "properties" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInProperties, true),
                "accessors" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAccessors, true),
                "control_blocks" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInControlBlocks, true),
                "anonymous_methods" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAnonymousMethods, true),
                "anonymous_types" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAnonymousTypes, true),
                "object_collection_array_initializers" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInObjectCollectionArrayInitializers, true),
                "lambdas" => optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInLambdaExpressionBody, true),
                _ => optionSet
            };
        }

        return optionSet;
    }

    private static OptionSet SetAllBraceNewlines(OptionSet optionSet, bool value)
    {
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInTypes, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInMethods, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInProperties, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAccessors, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInControlBlocks, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAnonymousMethods, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAnonymousTypes, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInObjectCollectionArrayInitializers, value);
        optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInLambdaExpressionBody, value);
        return optionSet;
    }
}
