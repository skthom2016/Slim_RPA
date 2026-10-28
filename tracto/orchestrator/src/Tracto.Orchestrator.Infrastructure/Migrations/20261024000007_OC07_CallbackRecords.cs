#nullable enable
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-07: Adds the <c>CallbackRecords</c> table for async callback intake and replay protection.
///
/// Columns:
///   Id              TEXT NOT NULL  — surrogate primary key (Guid).
///   EventId         TEXT NOT NULL  — caller-supplied deduplication key; unique.
///   CorrelationId   TEXT NOT NULL  — maps to the originating RunRecord.
///   KeyId           TEXT NOT NULL  — HMAC signing key identifier; key material is never stored.
///   ReceivedAt      TEXT NOT NULL  — UTC instant the callback arrived at the API boundary.
///   PayloadJson     TEXT NOT NULL  — raw opaque JSON payload; forwarded to OC-08.
///   Processed       INTEGER NOT NULL — false until the continuation enqueue commits; then true.
///
/// Indexes:
///   IX_CallbackRecords_EventId (UNIQUE)          — O(1) duplicate-event lookup on every ingestion.
///   IX_CallbackRecords_CorrelationId_Processed   — lookup for pending continuation work.
/// </summary>
public partial class OC07_CallbackRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CallbackRecords",
            columns: table => new
            {
                Id            = table.Column<Guid>(type: "TEXT", nullable: false),
                EventId       = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                CorrelationId = table.Column<Guid>(type: "TEXT", nullable: false),
                KeyId         = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ReceivedAt    = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                PayloadJson   = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                Processed     = table.Column<bool>(type: "INTEGER", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CallbackRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CallbackRecords_EventId",
            table: "CallbackRecords",
            column: "EventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CallbackRecords_CorrelationId_Processed",
            table: "CallbackRecords",
            columns: ["CorrelationId", "Processed"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CallbackRecords");
    }
}
