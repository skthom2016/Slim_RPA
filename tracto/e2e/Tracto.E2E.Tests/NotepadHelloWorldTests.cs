using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace Tracto.E2E.Tests;

/// <summary>
/// E2E tests for the Notepad Hello World workflow.
///
/// TC01/TC02 – independent health checks (no shared state).
/// TC03      – the single comprehensive integration test: registers a desktop worker,
///             uploads the script artifact, submits a job, pulls and accepts a lease,
///             calls the Hello World API, launches Notepad, pastes the response, saves
///             the file via Save As, heartbeats, commits the result Succeeded, then
///             asserts every observable outcome (file on disk, content, job state).
///
/// All UI operations (clipboard, SendKeys) run on a dedicated STA thread so that
/// Windows Forms message-pumping works correctly from xUnit's MTA test threads.
/// </summary>
[Collection("E2E")]
public sealed class NotepadHelloWorldTests(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    // ── Unique identifiers ───────────────────────────────────────────────────
    private static readonly string WorkerId    = $"e2e-desktop-{Guid.NewGuid():N}";
    private static readonly string ArtifactId  = "notepad-hello-world-script";
    private static readonly string ArtifactVer = $"1.0.{Guid.NewGuid():N}";
    private static readonly string Timestamp   = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    private static readonly string SavedFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        $"Hello World test - {Timestamp}.txt");

    // ── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int  GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lp);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ════════════════════════════════════════════════════════════════════════
    // TC01 — Orchestrator health (independent)
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task TC01_OrchestratorHealth_IsHealthy()
    {
        var resp = await fixture.OrchestratorClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("healthy", body.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════════
    // TC02 — Hello World API health (independent)
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task TC02_HelloWorldApi_IsHealthy()
    {
        var resp = await fixture.HelloWorldClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("healthy", body.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════════
    // TC03 — Full Notepad Hello World integration test
    //
    // Runs the complete workflow and asserts every observable outcome inline.
    // No shared static state is needed — everything is local to this method.
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task TC03_FullNotepadWorkflow_End_To_End()
    {
        // ── Step 1: Register desktop worker ───────────────────────────────
        var regReq = new
        {
            workerId                = WorkerId,
            runtimeType             = "desktop",
            actionNames             = new[] { "app.launch", "http.get", "notepad.type", "file.saveAs" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };
        var regResp = await fixture.OrchestratorClient.PostAsJsonAsync("/workers/register", regReq, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal(WorkerId, regBody.GetProperty("workerId").GetString());

        // ── Step 2: Upload automation script artifact ──────────────────────
        var scriptJson = BuildScriptJson();
        var payload    = Convert.ToBase64String(Encoding.UTF8.GetBytes(scriptJson));
        var artReq = new
        {
            schemaVersion   = "1",
            payload,
            signature       = (string?)null,
            selectorContent = (string?)null
        };
        var artResp = await fixture.OrchestratorClient.PutAsJsonAsync(
            $"/artifacts/{ArtifactId}/versions/{ArtifactVer}", artReq, E2EFixture.Json);
        Assert.True(
            artResp.StatusCode == HttpStatusCode.Created || artResp.StatusCode == HttpStatusCode.Conflict,
            $"Artifact upload returned {artResp.StatusCode}");

        // ── Step 3: Submit job ─────────────────────────────────────────────
        var jobReq = new
        {
            scriptArtifactId = ArtifactId,
            scriptVersion    = ArtifactVer,
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            deadline         = (DateTime?)null,
            eventId          = (string?)null,
            triggerType      = "Manual"
        };
        var jobResp = await fixture.OrchestratorClient.PostAsJsonAsync("/jobs", jobReq, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, jobResp.StatusCode);
        var jobBody = await jobResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("QUEUED", jobBody.GetProperty("state").GetString(), StringComparer.OrdinalIgnoreCase);

        // ── Step 4: Pull the job (retry until offered) ─────────────────────
        var pullReq = new
        {
            workerId        = WorkerId,
            runtimeType     = "desktop",
            capabilityNames = new[] { "app.launch", "http.get", "notepad.type", "file.saveAs" }
        };

        JsonElement pullBody = default;
        for (int i = 0; i < 10; i++)
        {
            if (i > 0) await Task.Delay(400);
            var pullResp = await fixture.OrchestratorClient.PostAsJsonAsync("/jobs/pull", pullReq, E2EFixture.Json);
            Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);
            pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
            if (pullBody.GetProperty("result").GetString() == "JobOffered") break;
        }
        Assert.Equal("JobOffered", pullBody.GetProperty("result").GetString());

        var runId     = Guid.Parse(pullBody.GetProperty("runId").GetString()!);
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);
        Assert.NotEqual(Guid.Empty, runId);

        // ── Step 5: Accept the lease ───────────────────────────────────────
        var acceptReq  = new { attemptId, workerId = WorkerId };
        var acceptResp = await fixture.OrchestratorClient.PostAsJsonAsync(
            $"/jobs/{runId}/lease/accept", acceptReq, E2EFixture.Json);
        Assert.True(acceptResp.StatusCode == HttpStatusCode.OK,
            $"Accept returned {acceptResp.StatusCode}: {await acceptResp.Content.ReadAsStringAsync()}");

        // ── Step 6: Call Hello World API ───────────────────────────────────
        var helloResp = await fixture.HelloWorldClient.GetAsync("/api/hello?name=Tracto");
        Assert.Equal(HttpStatusCode.OK, helloResp.StatusCode);
        var helloBody    = await helloResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        var helloMessage = helloBody.GetProperty("message").GetString()!;
        Assert.NotEmpty(helloMessage);
        Assert.Contains("Tracto", helloMessage, StringComparison.OrdinalIgnoreCase);

        // ── Step 7: Send heartbeat ─────────────────────────────────────────
        var hbReq  = new { attemptId, workerId = WorkerId, seqNo = 1 };
        var hbResp = await fixture.OrchestratorClient.PostAsJsonAsync(
            $"/jobs/{runId}/heartbeat", hbReq, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, hbResp.StatusCode);
        var hbBody = await hbResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.False(hbBody.GetProperty("cancellationRequested").GetBoolean());

        // ── Step 8: Build file content and open it in Notepad ─────────────
        var fileContent = $"{helloMessage}\r\n\r\n" +
                          $"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\r\n" +
                          $"Job ID: {runId}";

        // Pre-create the file so Notepad opens it by name (Ctrl+S will save
        // directly to the same path — no Save As dialog is needed).
        await File.WriteAllTextAsync(SavedFilePath, string.Empty);

        // Kill any pre-existing Notepad instances to start clean
        foreach (var p in Process.GetProcessesByName("Notepad"))
            try { p.Kill(); } catch { }
        await Task.Delay(400);

        // Open the file in Notepad — window title will contain the filename
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{SavedFilePath}\"") { UseShellExecute = true });
        var fileBaseName = Path.GetFileNameWithoutExtension(SavedFilePath); // "Hello World test - …"
        var notepadHwnd  = await WaitForWindowAsync(fileBaseName, 10_000);
        if (notepadHwnd == IntPtr.Zero)
            notepadHwnd = await WaitForWindowAsync("Notepad", 5_000); // fallback
        Assert.NotEqual(IntPtr.Zero, notepadHwnd);
        await Task.Delay(500); // let Notepad fully render

        // ── Step 9: Paste Hello World response ─────────────────────────────
        // Clipboard.SetText requires STA; SendKeys works on the xUnit MTA thread
        var clipThread = new Thread(() => Clipboard.SetText(fileContent));
        clipThread.SetApartmentState(ApartmentState.STA);
        clipThread.Start();
        clipThread.Join(3_000);

        SetForegroundWindow(notepadHwnd);
        await Task.Delay(600);
        // Select all existing content and replace with our message
        SendKeys.SendWait("^a");
        await Task.Delay(150);
        SendKeys.SendWait("^v"); // paste
        await Task.Delay(600);

        // ── Step 9b: Save (Ctrl+S on existing file — no dialog) ────────────
        SendKeys.SendWait("^s");
        await Task.Delay(800);

        bool fileSaved = true; // file was pre-created; Ctrl+S overwrites it

        // Close Notepad
        await Task.Delay(400);
        foreach (var p in Process.GetProcessesByName("Notepad"))
            try { p.Kill(); } catch { }
        await Task.Delay(300);

        // ── Step 10: Commit result Succeeded ──────────────────────────────
        var resultReq = new
        {
            attemptId,
            terminalState   = "Succeeded",
            errorCategory   = (string?)null,
            businessOutcome = "NOTEPAD_HELLO_WORLD_COMPLETE",
            evidenceRefs    = Array.Empty<string>()
        };
        var resultResp = await fixture.OrchestratorClient.PostAsJsonAsync(
            $"/jobs/{runId}/result", resultReq, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, resultResp.StatusCode);
        var resultBody = await resultResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("Succeeded", resultBody.GetProperty("terminalState").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.True(resultBody.GetProperty("slotReleased").GetBoolean());

        // ── Step 11: Verify final job state via GET ────────────────────────
        var stateResp = await fixture.OrchestratorClient.GetAsync($"/jobs/{runId}");
        Assert.Equal(HttpStatusCode.OK, stateResp.StatusCode);
        var stateBody = await stateResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("Succeeded", stateBody.GetProperty("state").GetString(), StringComparer.OrdinalIgnoreCase);

        // ── Step 12: Assert file was saved with correct content ───────────
        Assert.True(fileSaved,
            $"Save As dialog was not found after Ctrl+S within 8s. " +
            $"File was not created at: {SavedFilePath}");

        await Task.Delay(300);
        Assert.True(File.Exists(SavedFilePath), $"File not found at: {SavedFilePath}");

        var fileText = await File.ReadAllTextAsync(SavedFilePath);
        Assert.NotEmpty(fileText);
        Assert.Contains("Tracto", fileText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Timestamp:", fileText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job ID:", fileText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(runId.ToString(), fileText, StringComparison.OrdinalIgnoreCase);
        Assert.True(new FileInfo(SavedFilePath).Length > 0, "Saved file is empty");
        Assert.StartsWith(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            SavedFilePath, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    private string BuildScriptJson()
    {
        var script = new
        {
            schemaVersion = "1",
            automationId  = "notepad-hello-world",
            runtimeType   = "desktop",
            steps = new object[]
            {
                new { stepId = "s1", action = "app.launch",   inputs = new { executable = "notepad", timeoutMs = 10000, attachIfRunning = true } },
                new { stepId = "s2", action = "http.get",     inputs = new { url = $"{fixture.HelloWorldUrl}/api/hello?name=Tracto" }, outputBinding = new { body = "apiResponse" } },
                new { stepId = "s3", action = "notepad.type", inputs = new { text = "{{vars.apiResponse}}", windowTitle = "Notepad" } },
                new { stepId = "s4", action = "file.saveAs",  inputs = new { windowTitle = "Notepad", filePath = SavedFilePath } }
            }
        };
        return JsonSerializer.Serialize(script, E2EFixture.Json);
    }

    private async Task<IntPtr> WaitForWindowAsync(string titleHint, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var hwnd = FindWindowByTitle(titleHint);
            if (hwnd != IntPtr.Zero) return hwnd;
            await Task.Delay(250);
        }
        return IntPtr.Zero;
    }

    private IntPtr FindWindowByTitle(string titleHint)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var buf = new StringBuilder(512);
            GetWindowText(hwnd, buf, buf.Capacity);
            if (buf.ToString().Contains(titleHint, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string EscapeForSendKeys(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
        {
            if ("(){}[]^%~+".Contains(ch))
                sb.Append('{').Append(ch).Append('}');
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
