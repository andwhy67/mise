using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderShortages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_shortages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    available = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    shortage = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_shortages", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_shortages_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_shortages_created_at",
                table: "order_shortages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_order_shortages_product_id_created_at",
                table: "order_shortages",
                columns: new[] { "product_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_shortages");
        }
    }
}
