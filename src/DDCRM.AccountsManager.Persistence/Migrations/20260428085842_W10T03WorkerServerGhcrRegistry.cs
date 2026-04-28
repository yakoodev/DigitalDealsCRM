using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.AccountsManager.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W10T03WorkerServerGhcrRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RegistryEnabled",
                table: "worker_servers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RegistryHost",
                table: "worker_servers",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistryTokenEncrypted",
                table: "worker_servers",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RegistryTokenUpdatedAtUtc",
                table: "worker_servers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistryUsername",
                table: "worker_servers",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegistryEnabled",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "RegistryHost",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "RegistryTokenEncrypted",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "RegistryTokenUpdatedAtUtc",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "RegistryUsername",
                table: "worker_servers");
        }
    }
}
