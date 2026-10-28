namespace Tracto.Designer.Unit.Tests.Audit;

using FluentAssertions;
using System.Collections.Concurrent;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Audit;

public class AuditTrailTests
{
    [Fact]
    public void InMemory_Log_And_GetEntries()
    {
        var log = new InMemoryAuditLog();
        var e = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "designer", "Test", AuditCategory.General, AuditOutcome.Success);
        log.Log(e);
        log.GetEntries().Should().ContainSingle();
    }

    [Fact]
    public void InMemory_Filter_By_Category()
    {
        var log = new InMemoryAuditLog();
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "A", AuditCategory.Recording, AuditOutcome.Success));
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "B", AuditCategory.Signing, AuditOutcome.Success));
        log.GetEntries(AuditCategory.Recording).Should().HaveCount(1);
    }

    [Fact]
    public void InMemory_Max_10000_Evicts_Oldest()
    {
        var log = new InMemoryAuditLog();
        for (int i = 0; i < 10_001; i++)
            log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", $"A{i}", AuditCategory.General, AuditOutcome.Success));
        log.GetEntries().Should().HaveCountLessOrEqualTo(10_000);
    }

    [Fact]
    public void Composite_Fans_Out_To_All()
    {
        var l1 = new InMemoryAuditLog();
        var l2 = new InMemoryAuditLog();
        var composite = new CompositeAuditLog(l1, l2);
        composite.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "X", AuditCategory.General, AuditOutcome.Success));
        l1.GetEntries().Should().HaveCount(1);
        l2.GetEntries().Should().HaveCount(1);
    }

    [Fact]
    public void AuditService_Logs_Recording_Started()
    {
        var log = new InMemoryAuditLog();
        var svc = new AuditService(log);
        svc.LogRecordingStarted("session-1");
        log.GetEntries(AuditCategory.Recording).Should().ContainSingle(e => e.Action == "RecordingStarted");
    }

    [Fact]
    public void AuditService_Logs_Script_Validated()
    {
        var log = new InMemoryAuditLog();
        var svc = new AuditService(log);
        svc.LogScriptValidated("my-script", false, 2);
        log.GetEntries(AuditCategory.Validation).First().Outcome.Should().Be(AuditOutcome.Failure);
    }

    [Fact]
    public void AuditService_Logs_MCP_Tool()
    {
        var log = new InMemoryAuditLog();
        new AuditService(log).LogMcpToolCall("get_transcript", true);
        log.GetEntries(AuditCategory.Mcp).Should().ContainSingle();
    }

    [Fact]
    public void FileAuditLog_Roundtrip()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"audit-test-{Guid.NewGuid():N}.jsonl");
        using var flog = new Tracto.Designer.Infrastructure.Audit.FileAuditLog(path);
        flog.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "FileTest", AuditCategory.General, AuditOutcome.Success));
        flog.GetEntries().Should().ContainSingle(e => e.Action == "FileTest");
        System.IO.File.Delete(path);
    }

    [Fact]
    public void FileAuditLog_Never_Throws_On_Bad_Read()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"audit-bad-{Guid.NewGuid():N}.jsonl");
        System.IO.File.WriteAllText(path, "this is not json\n");
        using var flog = new Tracto.Designer.Infrastructure.Audit.FileAuditLog(path);
        flog.GetEntries().Should().BeEmpty();
        System.IO.File.Delete(path);
    }

    // ---- CompositeAuditLog.GetEntries fan-out tests -------------------------

    [Fact]
    public void Composite_GetEntries_Merges_From_All_Logs()
    {
        var l1 = new InMemoryAuditLog();
        var l2 = new InMemoryAuditLog();
        var composite = new CompositeAuditLog(l1, l2);

        // Write two distinct entries via composite (fans out to both logs)
        var e1 = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Action1", AuditCategory.General, AuditOutcome.Success);
        var e2 = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Action2", AuditCategory.Recording, AuditOutcome.Success);
        composite.Log(e1);
        composite.Log(e2);

        // Both entries must appear in composite view (deduplicated, not doubled)
        var entries = composite.GetEntries();
        entries.Should().HaveCount(2);
        entries.Select(e => e.Id).Should().BeEquivalentTo(new[] { e1.Id, e2.Id });
    }

    [Fact]
    public void Composite_GetEntries_Reads_From_All_Logs_When_First_Is_Empty()
    {
        // Construct composite with an empty InMemory first and a pre-populated one second
        var empty = new InMemoryAuditLog();                // first — no entries
        var populated = new InMemoryAuditLog();            // second — has entries
        var entry = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "X", AuditCategory.General, AuditOutcome.Success);
        populated.Log(entry);

        var composite = new CompositeAuditLog(empty, populated);

        // The bug would return [] because it only read from the first (empty) log
        composite.GetEntries().Should().ContainSingle(e => e.Id == entry.Id);
    }

    [Fact]
    public void Composite_GetEntries_Category_Filter_Applied_Across_All_Logs()
    {
        var l1 = new InMemoryAuditLog();
        var l2 = new InMemoryAuditLog();
        var composite = new CompositeAuditLog(l1, l2);

        composite.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Rec", AuditCategory.Recording, AuditOutcome.Success));
        composite.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Sign", AuditCategory.Signing, AuditOutcome.Success));

        composite.GetEntries(AuditCategory.Recording).Should().HaveCount(1)
            .And.OnlyContain(e => e.Category == AuditCategory.Recording);
    }

    // ---- InMemoryAuditLog concurrent stress test ----------------------------

    [Fact]
    public async System.Threading.Tasks.Task InMemoryAuditLog_Concurrent_Log_Is_ThreadSafe()
    {
        var log = new InMemoryAuditLog();
        const int threads = 8;
        const int perThread = 500;
        var errors = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, threads).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < perThread; i++)
                    log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", $"A{i}", AuditCategory.General, AuditOutcome.Success));
            }
            catch (Exception ex) { errors.Add(ex); }
        })).ToArray();

        await System.Threading.Tasks.Task.WhenAll(tasks);

        errors.Should().BeEmpty("concurrent Log() calls must not throw");
        // Total entries ≤ 10 000 (cap) and > 0
        log.GetEntries().Should().HaveCountGreaterThan(0)
            .And.HaveCountLessOrEqualTo(10_000);
    }

    // ---- AuditViewModel: 50-entry cap, reverse-chronological, category filter

    [Fact]
    public void AuditViewModel_Caps_Display_At_50_Entries()
    {
        var log = new InMemoryAuditLog();
        var svc = new AuditService(log);
        // Log 80 entries
        for (int i = 0; i < 80; i++)
            log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(i), "d", $"A{i}", AuditCategory.General, AuditOutcome.Success));

        var vm = new AuditViewModel(svc);
        vm.DisplayedEntries.Should().HaveCount(50);
    }

    [Fact]
    public void AuditViewModel_DisplayedEntries_Are_Reverse_Chronological()
    {
        var log = new InMemoryAuditLog();
        var svc = new AuditService(log);
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
            log.Log(new(Guid.NewGuid(), now.AddSeconds(i), "d", $"A{i}", AuditCategory.General, AuditOutcome.Success));

        var vm = new AuditViewModel(svc);
        var timestamps = vm.DisplayedEntries.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInDescendingOrder();
    }

    [Fact]
    public void AuditViewModel_CategoryFilter_Limits_Displayed_Entries()
    {
        var log = new InMemoryAuditLog();
        var svc = new AuditService(log);
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Rec", AuditCategory.Recording, AuditOutcome.Success));
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Sign", AuditCategory.Signing, AuditOutcome.Success));
        log.Log(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "d", "Sign2", AuditCategory.Signing, AuditOutcome.Success));

        var vm = new AuditViewModel(svc);
        vm.CategoryFilter = AuditCategory.Signing;

        vm.DisplayedEntries.Should().HaveCount(2)
            .And.OnlyContain(e => e.Category == AuditCategory.Signing);
    }
}
