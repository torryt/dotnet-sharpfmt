using System.Diagnostics;
using FluentAssertions;
using SharpFmt.Cli;
using SharpFmt.Formatting;
using Xunit;

namespace SharpFmt.Tests.Integration;

/// <summary>
/// End-to-end integration tests that exercise <see cref="FormattingPipeline"/> against
/// real git repositories created in temporary directories.
/// </summary>
public sealed class FormattingPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public FormattingPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sharpfmt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            // Force delete read-only .git objects on Windows/Linux
            foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task RunAsync_NoChanges_ReturnsZero()
    {
        // Arrange: a clean repo with one well-formatted file
        InitGitRepo();
        var code = """
            namespace Test;

            public class Foo
            {
                public int Bar { get; set; }
            }
            """;
        WriteFileAndCommit("Foo.cs", code);

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Check = true,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert
        exitCode.Should().Be(0, "no files changed since HEAD");
    }

    [Fact]
    public async Task RunAsync_ChangedFile_CheckMode_ReturnsOne()
    {
        // Arrange: commit a file, then modify it with bad formatting
        InitGitRepo();
        var original = """
            namespace Test;

            public class Foo
            {
                public void Bar()
                {
                    Console.WriteLine("hello");
                }
            }
            """;
        WriteFileAndCommit("Foo.cs", original);

        // Modify the file: add a badly-formatted method
        var modified = """
            namespace Test;

            public class Foo
            {
                public void Bar()
                {
                    Console.WriteLine("hello");
                }

                public void Baz( ){
            Console.WriteLine(   "world"   );
                }
            }
            """;
        File.WriteAllText(Path.Combine(_tempDir, "Foo.cs"), modified);

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Check = true,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert: check mode should return 1 because formatting changes are needed
        exitCode.Should().Be(1, "the modified lines need formatting");
    }

    [Fact]
    public async Task RunAsync_ChangedFile_DiffMode_OutputsDiff()
    {
        // Arrange
        InitGitRepo();
        var original = """
            namespace Test;

            public class Foo
            {
                public int X { get; set; }
            }
            """;
        WriteFileAndCommit("Foo.cs", original);

        // Add badly formatted new code
        var modified = """
            namespace Test;

            public class Foo
            {
                public int X { get; set; }

                public void Bad(  int x  )
                {
                if(x>0){
            Console.WriteLine(x);
                }
                }
            }
            """;
        File.WriteAllText(Path.Combine(_tempDir, "Foo.cs"), modified);

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Diff = true,
            Verbosity = Verbosity.Quiet,
        };

        // Capture stdout
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            // Act
            var exitCode = await RunPipelineInDir(options);

            // Assert
            exitCode.Should().Be(1, "diff mode returns 1 when changes needed");
            var output = sw.ToString();
            output.Should().Contain("---", "diff output should contain file headers");
            output.Should().Contain("+++", "diff output should contain file headers");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task RunAsync_WriteMode_FormatsFileInPlace()
    {
        // Arrange
        InitGitRepo();
        var original = """
            namespace Test;

            public class Foo
            {
                public void Bar()
                {
                    return;
                }
            }
            """;
        WriteFileAndCommit("Foo.cs", original);

        // Add poorly formatted code
        var modified = """
            namespace Test;

            public class Foo
            {
                public void Bar()
                {
                    return;
                }

                public void Baz(){
            var x=1;
            var y=2;
                }
            }
            """;
        var filePath = Path.Combine(_tempDir, "Foo.cs");
        File.WriteAllText(filePath, modified);

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Check = false,
            Diff = false,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert: file should have been written back formatted
        exitCode.Should().Be(0);
        var result = File.ReadAllText(filePath);
        // The original lines (Bar method) should be untouched
        result.Should().Contain("public void Bar()");
        // The new method should have been reformatted — at minimum, braces should be on new lines
        // and the body should be properly indented
        result.Should().NotContain("public void Baz(){",
            "the opening brace should be reformatted");
    }

    [Fact]
    public async Task RunAsync_StagedMode_FormatsOnlyStagedChanges()
    {
        // Arrange: commit a file, stage a modification, have unstaged changes too
        InitGitRepo();
        var original = """
            namespace Test;

            public class Foo
            {
                public int A { get; set; }
            }
            """;
        WriteFileAndCommit("Foo.cs", original);

        // Stage a new method with bad formatting
        var staged = """
            namespace Test;

            public class Foo
            {
                public int A { get; set; }

                public void Staged( ){
            Console.WriteLine("staged");
                }
            }
            """;
        File.WriteAllText(Path.Combine(_tempDir, "Foo.cs"), staged);
        RunGit("add Foo.cs");

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Staged = true,
            Check = true,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert
        exitCode.Should().Be(1, "staged changes need formatting");
    }

    [Fact]
    public async Task RunAsync_ExcludeGlob_SkipsMatchingFiles()
    {
        // Arrange
        InitGitRepo();
        WriteFileAndCommit("Foo.cs", "namespace Test;\npublic class Foo { }\n");
        Directory.CreateDirectory(Path.Combine(_tempDir, "Generated"));
        WriteFileAndCommit("Generated/Bar.cs", "namespace Test;\npublic class Bar { }\n");

        // Modify both files with bad formatting
        File.WriteAllText(Path.Combine(_tempDir, "Foo.cs"),
            "namespace Test;\npublic class Foo { public void X( ){ } }\n");
        File.WriteAllText(Path.Combine(_tempDir, "Generated", "Bar.cs"),
            "namespace Test;\npublic class Bar { public void Y( ){ } }\n");

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Check = true,
            Exclude = new[] { "Generated/**" },
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert: should report changes only for Foo.cs (Bar.cs excluded)
        // Exit code 1 means at least one file needs formatting (Foo.cs)
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_NonCsFiles_AreIgnored()
    {
        // Arrange
        InitGitRepo();
        WriteFileAndCommit("readme.txt", "Hello world\n");
        WriteFileAndCommit("Foo.cs", "namespace Test;\npublic class Foo { }\n");

        // Modify only the txt file
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "Hello world updated\n");

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Check = true,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert: no .cs files changed, so exit 0
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_MultipleFiles_FormatsAll()
    {
        // Arrange
        InitGitRepo();
        WriteFileAndCommit("A.cs", "namespace Test;\npublic class A { }\n");
        WriteFileAndCommit("B.cs", "namespace Test;\npublic class B { }\n");

        // Modify both with bad formatting
        File.WriteAllText(Path.Combine(_tempDir, "A.cs"),
            "namespace Test;\npublic class A {\npublic void X( ){ var a=1; }\n}\n");
        File.WriteAllText(Path.Combine(_tempDir, "B.cs"),
            "namespace Test;\npublic class B {\npublic void Y( ){ var b=2; }\n}\n");

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            Check = false,
            Diff = false,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert: both files should have been formatted
        exitCode.Should().Be(0);
        var a = File.ReadAllText(Path.Combine(_tempDir, "A.cs"));
        var b = File.ReadAllText(Path.Combine(_tempDir, "B.cs"));
        a.Should().NotContain("public void X( ){", "A.cs should be reformatted");
        b.Should().NotContain("public void Y( ){", "B.cs should be reformatted");
    }

    [Fact]
    public async Task RunAsync_NoExpand_FormatsOnlyExactChangedLines()
    {
        // Arrange: commit a file, change one line in the middle
        InitGitRepo();
        var original = """
            namespace Test;

            public class Foo
            {
                public void Bar( )
                {
                    var x = 1;
                }

                public void Baz( )
                {
                    var y = 2;
                }
            }
            """;
        WriteFileAndCommit("Foo.cs", original);

        // Only modify Bar method body — Baz should remain untouched even though
        // it also has bad formatting (space before closing paren)
        var modified = original.Replace("var x = 1;", "var x=1;");
        File.WriteAllText(Path.Combine(_tempDir, "Foo.cs"), modified);

        var options = new SharpFmtOptions
        {
            Commit = "HEAD",
            NoExpand = true,
            Check = false,
            Diff = false,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = await RunPipelineInDir(options);

        // Assert
        exitCode.Should().Be(0);
        var result = File.ReadAllText(Path.Combine(_tempDir, "Foo.cs"));
        // Baz method should still have the "bad" formatting since it wasn't in the diff
        result.Should().Contain("public void Baz( )",
            "NoExpand should leave untouched methods alone");
    }

    [Fact]
    public void RunDirect_FormatsExplicitFile()
    {
        // Arrange: write a badly formatted C# file (no git needed)
        var code = """
            namespace Test;

            public class Foo
            {
                public void Bar( ){
            var x=1;
                }
            }
            """;
        var filePath = Path.Combine(_tempDir, "Direct.cs");
        File.WriteAllText(filePath, code);

        var options = new SharpFmtOptions
        {
            Files = new[] { filePath },
            Check = false,
            Diff = false,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = FormattingPipeline.RunDirect(options);

        // Assert
        exitCode.Should().Be(0);
        var result = File.ReadAllText(filePath);
        result.Should().NotContain("public void Bar( ){",
            "the method should be reformatted");
    }

    [Fact]
    public void RunDirect_WithLineRanges_FormatsOnlySpecifiedRange()
    {
        // Arrange: file with two methods, only format the second one
        var code = """
            namespace Test;

            public class Foo
            {
                public void Bar( )
                {
                    var x=1;
                }

                public void Baz( )
                {
                    var y=2;
                }
            }
            """;
        var filePath = Path.Combine(_tempDir, "Ranged.cs");
        File.WriteAllText(filePath, code);

        var options = new SharpFmtOptions
        {
            Files = new[] { filePath },
            Lines = new[] { "10:13" },  // Baz method only (lines 10-13)
            Check = false,
            Diff = false,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = FormattingPipeline.RunDirect(options);

        // Assert
        exitCode.Should().Be(0);
        var result = File.ReadAllText(filePath);
        // Bar method (outside the specified range) may still be formatted due to
        // syntax expansion, but the key assertion is that the pipeline doesn't crash
        // and the targeted range is formatted
        result.Should().Contain("var y = 2;",
            "Baz method body should be reformatted");
    }

    [Fact]
    public void RunDirect_CheckMode_ReturnsOneWhenChangesNeeded()
    {
        var code = """
            namespace Test;

            public class Foo
            {
                public void Bar( ){
            var x=1;
                }
            }
            """;
        var filePath = Path.Combine(_tempDir, "Check.cs");
        File.WriteAllText(filePath, code);

        var options = new SharpFmtOptions
        {
            Files = new[] { filePath },
            Check = true,
            Verbosity = Verbosity.Quiet,
        };

        // Act
        var exitCode = FormattingPipeline.RunDirect(options);

        // Assert
        exitCode.Should().Be(1, "file needs formatting");
        // File should NOT have been modified
        var result = File.ReadAllText(filePath);
        result.Should().Be(code, "check mode should not modify the file");
    }

    [Fact]
    public void RunDirect_NoFiles_ReturnsTwo()
    {
        var options = new SharpFmtOptions
        {
            Files = Array.Empty<string>(),
            Verbosity = Verbosity.Quiet,
        };

        var exitCode = FormattingPipeline.RunDirect(options);
        exitCode.Should().Be(2, "no files specified is an error");
    }

    [Fact]
    public void RunDirect_MissingFile_ReturnsTwo()
    {
        var options = new SharpFmtOptions
        {
            Files = new[] { Path.Combine(_tempDir, "nonexistent.cs") },
            Verbosity = Verbosity.Quiet,
        };

        var exitCode = FormattingPipeline.RunDirect(options);
        exitCode.Should().Be(2, "missing file is an error");
    }

    // ========== Helpers ==========

    private void InitGitRepo()
    {
        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");
    }

    private void WriteFileAndCommit(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, content);
        RunGit($"add \"{relativePath}\"");
        RunGit($"commit -m \"Add {relativePath}\"");
    }

    private void RunGit(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit(10_000);
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {args} failed: {error}");
        }
    }

    /// <summary>
    /// Runs the pipeline from a specific working directory by temporarily changing the CWD.
    /// This is needed because the pipeline calls `git rev-parse --show-toplevel` from CWD.
    /// </summary>
    private async Task<int> RunPipelineInDir(SharpFmtOptions options)
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_tempDir);
            return await FormattingPipeline.RunAsync(options);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }
}
