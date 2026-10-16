using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReversalAndPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "purchase_order_number_seq");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceLineId",
                table: "InventoryMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Name",
                table: "Suppliers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ReversesMovementId",
                table: "InventoryMovements",
                column: "ReversesMovementId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_Name",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_ReversesMovementId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "SourceLineId",
                table: "InventoryMovements");

            migrationBuilder.DropSequence(
                name: "purchase_order_number_seq");
        }
    }
}
