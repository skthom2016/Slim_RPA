using System.Reflection;
using FluentAssertions;
using Tracto.Desktop.Ipc;
using Xunit;

namespace Tracto.Desktop.Ipc.Tests;

/// <summary>
/// DT-04 acceptance tests for the Supervisor/Executor named-pipe IPC layer.
/// All pipe names are unique per test to allow parallel execution.
/// </summary>
public sealed class IpcProtocolTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // AC: Handshake end-to-end
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handshake_SupervisorStub_ClientConnects_ReturnsOrchestratorEndpoint()
    {
        // Arrange
        var expectedEndpoint = new OrchestratorEndpoint("10.0.2.2", 7777, "grpc");
        await using var stub = new SupervisorStub(expectedEndpoint);

        using var client = new ExecutorPipeClient();

        // Act
        await client.ConnectAsync(stub.PipeName, timeoutMs: 5_000, ct: default);
        OrchestratorEndpoint result = await IpcHandshakeProtocol.ExecutorHandshakeAsync(client);

        // Assert
        result.Should().Be(expectedEndpoint);
        result.Host.Should().Be("10.0.2.2");
        result.Port.Should().Be(7777);
        result.Scheme.Should().Be("grpc");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC: OrchestratorEndpoint immutability
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OrchestratorEndpoint_Properties_AreInitOnly()
    {
        // All properties of a record are init-only by default; verify via reflection
        // that no public settable (non-init) property is exposed.
        var type = typeof(OrchestratorEndpoint);
        var writableNonInit = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod is { IsPublic: true })
            .Where(p =>
            {
                // init-only setters have a ReturnTypeRequiredMemberAttribute-like flag;
                // a simpler test: the setter's custom modifiers contain IsExternalInit.
                var setMethod = p.SetMethod!;
                // In C# records, init-only setters have the [IsExternalInit] modifier.
                // We detect this by checking for the modopt on the return type.
                return !IsInitOnly(setMethod);
            })
            .ToList();

        writableNonInit.Should().BeEmpty("OrchestratorEndpoint must expose only init-only setters (record positional properties).");
    }

    private static bool IsInitOnly(System.Reflection.MethodInfo setMethod)
    {
        // The CLR marks init-only setters with a required custom modifier: System.Runtime.CompilerServices.IsExternalInit
        var returnParam = setMethod.ReturnParameter;
        var requiredMods = returnParam?.GetRequiredCustomModifiers() ?? [];
        return requiredMods.Any(m => m.Name == "IsExternalInit");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC: SupervisorPipeServer has no GUI-input methods (DT-04-AC03)
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SendInput")]
    [InlineData("mouse_event")]
    [InlineData("keybd_event")]
    public void SupervisorPipeServer_HasNo_GuiInputMethod(string methodName)
    {
        var type = typeof(SupervisorPipeServer);
        var allMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.Instance | BindingFlags.Static);

        allMethods.Should().NotContain(
            m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase),
            $"SupervisorPipeServer must not expose '{methodName}' — the Supervisor cannot inject GUI events.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC: ConnectAsync with pre-cancelled token → OperationCanceledException
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        using var client = new ExecutorPipeClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => client.ConnectAsync(
            $"tracto-nonexistent-{Guid.NewGuid():N}",
            timeoutMs: 5_000,
            ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AC: Round-trip serialization — IpcMessage fields preserved
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_IpcMessage_SerializerPreservesAllFields()
    {
        // Arrange: start a stub, connect a client, then do a raw send/receive
        await using var stub = new SupervisorStub();
        using var client = new ExecutorPipeClient();

        // We drive the server manually (not via handshake) to test raw round-trip.
        // Start server accept in background:
        var serverConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var serverPipe = new Tracto.Desktop.Ipc.SupervisorPipeServer(
            $"tracto-roundtrip-{Guid.NewGuid():N}");

        var serverTask = Task.Run(async () =>
        {
            await serverPipe.AcceptConnectionAsync().ConfigureAwait(false);
            serverConnected.SetResult();
            // Echo back the first message unchanged
            IpcMessage received = await serverPipe.ReceiveMessageAsync().ConfigureAwait(false);
            await serverPipe.SendMessageAsync(received).ConfigureAwait(false);
        });

        await client.ConnectAsync(serverPipe.PipeName, timeoutMs: 5_000);
        await serverConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var original = new IpcMessage(
            Type: IpcMessageType.Heartbeat,
            Payload: "{\"jobId\":\"abc-123\"}",
            CorrelationId: "corr-99");

        // Act
        await client.SendMessageAsync(original);
        IpcMessage echoed = await client.ReceiveMessageAsync();

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — all fields survive the wire round-trip
        echoed.Type.Should().Be(original.Type);
        echoed.Payload.Should().Be(original.Payload);
        echoed.CorrelationId.Should().Be(original.CorrelationId);
    }
}
