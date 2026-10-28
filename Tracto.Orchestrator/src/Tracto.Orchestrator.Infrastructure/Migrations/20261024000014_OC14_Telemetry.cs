using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <inheritdoc />
public partial class OC14_Telemetry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TelemetryEvents",
            columns: table => new
            {
                Id         = table.Column<Guid>(type: "TEXT", nullable: false),
                RunId      = table.Column<Guid>(type: "TEXT", nullable: false),
                WorkerId   = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                LeaseId    = table.Column<Guid>(type: "TEXT", nullable: true),
                AttemptId  = table.Column<Guid>(type: "TEXT", nullable: true),
                EventType  = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Metadata   = table.Column<string>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TelemetryEvents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TelemetryEvents_RunId_OccurredAt",
            table: "TelemetryEvents",
            columns: new[] { "RunId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_TelemetryEvents_EventType",
            table: "TelemetryEvents",
            column: "EventType");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TelemetryEvents");
    }
}
