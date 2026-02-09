# csharp-formatter: Plan Document

## Name Suggestions

| Name | Rationale |
|---|---|
| **dotnet-deltaformat** | "delta" = only changed lines; clear dotnet tool naming convention |
| **dotnet-fmtdiff** | Short, echoes `git clang-format` + `fmt`; dotnet tool convention |
| **dotnet-sharpfmt** | "sharp" for C#, "fmt" for format; memorable |
| **csfmt** | Terse; mirrors `gofmt`, `rustfmt`; easy to type |
| **dotnet-surgefmt** | "surge" implies speed; distinctive |

Recommendation: **`dotnet-sharpfmt`** — installs as `dotnet tool install dotnet-sharpfmt`, invoked as `dotnet sharpfmt` or just `sharpfmt`. Short, obvious, follows the `*fmt` convention.

---

## 1. Problem Statement

`dotnet format` is slow (~seconds) because it loads full MSBuild projects, restores NuGet, and runs analyzers. It cannot format only changed lines — it's all-or-nothing per file. This makes it unusable as a fast pre-commit hook or CI gate for large repos.

We need a tool that:
- Formats **only the changed lines** (like `git clang-format` does for C/C++)
- Reads **.editorconfig** with the same interpretation as `dotnet format`
- Is **fast** (target: <200ms for a typical PR touching 10-20 files)
- Supports a **`--check` mode** for CI (exit code 1 if changes needed)
- Distributes as a **dotnet tool**

---

## 2. How `git clang-format` Works (and how we'll mirror it)

After reading the `git-clang-format` source, the algorithm is:

1. Run `git diff-index -p -U0 <commit>` to get a zero-context unified diff
2. Parse the `@@ -a,b +c,d @@` hunk headers to extract **(filename → list of (start_line, count))** ranges
3. Filter to only files with supported extensions (`.cs` in our case)
4. For each file, invoke the formatter with `--lines=start:end` for each range
5. Compare original vs formatted; apply changes or report diff

**Our equivalent:**

1. Run `git diff-index -p -U0 HEAD` (or `--staged`, or between two commits)
2. Parse hunk headers → extract changed line ranges per `.cs` file
3. For each file, use Roslyn to parse → expand line ranges to enclosing syntax nodes → format those spans
4. Compare and apply/report

---

## 3. Architecture

```
┌─────────────────────────────────────────────────────────┐
│                      CLI Entry Point                     │
│  (System.CommandLine)                                    │
│                                                          │
│  Commands:                                               │
│    sharpfmt [<commit>] [--staged] [--check] [--diff]    │
│    sharpfmt format <files...> [--lines=S:E]             │
│                                                          │
└──────────┬──────────────────────────────────┬───────────┘
           │                                  │
           ▼                                  ▼
┌─────────────────────┐           ┌─────────────────────┐
│   Git Diff Engine   │           │  Direct File Mode   │
│                     │           │  (explicit files +   │
│  • Shells out to    │           │   --lines ranges)    │
│    git diff-index   │           │                      │
│  • Parses unified   │           └──────────┬──────────┘
│    diff hunks       │                      │
│  • Returns:         │                      │
│    Dict<file,       │                      │
│      List<Range>>   │                      │
└──────────┬──────────┘                      │
           │                                  │
           ▼                                  ▼
┌─────────────────────────────────────────────────────────┐
│                  Formatting Pipeline                     │
│                                                          │
│  1. Load .editorconfig (EditorConfigParser)              │
│  2. Parse C# file → SyntaxTree (Roslyn)                 │
│  3. Expand line ranges to enclosing syntax nodes         │
│  4. Convert to TextSpan regions                          │
│  5. Call Formatter.Format(root, spans, workspace, opts)  │
│  6. Diff original vs formatted text                      │
│                                                          │
└──────────┬──────────────────────────────────────────────┘
           │
           ▼
┌─────────────────────────────────────────────────────────┐
│                    Output Handler                        │
│                                                          │
│  --check mode:  exit 0 (clean) or exit 1 (dirty)       │
│  --diff mode:   print unified diff to stdout            │
│  default:       write formatted files in place          │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## 4. Key Design Decisions

### 4.1 No MSBuild / No NuGet Restore

This is the single biggest speed win. `dotnet format` loads MSBuild to resolve projects, restores packages, and spins up analyzers. We skip all of that:

- Parse `.cs` files directly with `CSharpSyntaxTree.ParseText()`
- Load `.editorconfig` ourselves using Roslyn's `AnalyzerConfigSet` API (the same code path `dotnet format` uses internally, but without the MSBuild wrapper)
- No project/solution file required

**Trade-off:** We can only do **whitespace/formatting** rules (IDE0055 category — indentation, spacing, newlines, braces). We cannot do style analysis rules that require semantic models (e.g., "use `var`"). This is acceptable — those are slow by nature and not what a fast pre-commit formatter should do.

### 4.2 Syntax-Aware Range Expansion

When git reports that line 42 changed, we don't just format line 42. We:

1. Map line 42 to a character offset in the `SourceText`
2. Find the smallest enclosing `SyntaxNode` that represents a complete statement, member, or block
3. Expand the `TextSpan` to cover that entire node
4. Merge overlapping/adjacent spans

This ensures formatting is syntactically consistent. For example, if you change one line inside an `if` block, we reformat the entire `if` block.

**Expansion heuristic (ordered, take first match):**
- `StatementSyntax` (if, for, while, return, expression statement, etc.)
- `MemberDeclarationSyntax` (method, property, field, constructor)
- `TypeDeclarationSyntax` (class, struct, interface, record)
- `CompilationUnitSyntax` (whole file — only as ultimate fallback)

### 4.3 .editorconfig Loading

We use Roslyn's `AnalyzerConfigOptionsResult` to interpret `.editorconfig` files. This gives us byte-for-byte compatibility with how `dotnet format` and the C# compiler read `.editorconfig`:

```csharp
var configs = AnalyzerConfigSet.Create(analyzerConfigs);
var options = configs.GetOptionsForSourcePath(filePath);
// Convert to CSharpFormattingOptions
```

We walk up from each `.cs` file's directory to find `.editorconfig` files, respecting `root = true`.

### 4.4 Parallelism

Files are independent. We format them in parallel using `Parallel.ForEachAsync` with a configurable degree of parallelism (default: `Environment.ProcessorCount`).

### 4.5 Language: C# (.NET 8+)

Since we depend on Roslyn's `Microsoft.CodeAnalysis.CSharp.Workspaces` anyway, writing it in Go/Rust would require P/Invoke or hosting the .NET runtime — adding complexity for no benefit. C# with NativeAOT compilation is an option for faster startup if needed later, but a standard dotnet tool is simpler to distribute and maintain.

---

## 5. CLI Interface

```
USAGE:
    sharpfmt [OPTIONS] [<commit>] [--] [<file>...]

DESCRIPTION:
    Format only the C# lines that changed since <commit> (default: HEAD).

OPTIONS:
    --staged            Format changes in the staging area (index)
    --check             Exit with code 1 if any files would change (CI mode)
    --diff              Print a unified diff instead of modifying files
    --no-expand         Format only the exact changed lines, not enclosing blocks
    --include <glob>    Only format files matching this glob (default: **/*.cs)
    --exclude <glob>    Exclude files matching this glob
    -j, --jobs <N>      Number of parallel workers (default: CPU count)
    -v, --verbosity     quiet | normal | verbose
    --version           Print version
    -h, --help          Print help

EXAMPLES:
    # Format staged changes (pre-commit hook)
    sharpfmt --staged

    # Check a PR branch against main (CI)
    sharpfmt --check main

    # Format everything touched since last commit
    sharpfmt HEAD~1

    # Format specific files, specific line ranges
    sharpfmt format Program.cs --lines=10:25 --lines=40:60
```

### Exit Codes

| Code | Meaning |
|------|---------|
| 0    | Success (no changes needed, or changes applied) |
| 1    | `--check` mode: formatting changes would be needed |
| 2    | Error (git failure, parse error, etc.) |

---

## 6. Project Structure

```
dotnet-sharpfmt/
├── src/
│   └── SharpFmt/
│       ├── SharpFmt.csproj              # dotnet tool, net8.0
│       ├── Program.cs                    # CLI entry point
│       ├── Cli/
│       │   ├── RootCommand.cs            # System.CommandLine setup
│       │   └── Options.cs                # Parsed CLI options record
│       ├── Git/
│       │   ├── GitDiffParser.cs          # Run git diff, parse hunks
│       │   └── DiffRange.cs              # (file, startLine, lineCount) model
│       ├── Config/
│       │   └── EditorConfigLoader.cs     # Walk dirs, load AnalyzerConfigs
│       ├── Formatting/
│       │   ├── RangeExpander.cs          # Line ranges → enclosing syntax spans
│       │   ├── FileFormatter.cs          # Parse + format single file
│       │   └── FormatResult.cs           # Original text, formatted text, path
│       └── Output/
│           ├── DiffPrinter.cs            # Unified diff output
│           └── ResultWriter.cs           # Write files / report results
├── tests/
│   └── SharpFmt.Tests/
│       ├── SharpFmt.Tests.csproj
│       ├── GitDiffParserTests.cs
│       ├── RangeExpanderTests.cs
│       ├── FileFormatterTests.cs
│       └── EditorConfigLoaderTests.cs
├── samples/
│   └── .editorconfig                     # Sample config for testing
├── dotnet-sharpfmt.sln
├── Directory.Build.props
├── .editorconfig
└── README.md
```

---

## 7. Dependencies

| Package | Purpose | Version |
|---------|---------|---------|
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | Roslyn parser + Formatter API | 4.9+ |
| `System.CommandLine` | CLI parsing | 2.0.0-beta4 |
| `Microsoft.Extensions.FileSystemGlobbing` | File glob matching | 8.0+ |

No MSBuild packages. No NuGet restore logic. No analyzer hosting.

---

## 8. Performance Budget

| Operation | Target | Notes |
|-----------|--------|-------|
| Git diff parsing | <10ms | Simple text processing |
| .editorconfig loading | <20ms | File I/O + parsing, cached |
| Roslyn parse (per file) | ~5-15ms | Syntax-only, no semantic model |
| Roslyn format (per file, partial spans) | ~5-20ms | Depends on span size |
| Total for 10 files | <200ms | With parallel processing |
| Total for 1 file | <50ms | Pre-commit fast path |

The key insight is that **syntax-only parsing is fast**. The expensive parts of Roslyn (semantic analysis, binding, type resolution) are completely avoided.

---

## 9. Implementation Phases

### Phase 1: Core Formatter (MVP)
- [ ] Project scaffolding (sln, csproj, Directory.Build.props)
- [ ] EditorConfigLoader: walk directories, parse configs, produce formatting options
- [ ] FileFormatter: parse a .cs file, format given TextSpan regions, return result
- [ ] RangeExpander: convert line ranges to syntax-aware TextSpans
- [ ] Unit tests for the above

### Phase 2: Git Integration
- [ ] GitDiffParser: shell out to `git diff-index`, parse hunks
- [ ] Wire up: git diff → range expansion → formatting pipeline
- [ ] Support `--staged`, `<commit>`, two-commit mode
- [ ] Integration tests with a test git repo

### Phase 3: CLI & Output
- [ ] System.CommandLine setup with all options
- [ ] `--check` mode (exit code 1 if dirty)
- [ ] `--diff` mode (print unified diff)
- [ ] Default mode (write in place)
- [ ] `format` subcommand for explicit file + `--lines` usage

### Phase 4: Polish
- [ ] Parallel file processing
- [ ] Glob-based include/exclude
- [ ] Verbosity levels and structured logging
- [ ] dotnet tool packaging (`<PackAsTool>true</PackAsTool>`)
- [ ] README, usage docs
- [ ] CI pipeline for the tool itself

### Phase 5: Stretch Goals
- [ ] NativeAOT compilation for faster cold-start
- [ ] Pre-built git hook script (`sharpfmt --install-hook`)
- [ ] GitHub Action wrapper
- [ ] Watch mode (format on save)

---

## 10. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Roslyn's `Formatter.Format` with partial spans may produce inconsistent results at span boundaries | Incorrect formatting | Expand spans to syntactically meaningful boundaries (Phase 1 design) |
| `.editorconfig` options not 1:1 with `CSharpFormattingOptions` | Config parity gaps | Use Roslyn's own `AnalyzerConfig` parser (same code path as compiler) |
| `git diff-index` not available (not a git repo) | Tool fails | Graceful error message; support `format` subcommand for non-git use |
| Roslyn parse errors in incomplete files | Crash | Roslyn produces trees even for broken code; handle diagnostics gracefully |
| Cold start time of .NET runtime | Slow first invocation | Phase 5: NativeAOT. For now, acceptable — dotnet tools are typically ~100ms startup |

---

## 11. Non-Goals (Explicit)

- **Semantic analysis / style rules** (e.g., "use var", "prefer expression body") — these require project compilation and are inherently slow. Use `dotnet format` for those.
- **Non-C# languages** — this tool is C#-only.
- **IDE integration** — this is a CLI tool for CI/hooks. IDEs already have formatting built in.
- **Custom rule plugins** — we format according to .editorconfig only, no extensibility model.
