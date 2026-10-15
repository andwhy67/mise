using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Restaurants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_supplies_idempotency_key",
                table: "supplies");

            migrationBuilder.DropIndex(
                name: "ix_supplies_number",
                table: "supplies");

            migrationBuilder.DropIndex(
                name: "ix_products_name",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_orders_idempotency_key",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_number",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_dishes_name",
                table: "dishes");

            migrationBuilder.DropSequence(
                name: "order_number_seq");

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "supplies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "stoplist_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "push_subscriptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "order_shortages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "dishes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "consumption_hourly",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "batches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "restaurant_id",
                table: "alerts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("5e7a0000-0000-4000-8000-00000000d3e0"));

            migrationBuilder.CreateTable(
                name: "restaurants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false),
                    last_order_number = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurants", x => x.id);
                    table.CheckConstraint("ck_restaurants_last_order_number", "last_order_number >= 0");
                });

            // Всё, что было до ресторанов, — демо-ресторана (демо-учётки, словарь, симулятор). Нумерация его заказов
            // продолжается с последнего номера общей последовательности, которую заменяет счётчик ресторана.
            migrationBuilder.Sql("""
                INSERT INTO restaurants (id, name, created_at, is_demo, last_order_number)
                VALUES ('5e7a0000-0000-4000-8000-00000000d3e0', 'Mise · демо', now(), true,
                        COALESCE((SELECT MAX(CAST(substring(number from '[0-9]+$') AS bigint)) FROM orders), 0))
                ON CONFLICT (id) DO NOTHING;
                """);
            migrationBuilder.Sql("UPDATE audit_log SET restaurant_id = '5e7a0000-0000-4000-8000-00000000d3e0' WHERE restaurant_id IS NULL;");

            // Значение по умолчанию нужно было только для существующих строк: новая запись без ресторана — ошибка,
            // а не тихое попадание в демо-ресторан.
            migrationBuilder.Sql("ALTER TABLE users ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE supplies ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE stoplist_history ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE stock_movements ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE push_subscriptions ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE products ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE orders ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE order_shortages ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE dishes ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE consumption_hourly ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE batches ALTER COLUMN restaurant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE alerts ALTER COLUMN restaurant_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_users_restaurant_id",
                table: "users",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplies_restaurant_id",
                table: "supplies",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplies_restaurant_id_idempotency_key",
                table: "supplies",
                columns: new[] { "restaurant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplies_restaurant_id_number",
                table: "supplies",
                columns: new[] { "restaurant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stoplist_history_restaurant_id",
                table: "stoplist_history",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_restaurant_id",
                table: "stock_movements",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_restaurant_id",
                table: "push_subscriptions",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_restaurant_id",
                table: "products",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_restaurant_id_name",
                table: "products",
                columns: new[] { "restaurant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_restaurant_id",
                table: "orders",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_restaurant_id_idempotency_key",
                table: "orders",
                columns: new[] { "restaurant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_restaurant_id_number",
                table: "orders",
                columns: new[] { "restaurant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_shortages_restaurant_id",
                table: "order_shortages",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_dishes_restaurant_id",
                table: "dishes",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_dishes_restaurant_id_name",
                table: "dishes",
                columns: new[] { "restaurant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consumption_hourly_restaurant_id",
                table: "consumption_hourly",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_restaurant_id",
                table: "batches",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_restaurant_id",
                table: "audit_log",
                column: "restaurant_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_restaurant_id",
                table: "alerts",
                column: "restaurant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_alerts_restaurants_restaurant_id",
                table: "alerts",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_log_restaurants_restaurant_id",
                table: "audit_log",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_batches_restaurants_restaurant_id",
                table: "batches",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_consumption_hourly_restaurants_restaurant_id",
                table: "consumption_hourly",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_dishes_restaurants_restaurant_id",
                table: "dishes",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_shortages_restaurants_restaurant_id",
                table: "order_shortages",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_restaurants_restaurant_id",
                table: "orders",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_products_restaurants_restaurant_id",
                table: "products",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_push_subscriptions_restaurants_restaurant_id",
                table: "push_subscriptions",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_restaurants_restaurant_id",
                table: "stock_movements",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stoplist_history_restaurants_restaurant_id",
                table: "stoplist_history",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_supplies_restaurants_restaurant_id",
                table: "supplies",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_restaurants_restaurant_id",
                table: "users",
                column: "restaurant_id",
                principalTable: "restaurants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_alerts_restaurants_restaurant_id",
                table: "alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_log_restaurants_restaurant_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "fk_batches_restaurants_restaurant_id",
                table: "batches");

            migrationBuilder.DropForeignKey(
                name: "fk_consumption_hourly_restaurants_restaurant_id",
                table: "consumption_hourly");

            migrationBuilder.DropForeignKey(
                name: "fk_dishes_restaurants_restaurant_id",
                table: "dishes");

            migrationBuilder.DropForeignKey(
                name: "fk_order_shortages_restaurants_restaurant_id",
                table: "order_shortages");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_restaurants_restaurant_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_products_restaurants_restaurant_id",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "fk_push_subscriptions_restaurants_restaurant_id",
                table: "push_subscriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_restaurants_restaurant_id",
                table: "stock_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_stoplist_history_restaurants_restaurant_id",
                table: "stoplist_history");

            migrationBuilder.DropForeignKey(
                name: "fk_supplies_restaurants_restaurant_id",
                table: "supplies");

            migrationBuilder.DropForeignKey(
                name: "fk_users_restaurants_restaurant_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "restaurants");

            migrationBuilder.DropIndex(
                name: "ix_users_restaurant_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_supplies_restaurant_id",
                table: "supplies");

            migrationBuilder.DropIndex(
                name: "ix_supplies_restaurant_id_idempotency_key",
                table: "supplies");

            migrationBuilder.DropIndex(
                name: "ix_supplies_restaurant_id_number",
                table: "supplies");

            migrationBuilder.DropIndex(
                name: "ix_stoplist_history_restaurant_id",
                table: "stoplist_history");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_restaurant_id",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_push_subscriptions_restaurant_id",
                table: "push_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_products_restaurant_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_restaurant_id_name",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_orders_restaurant_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_restaurant_id_idempotency_key",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_restaurant_id_number",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_order_shortages_restaurant_id",
                table: "order_shortages");

            migrationBuilder.DropIndex(
                name: "ix_dishes_restaurant_id",
                table: "dishes");

            migrationBuilder.DropIndex(
                name: "ix_dishes_restaurant_id_name",
                table: "dishes");

            migrationBuilder.DropIndex(
                name: "ix_consumption_hourly_restaurant_id",
                table: "consumption_hourly");

            migrationBuilder.DropIndex(
                name: "ix_batches_restaurant_id",
                table: "batches");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_restaurant_id",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_alerts_restaurant_id",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "supplies");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "stoplist_history");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "push_subscriptions");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "order_shortages");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "dishes");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "consumption_hourly");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "restaurant_id",
                table: "alerts");

            migrationBuilder.CreateSequence(
                name: "order_number_seq");

            migrationBuilder.CreateIndex(
                name: "ix_supplies_idempotency_key",
                table: "supplies",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplies_number",
                table: "supplies",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                table: "products",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_idempotency_key",
                table: "orders",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_number",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dishes_name",
                table: "dishes",
                column: "name",
                unique: true);
        }
    }
}
