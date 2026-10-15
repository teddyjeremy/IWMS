using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdempotencyKey",
                table: "GoodsReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_IdempotencyKey",
                table: "GoodsReceipts",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_IdempotencyKey",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "GoodsReceipts");
        }
    }
}
