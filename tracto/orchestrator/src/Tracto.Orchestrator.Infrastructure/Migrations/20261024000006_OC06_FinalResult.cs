#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-06: Adds the <c>FinalResultRecords</c> table.
///
/// New table columns:
///   Id              TEXT NOT NULL  — unique primary key (Guid).
///   RunId           TEXT NOT NULL  — foreign key to RunRecords.Id; unique (one result per run).
///   AttemptId       TEXT NOT NULL  — AttemptId fence that was active when the result was committed.
///   TerminalState   TEXT NOT NULL  — SUCCEEDED | FAILED | HANDED_OFF | CANCELLED | TIMED_OUT | UNKNOWN.
///   NeedsAttention  INTEGER NOT NULL — true when TerminalState == UNKNOWN (invariant 2).
///   UnknownReason   TEXT NULL      — required when NeedsAttention; max 2048 chars.
///   ErrorCategory   TEXT NULL      — opaque error category; max 256 chars.
///   ErrorMessage    TEXT NULL      — human-readable detail; max 4096 chars.
///   BusinessOutcome TEXT NULL      — caller-supplied outcome label; max 512 chars.
///   EvidenceRefsJson TEXT NOT NULL — JSON array of evidence artifact references.
///   BusinessJobId   TEXT NULL      — caller-supplied business job identifier; max 256 chars.
///   CompletedAt     TEXT NOT NULL  — UTC timestamp when the result was committed.
///
/// New indexes:
///   IX_FinalResultRecords_RunId (UNIQUE) — O(1) lookup and enforces one-result-per-run constraint.
/// </summary>
public partial class OC06_FinalResult : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FinalResultRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                TerminalState = table.Column<string>(type: "TEXT", nullable: false),
                NeedsAttention = table.Column<bool>(type: "INTEGER", nullable: false),
                UnknownReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                ErrorCategory = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                BusinessOutcome = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                EvidenceRefsJson = table.Column<string>(type: "TEXT", nullable: false),
                BusinessJobId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FinalResultRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FinalResultRecords_RunId",
            table: "FinalResultRecords",
            column: "RunId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FinalResultRecords");
    }
}
