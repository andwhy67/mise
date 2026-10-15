using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuestMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "guest_ordering",
                table: "restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "guest_tables",
                table: "restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "menu_key",
                table: "restaurants",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guest_token",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment",
                table: "orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Все заказы до QR-меню оформлены сотрудниками.
                defaultValue: "Staff");

            migrationBuilder.AddColumn<int>(
                name: "table_number",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_menu_key",
                table: "restaurants",
                column: "menu_key",
                unique: true,
                filter: "menu_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_guest_token",
                table: "orders",
                column: "guest_token",
                unique: true,
                filter: "guest_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_restaurants_menu_key",
                table: "restaurants");

            migrationBuilder.DropIndex(
                name: "ix_orders_guest_token",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "guest_ordering",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "guest_tables",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "menu_key",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "guest_token",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "table_number",
                table: "orders");
        }
    }
}
