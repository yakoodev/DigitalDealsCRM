using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowMessageCursorsV13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_message_cursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LastSeenMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_message_cursors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_message_cursors_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_message_cursors_ProjectId_AccountId_ConversationId",
                table: "workflow_message_cursors",
                columns: new[] { "ProjectId", "AccountId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_message_cursors_ProjectId_UpdatedAtUtc",
                table: "workflow_message_cursors",
                columns: new[] { "ProjectId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_message_cursors");
        }
    }
}
