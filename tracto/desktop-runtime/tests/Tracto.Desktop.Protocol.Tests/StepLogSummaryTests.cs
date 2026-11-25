#nullable enable

using System.Text.Json;
using FluentAssertions;
using Tracto.Desktop.Protocol;
using Xunit;

namespace Tracto.Desktop.Protocol.Tests;

/// <summary>
/// The one line a step that called a service leaves in the run's log. It is what lets a person see
/// that a batch was started and answered "nothing to do", without asking the service.
/// </summary>
public sealed class StepLogSummaryTests
{
    // What http.post returns for the sheet service's reply, as the handler builds it.
    private static Dictionary<string, object?> BatchReply(string state, string total, string? message = null)
    {
        var o = new Dictionary<string, object?>
        {
            ["statusCode"] = 202,
            ["body"]       = $"{{\"state\":\"{state}\"}}",
            ["json"]       = JsonDocument.Parse("{\"state\":\"x\"}").RootElement.Clone(),
            ["batchId"]    = "b-20261125-093544-36b35b",
            ["state"]      = state,
            ["total"]      = total,
            ["childVersion"] = "1.0.0",
        };
        if (message is not null) o["message"] = message;
        return o;
    }

    [Fact]
    public void Describes_TheStatusAndTheFieldsOfAnHttpReply()
    {
        var text = StepLogSummary.Describe("http.post", BatchReply("Completed", "0", "nothing to do: every row is already done"));

        text.Should().Be(
            "statusCode=202; batchId=b-20261125-093544-36b35b; state=Completed; total=0; childVersion=1.0.0; " +
            "message=nothing to do: every row is already done");
    }

    [Fact]
    public void PutsTheStatusCodeFirstWhateverTheOrder()
    {
        var o = new Dictionary<string, object?> { ["state"] = "Running", ["statusCode"] = 200 };

        StepLogSummary.Describe("http.get", o).Should().Be("statusCode=200; state=Running");
    }

    [Fact]
    public void LeavesOutTheRawBodyAndTheParsedJson()
    {
        var text = StepLogSummary.Describe("http.post", BatchReply("Running", "3"))!;

        text.Should().NotContain("body=").And.NotContain("json=");
        text.Should().NotContain("{");
    }

    [Theory]
    [InlineData("clipboard.get")]
    [InlineData("file.read")]
    [InlineData("type")]
    [InlineData("key")]
    [InlineData("notepad.type")]
    [InlineData(null)]
    public void NeverDescribesOtherSteps_SoClipboardTextAndFileContentsStayOutOfTheLog(string? action)
    {
        var outputs = new Dictionary<string, object?>
        {
            ["text"] = "the clipboard held this",
            ["firstLine"] = "a secret first line",
            ["statusCode"] = 200,
        };

        StepLogSummary.Describe(action, outputs).Should().BeNull();
        StepLogSummary.Describes(action).Should().BeFalse();
    }

    [Theory]
    [InlineData("http.get")]
    [InlineData("HTTP.POST")]
    public void ActionNamesAreMatchedWithoutRegardToCase(string action)
    {
        StepLogSummary.Describes(action).Should().BeTrue();
    }

    [Fact]
    public void ReturnsNothingWhenThereIsNothingToSay()
    {
        StepLogSummary.Describe("http.post", null).Should().BeNull();
        StepLogSummary.Describe("http.post", new Dictionary<string, object?>()).Should().BeNull();
        StepLogSummary.Describe("http.post", new Dictionary<string, object?> { ["body"] = "{}", ["json"] = null }).Should().BeNull();
    }

    [Fact]
    public void SkipsNestedValues_OnlySimpleOnesAreListed()
    {
        var o = new Dictionary<string, object?>
        {
            ["statusCode"] = 200,
            ["items"] = JsonDocument.Parse("[1,2,3]").RootElement.Clone(),    // an array
            ["owner"] = JsonDocument.Parse("{\"a\":1}").RootElement.Clone(),  // an object
            ["name"] = JsonDocument.Parse("\"widget\"").RootElement.Clone(),  // a string that arrived as JSON
            ["nothing"] = null,
        };

        StepLogSummary.Describe("http.get", o).Should().Be("statusCode=200; name=widget");
    }

    [Fact]
    public void KeepsEachValueOnOneShortLine()
    {
        var long1 = "line one\r\n   line two" + new string('x', 400);
        var o = new Dictionary<string, object?> { ["statusCode"] = 200, ["message"] = long1 };

        var text = StepLogSummary.Describe("http.post", o)!;

        text.Should().NotContain("\n").And.NotContain("\r");
        text.Should().Contain("message=line one line two");
        text.Length.Should().BeLessThan(200, "a long value is cut, with an ellipsis");
        text.Should().EndWith("…");
    }

    [Fact]
    public void ListsAtMostTwelveFields_AndSaysHowManyWereLeftOut()
    {
        var o = new Dictionary<string, object?> { ["statusCode"] = 200 };
        for (var i = 1; i <= 20; i++) o[$"f{i:00}"] = i.ToString();

        var text = StepLogSummary.Describe("http.get", o)!;

        text.Split("; ").Length.Should().Be(13, "twelve fields and the note about the rest");
        text.Should().EndWith("(+9 more)");   // 21 values, 12 shown
    }

    [Fact]
    public void FormatsNumbersAndBooleansWithoutCultureSurprises()
    {
        var o = new Dictionary<string, object?> { ["statusCode"] = 200, ["ratio"] = 1234.5, ["ok"] = true };

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            StepLogSummary.Describe("http.get", o).Should().Be("statusCode=200; ratio=1234.5; ok=true");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
