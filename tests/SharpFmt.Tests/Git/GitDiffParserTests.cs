using FluentAssertions;
using SharpFmt.Git;
using Xunit;

namespace SharpFmt.Tests.Git;

public class GitDiffParserTests
{
    #region BuildGitArgs Tests

    [Fact]
    public void BuildGitArgs_WithDefaultCommit_ReturnsArgsWithHEAD()
    {
        // Arrange & Act
        var args = GitDiffParser.BuildGitArgs(commit: null, staged: false);

        // Assert
        args.Should().HaveCount(5);
        args[0].Should().Be("diff-index");
        args[1].Should().Be("-p");
        args[2].Should().Be("-U0");
        args[3].Should().Be("HEAD");
        args[4].Should().Be("--");
    }

    [Fact]
    public void BuildGitArgs_WithCustomCommit_ReturnsArgsWithCustomCommit()
    {
        // Arrange
        const string customCommit = "main";

        // Act
        var args = GitDiffParser.BuildGitArgs(commit: customCommit, staged: false);

        // Assert
        args.Should().HaveCount(5);
        args[0].Should().Be("diff-index");
        args[1].Should().Be("-p");
        args[2].Should().Be("-U0");
        args[3].Should().Be(customCommit);
        args[4].Should().Be("--");
    }

    [Fact]
    public void BuildGitArgs_WithStagedTrue_IncludesCachedFlag()
    {
        // Arrange & Act
        var args = GitDiffParser.BuildGitArgs(commit: null, staged: true);

        // Assert
        args.Should().HaveCount(6);
        args[0].Should().Be("diff-index");
        args[1].Should().Be("-p");
        args[2].Should().Be("-U0");
        args[3].Should().Be("--cached");
        args[4].Should().Be("HEAD");
        args[5].Should().Be("--");
    }

    [Fact]
    public void BuildGitArgs_WithCustomCommitAndStaged_ReturnsCompleteArgs()
    {
        // Arrange
        const string commit = "develop";

        // Act
        var args = GitDiffParser.BuildGitArgs(commit: commit, staged: true);

        // Assert
        args.Should().HaveCount(6);
        args[0].Should().Be("diff-index");
        args[1].Should().Be("-p");
        args[2].Should().Be("-U0");
        args[3].Should().Be("--cached");
        args[4].Should().Be(commit);
        args[5].Should().Be("--");
    }

    #endregion

    #region ParseUnifiedDiff - Basic Scenarios

    [Fact]
    public void ParseUnifiedDiff_SingleFileSingleHunk_ReturnsSingleFileDiff()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/test.cs b/test.cs
            +++ b/test.cs
            @@ -10,5 +20,3 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("test.cs");
        result[0].Ranges.Should().HaveCount(1);
        result[0].Ranges[0].StartLine.Should().Be(20);
        result[0].Ranges[0].LineCount.Should().Be(3);
    }

    [Fact]
    public void ParseUnifiedDiff_SingleFileMultipleHunks_ReturnsMultipleRanges()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/file.cs b/file.cs
            +++ b/file.cs
            @@ -5,2 +10,3 @@
            @@ -20,4 +30,5 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("file.cs");
        result[0].Ranges.Should().HaveCount(2);
        
        result[0].Ranges[0].StartLine.Should().Be(10);
        result[0].Ranges[0].LineCount.Should().Be(3);
        
        result[0].Ranges[1].StartLine.Should().Be(30);
        result[0].Ranges[1].LineCount.Should().Be(5);
    }

    [Fact]
    public void ParseUnifiedDiff_MultipleFiles_ReturnsMultipleFileDiffs()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/file1.cs b/file1.cs
            +++ b/file1.cs
            @@ -1,2 +5,3 @@
            diff --git a/file2.cs b/file2.cs
            +++ b/file2.cs
            @@ -10,1 +20,2 @@
            diff --git a/file3.cs b/file3.cs
            +++ b/file3.cs
            @@ -100,5 +150,6 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(3);
        
        result[0].FilePath.Should().Be("file1.cs");
        result[0].Ranges.Should().HaveCount(1);
        result[0].Ranges[0].StartLine.Should().Be(5);
        
        result[1].FilePath.Should().Be("file2.cs");
        result[1].Ranges.Should().HaveCount(1);
        result[1].Ranges[0].StartLine.Should().Be(20);
        
        result[2].FilePath.Should().Be("file3.cs");
        result[2].Ranges.Should().HaveCount(1);
        result[2].Ranges[0].StartLine.Should().Be(150);
    }

    #endregion

    #region ParseUnifiedDiff - Edge Cases

    [Fact]
    public void ParseUnifiedDiff_HunkWithoutLineCount_DefaultsToOneLineChange()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/test.cs b/test.cs
            +++ b/test.cs
            @@ -15 +25 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].Ranges.Should().HaveCount(1);
        result[0].Ranges[0].StartLine.Should().Be(25);
        result[0].Ranges[0].LineCount.Should().Be(1);
    }

    [Fact]
    public void ParseUnifiedDiff_HunkWithZeroLineCount_IsSkipped()
    {
        // Arrange - lineCount=0 means no new lines added (e.g., lines were only removed)
        // When a file has no valid hunks, it's excluded from results
        const string diffOutput = """
            diff --git a/test.cs b/test.cs
            +++ b/test.cs
            @@ -10,5 +10,0 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseUnifiedDiff_HunkWithStartLineZero_IsSkipped()
    {
        // Arrange - startLine=0 means file was empty or being created from scratch
        // When a file has no valid hunks, it's excluded from results
        const string diffOutput = """
            diff --git a/test.cs b/test.cs
            +++ b/test.cs
            @@ -1 +0,0 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseUnifiedDiff_FileWithMultipleHunksIncludingSkipped_ReturnsOnlyValidRanges()
    {
        // Arrange - hunks with lineCount=0 or startLine=0 should be skipped
        const string diffOutput = """
            diff --git a/test.cs b/test.cs
            +++ b/test.cs
            @@ -5,2 +10,3 @@
            @@ -20,5 +20,0 @@
            @@ -15,1 +30,2 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].Ranges.Should().HaveCount(2);
        result[0].Ranges[0].StartLine.Should().Be(10);
        result[0].Ranges[1].StartLine.Should().Be(30);
    }

    [Fact]
    public void ParseUnifiedDiff_EmptyDiff_ReturnsEmptyList()
    {
        // Arrange
        const string diffOutput = "";

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseUnifiedDiff_OnlyFileHeaders_ReturnsEmptyList()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/file1.cs b/file1.cs
            +++ b/file1.cs
            diff --git a/file2.cs b/file2.cs
            +++ b/file2.cs
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseUnifiedDiff_HunkBeforeAnyFile_IsIgnored()
    {
        // Arrange
        const string diffOutput = """
            @@ -5,2 +10,3 @@
            diff --git a/test.cs b/test.cs
            +++ b/test.cs
            @@ -15,1 +20,2 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].Ranges.Should().HaveCount(1);
        result[0].Ranges[0].StartLine.Should().Be(20);
    }

    #endregion

    #region ParseUnifiedDiff - File Path Handling

    [Fact]
    public void ParseUnifiedDiff_FilePathWithSpaces_PreservesPath()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/folder/my file.cs b/folder/my file.cs
            +++ b/folder/my file.cs
            @@ -5,2 +10,3 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("folder/my file.cs");
    }

    [Fact]
    public void ParseUnifiedDiff_FilePathWithSpecialCharacters_ExtractsCorrectPath()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/folder/test-file_v2.cs b/folder/test-file_v2.cs
            +++ b/folder/test-file_v2.cs
            @@ -1,2 +1,3 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("folder/test-file_v2.cs");
    }

    [Fact]
    public void ParseUnifiedDiff_FilePathWithCarriageReturn_IsTrimmed()
    {
        // Arrange
        var diffOutput = "diff --git a/test.cs b/test.cs\n+++ b/test.cs\r\n@@ -5,2 +10,3 @@";

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("test.cs");
        result[0].FilePath.Should().NotContain("\r");
    }

    #endregion

    #region ParseUnifiedDiff - Complex Scenarios

    [Fact]
    public void ParseUnifiedDiff_FileWithManyHunks_ReturnsAllRanges()
    {
        // Arrange
        const string diffOutput = """
            diff --git a/large-file.cs b/large-file.cs
            +++ b/large-file.cs
            @@ -10,5 +10,5 @@
            @@ -50,3 +50,4 @@
            @@ -100,2 +100,3 @@
            @@ -200,10 +200,15 @@
            @@ -500,1 +500,2 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(1);
        result[0].Ranges.Should().HaveCount(5);
        result[0].Ranges[0].StartLine.Should().Be(10);
        result[0].Ranges[1].StartLine.Should().Be(50);
        result[0].Ranges[2].StartLine.Should().Be(100);
        result[0].Ranges[3].StartLine.Should().Be(200);
        result[0].Ranges[4].StartLine.Should().Be(500);
    }

    [Fact]
    public void ParseUnifiedDiff_MultipleFilesWithMixedValidAndInvalidHunks_ReturnsOnlyValid()
    {
        // Arrange - hunks with lineCount=0 or startLine=0 on the new side should be skipped
        const string diffOutput = """
            diff --git a/valid.cs b/valid.cs
            +++ b/valid.cs
            @@ -5,2 +10,3 @@
            @@ -20,5 +20,0 @@
            diff --git a/another.cs b/another.cs
            +++ b/another.cs
            @@ -100,5 +200,5 @@
            @@ -150,1 +250,0 @@
            diff --git a/empty-hunks.cs b/empty-hunks.cs
            +++ b/empty-hunks.cs
            @@ -0,0 +1,0 @@
            """;

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(2);
        
        result[0].FilePath.Should().Be("valid.cs");
        result[0].Ranges.Should().HaveCount(1);
        result[0].Ranges[0].StartLine.Should().Be(10);
        
        result[1].FilePath.Should().Be("another.cs");
        result[1].Ranges.Should().HaveCount(1);
        result[1].Ranges[0].StartLine.Should().Be(200);
    }

    [Fact]
    public void ParseUnifiedDiff_WithVaryingLineEndings_ParsesCorrectly()
    {
        // Arrange
        var diffOutput = "diff --git a/file1.cs b/file1.cs\r\n+++ b/file1.cs\r\n@@ -5,2 +10,3 @@\r\n" +
                         "diff --git a/file2.cs b/file2.cs\n+++ b/file2.cs\n@@ -15,1 +20,2 @@";

        // Act
        var result = GitDiffParser.ParseUnifiedDiff(diffOutput);

        // Assert
        result.Should().HaveCount(2);
        result[0].FilePath.Should().Be("file1.cs");
        result[1].FilePath.Should().Be("file2.cs");
    }

    #endregion

    #region DiffRange Tests

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(10, 5, 14)]
    [InlineData(100, 1, 100)]
    [InlineData(50, 100, 149)]
    public void DiffRange_EndLine_CalculatesCorrectValue(int startLine, int lineCount, int expectedEndLine)
    {
        // Arrange
        var range = new DiffRange(startLine, lineCount);

        // Act
        var endLine = range.EndLine;

        // Assert
        endLine.Should().Be(expectedEndLine);
    }

    [Fact]
    public void DiffRange_WithLineCount1_EndLineEqualStartLine()
    {
        // Arrange
        var range = new DiffRange(42, 1);

        // Act & Assert
        range.EndLine.Should().Be(42);
    }

    [Fact]
    public void DiffRange_WithLargeLineCount_CalculatesEndLineCorrectly()
    {
        // Arrange
        var range = new DiffRange(1000, 5000);

        // Act & Assert
        range.EndLine.Should().Be(5999);
    }

    [Fact]
    public void DiffRange_IsValueType_EqualsComparesValues()
    {
        // Arrange
        var range1 = new DiffRange(10, 5);
        var range2 = new DiffRange(10, 5);

        // Act & Assert
        range1.Should().Be(range2);
    }

    [Fact]
    public void DiffRange_WithDifferentValues_IsNotEqual()
    {
        // Arrange
        var range1 = new DiffRange(10, 5);
        var range2 = new DiffRange(10, 6);

        // Act & Assert
        range1.Should().NotBe(range2);
    }

    #endregion

    #region FileDiff Tests

    [Fact]
    public void FileDiff_WithMultipleRanges_StoresAllRanges()
    {
        // Arrange
        var ranges = new List<DiffRange>
        {
            new(10, 5),
            new(20, 3),
            new(50, 1)
        };

        // Act
        var fileDiff = new FileDiff("test.cs", ranges);

        // Assert
        fileDiff.FilePath.Should().Be("test.cs");
        fileDiff.Ranges.Should().HaveCount(3);
        fileDiff.Ranges[0].Should().Be(new DiffRange(10, 5));
        fileDiff.Ranges[1].Should().Be(new DiffRange(20, 3));
        fileDiff.Ranges[2].Should().Be(new DiffRange(50, 1));
    }

    [Fact]
    public void FileDiff_WithEmptyRanges_StoresEmptyList()
    {
        // Arrange
        var ranges = new List<DiffRange>();

        // Act
        var fileDiff = new FileDiff("empty.cs", ranges);

        // Assert
        fileDiff.FilePath.Should().Be("empty.cs");
        fileDiff.Ranges.Should().BeEmpty();
    }

    #endregion
}
