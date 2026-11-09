using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMeal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P7_RecommendationSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_synced_at",
                schema: "app",
                table: "recommendation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "locally_modified",
                schema: "app",
                table: "recommendation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "remote_hash",
                schema: "app",
                table: "recommendation",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                schema: "app",
                table: "recommendation",
                type: "integer",
                nullable: false,
                defaultValue: 1); // DataSource.Manual for existing rows
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_synced_at",
                schema: "app",
                table: "recommendation");

            migrationBuilder.DropColumn(
                name: "locally_modified",
                schema: "app",
                table: "recommendation");

            migrationBuilder.DropColumn(
                name: "remote_hash",
                schema: "app",
                table: "recommendation");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "app",
                table: "recommendation");
        }
    }
}
