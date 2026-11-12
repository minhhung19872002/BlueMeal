using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMeal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyConsent_InactiveAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reactivated_at",
                schema: "app",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "privacy_consent",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notice_version = table.Column<int>(type: "integer", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_privacy_consent", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_privacy_consent_user_id_notice_version",
                schema: "app",
                table: "privacy_consent",
                columns: new[] { "user_id", "notice_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "privacy_consent",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "reactivated_at",
                schema: "app",
                table: "app_user");
        }
    }
}
