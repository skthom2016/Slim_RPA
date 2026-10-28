using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <inheritdoc />
[Migration("20261024000001_WorkerCapabilities")]
public partial class WorkerCapabilities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // OC-01: add Capabilities (JSON text) and LastRegisteredAt to WorkerRecords.
        // Both columns are nullable to preserve backward-compatibility with rows created
        // before this migration was applied.

        migrationBuilder.AddColumn<string>(
            name:     "CapabilitiesJson",
            table:    "WorkerRecords",
            type:     "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name:     "LastRegisteredAt",
            table:    "WorkerRecords",
            type:     "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CapabilitiesJson",   table: "WorkerRecords");
        migrationBuilder.DropColumn(name: "LastRegisteredAt",   table: "WorkerRecords");
    }
}
