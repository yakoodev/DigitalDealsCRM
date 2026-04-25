using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.AccountsManager.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkerServerControlPlaneV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_servers",
                columns: table => new
                {
                    ServerId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BaseUrlTemplate = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Health = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    CurrentLoad = table.Column<int>(type: "integer", nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_servers", x => x.ServerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_worker_servers_Status_Health",
                table: "worker_servers",
                columns: new[] { "Status", "Health" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_servers");
        }
    }
}
