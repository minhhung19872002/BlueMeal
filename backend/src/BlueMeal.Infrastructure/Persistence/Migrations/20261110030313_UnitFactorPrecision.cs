using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMeal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitFactorPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "factor_to_base",
                schema: "app",
                table: "unit_of_measure",
                type: "numeric(24,12)",
                precision: 24,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            // Seeded factors that numeric(18,4) had rounded (µg → 0, kJ → 0.2390); untouched rows only.
            migrationBuilder.Sql("UPDATE app.unit_of_measure SET factor_to_base = 0.000001 WHERE code = 'UG' AND factor_to_base = 0;");
            migrationBuilder.Sql("UPDATE app.unit_of_measure SET factor_to_base = 0.239006 WHERE code = 'KJ' AND factor_to_base = 0.2390;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "factor_to_base",
                schema: "app",
                table: "unit_of_measure",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(24,12)",
                oldPrecision: 24,
                oldScale: 12);
        }
    }
}
