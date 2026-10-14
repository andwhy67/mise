using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_written_off",
                table: "alerts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "limit_qty",
                table: "alerts",
                type: "numeric(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                table: "alerts",
                type: "numeric(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_written_off",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "limit_qty",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "alerts");
        }
    }
}
