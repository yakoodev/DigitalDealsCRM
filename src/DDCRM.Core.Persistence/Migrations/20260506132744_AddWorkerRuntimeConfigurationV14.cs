using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRuntimeConfigurationV14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationCiphertext",
                table: "project_integration_worker_runtimes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfigurationUpdatedAtUtc",
                table: "project_integration_worker_runtimes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfigurationCiphertext",
                table: "project_integration_worker_runtimes");

            migrationBuilder.DropColumn(
                name: "ConfigurationUpdatedAtUtc",
                table: "project_integration_worker_runtimes");
        }
    }
}
