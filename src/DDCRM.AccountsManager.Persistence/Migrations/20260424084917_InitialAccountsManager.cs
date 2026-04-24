using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.AccountsManager.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountsManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "worker_placements",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServerId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PodId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LifecycleStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProxyConfigured = table.Column<bool>(type: "boolean", nullable: false),
                    RouteVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_placements", x => x.AccountId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_Scope_Key",
                table: "idempotency_records",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_audits_AccountId_CreatedAtUtc",
                table: "lifecycle_audits",
                columns: new[] { "AccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_worker_placements_ProjectId",
                table: "worker_placements",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "lifecycle_audits");

            migrationBuilder.DropTable(
                name: "worker_placements");
        }
    }
}
