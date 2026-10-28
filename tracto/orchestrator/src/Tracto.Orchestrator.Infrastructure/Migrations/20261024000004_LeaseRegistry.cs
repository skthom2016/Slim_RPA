#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-03: Creates the LeaseRecords table used by the PullJob / capability-matching subsystem.
/// </summary>
public partial class LeaseRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LeaseRecords",
            columns: table => new
            {
                LeaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkerId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ArtifactId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                ArtifactVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ArtifactHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                OfferedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AcceptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                State = table.Column<string>(type: "TEXT", nullable: false),
                InteractiveSessionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LeaseRecords", x => x.LeaseId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LeaseRecords_RunId_State",
            table: "LeaseRecords",
            columns: new[] { "RunId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_LeaseRecords_WorkerId_State",
            table: "LeaseRecords",
            columns: new[] { "WorkerId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_LeaseRecords_InteractiveSessionId",
            table: "LeaseRecords",
            column: "InteractiveSessionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LeaseRecords");
    }
}
