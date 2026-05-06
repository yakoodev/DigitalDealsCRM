using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationInstancesV15 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationKey",
                table: "project_integration_worker_runtimes");

            migrationBuilder.DropIndex(
                name: "IX_integration_worker_runtime_outbox_ProjectId_IntegrationKey_~",
                table: "integration_worker_runtime_outbox");

            migrationBuilder.AddColumn<string>(
                name: "InstanceDisplayName",
                table: "project_integration_worker_runtimes",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "project_integration_worker_runtimes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxInstances",
                table: "project_integration_grants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationK~1",
                table: "project_integration_worker_runtimes",
                columns: new[] { "ProjectId", "IntegrationKey", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationKe~",
                table: "project_integration_worker_runtimes",
                columns: new[] { "ProjectId", "IntegrationKey", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_worker_runtime_outbox_ProjectId_IntegrationKey_~",
                table: "integration_worker_runtime_outbox",
                columns: new[] { "ProjectId", "IntegrationKey", "RuntimeAccountId", "Operation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationK~1",
                table: "project_integration_worker_runtimes");

            migrationBuilder.DropIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationKe~",
                table: "project_integration_worker_runtimes");

            migrationBuilder.DropIndex(
                name: "IX_integration_worker_runtime_outbox_ProjectId_IntegrationKey_~",
                table: "integration_worker_runtime_outbox");

            migrationBuilder.DropColumn(
                name: "InstanceDisplayName",
                table: "project_integration_worker_runtimes");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "project_integration_worker_runtimes");

            migrationBuilder.DropColumn(
                name: "MaxInstances",
                table: "project_integration_grants");

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_worker_runtimes_ProjectId_IntegrationKey",
                table: "project_integration_worker_runtimes",
                columns: new[] { "ProjectId", "IntegrationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_worker_runtime_outbox_ProjectId_IntegrationKey_~",
                table: "integration_worker_runtime_outbox",
                columns: new[] { "ProjectId", "IntegrationKey", "Operation" });
        }
    }
}
