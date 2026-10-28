#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-12: Adds the <c>ResolutionRecords</c> table for UNKNOWN run operator resolutions.
///
/// Table columns:
///   Id            TEXT NOT NULL — unique primary key (Guid, never auto-generated).
///   RunId         TEXT NOT NULL — references the UNKNOWN RunRecord being resolved.
///   Type          TEXT NOT NULL — "InvestigatedNoRerun" | "ApprovedNewRun".
///   Justification TEXT NOT NULL — mandatory operator-supplied rationale (max 4096 chars).
///   OperatorId    TEXT NOT NULL — identity of the resolving operator (max 256 chars).
///   ResolvedAt    TEXT NOT NULL — UTC timestamp when the resolution was recorded.
///   NewRunId      TEXT NULL     — Id of the newly created RunRecord for ApprovedNewRun; null otherwise.
///
/// Indexes:
///   IX_ResolutionRecords_RunId — fast lookup of all resolutions for a given run.
///
/// Invariants preserved by schema:
///   - The RunRecord referenced by RunId is NEVER updated (state stays UNKNOWN).
///   - NewRunId null⟺Type=InvestigatedNoRerun; non-null⟺Type=ApprovedNewRun
///     (enforced by Application layer, not a DB constraint, to avoid FK complexities).
///   - Records are append-only; no UPDATE or DELETE paths exist in the application.
/// </summary>
public partial class OC12_ResolutionRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ResolutionRecords",
            columns: table => new
            {
                Id            = table.Column<Guid>(type: "TEXT", nullable: false),
                RunId         = table.Column<Guid>(type: "TEXT", nullable: false),
                Type          = table.Column<string>(type: "TEXT", nullable: false),
                Justification = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                OperatorId    = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ResolvedAt    = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                NewRunId      = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ResolutionRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ResolutionRecords_RunId",
            table: "ResolutionRecords",
            column: "RunId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ResolutionRecords");
    }
}
