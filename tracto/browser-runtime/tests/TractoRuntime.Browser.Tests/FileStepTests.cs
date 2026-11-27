using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.FileTransfer;
using TractoRuntime.Browser.Host;
using TractoRuntime.Browser.Locators;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// The <c>download</c> and <c>upload</c> script steps: how the translator reads them, which
/// folders the policy allows, and a real headless browser downloading and uploading a file.
/// </summary>
public sealed class FileStepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tracto-filestep-" + Guid.NewGuid().ToString("N"));

    public FileStepTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static Dictionary<string, string?> P(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => (string?)i.Value, StringComparer.OrdinalIgnoreCase);

    // ── translator ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("download")]
    [InlineData("browser.download")]
    [InlineData("browser.file.download")]
    public void Download_names_map_to_the_download_action(string name)
    {
        var step = StepTranslator.Translate("step.1", name, P(("text", "Download report")), allowCss: true, _root);

        step.Action.Should().Be(StepTranslator.DownloadAction);
        step.Browser!.DownloadRef!.DestinationDirectory.Should().Be(_root);
        step.Browser.Selector!.Text.Should().Be("Download report");
    }

    [Fact]
    public void Download_without_a_target_waits_for_a_download_already_started()
    {
        var step = StepTranslator.Translate("step.1", "download", P(), allowCss: true, _root);

        step.Browser!.Selector.Should().BeNull();
        step.Browser.DownloadRef!.DestinationDirectory.Should().Be(_root);
    }

    [Fact]
    public void A_relative_folder_is_taken_inside_the_default_folder()
    {
        var step = StepTranslator.Translate("step.1", "download",
            P(("text", "x"), ("folder", "portals\\givewell")), allowCss: true, _root);

        step.Browser!.DownloadRef!.DestinationDirectory.Should().Be(Path.Combine(_root, "portals\\givewell"));
    }

    [Fact]
    public void A_full_folder_is_kept_and_the_checks_are_read()
    {
        var step = StepTranslator.Translate("step.1", "download",
            P(("text", "x"), ("folder", @"D:\reports"), ("expectedFileName", "donations.csv"),
              ("contentType", "text/csv"), ("maxSizeBytes", "1048576")), allowCss: true, _root);

        var d = step.Browser!.DownloadRef!;
        d.DestinationDirectory.Should().Be(@"D:\reports");
        d.ExpectedFilename.Should().Be("donations.csv");
        d.ExpectedContentType.Should().Be("text/csv");
        d.MaxSizeBytes.Should().Be(1048576);
    }

    [Fact]
    public void The_name_is_stamped_unless_the_script_says_not_to()
    {
        StepTranslator.Translate("step.1", "download", P(("text", "x")), allowCss: true, _root)
            .Browser!.DownloadRef!.StampFileName.Should().BeTrue();
        StepTranslator.Translate("step.1", "download", P(("text", "x"), ("stampName", "false")), allowCss: true, _root)
            .Browser!.DownloadRef!.StampFileName.Should().BeFalse();

        var bad = () => StepTranslator.Translate("step.1", "download", P(("text", "x"), ("stampName", "yes")), allowCss: true, _root);
        bad.Should().Throw<ScriptValidationException>().WithMessage("*stampName*");
    }

    [Fact]
    public void A_stamped_name_carries_date_and_time_and_never_replaces_a_file()
    {
        var at = new DateTime(2026, 11, 27, 14, 14, 21);

        var first = FileDownloadHandler.StampedPath(_root, "donations.csv", at);
        first.Should().Be(Path.Combine(_root, "donations_2026-11-27_14-14-21.csv"));

        File.WriteAllText(first, "x");
        FileDownloadHandler.StampedPath(_root, "donations.csv", at)
            .Should().Be(Path.Combine(_root, "donations_2026-11-27_14-14-21_2.csv"));

        FileDownloadHandler.StampedPath(_root, "README", at)
            .Should().Be(Path.Combine(_root, "README_2026-11-27_14-14-21"));
    }

    [Fact]
    public void Download_without_any_folder_is_refused()
    {
        var act = () => StepTranslator.Translate("step.1", "download", P(("text", "x")), allowCss: true);
        act.Should().Throw<ScriptValidationException>().WithMessage("*needs 'folder'*");
    }

    [Fact]
    public void A_bad_size_limit_is_refused()
    {
        var act = () => StepTranslator.Translate("step.1", "download",
            P(("text", "x"), ("maxSizeBytes", "big")), allowCss: true, _root);
        act.Should().Throw<ScriptValidationException>().WithMessage("*maxSizeBytes*");
    }

    [Fact]
    public void Upload_needs_a_target_and_a_full_path()
    {
        var noTarget = () => StepTranslator.Translate("step.1", "upload", P(("path", @"C:\a.csv")), allowCss: true);
        noTarget.Should().Throw<ScriptValidationException>().WithMessage("*needs a target*");

        var noPath = () => StepTranslator.Translate("step.1", "upload", P(("label", "File")), allowCss: true);
        noPath.Should().Throw<ScriptValidationException>().WithMessage("*needs 'path'*");

        var relative = () => StepTranslator.Translate("step.1", "upload", P(("label", "File"), ("path", "a.csv")), allowCss: true);
        relative.Should().Throw<ScriptValidationException>().WithMessage("*full path*");

        var ok = StepTranslator.Translate("step.1", "upload", P(("label", "File"), ("path", @"C:\a.csv")), allowCss: true);
        ok.Action.Should().Be(StepTranslator.UploadAction);
        ok.Browser!.UploadRef!.SourcePath.Should().Be(@"C:\a.csv");
    }

    [Fact]
    public void A_path_with_a_variable_is_checked_when_the_step_runs()
    {
        var step = StepTranslator.Translate("step.1", "upload",
            P(("label", "File"), ("path", "${report.path}")), allowCss: true);
        step.Browser!.UploadRef!.SourcePath.Should().Be("${report.path}");
    }

    [Fact]
    public void Both_actions_are_advertised_to_the_orchestrator()
    {
        StepTranslator.RuntimeActionNames.Should().Contain([StepTranslator.DownloadAction, StepTranslator.UploadAction]);
    }

    // ── policy ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_root_itself_and_folders_inside_it_are_allowed_but_a_sibling_is_not()
    {
        var policy = new FileTransferPolicy(
            new FileTransferPolicyOptions { ApprovedDownloadRoots = [_root], ApprovedUploadRoots = [_root] },
            NullLogger<FileTransferPolicy>.Instance);

        policy.IsDownloadPathApproved(_root).Should().BeTrue();
        policy.IsDownloadPathApproved(Path.Combine(_root, "sub")).Should().BeTrue();
        policy.IsDownloadPathApproved(_root + "-other").Should().BeFalse();
        policy.IsDownloadPathApproved(Path.Combine(_root, "..")).Should().BeFalse();
        policy.IsUploadPathApproved(Path.Combine(_root, "a.csv")).Should().BeTrue();
    }

    // ── real browser ─────────────────────────────────────────────────────────

    private const string Csv = "donor,amount\nA. Donor,25.00\n";

    [Fact]
    public async Task A_real_browser_downloads_a_report_and_uploads_it_again()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            return; // Chromium for Playwright is not installed on this machine.
        }

        await using var _ = browser;
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { AcceptDownloads = true });
        await context.RouteAsync("http://portal.test/**", route => route.Request.Url.EndsWith("/report.csv")
            ? route.FulfillAsync(new RouteFulfillOptions
              {
                  Status = 200, ContentType = "text/csv", Body = Csv,
                  Headers = new Dictionary<string, string> { ["Content-Disposition"] = "attachment; filename=\"donations.csv\"" }
              })
            : route.FulfillAsync(new RouteFulfillOptions
              {
                  Status = 200, ContentType = "text/html",
                  Body = "<html><body><h1>GiveWell portal</h1>" +
                         "<a href=\"/report.csv\">Download report</a>" +
                         "<label>Import file <input type=\"file\" id=\"f\"></label></body></html>"
              }));

        var page = await context.NewPageAsync();
        await page.GotoAsync("http://portal.test/");

        var resolver = new WaitingSelectorStrategy(new LocatorResolver(), 5_000, NullLogger<WaitingSelectorStrategy>.Instance);
        var policy = new FileTransferPolicy(
            new FileTransferPolicyOptions { ApprovedDownloadRoots = [_root], ApprovedUploadRoots = [_root] },
            NullLogger<FileTransferPolicy>.Instance);
        var executor = new ActionExecutor(
            resolver,
            new FailureClassifier(NullLogger<FailureClassifier>.Instance),
            NullLogger<ActionExecutor>.Instance,
            fileDownloadHandler: new FileDownloadHandler(policy, resolver, NullLogger<FileDownloadHandler>.Instance),
            fileUploadHandler:   new FileUploadHandler(policy, resolver, NullLogger<FileUploadHandler>.Instance));

        // download into a sub-folder of the root
        var download = StepTranslator.Translate("step.1", "download",
            P(("role", "link"), ("name", "Download report"), ("folder", "givewell"),
              ("expectedFileName", "donations.csv"), ("contentType", "text/csv")), allowCss: true, _root);
        var got = await executor.ExecuteAsync(page, download.Browser!, CancellationToken.None);

        got.Succeeded.Should().BeTrue(got.ErrorMessage);
        var saved = (string)got.Evidence["downloadedFilePath"]!;
        Path.GetDirectoryName(saved).Should().Be(Path.Combine(_root, "givewell"));
        Path.GetFileName(saved).Should().MatchRegex(@"^donations_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.csv$");
        (await File.ReadAllTextAsync(saved)).Should().Be(Csv);
        got.Evidence["filename"].Should().Be(Path.GetFileName(saved));
        got.Evidence["suggestedFilename"].Should().Be("donations.csv");

        // the same file again keeps the first one
        var again = await executor.ExecuteAsync(page, download.Browser!, CancellationToken.None);
        again.Succeeded.Should().BeTrue(again.ErrorMessage);
        again.Evidence["downloadedFilePath"].Should().NotBe(saved);
        Directory.GetFiles(Path.Combine(_root, "givewell")).Should().HaveCount(2);

        // stampName=false keeps the site's name
        var plain = StepTranslator.Translate("step.1b", "download",
            P(("role", "link"), ("name", "Download report"), ("folder", "plain"), ("stampName", "false")), allowCss: true, _root);
        var kept = await executor.ExecuteAsync(page, plain.Browser!, CancellationToken.None);
        kept.Evidence["downloadedFilePath"].Should().Be(Path.Combine(_root, "plain", "donations.csv"));

        // a folder outside the roots is refused and nothing is written
        var outside = Path.Combine(Path.GetTempPath(), "tracto-outside-" + Guid.NewGuid().ToString("N"));
        var refused = StepTranslator.Translate("step.2", "download",
            P(("role", "link"), ("name", "Download report"), ("folder", outside)), allowCss: true, _root);
        var denied = await executor.ExecuteAsync(page, refused.Browser!, CancellationToken.None);
        denied.Succeeded.Should().BeFalse();
        denied.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        Directory.Exists(outside).Should().BeFalse();

        // upload the downloaded file into the page's file field
        var upload = StepTranslator.Translate("step.3", "upload", P(("label", "Import file"), ("path", saved)), allowCss: true, _root);
        var put = await executor.ExecuteAsync(page, upload.Browser!, CancellationToken.None);

        put.Succeeded.Should().BeTrue(put.ErrorMessage);
        (await page.EvaluateAsync<string>("() => document.getElementById('f').files[0].name")).Should().Be(Path.GetFileName(saved));
    }
}
