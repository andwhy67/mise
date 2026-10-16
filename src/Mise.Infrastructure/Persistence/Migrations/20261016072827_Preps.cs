using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Preps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "prep_yield",
                table: "products",
                type: "numeric(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_prep_ingredients",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_prep_ingredients", x => new { x.product_id, x.ingredient_id });
                    table.CheckConstraint("ck_product_prep_ingredients_not_self", "product_id <> ingredient_id");
                    table.CheckConstraint("ck_product_prep_ingredients_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_product_prep_ingredients_products_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_prep_ingredients_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_prep_ingredients_ingredient_id",
                table: "product_prep_ingredients",
                column: "ingredient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_prep_ingredients");

            migrationBuilder.DropColumn(
                name: "prep_yield",
                table: "products");
        }
    }
}
