using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <inheritdoc />
public partial class OC17_SecurityEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SecurityEvents",
            columns: table => new
            {
                Id        = table.Column<Guid>(type: "TEXT", nullable: false),
                EventType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                SourceIp  = table.Column<string>(type: "TEXT", maxLength: 64,  nullable: true),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Details   = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SecurityEvents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SecurityEvents_OccurredAt",
            table: "SecurityEvents",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_SecurityEvents_EventType",
            table: "SecurityEvents",
            column: "EventType");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SecurityEvents");
    }
}
