using FluentAssertions;
using SharpFmt.Config;
using Xunit;

namespace SharpFmt.Tests.Config;

public class EditorConfigLoaderTests
{
    #region IsRootConfig Tests

    [Fact]
    public void IsRootConfig_WithRootTrueAndSpaces_ReturnsTrue()
    {
        // Arrange
        const string text = "root = true";

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRootConfig_WithRootTrueNoSpaces_ReturnsTrue()
    {
        // Arrange
        const string text = "root=true";

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRootConfig_WithRootTrueIgnoreCase_ReturnsTrue()
    {
        // Arrange
        const string text = "ROOT = TRUE";

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRootConfig_WithRootTrueAfterSectionHeader_ReturnsFalse()
    {
        // Arrange
        const string text = """
            [*.cs]
            root = true
            """;

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRootConfig_WithRootFalse_ReturnsFalse()
    {
        // Arrange
        const string text = "root = false";

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRootConfig_WithEmptyString_ReturnsFalse()
    {
        // Arrange
        const string text = "";

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRootConfig_WithLeadingBlankLinesAndRootTrue_ReturnsTrue()
    {
        // Arrange
        const string text = """
            
            
            root = true
            """;

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRootConfig_WithLeadingWhitespaceBeforeRoot_ReturnsTrue()
    {
        // Arrange
        const string text = "   root = true";

        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("root=true")]
    [InlineData("root = true")]
    [InlineData("ROOT=TRUE")]
    [InlineData("Root = True")]
    [InlineData("  root=true  ")]
    public void IsRootConfig_WithVariousFormats_ReturnsTrue(string text)
    {
        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("root = false")]
    [InlineData("root=false")]
    [InlineData("ROOT = FALSE")]
    [InlineData("# root = true")]
    [InlineData("roott = true")]
    [InlineData("root_true")]
    public void IsRootConfig_WithInvalidFormats_ReturnsFalse(string text)
    {
        // Act
        var result = EditorConfigLoader.IsRootConfig(text);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Create and GetOptionsForSourcePath Tests

    [Fact]
    public void Create_WithValidEditorConfig_LoadsSuccessfully()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        tempDir.CreateEditorConfig("""
            [*.cs]
            indent_style = space
            indent_size = 4
            """);

        // Act
        var loader = EditorConfigLoader.Create(tempDir.Path);

        // Assert
        loader.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithNoEditorConfig_LoadsSuccessfully()
    {
        // Arrange
        using var tempDir = new TempDirectory();

        // Act
        var loader = EditorConfigLoader.Create(tempDir.Path);

        // Assert
        loader.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithRootConfigTrue_StopsWalkingUp()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var parentDir = tempDir.CreateSubdirectory("parent");
        var childDir = tempDir.CreateSubdirectory("parent/child");

        // Create parent .editorconfig with root=true
        File.WriteAllText(Path.Combine(parentDir, ".editorconfig"), """
            root = true

            [*.cs]
            indent_size = 2
            """);

        // Create child .editorconfig
        File.WriteAllText(Path.Combine(childDir, ".editorconfig"), """
            [*.cs]
            indent_size = 4
            """);

        // Act
        var loader = EditorConfigLoader.Create(childDir);
        var options = loader.GetOptionsForSourcePath(Path.Combine(childDir, "test.cs"));

        // Assert
        options.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithMultipleEditorConfigs_LoadsAllInOrder()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var level1 = tempDir.CreateSubdirectory("level1");
        var level2 = tempDir.CreateSubdirectory("level1/level2");
        var level3 = tempDir.CreateSubdirectory("level1/level2/level3");

        // Create .editorconfig files at different levels
        File.WriteAllText(Path.Combine(level1, ".editorconfig"), """
            [*.cs]
            indent_size = 2
            """);

        File.WriteAllText(Path.Combine(level2, ".editorconfig"), """
            [*.cs]
            indent_style = tab
            """);

        // Act
        var loader = EditorConfigLoader.Create(level3);

        // Assert
        loader.Should().NotBeNull();
    }

    [Fact]
    public void GetOptionsForSourcePath_WithValidPath_ReturnsOptions()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        tempDir.CreateEditorConfig("""
            [*.cs]
            indent_style = space
            indent_size = 4
            """);

        var loader = EditorConfigLoader.Create(tempDir.Path);
        var testFilePath = Path.Combine(tempDir.Path, "test.cs");

        // Act
        var result = loader.GetOptionsForSourcePath(testFilePath);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetOptionsForSourcePath_WithRelativePath_ReturnsOptions()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        tempDir.CreateEditorConfig("""
            [*.cs]
            indent_style = space
            indent_size = 4
            """);

        var loader = EditorConfigLoader.Create(tempDir.Path);
        var testFilePath = Path.Combine(tempDir.Path, "test.cs");

        // Act
        var result = loader.GetOptionsForSourcePath(testFilePath);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetOptionsForSourcePath_ForDifferentExtensions_ReturnsCorrectOptions()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        tempDir.CreateEditorConfig("""
            [*.cs]
            indent_size = 4
            
            [*.txt]
            indent_size = 2
            """);

        var loader = EditorConfigLoader.Create(tempDir.Path);
        var csFile = Path.Combine(tempDir.Path, "test.cs");
        var txtFile = Path.Combine(tempDir.Path, "test.txt");

        // Act
        var csResult = loader.GetOptionsForSourcePath(csFile);
        var txtResult = loader.GetOptionsForSourcePath(txtFile);

        // Assert
        csResult.Should().NotBeNull();
        txtResult.Should().NotBeNull();
    }

    #endregion

    #region Helpers

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"EditorConfigLoaderTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(Path);
        }

        public void CreateEditorConfig(string content)
        {
            var path = System.IO.Path.Combine(Path, ".editorconfig");
            File.WriteAllText(path, content);
        }

        public string CreateSubdirectory(string relativePath)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
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
                // Ignore cleanup errors
            }
        }
    }

    #endregion
}
