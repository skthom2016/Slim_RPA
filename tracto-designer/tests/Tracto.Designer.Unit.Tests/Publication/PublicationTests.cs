namespace Tracto.Designer.Unit.Tests.Publication;

using System.IO;
using FluentAssertions;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Signing;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;

public class PublicationTests
{
    private static readonly byte[] _key = System.Text.Encoding.UTF8.GetBytes("test-key-12345678");

    private static Script ValidScript()
    {
        var s = new Script { Id = "pub-test", Name = "PubTest", Version = "1.0.0", TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click" });
        return s;
    }

    private static ScriptSignature GoodSig(Script s) =>
        new HmacScriptSigner("ts", _key, new ScriptValidator()).Sign(s);

    private static ScriptTestResult PassedTest() =>
        new(true, [new StepTestResult(1, true, "OK", TimeSpan.FromMilliseconds(50))], TimeSpan.FromMilliseconds(50));

    private static ScriptTestResult FailedTest() =>
        new(false, [], TimeSpan.Zero, "simulated fail");

    [Fact]
    public async Task NullPublisher_Always_Succeeds()
    {
        var req = new PublicationRequest(ValidScript(), GoodSig(ValidScript()), PassedTest(), "catalog");
        NullPublisher p = new();
        var result = await p.PublishAsync(req);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Gate_Blocks_Invalid_Script()
    {
        var invalid = new Script { Id = "", Name = "", Version = "", TargetRuntime = "" };
        var gate = new PublicationGate(new ScriptValidator(), new HmacScriptSigner("ts", _key, new ScriptValidator()));
        var sig = new ScriptSignature("ts", "HMAC-SHA256", DateTimeOffset.UtcNow, new string('0', 64));
        var req = new PublicationRequest(invalid, sig, PassedTest(), "cat");
        gate.CheckGates(req).Should().NotBeNull();
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Blocks_Failed_Test()
    {
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        var gate = new PublicationGate(new ScriptValidator(), signer);
        var req = new PublicationRequest(s, signer.Sign(s), FailedTest(), "cat");
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Blocks_Bad_Signature()
    {
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        var gate = new PublicationGate(new ScriptValidator(), signer);
        var badSig = new ScriptSignature("ts", "HMAC-SHA256", DateTimeOffset.UtcNow, new string('0', 64));
        var req = new PublicationRequest(s, badSig, PassedTest(), "cat");
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Passes_All_Good()
    {
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        var gate = new PublicationGate(new ScriptValidator(), signer);
        var req = new PublicationRequest(s, signer.Sign(s), PassedTest(), "cat");
        gate.CheckGates(req).Should().BeNull();
    }

    // DS-16-AC01: when deployment policy does not mandate testing, a VALIDATED script
    // must be publishable without a passing test result.
    [Fact]
    public void Gate_Passes_When_Test_Not_Required_And_Test_Failed()
    {
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        // testRequired = false → Gate 2 is skipped
        var gate = new PublicationGate(new ScriptValidator(), signer, testRequired: false);
        var req = new PublicationRequest(s, signer.Sign(s), FailedTest(), "cat");
        gate.CheckGates(req).Should().BeNull("VALIDATED publish must succeed when policy does not mandate a test");
    }

    [Fact]
    public void Gate_Blocks_Failed_Test_When_Test_Required()
    {
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        // Explicit testRequired = true — same semantics as the original default
        var gate = new PublicationGate(new ScriptValidator(), signer, testRequired: true);
        var req = new PublicationRequest(s, signer.Sign(s), FailedTest(), "cat");
        gate.CheckGates(req)!.Success.Should().BeFalse();
    }

    [Fact]
    public void Gate_Passes_When_Test_Not_Required_And_No_Test_Run()
    {
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        var gate = new PublicationGate(new ScriptValidator(), signer, testRequired: false);
        var req = new PublicationRequest(s, signer.Sign(s), ScriptTestResult.NotRun, "cat");
        gate.CheckGates(req).Should().BeNull("VALIDATED publish with NotRun sentinel must succeed when test is not policy-required");
    }

    [Fact]
    public void PublicationResult_Failure_Factory()
    {
        var r = PublicationResult.Failure("test fail");
        r.Success.Should().BeFalse();
        r.Message.Should().Be("test fail");
    }

    [Fact]
    public void PublicationResult_Ok_Factory()
    {
        var r = PublicationResult.Ok("abc123");
        r.Success.Should().BeTrue();
        r.PublicationId.Should().Be("abc123");
        r.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LocalFilePublisher_Publishes_To_Temp_Dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tracto-pub-test-" + Guid.NewGuid().ToString("N"));
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        var gate = new PublicationGate(new ScriptValidator(), signer);
        var pub = new Tracto.Designer.Infrastructure.Publication.LocalFilePublisher(dir, gate);
        var req = new PublicationRequest(s, signer.Sign(s), PassedTest(), dir);
        var r = await pub.PublishAsync(req);
        r.Success.Should().BeTrue();
        File.Exists(Path.Combine(dir, "catalog.json")).Should().BeTrue();
        // cleanup
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task LocalFilePublisher_Blocks_Duplicate_Version()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tracto-pub-dup-" + Guid.NewGuid().ToString("N"));
        var s = ValidScript();
        var signer = new HmacScriptSigner("ts", _key, new ScriptValidator());
        var gate = new PublicationGate(new ScriptValidator(), signer);
        var pub = new Tracto.Designer.Infrastructure.Publication.LocalFilePublisher(dir, gate);
        var req = new PublicationRequest(s, signer.Sign(s), PassedTest(), dir);
        await pub.PublishAsync(req); // first publish succeeds
        var r2 = await pub.PublishAsync(req); // second should fail
        r2.Success.Should().BeFalse();
        r2.Message.Should().Contain("already published");
        Directory.Delete(dir, true);
    }
}
