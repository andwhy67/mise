using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Substitutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_substitutions",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    substitute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ratio = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_substitutions", x => new { x.order_id, x.product_id });
                    table.CheckConstraint("ck_order_substitutions_ratio_positive", "ratio > 0");
                    table.ForeignKey(
                        name: "fk_order_substitutions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_substitutions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_substitutions_products_substitute_id",
                        column: x => x.substitute_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_substitutes",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    substitute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ratio = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_substitutes", x => new { x.product_id, x.substitute_id });
                    table.CheckConstraint("ck_product_substitutes_not_self", "product_id <> substitute_id");
                    table.CheckConstraint("ck_product_substitutes_ratio_positive", "ratio > 0");
                    table.ForeignKey(
                        name: "fk_product_substitutes_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_substitutes_products_substitute_id",
                        column: x => x.substitute_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_substitutions_product_id",
                table: "order_substitutions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_substitutions_substitute_id",
                table: "order_substitutions",
                column: "substitute_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_substitutes_substitute_id",
                table: "product_substitutes",
                column: "substitute_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_substitutions");

            migrationBuilder.DropTable(
                name: "product_substitutes");
        }
    }
}
