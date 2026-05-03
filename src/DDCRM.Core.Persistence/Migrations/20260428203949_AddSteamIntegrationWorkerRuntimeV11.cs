using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamIntegrationWorkerRuntimeV11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_worker_runtime_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RuntimeAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_worker_runtime_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project_integration_worker_runtimes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RuntimeAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ProvisionedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeprovisionedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_integration_worker_runtimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_integration_worker_runtimes_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_worker_runtime_outbox_ProjectId_IntegrationKey_~",
                table: "integration_worker_runtime_outbox",
                columns: new[] { "ProjectId", "IntegrationKey", "Operation" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_worker_runtime_outbox_Status_NextAttemptAtUtc",
                table: "integration_worker_runtime_outbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationKey",
                table: "project_integration_worker_runtimes",
                columns: new[] { "ProjectId", "IntegrationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_worker_runtimes_RuntimeAccountId",
                table: "project_integration_worker_runtimes",
                column: "RuntimeAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_worker_runtime_outbox");

            migrationBuilder.DropTable(
                name: "project_integration_worker_runtimes");
        }
    }
}
