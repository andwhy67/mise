using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemModifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "modifications",
                table: "order_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "removable",
                table: "dish_ingredients",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "modifications",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "removable",
                table: "dish_ingredients");
        }
    }
}
