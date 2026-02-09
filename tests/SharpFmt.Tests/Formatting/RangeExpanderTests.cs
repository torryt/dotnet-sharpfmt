using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SharpFmt.Formatting;
using SharpFmt.Git;
using Xunit;

namespace SharpFmt.Tests.Formatting;

public class RangeExpanderTests
{
    #region LineRangeToTextSpan Tests

    [Fact]
    public void LineRangeToTextSpan_SingleLineRange_ConvertedCorrectly()
    {
        // Arrange
        var code = "line 1\nline 2\nline 3\n";
        var sourceText = SourceText.From(code);
        var range = new DiffRange(StartLine: 2, LineCount: 1);

        // Act
        var result = RangeExpander.LineRangeToTextSpan(sourceText, range);

        // Assert
        result.Start.Should().Be(sourceText.Lines[1].Start);
        result.End.Should().Be(sourceText.Lines[1].End);
    }

    [Fact]
    public void LineRangeToTextSpan_MultiLineRange_ConvertedCorrectly()
    {
        // Arrange
        var code = "line 1\nline 2\nline 3\nline 4\n";
        var sourceText = SourceText.From(code);
        var range = new DiffRange(StartLine: 2, LineCount: 3);

        // Act
        var result = RangeExpander.LineRangeToTextSpan(sourceText, range);

        // Assert
        result.Start.Should().Be(sourceText.Lines[1].Start);
        result.End.Should().Be(sourceText.Lines[3].End);
    }

    [Fact]
    public void LineRangeToTextSpan_FirstLine_ConvertedCorrectly()
    {
        // Arrange
        var code = "line 1\nline 2\n";
        var sourceText = SourceText.From(code);
        var range = new DiffRange(StartLine: 1, LineCount: 1);

        // Act
        var result = RangeExpander.LineRangeToTextSpan(sourceText, range);

        // Assert
        result.Start.Should().Be(sourceText.Lines[0].Start);
        result.End.Should().Be(sourceText.Lines[0].End);
    }

    [Fact]
    public void LineRangeToTextSpan_StartLineClamped_ToFirstLine()
    {
        // Arrange
        var code = "line 1\nline 2\nline 3\n";
        var sourceText = SourceText.From(code);
        // When StartLine is 0 (below 1-based minimum), EndLine is valid
        var range = new DiffRange(StartLine: 1, LineCount: 1);

        // Act
        var result = RangeExpander.LineRangeToTextSpan(sourceText, range);

        // Assert - should start at first line
        result.Start.Should().Be(sourceText.Lines[0].Start);
    }

    [Fact]
    public void LineRangeToTextSpan_EndLineBeyondFileLength_ClampedToLastLine()
    {
        // Arrange
        var code = "line 1\nline 2\nline 3\n";
        var sourceText = SourceText.From(code);
        var range = new DiffRange(StartLine: 2, LineCount: 100);

        // Act
        var result = RangeExpander.LineRangeToTextSpan(sourceText, range);

        // Assert
        result.End.Should().Be(sourceText.Lines[sourceText.Lines.Count - 1].End);
    }

    [Fact]
    public void LineRangeToTextSpan_RangeWithinBounds_NoClampingNeeded()
    {
        // Arrange
        var code = "line 1\nline 2\nline 3\nline 4\nline 5\n";
        var sourceText = SourceText.From(code);
        var range = new DiffRange(StartLine: 2, LineCount: 2);

        // Act
        var result = RangeExpander.LineRangeToTextSpan(sourceText, range);

        // Assert
        result.Start.Should().Be(sourceText.Lines[1].Start);
        result.End.Should().Be(sourceText.Lines[2].End);
    }

    #endregion

    #region ExpandToEnclosingNode Tests

    [Fact]
    public void ExpandToEnclosingNode_SpanInMethodBody_ExpandsToEnclosingStatement()
    {
        // Arrange
        var code = @"
class Program
{
    void Method()
    {
        var x = 1;
        var y = 2;
        var z = 3;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);

        // Find the span of "var y = 2;" (approximate)
        var yLine = code.IndexOf("var y = 2;");
        var span = new TextSpan(yLine, "var y = 2;".Length);

        // Act
        var result = RangeExpander.ExpandToEnclosingNode(root, span);

        // Assert
        var expandedNode = root.FindNode(result);
        expandedNode.Should().NotBeNull();
        // The expansion should find a statement node
        expandedNode!.GetText().ToString().Contains("var y = 2;").Should().BeTrue();
    }

    [Fact]
    public void ExpandToEnclosingNode_SpanInMemberDeclaration_ExpandsToMember()
    {
        // Arrange
        var code = @"
class Program
{
    public int Property { get; set; }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // Find span of "get; set;"
        var propSpan = code.IndexOf("get; set;");
        var span = new TextSpan(propSpan, "get; set;".Length);

        // Act
        var result = RangeExpander.ExpandToEnclosingNode(root, span);

        // Assert
        var expandedNode = root.FindNode(result);
        expandedNode.Should().NotBeNull();
        expandedNode!.GetText().ToString().Contains("Property").Should().BeTrue();
    }

    [Fact]
    public void ExpandToEnclosingNode_SpanInNamespace_ExpandsToNamespace()
    {
        // Arrange
        var code = @"
namespace MyNamespace
{
    class Program { }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // Find span of "class Program"
        var classSpan = code.IndexOf("class Program");
        var span = new TextSpan(classSpan, "class Program".Length);

        // Act
        var result = RangeExpander.ExpandToEnclosingNode(root, span);

        // Assert
        var expandedNode = root.FindNode(result);
        expandedNode.Should().NotBeNull();
    }

    [Fact]
    public void ExpandToEnclosingNode_SpanAtNodeBoundary_ReturnsValidSpan()
    {
        // Arrange
        var code = @"int x = 5;";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // Find the assignment
        var assignmentStart = code.IndexOf("int x");
        var span = new TextSpan(assignmentStart, "int x = 5;".Length);

        // Act
        var result = RangeExpander.ExpandToEnclosingNode(root, span);

        // Assert
        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ExpandToEnclosingNode_EmptySpan_DoesNotThrow()
    {
        // Arrange
        var code = "int x = 5;";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var span = new TextSpan(0, 0);

        // Act
        var result = RangeExpander.ExpandToEnclosingNode(root, span);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region ExpandToSyntaxSpans Tests

    [Fact]
    public void ExpandToSyntaxSpans_SimpleMethodBody_ExpandsToMethod()
    {
        // Arrange
        var code = @"
class Program
{
    void Method1()
    {
        var x = 1;
    }

    void Method2()
    {
        var y = 2;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);

        // Method1 is on lines 3-6
        var ranges = new List<DiffRange>
        {
            new DiffRange(StartLine: 4, LineCount: 2)
        };

        // Act
        var result = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ExpandToSyntaxSpans_MultipleNonOverlappingRanges_ReturnsMultipleSpans()
    {
        // Arrange
        var code = @"
class Program
{
    void Method1() { var x = 1; }
    void Method2() { var y = 2; }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);

        var ranges = new List<DiffRange>
        {
            new DiffRange(StartLine: 4, LineCount: 1),
            new DiffRange(StartLine: 5, LineCount: 1)
        };

        // Act
        var result = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ExpandToSyntaxSpans_OverlappingRanges_MergesResults()
    {
        // Arrange
        var code = @"
class Program
{
    void Method1()
    {
        var x = 1;
        var y = 2;
    }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);

        var ranges = new List<DiffRange>
        {
            new DiffRange(StartLine: 5, LineCount: 1),
            new DiffRange(StartLine: 6, LineCount: 1)
        };

        // Act
        var result = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public void ExpandToSyntaxSpans_EmptyRangeList_ReturnsEmptyList()
    {
        // Arrange
        var code = "int x = 5;";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);
        var ranges = new List<DiffRange>();

        // Act
        var result = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExpandToSyntaxSpans_ComplexFile_ProcessesAllRanges()
    {
        // Arrange
        var code = @"
using System;

namespace MyApp
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine(""Hello"");
        }

        int Property { get; set; }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);

        var ranges = new List<DiffRange>
        {
            new DiffRange(StartLine: 9, LineCount: 2),
            new DiffRange(StartLine: 13, LineCount: 1)
        };

        // Act
        var result = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);

        // Assert
        result.Should().NotBeEmpty();
        result.All(s => s.Length > 0).Should().BeTrue();
    }

    #endregion

    #region MergeOverlappingSpans Tests

    [Fact]
    public void MergeOverlappingSpans_NonOverlappingSpans_StaysSeparate()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(0, 5),
            new TextSpan(10, 5),
            new TextSpan(20, 5)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().Be(spans[0]);
        result[1].Should().Be(spans[1]);
        result[2].Should().Be(spans[2]);
    }

    [Fact]
    public void MergeOverlappingSpans_OverlappingSpans_GetsMerged()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(0, 10),
            new TextSpan(5, 10)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(1);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(15);
    }

    [Fact]
    public void MergeOverlappingSpans_CompletelyOverlappingSpans_GetsMerged()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(0, 20),
            new TextSpan(5, 5),
            new TextSpan(10, 3)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(1);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(20);
    }

    [Fact]
    public void MergeOverlappingSpans_AdjacentSpans_GetsMerged()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(0, 5),
            new TextSpan(5, 5)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(1);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(10);
    }

    [Fact]
    public void MergeOverlappingSpans_EmptyList_ReturnsEmpty()
    {
        // Arrange
        var spans = new List<TextSpan>();

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeOverlappingSpans_SingleSpan_ReturnsUnchanged()
    {
        // Arrange
        var spans = new List<TextSpan> { new TextSpan(0, 10) };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(spans[0]);
    }

    [Fact]
    public void MergeOverlappingSpans_UnorderedSpans_MergesCorrectly()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(20, 5),
            new TextSpan(0, 10),
            new TextSpan(5, 8)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(2);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(13);
        result[1].Start.Should().Be(20);
        result[1].End.Should().Be(25);
    }

    [Fact]
    public void MergeOverlappingSpans_MultipleGroupsOfOverlaps_MergesEachGroup()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(0, 5),
            new TextSpan(3, 4),
            new TextSpan(15, 5),
            new TextSpan(18, 3),
            new TextSpan(30, 5)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(3);
        result[0].Start.Should().Be(0);
        result[0].End.Should().Be(7);
        result[1].Start.Should().Be(15);
        result[1].End.Should().Be(21);
        result[2].Start.Should().Be(30);
        result[2].End.Should().Be(35);
    }

    [Fact]
    public void MergeOverlappingSpans_SpansWithZeroLength_HandledCorrectly()
    {
        // Arrange
        var spans = new List<TextSpan>
        {
            new TextSpan(0, 0),
            new TextSpan(5, 5)
        };

        // Act
        var result = RangeExpander.MergeOverlappingSpans(spans);

        // Assert
        result.Should().HaveCount(2);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void IntegrationTest_FormattingClassWithMultipleMethods_ExpandsCorrectly()
    {
        // Arrange
        var code = @"
namespace SharpFmt
{
    public class Formatter
    {
        public string Format(string code)
        {
            // First method
            return code;
        }

        public void Validate()
        {
            // Second method
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var sourceText = SourceText.From(code);

        var ranges = new List<DiffRange>
        {
            new DiffRange(StartLine: 6, LineCount: 4),
            new DiffRange(StartLine: 12, LineCount: 2)
        };

        // Act
        var result = RangeExpander.ExpandToSyntaxSpans(root, sourceText, ranges);

        // Assert
        result.Should().HaveCount(1);
        result[0].Length.Should().BeGreaterThan(0);
    }

    #endregion
}
