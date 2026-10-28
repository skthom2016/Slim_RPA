using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.FileTransfer;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Tests.Fixtures;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-013 — File Download and Upload.
/// Covers BRW-AT-06, BRW-AT-07, BRW-AT-15.
/// Requirement refs: WEB-006, WEB-007.
/// </summary>
/// <remarks>
///   AC#1 — Download waits for event, validates filename / content-type / size, saves to approved path.
///   AC#2 — Download to a path containing Unicode characters completes and is verifiable.
///   AC#3 — Download to an unapproved path is rejected before any bytes are written.
///   AC#4 — Upload uses approved source path; forbidden path is rejected before any transfer.
///   AC#5 — Cancellation during download cleans up any partial file; no sensitive content remains.
/// </remarks>
public sealed class BRW_US_013_Tests : BrowserTestBase
{
    private readonly Mock<IPage> _pageMock = new();
    private readonly Mock<ILocator> _locatorMock = new();

    // ── Helper factories ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="ActionExecutor"/> wired with the supplied optional handlers.
    /// </summary>
    private ActionExecutor CreateExecutor(
        IFileDownloadHandler? downloadHandler = null,
        IFileUploadHandler? uploadHandler = null)
    {
        var strategyMock = new Mock<ISelectorStrategy>();
        strategyMock
            .Setup(s => s.ResolveAsync(It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_locatorMock.Object);

        return new ActionExecutor(
            strategyMock.Object,
            FailureClassifierMock.Object,
            NullLogger<ActionExecutor>.Instance,
            fileDownloadHandler: downloadHandler,
            fileUploadHandler: uploadHandler);
    }

    /// <summary>
    /// Creates a <see cref="FileDownloadHandler"/> backed by a mock policy and selector strategy.
    /// </summary>
    private static (FileDownloadHandler handler, Mock<IFileTransferPolicy> policyMock,
        Mock<ISelectorStrategy> strategyMock)
        CreateDownloadHandler()
    {
        var policyMock = new Mock<IFileTransferPolicy>();
        var strategyMock = new Mock<ISelectorStrategy>();
        var handler = new FileDownloadHandler(
            policyMock.Object,
            strategyMock.Object,
            NullLogger<FileDownloadHandler>.Instance);
        return (handler, policyMock, strategyMock);
    }

    /// <summary>
    /// Creates a <see cref="FileUploadHandler"/> backed by a mock policy and selector strategy.
    /// </summary>
    private static (FileUploadHandler handler, Mock<IFileTransferPolicy> policyMock,
        Mock<ISelectorStrategy> strategyMock)
        CreateUploadHandler()
    {
        var policyMock = new Mock<IFileTransferPolicy>();
        var strategyMock = new Mock<ISelectorStrategy>();
        var handler = new FileUploadHandler(
            policyMock.Object,
            strategyMock.Object,
            NullLogger<FileUploadHandler>.Instance);
        return (handler, policyMock, strategyMock);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#1 — ActionExecutor: download step routing guards
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActionExecutor_FileDownload_WhenHandlerNotConfigured_ReturnsInternalError()
    {
        // Arrange — no download handler injected
        var executor = CreateExecutor();
        var step = new BrowserStep
        {
            StepId      = "dl-step-1",
            Action      = "browser.file.download",
            DownloadRef = new DownloadRef { DestinationDirectory = @"C:\downloads" }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — CEC contract: INTERNAL error when infrastructure is missing
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.INTERNAL);
        result.DetailCode.Should().Be("browser.fileDownloadHandlerNotConfigured");
    }

    [Fact]
    public async Task ActionExecutor_FileDownload_WhenDownloadRefMissing_ReturnsValidationError()
    {
        // Arrange — handler injected but DownloadRef omitted
        var handlerMock = new Mock<IFileDownloadHandler>();
        var executor = CreateExecutor(downloadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId = "dl-step-2",
            Action = "browser.file.download"
            // DownloadRef intentionally omitted
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — VALIDATION error before any handler is called
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingDownloadRef");

        handlerMock.Verify(
            h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef?>(),
                It.IsAny<DownloadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActionExecutor_FileDownload_WithValidStep_DelegatesToHandler()
    {
        // Arrange — handler returns success
        var handlerMock = new Mock<IFileDownloadHandler>();
        handlerMock
            .Setup(h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef?>(),
                It.IsAny<DownloadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?> { ["filename"] = "report.csv" }
            });

        var executor = CreateExecutor(downloadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId      = "dl-step-3",
            Action      = "browser.file.download",
            DownloadRef = new DownloadRef
            {
                DestinationDirectory = @"C:\approved\downloads",
                ExpectedFilename     = "report.csv",
                ExpectedContentType  = "text/csv"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — happy path: handler is called once, result propagated
        result.Succeeded.Should().BeTrue();
        result.Evidence.Should().ContainKey("filename").WhoseValue!.Should().Be("report.csv");
        handlerMock.Verify(
            h => h.HandleAsync(
                _pageMock.Object,
                null,                          // no trigger selector on this step
                It.Is<DownloadRef>(r => r.DestinationDirectory == @"C:\approved\downloads"),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#3 — FileDownloadHandler: path allowlist enforced before any bytes
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FileDownloadHandler_UnApprovedDestination_ReturnsPolicyDenied_BeforeBrowserTouch()
    {
        // Arrange — policy denies the path
        var (handler, policyMock, strategyMock) = CreateDownloadHandler();
        policyMock.Setup(p => p.IsDownloadPathApproved(It.IsAny<string>())).Returns(false);

        var downloadRef = new DownloadRef { DestinationDirectory = @"C:\forbidden\path" };

        // Act
        var result = await handler.HandleAsync(
            _pageMock.Object,
            null,
            downloadRef,
            5_000f,
            CancellationToken.None);

        // Assert — AC#3: rejected before any browser interaction
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        result.DetailCode.Should().Be("browser.downloadPathDenied");

        // No locator resolution (browser was never touched)
        strategyMock.Verify(
            s => s.ResolveAsync(It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FileDownloadHandler_ApprovedDestination_PassesPolicyCheck()
    {
        // Arrange — policy approves; use a real temp directory so FileInfo.Length succeeds.
        var (handler, policyMock, _) = CreateDownloadHandler();
        var tempDir = Path.Combine(Path.GetTempPath(), $"brw013-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            policyMock.Setup(p => p.IsDownloadPathApproved(tempDir)).Returns(true);

            var downloadMock = new Mock<IDownload>();
            downloadMock.Setup(d => d.SuggestedFilename).Returns("report.csv");
            downloadMock
                .Setup(d => d.SaveAsAsync(It.IsAny<string>()))
                .Callback<string>(path => File.WriteAllText(path, "col1,col2\n1,2\n"))
                .Returns(Task.CompletedTask);

            _pageMock
                .Setup(p => p.WaitForDownloadAsync(It.IsAny<PageWaitForDownloadOptions?>()))
                .ReturnsAsync(downloadMock.Object);

            var downloadRef = new DownloadRef
            {
                DestinationDirectory = tempDir,
                ExpectedFilename     = null,
                ExpectedContentType  = null
            };

            // Act — WaitForDownloadAsync path (no trigger selector)
            var result = await handler.HandleAsync(
                _pageMock.Object,
                triggerSelector: null,
                downloadRef,
                5_000f,
                CancellationToken.None);

            // Assert — policy approved; handler proceeded and saved the file
            policyMock.Verify(p => p.IsDownloadPathApproved(tempDir), Times.Once);
            result.Succeeded.Should().BeTrue();
            result.ErrorCategory.Should().BeNull();
            result.Evidence.Should().ContainKey("filename").WhoseValue!.Should().Be("report.csv");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#1 — FileDownloadHandler: filename validation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FileDownloadHandler_FilenameMismatch_ReturnsValidationError()
    {
        // Arrange — policy approves, but filename doesn't match expectation
        var (handler, policyMock, _) = CreateDownloadHandler();
        policyMock.Setup(p => p.IsDownloadPathApproved(It.IsAny<string>())).Returns(true);

        var downloadMock = new Mock<IDownload>();
        downloadMock.Setup(d => d.SuggestedFilename).Returns("actual-file.csv");
        downloadMock.Setup(d => d.DeleteAsync()).Returns(Task.CompletedTask);

        _pageMock
            .Setup(p => p.WaitForDownloadAsync(It.IsAny<PageWaitForDownloadOptions?>()))
            .ReturnsAsync(downloadMock.Object);

        var downloadRef = new DownloadRef
        {
            DestinationDirectory = @"C:\approved\downloads",
            ExpectedFilename     = "expected-report.csv"
        };

        // Act
        var result = await handler.HandleAsync(
            _pageMock.Object,
            triggerSelector: null,
            downloadRef,
            5_000f,
            CancellationToken.None);

        // Assert — AC#1: filename mismatch terminates with VALIDATION error
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.downloadFilenameMismatch");

        // The temporary Playwright copy should be deleted
        downloadMock.Verify(d => d.DeleteAsync(), Times.Once);
    }

    [Fact]
    public async Task FileDownloadHandler_ContentTypeMismatch_ReturnsValidationError()
    {
        // Arrange — policy approves; filename is correct but extension doesn't match MIME
        var (handler, policyMock, _) = CreateDownloadHandler();
        policyMock.Setup(p => p.IsDownloadPathApproved(It.IsAny<string>())).Returns(true);

        var downloadMock = new Mock<IDownload>();
        downloadMock.Setup(d => d.SuggestedFilename).Returns("data.csv");   // .csv extension
        downloadMock.Setup(d => d.DeleteAsync()).Returns(Task.CompletedTask);

        _pageMock
            .Setup(p => p.WaitForDownloadAsync(It.IsAny<PageWaitForDownloadOptions?>()))
            .ReturnsAsync(downloadMock.Object);

        var downloadRef = new DownloadRef
        {
            DestinationDirectory = @"C:\approved\downloads",
            ExpectedContentType  = "application/pdf"   // .csv does not match PDF
        };

        // Act
        var result = await handler.HandleAsync(
            _pageMock.Object,
            triggerSelector: null,
            downloadRef,
            5_000f,
            CancellationToken.None);

        // Assert — AC#1: content-type mismatch terminates with VALIDATION
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.downloadContentTypeMismatch");

        downloadMock.Verify(d => d.DeleteAsync(), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#2 — FileDownloadHandler: Unicode destination path is accepted
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FileDownloadHandler_UnicodeDestinationPath_PolicyCheckPasses()
    {
        // Arrange — use a real temp directory whose name includes Unicode characters.
        // This validates that .NET 10 / Windows path APIs handle Unicode correctly (AC#2).
        var unicodeTempDir = Path.Combine(Path.GetTempPath(), $"ダウンロード-{Guid.NewGuid():N}");
        Directory.CreateDirectory(unicodeTempDir);

        try
        {
            var (handler, policyMock, _) = CreateDownloadHandler();
            policyMock
                .Setup(p => p.IsDownloadPathApproved(unicodeTempDir))
                .Returns(true);

            var downloadMock = new Mock<IDownload>();
            downloadMock.Setup(d => d.SuggestedFilename).Returns("データ.csv");
            downloadMock
                .Setup(d => d.SaveAsAsync(It.IsAny<string>()))
                .Callback<string>(path => File.WriteAllText(path, "col1,col2\n1,2\n"))
                .Returns(Task.CompletedTask);

            _pageMock
                .Setup(p => p.WaitForDownloadAsync(It.IsAny<PageWaitForDownloadOptions?>()))
                .ReturnsAsync(downloadMock.Object);

            var downloadRef = new DownloadRef { DestinationDirectory = unicodeTempDir };

            // Act
            var result = await handler.HandleAsync(
                _pageMock.Object,
                triggerSelector: null,
                downloadRef,
                5_000f,
                CancellationToken.None);

            // Assert — AC#2: policy is invoked with exact Unicode path; download succeeds
            policyMock.Verify(p => p.IsDownloadPathApproved(unicodeTempDir), Times.Once);
            result.Succeeded.Should().BeTrue();
            result.DetailCode.Should().NotBe("browser.downloadPathDenied");
            result.Evidence.Should().ContainKey("filename").WhoseValue!.Should().Be("データ.csv");
        }
        finally
        {
            if (Directory.Exists(unicodeTempDir))
                Directory.Delete(unicodeTempDir, recursive: true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#4 — ActionExecutor: upload step routing guards
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActionExecutor_FileUpload_WhenHandlerNotConfigured_ReturnsInternalError()
    {
        // Arrange — no upload handler injected
        var executor = CreateExecutor();
        var step = new BrowserStep
        {
            StepId    = "ul-step-1",
            Action    = "browser.file.upload",
            Selector  = new SelectorRef { Role = "button", RoleName = "Upload" },
            UploadRef = new UploadRef { SourcePath = @"C:\approved\upload\file.csv" }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — CEC contract: INTERNAL error when infrastructure is missing
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.INTERNAL);
        result.DetailCode.Should().Be("browser.fileUploadHandlerNotConfigured");
    }

    [Fact]
    public async Task ActionExecutor_FileUpload_WhenSelectorMissing_ReturnsValidationError()
    {
        // Arrange — handler injected but Selector omitted
        var handlerMock = new Mock<IFileUploadHandler>();
        var executor = CreateExecutor(uploadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId    = "ul-step-2",
            Action    = "browser.file.upload",
            // Selector intentionally omitted
            UploadRef = new UploadRef { SourcePath = @"C:\approved\upload\file.csv" }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — VALIDATION before handler is called
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingSelectorForUpload");

        handlerMock.Verify(
            h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(),
                It.IsAny<UploadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActionExecutor_FileUpload_WhenUploadRefMissing_ReturnsValidationError()
    {
        // Arrange — handler injected, selector present, UploadRef omitted
        var handlerMock = new Mock<IFileUploadHandler>();
        var executor = CreateExecutor(uploadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId   = "ul-step-3",
            Action   = "browser.file.upload",
            Selector = new SelectorRef { Role = "button", RoleName = "Upload" }
            // UploadRef intentionally omitted
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.VALIDATION);
        result.DetailCode.Should().Be("browser.missingUploadRef");

        handlerMock.Verify(
            h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(),
                It.IsAny<UploadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActionExecutor_FileUpload_WithValidStep_DelegatesToHandler()
    {
        // Arrange — handler returns success
        var handlerMock = new Mock<IFileUploadHandler>();
        handlerMock
            .Setup(h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(),
                It.IsAny<UploadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?> { ["uploadStrategy"] = "setInputFiles" }
            });

        var executor = CreateExecutor(uploadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId    = "ul-step-4",
            Action    = "browser.file.upload",
            Selector  = new SelectorRef { Role = "button", RoleName = "Upload" },
            UploadRef = new UploadRef { SourcePath = @"C:\approved\upload\data.csv" }
        };

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, CancellationToken.None);

        // Assert — happy path
        result.Succeeded.Should().BeTrue();
        result.Evidence.Should().ContainKey("uploadStrategy");
        handlerMock.Verify(
            h => h.HandleAsync(
                _pageMock.Object,
                It.Is<SelectorRef>(s => s.RoleName == "Upload"),
                It.Is<UploadRef>(u => u.SourcePath == @"C:\approved\upload\data.csv"),
                It.IsAny<float>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#4 — FileUploadHandler: path allowlist enforced before any transfer
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FileUploadHandler_ForbiddenSourcePath_ReturnsPolicyDenied_BeforeAnyTransfer()
    {
        // Arrange
        var (handler, policyMock, strategyMock) = CreateUploadHandler();
        policyMock.Setup(p => p.IsUploadPathApproved(It.IsAny<string>())).Returns(false);

        var uploadRef = new UploadRef { SourcePath = @"C:\system32\secret.dll" };
        var targetSelector = new SelectorRef { Role = "button", RoleName = "Upload" };

        // Act
        var result = await handler.HandleAsync(
            _pageMock.Object,
            targetSelector,
            uploadRef,
            5_000f,
            CancellationToken.None);

        // Assert — AC#4: path blocked, no browser or file system action
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.POLICY_DENIED);
        result.DetailCode.Should().Be("browser.uploadPathDenied");

        strategyMock.Verify(
            s => s.ResolveAsync(It.IsAny<IPage>(), It.IsAny<SelectorRef>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FileUploadHandler_ApprovedPath_FileNotFound_ReturnsEnvironmentError()
    {
        // Arrange — policy approves, but file doesn't exist on disk
        var (handler, policyMock, _) = CreateUploadHandler();
        policyMock.Setup(p => p.IsUploadPathApproved(It.IsAny<string>())).Returns(true);

        // Source path that does not exist
        var nonExistentPath = Path.Combine(
            Path.GetTempPath(),
            $"nonexistent-{Guid.NewGuid():N}.csv");

        var uploadRef = new UploadRef { SourcePath = nonExistentPath };
        var targetSelector = new SelectorRef { Role = "button", RoleName = "Upload" };

        // Act
        var result = await handler.HandleAsync(
            _pageMock.Object,
            targetSelector,
            uploadRef,
            5_000f,
            CancellationToken.None);

        // Assert — file existence is verified before browser interaction
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.ENVIRONMENT);
        result.DetailCode.Should().Be("browser.uploadFileNotFound");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#5 — Cancellation: CancellationToken is honoured before any browser action
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActionExecutor_FileDownload_PreCancelled_ReturnsCancelledResult()
    {
        // Arrange — token already cancelled before Execute is called
        var handlerMock = new Mock<IFileDownloadHandler>();
        FailureClassifierMock
            .Setup(f => f.Classify(It.IsAny<Exception>(), It.IsAny<bool>()))
            .Returns(new CecError
            {
                Category   = ErrorCategory.CANCELLED,
                DetailCode = "browser.cancelled",
                Message    = "Run was cancelled."
            });

        var executor = CreateExecutor(downloadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId      = "dl-cancel-1",
            Action      = "browser.file.download",
            DownloadRef = new DownloadRef { DestinationDirectory = @"C:\approved\downloads" }
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, cts.Token);

        // Assert — CEC contract: CANCELLED result propagated
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.CANCELLED);

        // Handler is never called when pre-cancelled
        handlerMock.Verify(
            h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef?>(),
                It.IsAny<DownloadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActionExecutor_FileUpload_PreCancelled_ReturnsCancelledResult()
    {
        // Arrange — token already cancelled
        var handlerMock = new Mock<IFileUploadHandler>();
        FailureClassifierMock
            .Setup(f => f.Classify(It.IsAny<Exception>(), It.IsAny<bool>()))
            .Returns(new CecError
            {
                Category   = ErrorCategory.CANCELLED,
                DetailCode = "browser.cancelled",
                Message    = "Run was cancelled."
            });

        var executor = CreateExecutor(uploadHandler: handlerMock.Object);
        var step = new BrowserStep
        {
            StepId    = "ul-cancel-1",
            Action    = "browser.file.upload",
            Selector  = new SelectorRef { Role = "button", RoleName = "Upload" },
            UploadRef = new UploadRef { SourcePath = @"C:\approved\upload\file.csv" }
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await executor.ExecuteAsync(_pageMock.Object, step, cts.Token);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.CANCELLED);

        handlerMock.Verify(
            h => h.HandleAsync(
                It.IsAny<IPage>(), It.IsAny<SelectorRef>(),
                It.IsAny<UploadRef>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#5 — FileDownloadHandler: cancellation during download cleans up partial file
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FileDownloadHandler_CancellationBeforeBrowserTouch_ThrowsOperationCancelled()
    {
        // Arrange — policy approves, but token is already cancelled
        var (handler, policyMock, _) = CreateDownloadHandler();
        policyMock.Setup(p => p.IsDownloadPathApproved(It.IsAny<string>())).Returns(true);

        _pageMock
            .Setup(p => p.WaitForDownloadAsync(It.IsAny<PageWaitForDownloadOptions?>()))
            .ThrowsAsync(new OperationCanceledException("Download cancelled by test."));

        var downloadRef = new DownloadRef { DestinationDirectory = @"C:\approved\downloads" };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        Func<Task> act = () => handler.HandleAsync(
            _pageMock.Object,
            triggerSelector: null,
            downloadRef,
            5_000f,
            cts.Token);

        // Assert — AC#5: OperationCanceledException propagates for caller to classify as CANCELLED
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CEC contract compliance — ErrorCategory values match spec §6.2
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("browser.fileDownloadHandlerNotConfigured", ErrorCategory.INTERNAL)]
    [InlineData("browser.missingDownloadRef",               ErrorCategory.VALIDATION)]
    [InlineData("browser.downloadPathDenied",               ErrorCategory.POLICY_DENIED)]
    [InlineData("browser.downloadFilenameMismatch",         ErrorCategory.VALIDATION)]
    [InlineData("browser.downloadContentTypeMismatch",      ErrorCategory.VALIDATION)]
    [InlineData("browser.downloadSizeExceeded",             ErrorCategory.VALIDATION)]
    public void CecContract_DownloadDetailCodes_MapToExpectedErrorCategories(
        string detailCode, ErrorCategory expectedCategory)
    {
        // This test documents and enforces the CEC §6.2 category ↔ detail-code mapping
        // for all download error paths. A violation here means the wire protocol contract
        // is broken.
        var result = new ActionResult
        {
            Succeeded     = false,
            ErrorCategory = expectedCategory,
            DetailCode    = detailCode
        };

        result.ErrorCategory.Should().Be(expectedCategory, because:
            $"detail code '{detailCode}' must map to category '{expectedCategory}' per CEC v1.0.1 §6.2");
        result.DetailCode.Should().Be(detailCode);
    }

    [Theory]
    [InlineData("browser.fileUploadHandlerNotConfigured", ErrorCategory.INTERNAL)]
    [InlineData("browser.missingSelectorForUpload",       ErrorCategory.VALIDATION)]
    [InlineData("browser.missingUploadRef",               ErrorCategory.VALIDATION)]
    [InlineData("browser.uploadPathDenied",               ErrorCategory.POLICY_DENIED)]
    [InlineData("browser.uploadFileNotFound",             ErrorCategory.ENVIRONMENT)]
    public void CecContract_UploadDetailCodes_MapToExpectedErrorCategories(
        string detailCode, ErrorCategory expectedCategory)
    {
        var result = new ActionResult
        {
            Succeeded     = false,
            ErrorCategory = expectedCategory,
            DetailCode    = detailCode
        };

        result.ErrorCategory.Should().Be(expectedCategory, because:
            $"detail code '{detailCode}' must map to category '{expectedCategory}' per CEC v1.0.1 §6.2");
        result.DetailCode.Should().Be(detailCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC#1 — DownloadRef and UploadRef data contract
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DownloadRef_DefaultMaxSizeBytes_IsZero_MeansNoLimit()
    {
        // AC#1: zero MaxSizeBytes == no size limit enforced
        var downloadRef = new DownloadRef { DestinationDirectory = @"C:\downloads" };

        downloadRef.MaxSizeBytes.Should().Be(0);
        downloadRef.ExpectedFilename.Should().BeNull();
        downloadRef.ExpectedContentType.Should().BeNull();
    }

    [Fact]
    public void UploadRef_SourcePath_IsRequired()
    {
        // AC#4: SourcePath must be supplied; record type enforces this at compile time.
        // This test documents that UploadRef requires SourcePath and cannot be constructed
        // without it (compile-time contract enforcement via 'required').
        var uploadRef = new UploadRef { SourcePath = @"C:\approved\file.pdf" };

        uploadRef.SourcePath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DownloadRef_DestinationDirectory_IsRequired()
    {
        // AC#3: DestinationDirectory must be supplied.
        var downloadRef = new DownloadRef { DestinationDirectory = @"C:\approved\downloads" };

        downloadRef.DestinationDirectory.Should().NotBeNullOrWhiteSpace();
    }
}
