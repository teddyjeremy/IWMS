using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdempotencyKey",
                table: "InventoryMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_IdempotencyKey_Type",
                table: "InventoryMovements",
                columns: new[] { "IdempotencyKey", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_IdempotencyKey_Type",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "InventoryMovements");
        }
    }
}
