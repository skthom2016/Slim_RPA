using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <inheritdoc />
[Migration("20261023000000_Initial")]
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RunRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkflowId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                State = table.Column<string>(type: "TEXT", nullable: false),
                WorkerId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                NeedsAttention = table.Column<bool>(type: "INTEGER", nullable: false),
                UnknownReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RunRecords", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WorkerRecords",
            columns: table => new
            {
                WorkerId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                RuntimeType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                State = table.Column<string>(type: "TEXT", nullable: false),
                RegisteredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                LastHeartbeatAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AbsenceThresholdSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                IsOnline = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkerRecords", x => x.WorkerId);
            });

        migrationBuilder.CreateTable(
            name: "JournalEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                AttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                EventType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Payload = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalEntries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RunRecords_State",
            table: "RunRecords",
            column: "State");

        // Composite index for RestartRecoveryService.FindByWorkerAndStateAsync(workerId, Running).
        // Without this index every recovery scan performs a full-table scan of Running rows
        // before filtering by WorkerId (M1 fix).
        migrationBuilder.CreateIndex(
            name: "IX_RunRecords_WorkerId_State",
            table: "RunRecords",
            columns: ["WorkerId", "State"]);

        migrationBuilder.CreateIndex(
            name: "IX_WorkerRecords_IsOnline",
            table: "WorkerRecords",
            column: "IsOnline");

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_RunId",
            table: "JournalEntries",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_OccurredAt",
            table: "JournalEntries",
            column: "OccurredAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_RunRecords_WorkerId_State", table: "RunRecords");
        migrationBuilder.DropTable(name: "JournalEntries");
        migrationBuilder.DropTable(name: "WorkerRecords");
        migrationBuilder.DropTable(name: "RunRecords");
    }
}
