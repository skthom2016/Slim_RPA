#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tracto.Orchestrator.Infrastructure.Migrations;

/// <summary>
/// OC-05: Adds heartbeat-processing columns to <c>LeaseRecords</c>.
///
/// New columns:
///   LastHeartbeatAt      TEXT NULL        — UTC timestamp of last accepted heartbeat.
///   LastSeqNo            INTEGER NOT NULL — Monotonic sequence number; default 0.
///   CancellationRequested INTEGER NOT NULL — Bool flag; default 0 (false).
///
/// New index:
///   IX_LeaseRecords_State_LastHeartbeatAt — fast scan by <c>LeaseExpiryMonitorService</c>.
/// </summary>
public partial class OC05_Heartbeat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name:       "LastHeartbeatAt",
            table:      "LeaseRecords",
            type:       "TEXT",
            nullable:   true,
            defaultValue: null);

        migrationBuilder.AddColumn<long>(
            name:         "LastSeqNo",
            table:        "LeaseRecords",
            type:         "INTEGER",
            nullable:     false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<bool>(
            name:         "CancellationRequested",
            table:        "LeaseRecords",
            type:         "INTEGER",
            nullable:     false,
            defaultValue: false);

        // Composite index used by LeaseExpiryMonitorService:
        //   WHERE State = 'Accepted' AND (LastHeartbeatAt IS NULL OR LastHeartbeatAt < ?)
        migrationBuilder.CreateIndex(
            name:    "IX_LeaseRecords_State_LastHeartbeatAt",
            table:   "LeaseRecords",
            columns: new[] { "State", "LastHeartbeatAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name:  "IX_LeaseRecords_State_LastHeartbeatAt",
            table: "LeaseRecords");

        migrationBuilder.DropColumn(
            name:  "CancellationRequested",
            table: "LeaseRecords");

        migrationBuilder.DropColumn(
            name:  "LastSeqNo",
            table: "LeaseRecords");

        migrationBuilder.DropColumn(
            name:  "LastHeartbeatAt",
            table: "LeaseRecords");
    }
}
