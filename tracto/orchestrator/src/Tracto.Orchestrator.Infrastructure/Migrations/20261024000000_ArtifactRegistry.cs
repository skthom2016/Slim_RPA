using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ArtifactRegistry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ArtifactRecords",
            columns: table => new
            {
                ArtifactId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                StoredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                SignatureValid = table.Column<bool>(type: "INTEGER", nullable: false),
                SelectorContent = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArtifactRecords", x => new { x.ArtifactId, x.Version });
            });

        migrationBuilder.CreateTable(
            name: "ArtifactContents",
            columns: table => new
            {
                ArtifactId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Payload = table.Column<byte[]>(type: "BLOB", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArtifactContents", x => new { x.ArtifactId, x.Version });
            });

        migrationBuilder.CreateIndex(
            name: "IX_ArtifactRecords_ArtifactId_IsDeleted",
            table: "ArtifactRecords",
            columns: ["ArtifactId", "IsDeleted"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ArtifactRecords_ArtifactId_IsDeleted",
            table: "ArtifactRecords");

        migrationBuilder.DropTable(name: "ArtifactContents");
        migrationBuilder.DropTable(name: "ArtifactRecords");
    }
}
