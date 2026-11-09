using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMeal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4_SessionRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rotated_at",
                schema: "app",
                table: "user_session",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rotated_at",
                schema: "app",
                table: "user_session");
        }
    }
}
