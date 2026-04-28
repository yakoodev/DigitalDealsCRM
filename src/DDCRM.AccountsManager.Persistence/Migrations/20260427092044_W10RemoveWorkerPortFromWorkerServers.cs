using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.AccountsManager.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class W10RemoveWorkerPortFromWorkerServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkerPort",
                table: "worker_servers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkerPort",
                table: "worker_servers",
                type: "integer",
                nullable: false,
                defaultValue: 8080);
        }
    }
}
