using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CartItemUniqueProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cart_items_cart_id",
                schema: "orders",
                table: "cart_items");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_id_product_id",
                schema: "orders",
                table: "cart_items",
                columns: new[] { "cart_id", "product_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cart_items_cart_id_product_id",
                schema: "orders",
                table: "cart_items");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_id",
                schema: "orders",
                table: "cart_items",
                column: "cart_id");
        }
    }
}
