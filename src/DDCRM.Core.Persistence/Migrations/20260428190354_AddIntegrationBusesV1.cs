using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DDCRM.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationBusesV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_outbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_outbox_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_integration_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopesCsv = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_integration_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_integration_grants_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_service_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ScopesCsv = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecretCiphertext = table.Column<string>(type: "text", nullable: false),
                    SecretHashSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecretMasked = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_service_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_service_credentials_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_credential_sync_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
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
                    table.PrimaryKey("PK_service_credential_sync_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "telegram_chat_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BindingType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChatTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LinkedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_chat_bindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_chat_bindings_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BindingType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_link_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_telegram_link_codes_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_proxy_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProxyType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    LoginCiphertext = table.Column<string>(type: "text", nullable: true),
                    PasswordCiphertext = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_proxy_profiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_outbox_DeduplicationKey",
                table: "notification_outbox",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_outbox_ProjectId",
                table: "notification_outbox",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_outbox_Status_NextAttemptAtUtc",
                table: "notification_outbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_grants_ProjectId_IntegrationKey",
                table: "project_integration_grants",
                columns: new[] { "ProjectId", "IntegrationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_service_credentials_ProjectId_IntegrationKey",
                table: "project_service_credentials",
                columns: new[] { "ProjectId", "IntegrationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_credential_sync_outbox_ProjectId_IntegrationKey_Ope~",
                table: "service_credential_sync_outbox",
                columns: new[] { "ProjectId", "IntegrationKey", "Operation" });

            migrationBuilder.CreateIndex(
                name: "IX_service_credential_sync_outbox_Status_NextAttemptAtUtc",
                table: "service_credential_sync_outbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_chat_bindings_ProjectId_BindingType_ChatId",
                table: "telegram_chat_bindings",
                columns: new[] { "ProjectId", "BindingType", "ChatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_chat_bindings_ProjectId_UserId_BindingType",
                table: "telegram_chat_bindings",
                columns: new[] { "ProjectId", "UserId", "BindingType" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_Code",
                table: "telegram_link_codes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_link_codes_ProjectId_ExpiresAtUtc",
                table: "telegram_link_codes",
                columns: new[] { "ProjectId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_proxy_profiles_IsActive",
                table: "telegram_proxy_profiles",
                column: "IsActive",
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_proxy_profiles_Name",
                table: "telegram_proxy_profiles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_outbox");

            migrationBuilder.DropTable(
                name: "project_integration_grants");

            migrationBuilder.DropTable(
                name: "project_service_credentials");

            migrationBuilder.DropTable(
                name: "service_credential_sync_outbox");

            migrationBuilder.DropTable(
                name: "telegram_chat_bindings");

            migrationBuilder.DropTable(
                name: "telegram_link_codes");

            migrationBuilder.DropTable(
                name: "telegram_proxy_profiles");
        }
    }
}
