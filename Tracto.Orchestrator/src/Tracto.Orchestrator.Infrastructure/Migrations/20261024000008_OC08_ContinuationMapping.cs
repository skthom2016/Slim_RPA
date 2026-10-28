#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-08: Adds <c>ContinuationMappings</c> and <c>ContinuationRunRecords</c> tables.
///
/// ContinuationMappings columns:
///   MappingId              TEXT NOT NULL — primary key (Guid).
///   TriggerRunId           TEXT NULL     — source run; null means global/state-level mapping.
///   TriggerState           TEXT NOT NULL — terminal RunState that activates the mapping.
///   TargetArtifactId       TEXT NOT NULL — artifact for Run B; max 256 chars.
///   TargetArtifactVersion  TEXT NOT NULL — pinned version; max 128 chars.
///   InputTemplate          TEXT NULL     — optional JSON template; max 65536 chars.
///   CreatedAt              TEXT NOT NULL — UTC creation timestamp.
///   UpdatedAt              TEXT NULL     — UTC last-update timestamp.
///   Version                INTEGER NOT NULL — optimistic-concurrency counter.
///
/// ContinuationRunRecords columns:
///   Id                     TEXT NOT NULL — primary key (Guid).
///   SourceRunId            TEXT NOT NULL — the run (A) that triggered continuation.
///   TriggerState           TEXT NOT NULL — the terminal state that was matched.
///   RunBId                 TEXT NOT NULL — the continuation run (B) that was created.
///   MappingId              TEXT NOT NULL — the mapping that was used.
///   MappingVersionSnapshot INTEGER NOT NULL — mapping version at Run B creation time.
///   CreatedAt              TEXT NOT NULL — UTC timestamp.
///
/// Unique constraint on (SourceRunId, TriggerState) — the authoritative dedup guard
/// ensuring at most one Run B ever exists for a given (source run, trigger state) pair.
/// </summary>
public partial class OC08_ContinuationMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── ContinuationMappings ──────────────────────────────────────────────

        migrationBuilder.CreateTable(
            name: "ContinuationMappings",
            columns: table => new
            {
                MappingId             = table.Column<Guid>(type: "TEXT", nullable: false),
                TriggerRunId          = table.Column<Guid>(type: "TEXT", nullable: true),
                TriggerState          = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                TargetArtifactId      = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                TargetArtifactVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                InputTemplate         = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                CreatedAt             = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt             = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Version               = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContinuationMappings", x => x.MappingId);
            });

        // Index: specific lookup for (run, state) — used by FindBestMatchAsync step 1.
        migrationBuilder.CreateIndex(
            name: "IX_ContinuationMappings_TriggerRunId_TriggerState",
            table: "ContinuationMappings",
            columns: ["TriggerRunId", "TriggerState"]);

        // Index: global (state-only) fallback — used by FindBestMatchAsync step 2.
        migrationBuilder.CreateIndex(
            name: "IX_ContinuationMappings_TriggerState",
            table: "ContinuationMappings",
            column: "TriggerState");

        // ── ContinuationRunRecords ────────────────────────────────────────────

        migrationBuilder.CreateTable(
            name: "ContinuationRunRecords",
            columns: table => new
            {
                Id                      = table.Column<Guid>(type: "TEXT", nullable: false),
                SourceRunId             = table.Column<Guid>(type: "TEXT", nullable: false),
                TriggerState            = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                RunBId                  = table.Column<Guid>(type: "TEXT", nullable: false),
                MappingId               = table.Column<Guid>(type: "TEXT", nullable: false),
                MappingVersionSnapshot  = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt               = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContinuationRunRecords", x => x.Id);
            });

        // THE dedup guard: unique constraint on (SourceRunId, TriggerState).
        migrationBuilder.CreateIndex(
            name: "IX_ContinuationRunRecords_SourceRunId_TriggerState",
            table: "ContinuationRunRecords",
            columns: ["SourceRunId", "TriggerState"],
            unique: true);

        // Secondary index: find run records by SourceRunId alone.
        migrationBuilder.CreateIndex(
            name: "IX_ContinuationRunRecords_SourceRunId",
            table: "ContinuationRunRecords",
            column: "SourceRunId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ContinuationRunRecords");
        migrationBuilder.DropTable(name: "ContinuationMappings");
    }
}
