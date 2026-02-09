using FluentAssertions;
using SharpFmt.Config;
using SharpFmt.Formatting;
using SharpFmt.Git;
using Xunit;

namespace SharpFmt.Tests.Formatting;

/// <summary>
/// Comprehensive test suite for <see cref="FileFormatter"/>.
/// Tests file formatting with optional diff ranges and editorconfig support.
/// </summary>
public class FileFormatterTests
{
    /// <summary>
    /// Helper class for managing temporary directories in tests.
    /// </summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "sharpfmt-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Suppress exceptions during cleanup
            }
        }
    }

    /// <summary>
    /// Test: Format whole file - badly formatted C# code gets properly indented.
    /// </summary>
    [Fact]
    public void Format_WholeFile_BadlyFormattedCode_GetsProperlyIndented()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var badlyFormatted = @"public class HelloWorld
{
public void Greet()
{
Console.WriteLine(""Hello"");
}
}";

        File.WriteAllText(filePath, badlyFormatted);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.FilePath.Should().Be(System.IO.Path.GetFullPath(filePath));
        result.OriginalText.Should().Be(badlyFormatted);
        result.FormattedText.Should().NotBeNullOrEmpty();
        result.HasChanges.Should().BeTrue();
        
        // The formatted text should have proper indentation (method indented relative to class)
        result.FormattedText.Should().Contain("public void Greet()");
        result.FormattedText.Should().Contain("Console.WriteLine");
    }

    /// <summary>
    /// Test: Format with ranges - only specified lines are touched.
    /// </summary>
    [Fact]
    public void Format_WithDiffRanges_OnlySpecifiedLinesAreFormatted()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        // Line 1-2: well formatted, lines 3-5: badly formatted
        var content = @"public class Test
{
public void Method()
{
Console.WriteLine(""Hello"");
}
}";

        File.WriteAllText(filePath, content);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Format only lines 3-5 (the badly formatted method)
        var ranges = new[] { new DiffRange(StartLine: 3, LineCount: 3) };

        // Act
        var result = formatter.Format(filePath, ranges);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
        
        // The first two lines should remain unchanged (class declaration)
        result.FormattedText.Should().StartWith("public class Test");
    }

    /// <summary>
    /// Test: Format already-formatted code - HasChanges should be false.
    /// </summary>
    [Fact]
    public void Format_AlreadyFormattedCode_HasChangesIsFalse()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        var editorConfigPath = System.IO.Path.Combine(tempDir.Path, ".editorconfig");

        // Create .editorconfig with tab indentation to match default behavior
        var editorConfig = @"root = true

[*.cs]
indent_style = tab
";
        File.WriteAllText(editorConfigPath, editorConfig);
        
        var wellFormatted = @"public class HelloWorld
{
	public void Greet()
	{
		Console.WriteLine(""Hello"");
	}
}
";

        File.WriteAllText(filePath, wellFormatted);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.OriginalText.Should().Be(wellFormatted);
        result.FormattedText.Should().Be(wellFormatted);
        result.HasChanges.Should().BeFalse();
    }

    /// <summary>
    /// Test: FormatResult.HasChanges property - returns true when text differs, false when same.
    /// </summary>
    [Fact]
    public void FormatResult_HasChanges_ReturnsTrueWhenTextDiffers()
    {
        // Arrange
        var original = "public class Foo { }";
        var formatted = "public class Foo\n{\n}";
        
        var result = new FormatResult(
            FilePath: "/test/file.cs",
            OriginalText: original,
            FormattedText: formatted);

        // Act & Assert
        result.HasChanges.Should().BeTrue();
    }

    /// <summary>
    /// Test: FormatResult.HasChanges property - returns false when text is identical.
    /// </summary>
    [Fact]
    public void FormatResult_HasChanges_ReturnsFalseWhenTextIsIdentical()
    {
        // Arrange
        var text = "public class Foo { }";
        
        var result = new FormatResult(
            FilePath: "/test/file.cs",
            OriginalText: text,
            FormattedText: text);

        // Act & Assert
        result.HasChanges.Should().BeFalse();
    }

    /// <summary>
    /// Test: Format respects .editorconfig indent_size setting.
    /// Creates editorconfig with indent_size=2 and verifies output uses 2-space indentation.
    /// </summary>
    [Fact]
    public void Format_RespectsEditorConfigIndentSize_Uses2Spaces()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        var editorConfigPath = System.IO.Path.Combine(tempDir.Path, ".editorconfig");

        var code = @"public class Test
{
public void Method()
{
Console.WriteLine(""Hello"");
}
}";

        File.WriteAllText(filePath, code);
        
        // Create .editorconfig with indent_size=2
        var editorConfig = @"root = true

[*.cs]
indent_size = 2
indent_style = space
";
        File.WriteAllText(editorConfigPath, editorConfig);

        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
        
        // Check that indentation uses 2 spaces (method body should have 2 spaces)
        result.FormattedText.Should().Contain("  public void Method()");
        result.FormattedText.Should().Contain("    Console.WriteLine");
    }

    /// <summary>
    /// Test: Format with empty DiffRanges list formats the entire file.
    /// </summary>
    [Fact]
    public void Format_WithEmptyDiffRangesList_FormatsEntireFile()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var badlyFormatted = @"public class Test
{
public void Method()
{
Console.WriteLine(""Hello"");
}
}";

        File.WriteAllText(filePath, badlyFormatted);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath, ranges: new List<DiffRange>());

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
        
        // Should have formatted the entire file
        result.FormattedText.Should().Contain("    public void Method()");
    }

    /// <summary>
    /// Test: Format preserves file path in result.
    /// </summary>
    [Fact]
    public void Format_PreservesFilePathInResult()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        var content = "public class Test { }";
        
        File.WriteAllText(filePath, content);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.FilePath.Should().Be(System.IO.Path.GetFullPath(filePath));
    }

    /// <summary>
    /// Test: Format with relative file path converts to absolute path.
    /// </summary>
    [Fact]
    public void Format_WithRelativeFilePath_ConvertsToAbsolutePath()
    {
        // Arrange
        using var tempDir = new TempDir();
        var fileName = "test.cs";
        var filePath = System.IO.Path.Combine(tempDir.Path, fileName);
        var content = "public class Test { }";
        
        File.WriteAllText(filePath, content);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);
        
        // Change to temp directory for this test
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.Path);

            // Act
            var result = formatter.Format(fileName);

            // Assert
            result.FilePath.Should().Be(System.IO.Path.GetFullPath(filePath));
            System.IO.Path.IsPathRooted(result.FilePath).Should().BeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    /// <summary>
    /// Test: Format handles C# with using statements and namespaces.
    /// </summary>
    [Fact]
    public void Format_HandlesComplexCSharpWithUsingsAndNamespaces()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"using System;
using System.Linq;
namespace MyApp
{
public class Program
{
public static void Main()
{
var numbers=new[]{1,2,3};
var sum=numbers.Sum();
Console.WriteLine(sum);
}
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().Contain("using System");
        result.FormattedText.Should().Contain("namespace MyApp");
    }

    /// <summary>
    /// Test: Format handles method with parameters and complex expressions.
    /// </summary>
    [Fact]
    public void Format_HandlesMethodsWithParametersAndComplexExpressions()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Calculator
{
public int Add(int a,int b)
{
return a+b;
}
public int Multiply(int x, int y)
{
return x*y;
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
        
        // Check that method signatures are properly formatted
        result.FormattedText.Should().Contain("public int Add(int a, int b)");
        result.FormattedText.Should().Contain("return a + b");
    }

    /// <summary>
    /// Test: Format result contains original and formatted text.
    /// </summary>
    [Fact]
    public void Format_ResultContainsOriginalAndFormattedText()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        var original = "public class Test{public void Foo(){}}";
        
        File.WriteAllText(filePath, original);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.OriginalText.Should().Be(original);
        result.FormattedText.Should().NotBe(original);
        result.FormattedText.Should().Contain("public class Test");
        result.FormattedText.Should().Contain("public void Foo()");
    }

    /// <summary>
    /// Test: Format handles multi-line method signatures.
    /// </summary>
    [Fact]
    public void Format_HandlesMultiLineMethodSignatures()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Service
{
public void ProcessData(string name, int age, bool isActive, string city)
{
Console.WriteLine(name);
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Test: Format handles interface definitions.
    /// </summary>
    [Fact]
    public void Format_HandlesInterfaceDefinitions()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public interface IService
{
void Execute();
string GetName();
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().Contain("public interface IService");
    }

    /// <summary>
    /// Test: Format preserves code semantics - does not alter logic.
    /// </summary>
    [Fact]
    public void Format_PreservesCodeSemantics()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Math
{
public static int Factorial(int n)
{
if(n<=1)return 1;
return n*Factorial(n-1);
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        
        // Semantic meaning should be preserved
        result.FormattedText.Should().Contain("if");
        result.FormattedText.Should().Contain("return");
        result.FormattedText.Should().Contain("Factorial");
    }

    /// <summary>
    /// Test: Format handles empty file.
    /// </summary>
    [Fact]
    public void Format_HandlesEmptyFile()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        File.WriteAllText(filePath, string.Empty);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.OriginalText.Should().Be(string.Empty);
        result.FormattedText.Should().Be(string.Empty);
        result.HasChanges.Should().BeFalse();
    }

    /// <summary>
    /// Test: Format handles file with only whitespace.
    /// </summary>
    [Fact]
    public void Format_HandlesFileWithOnlyWhitespace()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        var whitespace = "   \n\n   \t\n";
        
        File.WriteAllText(filePath, whitespace);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.OriginalText.Should().Be(whitespace);
        result.FormattedText.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Test: Format handles file with comments.
    /// </summary>
    [Fact]
    public void Format_HandlesFileWithComments()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"// This is a comment
public class Test
{
// Method comment
public void Foo()
{
// Body comment
Console.WriteLine(""test"");
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.FormattedText.Should().Contain("// This is a comment");
        result.FormattedText.Should().Contain("// Method comment");
    }

    /// <summary>
    /// Test: Format handles properties with get/set accessors.
    /// </summary>
    [Fact]
    public void Format_HandlesPropertiesWithAccessors()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Person
{
public string Name{get;set;}
public int Age{get{return _age;}set{_age=value;}}
private int _age;
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().Contain("public string Name");
        result.FormattedText.Should().Contain("public int Age");
    }

    /// <summary>
    /// Test: Format respects .editorconfig with indent_style tab.
    /// </summary>
    [Fact]
    public void Format_RespectsEditorConfigIndentStyle_UseTabs()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        var editorConfigPath = System.IO.Path.Combine(tempDir.Path, ".editorconfig");

        var code = @"public class Test
{
public void Method()
{
Console.WriteLine(""Hello"");
}
}";

        File.WriteAllText(filePath, code);
        
        // Create .editorconfig with tab indentation
        var editorConfig = @"root = true

[*.cs]
indent_style = tab
";
        File.WriteAllText(editorConfigPath, editorConfig);

        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
        
        // Check that indentation uses tabs
        result.FormattedText.Should().Contain("\t");
    }

    /// <summary>
    /// Test: Format handles multiple diff ranges on separate sections.
    /// </summary>
    [Fact]
    public void Format_WithMultipleDiffRanges_FormatsMultipleSections()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Test
{
public void Method1()
{
Console.WriteLine(""One"");
}

public void Method2()
{
Console.WriteLine(""Two"");
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Format lines 3-5 (Method1) and lines 8-10 (Method2)
        var ranges = new[]
        {
            new DiffRange(StartLine: 3, LineCount: 3),
            new DiffRange(StartLine: 8, LineCount: 3)
        };

        // Act
        var result = formatter.Format(filePath, ranges);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Test: Format handles lambda expressions.
    /// </summary>
    [Fact]
    public void Format_HandlesLambdaExpressions()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Test
{
public void ProcessNumbers()
{
var numbers=new[]{1,2,3,4,5};
var evens=numbers.Where(x=>x%2==0);
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().Contain("=>");
    }

    /// <summary>
    /// Test: Format handles LINQ expressions.
    /// </summary>
    [Fact]
    public void Format_HandlesLinqExpressions()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Test
{
public void QueryData()
{
var query=from x in items
where x.Age>18
select new{x.Name,x.Age};
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().Contain("from");
        result.FormattedText.Should().Contain("where");
        result.FormattedText.Should().Contain("select");
    }

    /// <summary>
    /// Test: Format handles async/await syntax.
    /// </summary>
    [Fact]
    public void Format_HandlesAsyncAwaitSyntax()
    {
        // Arrange
        using var tempDir = new TempDir();
        var filePath = System.IO.Path.Combine(tempDir.Path, "test.cs");
        
        var code = @"public class Service
{
public async Task<string> FetchDataAsync()
{
var result=await GetDataAsync();
return result;
}
private async Task<string> GetDataAsync()
{
await Task.Delay(100);
return ""data"";
}
}";

        File.WriteAllText(filePath, code);
        var configLoader = EditorConfigLoader.Create(tempDir.Path);
        var formatter = new FileFormatter(configLoader);

        // Act
        var result = formatter.Format(filePath);

        // Assert
        result.HasChanges.Should().BeTrue();
        result.FormattedText.Should().Contain("async");
        result.FormattedText.Should().Contain("await");
    }

    /// <summary>
    /// Test: FormatResult record structure and properties.
    /// </summary>
    [Fact]
    public void FormatResult_IsRecordStruct_WithCorrectProperties()
    {
        // Arrange
        var filePath = "/path/to/file.cs";
        var original = "original content";
        var formatted = "formatted content";

        // Act
        var result = new FormatResult(filePath, original, formatted);

        // Assert
        result.FilePath.Should().Be(filePath);
        result.OriginalText.Should().Be(original);
        result.FormattedText.Should().Be(formatted);
    }

    /// <summary>
    /// Test: DiffRange record structure and EndLine property.
    /// </summary>
    [Fact]
    public void DiffRange_EndLineProperty_CalculatesCorrectly()
    {
        // Arrange & Act
        var range = new DiffRange(StartLine: 10, LineCount: 5);

        // Assert
        range.StartLine.Should().Be(10);
        range.LineCount.Should().Be(5);
        range.EndLine.Should().Be(14);
    }

    /// <summary>
    /// Test: DiffRange with single line (LineCount = 1).
    /// </summary>
    [Fact]
    public void DiffRange_WithSingleLine_EndLineEqualToStartLine()
    {
        // Arrange & Act
        var range = new DiffRange(StartLine: 5, LineCount: 1);

        // Assert
        range.EndLine.Should().Be(5);
    }
}
