# dotnet-sharpfmt

Fast C# formatter for changed lines only. Like `git clang-format`, but for C#.

`sharpfmt` uses Roslyn to format **only the lines you changed** — no MSBuild, no NuGet restore, no project files required. It reads `.editorconfig` for formatting rules and runs in under a second on typical PRs.

## Installation

```bash
dotnet tool install -g dotnet-sharpfmt
```

Or as a local tool:

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

Because it skips MSBuild project loading, NuGet restore, and semantic analysis entirely, it's **fast** — typically under 1 second for a PR touching 20+ files.

## Commands

### Default (git-diff mode)

```
sharpfmt [<commit>] [options]
```

Formats lines that changed between your working tree (or staging area) and the given commit.

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

Format specific files directly, without git integration.

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

## `.editorconfig` Support

`sharpfmt` reads `.editorconfig` files the same way the C# compiler does (via Roslyn's `AnalyzerConfigSet`). It respects `root = true` and supports the following options:

**General:**
- `indent_style` (space/tab)
- `indent_size`
- `tab_width`

**C# spacing** (20 options):
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

Add to `.git/hooks/pre-commit`:

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

`sharpfmt` is designed as a complement to `dotnet format` — use `sharpfmt` for fast pre-commit and CI formatting checks, and `dotnet format` for full style enforcement when you need semantic analysis.

## Requirements

- .NET 9.0 or later
- Git (for the default git-diff mode; not needed for `sharpfmt format`)

## License

MIT
