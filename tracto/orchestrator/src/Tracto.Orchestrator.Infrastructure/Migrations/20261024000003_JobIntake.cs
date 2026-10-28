#nullable disable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-02: Adds Job Intake columns to RunRecords and creates the ScheduleRecords table.
/// </summary>
public partial class JobIntake : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── New columns on RunRecords ─────────────────────────────────────────
        migrationBuilder.AddColumn<string>(
            name: "BusinessJobId",
            table: "RunRecords",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CorrelationId",
            table: "RunRecords",
            type: "TEXT",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<DateTime>(
            name: "Deadline",
            table: "RunRecords",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EventId",
            table: "RunRecords",
            type: "TEXT",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InputRefsJson",
            table: "RunRecords",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SchemaVersion",
            table: "RunRecords",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScriptArtifactId",
            table: "RunRecords",
            type: "TEXT",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScriptVersion",
            table: "RunRecords",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SecretRefsJson",
            table: "RunRecords",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TriggerType",
            table: "RunRecords",
            type: "TEXT",
            nullable: false,
            defaultValue: "Manual");

        // Index for EventId dedup queries.
        migrationBuilder.CreateIndex(
            name: "IX_RunRecords_EventId_CreatedAt",
            table: "RunRecords",
            columns: ["EventId", "CreatedAt"]);

        // ── ScheduleRecords table ─────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "ScheduleRecords",
            columns: table => new
            {
                ScheduleId    = table.Column<Guid>(type: "TEXT", nullable: false),
                CronExpression = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ArtifactId    = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Version       = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Enabled       = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt     = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt     = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScheduleRecords", x => x.ScheduleId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ScheduleRecords_Enabled",
            table: "ScheduleRecords",
            column: "Enabled");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ScheduleRecords");

        migrationBuilder.DropIndex(
            name: "IX_RunRecords_EventId_CreatedAt",
            table: "RunRecords");

        migrationBuilder.DropColumn(name: "BusinessJobId",    table: "RunRecords");
        migrationBuilder.DropColumn(name: "CorrelationId",    table: "RunRecords");
        migrationBuilder.DropColumn(name: "Deadline",         table: "RunRecords");
        migrationBuilder.DropColumn(name: "EventId",          table: "RunRecords");
        migrationBuilder.DropColumn(name: "InputRefsJson",    table: "RunRecords");
        migrationBuilder.DropColumn(name: "SchemaVersion",    table: "RunRecords");
        migrationBuilder.DropColumn(name: "ScriptArtifactId", table: "RunRecords");
        migrationBuilder.DropColumn(name: "ScriptVersion",    table: "RunRecords");
        migrationBuilder.DropColumn(name: "SecretRefsJson",   table: "RunRecords");
        migrationBuilder.DropColumn(name: "TriggerType",      table: "RunRecords");
    }
}
