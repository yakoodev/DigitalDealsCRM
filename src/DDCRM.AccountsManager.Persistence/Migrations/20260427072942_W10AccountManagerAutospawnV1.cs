using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.AccountsManager.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W10AccountManagerAutospawnV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DockerHost",
                table: "worker_servers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DockerNetwork",
                table: "worker_servers",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkerPort",
                table: "worker_servers",
                type: "integer",
                nullable: false,
                defaultValue: 8080);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeConfigJson",
                table: "account_types",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DockerHost",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "DockerNetwork",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "WorkerPort",
                table: "worker_servers");

            migrationBuilder.DropColumn(
                name: "RuntimeConfigJson",
                table: "account_types");
        }
    }
}
