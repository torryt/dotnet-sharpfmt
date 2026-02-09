# dotnet-sharpfmt

Fast C# formatter for changed lines only. Like `git clang-format`, but for C#. Because apparently `dotnet format` taking 15 seconds to tell you about a missing space was a problem that needed solving.

`sharpfmt` uses Roslyn to format **only the lines you changed** — no MSBuild, no NuGet restore, no project files required. It reads `.editorconfig` for formatting rules and runs in under a second on typical PRs. Yes, under a second. No, we don't know how either.

> **Disclaimer:** This tool is **100% vibe coded**. A human mass-approved AI-generated code while sipping coffee. The test suite passes — all 123 tests, written by AI, reviewed by AI, approved by a human clicking "Looks good to me" with the confidence of someone who definitely understands every line. (They don't.) If you find a bug, congratulations — you've read more of the source code than the maintainer.

## Installation

```bash
dotnet tool install -g dotnet-sharpfmt
```

Or as a local tool, if you have trust issues with global installs (fair):

```bash
dotnet new tool-manifest   # if you don't have one yet
dotnet tool install dotnet-sharpfmt
```

## Quick Start

```bash
# Format changed lines since HEAD (default)
sharpfmt

# Format staged changes (for pre-commit hooks)
sharpfmt --staged

# Check if formatting is needed (CI mode, exits 1 if dirty)
sharpfmt --check HEAD

# Check a feature branch against main
sharpfmt --check main

# Show what would change as a unified diff
sharpfmt --diff

# Format everything changed since a specific commit
sharpfmt abc1234
```

## How It Works

1. Runs `git diff-index` to find changed line ranges in `.cs` files
2. Expands each changed range to the smallest enclosing syntax node (statement, member, or type) using Roslyn
3. Formats only those spans using `Roslyn Formatter.Format()` with your `.editorconfig` settings
4. Writes the result back (or reports via `--check`/`--diff`)

Because it skips MSBuild project loading, NuGet restore, and semantic analysis entirely, it's **fast** — typically under 1 second for a PR touching 20+ files. Your colleague's 200-file "small refactor" PR? Still under 2 seconds. The tool is faster than your code review, which, let's be honest, was just scrolling to the bottom and clicking approve.

## Commands

### Default (git-diff mode)

```
sharpfmt [<commit>] [options]
```

Formats lines that changed between your working tree (or staging area) and the given commit. You know, the thing you wish `dotnet format` did instead of reformatting your entire codebase and blaming you in the diff.

| Option | Description |
|--------|-------------|
| `<commit>` | Base commit to diff against (default: `HEAD`) |
| `--staged`, `--cached` | Format changes in the staging area |
| `--check` | Exit with code 1 if any files would change |
| `--diff` | Print a unified diff instead of modifying files |
| `--no-expand` | Format only the exact changed lines, not enclosing syntax blocks |
| `--include <glob>` | Only format files matching these globs (default: `**/*.cs`) |
| `--exclude <glob>` | Exclude files matching these globs |
| `-j, --jobs <N>` | Number of parallel workers (default: CPU count) |
| `-v, --verbosity` | `quiet`, `normal`, or `verbose` |

### `format` subcommand

```
sharpfmt format <files...> [options]
```

Format specific files directly, without git integration. For when you just want to format a file and not get into a philosophical debate about what "changed" means.

| Option | Description |
|--------|-------------|
| `<files>` | Files to format |
| `--lines <start:end>` | Line ranges to format (repeatable) |
| `--no-expand` | Don't expand line ranges to enclosing syntax blocks |
| `--check` | Exit with code 1 if any files would change |
| `--diff` | Print a unified diff instead of modifying files |
| `-v, --verbosity` | `quiet`, `normal`, or `verbose` |

Example:

```bash
sharpfmt format src/MyClass.cs --lines=10:25 --lines=40:60
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success — no changes needed, or changes applied |
| 1 | `--check`/`--diff` mode: formatting changes would be needed |
| 2 | Error (git failure, parse error, etc.) |

Exit code 2 means something went actually wrong. If you're seeing it a lot, maybe the problem isn't the tool.

## `.editorconfig` Support

`sharpfmt` reads `.editorconfig` files the same way the C# compiler does (via Roslyn's `AnalyzerConfigSet`). It respects `root = true` and supports a frankly unreasonable number of options:

**General:**
- `indent_style` (space/tab) — pick a side, we don't judge. Actually, we do. Spaces.
- `indent_size`
- `tab_width`

**C# spacing** (20 options, because apparently braces need personal space):
- `csharp_space_after_cast`, `csharp_space_after_comma`, `csharp_space_after_dot`
- `csharp_space_after_semicolon_in_for_statement`, `csharp_space_before_comma`, `csharp_space_before_dot`
- `csharp_space_before_semicolon_in_for_statement`
- `csharp_space_after_keywords_in_control_flow_statements`
- `csharp_space_between_method_declaration_parameter_list_parentheses`
- `csharp_space_between_method_call_parameter_list_parentheses`
- `csharp_space_between_method_declaration_empty_parameter_list_parentheses`
- `csharp_space_between_method_call_empty_parameter_list_parentheses`
- `csharp_space_between_method_declaration_name_and_open_parenthesis`
- `csharp_space_between_method_call_name_and_opening_parenthesis`
- `csharp_space_within_expression_parentheses`, `csharp_space_within_cast_parentheses`
- `csharp_space_within_other_parentheses`, `csharp_space_within_square_brackets`
- `csharp_space_before_open_square_brackets`, `csharp_space_between_empty_square_brackets`

**C# newlines** (7 options):
- `csharp_new_line_before_open_brace` (`all`, `none`, or comma-separated: `types`, `methods`, `properties`, `accessors`, `control_blocks`, `anonymous_methods`, `anonymous_types`, `object_collection_array_initializers`, `lambdas`)
- `csharp_new_line_before_else`, `csharp_new_line_before_catch`, `csharp_new_line_before_finally`
- `csharp_new_line_before_members_in_object_initializers`
- `csharp_new_line_before_members_in_anonymous_types`
- `csharp_new_line_between_query_expression_clauses`

**C# indentation** (5 options):
- `csharp_indent_block_contents`, `csharp_indent_braces`
- `csharp_indent_case_contents`, `csharp_indent_case_contents_when_block`
- `csharp_indent_switch_labels`

**C# wrapping** (2 options):
- `csharp_preserve_single_line_statements`
- `csharp_preserve_single_line_blocks`

## CI Integration

Because nothing says "team culture" like blocking PRs over a missing space before a brace.

### GitHub Actions

```yaml
- name: Check formatting
  run: |
    dotnet tool restore
    dotnet sharpfmt --check ${{ github.event.pull_request.base.sha }}
```

### Azure DevOps

```yaml
- script: |
    dotnet tool restore
    dotnet sharpfmt --check $(System.PullRequest.TargetBranch)
  displayName: Check C# formatting
```

### Pre-commit Hook

Add to `.git/hooks/pre-commit` if you enjoy being told "no" by your own computer:

```bash
#!/bin/sh
exec sharpfmt --staged --check
```

Or with [pre-commit](https://pre-commit.com/), add a local hook in `.pre-commit-config.yaml`:

```yaml
repos:
  - repo: local
    hooks:
      - id: sharpfmt
        name: sharpfmt
        entry: sharpfmt --staged --check
        language: system
        types: [c#]
```

## Compared to `dotnet format`

| | `dotnet format` | `sharpfmt` |
|---|---|---|
| Requires project/solution | Yes | No |
| NuGet restore | Yes | No |
| Formats changed lines only | No (whole file) | Yes |
| Speed (10 files) | ~5-15s | <1s |
| Semantic analysis | Yes | No |
| Style rules (use var, etc.) | Yes | No |
| Whitespace/formatting rules | Yes | Yes |
| Vibe coded | Probably not | Absolutely |

`sharpfmt` is designed as a complement to `dotnet format` — use `sharpfmt` for fast pre-commit and CI formatting checks, and `dotnet format` for full style enforcement when you need semantic analysis. Or, you know, use both and let the robots fight over your indentation.

## Requirements

- .NET 9.0 or later
- Git (for the default git-diff mode; not needed for `sharpfmt format`)
- A willingness to trust code that no human has fully read

## License

MIT — because even vibe-coded software deserves freedom.
