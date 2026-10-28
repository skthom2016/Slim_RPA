namespace Tracto.Designer.Unit.Tests.Mcp;

using FluentAssertions;
using Moq;
using System.Text.Json;
using Tracto.Designer.Core.Mcp;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

public class McpToolHandlerTests
{
    private static McpToolHandler MakeHandler(Script? script = null)
    {
        var mockSession = new Mock<IRecordingSessionService>();
        mockSession.Setup(s => s.Transcript).Returns([]);
        var handler = new McpToolHandler(mockSession.Object, new ScriptDraftGenerator(), new ScriptValidator());
        handler.SetCurrentScript(script);
        return handler;
    }

    private static Script ValidScript()
    {
        var s = new Script { Id = "x", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click" });
        return s;
    }

    // ------------------------------------------------------------------
    // get_capabilities — DS-08-AC03 / Appendix B
    // ------------------------------------------------------------------

    [Fact]
    public void GetCapabilities_Returns_ActionTypes_Catalogue()
    {
        var r = MakeHandler().HandleTool("get_capabilities", null);
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("actionTypes");
        r.Content.Should().Contain("predicateTypes");
        r.Content.Should().Contain("policyConstraints");
        r.Content.Should().Contain("maxIterations");
    }

    [Fact]
    public void GetCapabilities_Desktop_RuntimeType_Returns_Desktop_Actions()
    {
        var r = MakeHandler().HandleTool("get_capabilities", "{\"runtimeType\":\"desktop\"}");
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("click");
        r.Content.Should().Contain("windowOpen");
    }

    [Fact]
    public void GetCapabilities_Browser_RuntimeType_Returns_Browser_Actions()
    {
        var r = MakeHandler().HandleTool("get_capabilities", "{\"runtimeType\":\"browser\"}");
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("urlMatches");
        r.Content.Should().Contain("fillForm");
    }

    [Fact]
    public void GetCapabilities_Terminal_RuntimeType_Returns_Terminal_Actions()
    {
        var r = MakeHandler().HandleTool("get_capabilities", "{\"runtimeType\":\"terminal\"}");
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("command");
        r.Content.Should().Contain("assertOutput");
    }

    // ------------------------------------------------------------------
    // get_transcript
    // ------------------------------------------------------------------

    [Fact]
    public void GetTranscript_Returns_Empty_Array_When_No_Session()
    {
        var r = MakeHandler().HandleTool("get_transcript", null);
        r.Success.Should().BeTrue();
        r.Content.Should().Be("[]");
    }

    // ------------------------------------------------------------------
    // get_draft_script
    // ------------------------------------------------------------------

    [Fact]
    public void GetDraftScript_No_Script_Returns_Failure()
    {
        var r = MakeHandler(null).HandleTool("get_draft_script", null);
        r.Success.Should().BeFalse();
    }

    [Fact]
    public void GetDraftScript_With_Script_Returns_Json()
    {
        var s = new Script { Id = "x", Name = "Test", Version = "1.0.0", TargetRuntime = "desktop" };
        var r = MakeHandler(s).HandleTool("get_draft_script", null);
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("\"name\":\"Test\"");
    }

    // ------------------------------------------------------------------
    // validate_script — FRD response shape: {valid, violations[]}
    // ------------------------------------------------------------------

    [Fact]
    public void ValidateScript_No_Script_Returns_Failure()
    {
        MakeHandler(null).HandleTool("validate_script", null).Success.Should().BeFalse();
    }

    [Fact]
    public void ValidateScript_Valid_Script_Returns_Valid_True()
    {
        var r = MakeHandler(ValidScript()).HandleTool("validate_script", null);
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("\"valid\":true");
        r.Content.Should().Contain("violations");
    }

    [Fact]
    public void ValidateScript_Invalid_Script_Returns_Valid_False_With_Violations()
    {
        var s = new Script { Id = "", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click" });
        var r = MakeHandler(s).HandleTool("validate_script", null);
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("\"valid\":false");
        r.Content.Should().Contain("violations");
        r.Content.Should().Contain("fieldPath");
        r.Content.Should().Contain("violationType");
        r.Content.Should().Contain("suggestedFix");
    }

    [Fact]
    public void ValidateScript_CandidateScript_Validates_Candidate_Not_CurrentScript()
    {
        // Current script is invalid (empty id), candidate is valid
        var current = new Script { Id = "", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        current.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click" });
        var handler = MakeHandler(current);

        var candidateJson = "{\"id\":\"abc\",\"name\":\"Good\",\"version\":\"1.0.0\",\"targetRuntime\":\"desktop\",\"steps\":[{\"stepNumber\":1,\"description\":\"Click OK\",\"action\":\"click\"}]}";
        var args = JsonSerializer.Serialize(new { candidateScript = candidateJson });

        var r = handler.HandleTool("validate_script", args);
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("\"valid\":true");
    }

    [Fact]
    public void ValidateScript_CandidateScript_Invalid_Returns_Valid_False()
    {
        var handler = MakeHandler(ValidScript());
        var candidateJson = "{\"id\":\"\",\"name\":\"\",\"version\":\"bad\",\"targetRuntime\":\"\",\"steps\":[]}";
        var args = JsonSerializer.Serialize(new { candidateScript = candidateJson });

        var r = handler.HandleTool("validate_script", args);
        r.Success.Should().BeTrue();
        r.Content.Should().Contain("\"valid\":false");
    }

    // ------------------------------------------------------------------
    // update_script — DS-08-AC05: validate-first enforcement
    // ------------------------------------------------------------------

    [Fact]
    public void UpdateScript_Without_Prior_Validation_Returns_Failure()
    {
        // No validate_script call has been made — update must be rejected
        var s = ValidScript();
        var r = MakeHandler(s).HandleTool("update_script",
            "{\"approved\":true,\"steps\":[{\"stepNumber\":1,\"description\":\"New\"}]}");
        r.Success.Should().BeFalse();
        r.Error.Should().Contain("validate_script");
    }

    [Fact]
    public void UpdateScript_Without_Approval_Returns_Failure()
    {
        var handler = MakeHandler(ValidScript());
        // Validate first to set session flag
        handler.HandleTool("validate_script", null);

        var r = handler.HandleTool("update_script", "{\"approved\":false,\"steps\":[]}");
        r.Success.Should().BeFalse();
    }

    [Fact]
    public void UpdateScript_After_Invalid_Validation_Returns_Failure()
    {
        // validate_script returned valid=false; update must still be rejected
        var s = new Script { Id = "", Name = "T", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click" });
        var handler = MakeHandler(s);
        handler.HandleTool("validate_script", null); // returns valid=false

        var r = handler.HandleTool("update_script",
            "{\"approved\":true,\"steps\":[{\"stepNumber\":1,\"description\":\"New\"}]}");
        r.Success.Should().BeFalse();
        r.Error.Should().Contain("validate_script");
    }

    [Fact]
    public void UpdateScript_With_Prior_Valid_Validation_And_Approval_Updates_Descriptions()
    {
        var s = ValidScript();
        s.Steps[0].Description = "Old desc";
        var handler = MakeHandler(s);

        // validate first — script is valid so session flag becomes true
        var vr = handler.HandleTool("validate_script", null);
        vr.Success.Should().BeTrue();
        vr.Content.Should().Contain("\"valid\":true");

        var args = "{\"approved\":true,\"steps\":[{\"stepNumber\":1,\"description\":\"New desc\"}]}";
        var r = handler.HandleTool("update_script", args);
        r.Success.Should().BeTrue();
        s.Steps[0].Description.Should().Be("New desc");
    }

    [Fact]
    public void SetCurrentScript_Resets_Validation_State()
    {
        var s = ValidScript();
        var handler = MakeHandler(s);
        // Validate to set session flag
        handler.HandleTool("validate_script", null);

        // Loading a new script should reset the flag
        handler.SetCurrentScript(ValidScript());

        var r = handler.HandleTool("update_script",
            "{\"approved\":true,\"steps\":[{\"stepNumber\":1,\"description\":\"New\"}]}");
        r.Success.Should().BeFalse(); // flag was reset
    }

    // ------------------------------------------------------------------
    // unknown tool
    // ------------------------------------------------------------------

    [Fact]
    public void Unknown_Tool_Returns_Failure()
    {
        MakeHandler().HandleTool("nonexistent_tool", null).Success.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // NullMcpServer
    // ------------------------------------------------------------------

    [Fact]
    public void NullMcpServer_IsRunning_False()
    {
        new NullMcpServer().IsRunning.Should().BeFalse();
    }
}
