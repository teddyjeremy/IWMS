using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProductNullableFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BarCode",
                table: "Products",
                newName: "Barcode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Barcode",
                table: "Products",
                newName: "BarCode");
        }
    }
}
