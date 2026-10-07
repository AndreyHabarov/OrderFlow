using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_idempotency_key",
                schema: "orders",
                table: "orders",
                columns: new[] { "customer_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_customer_id_idempotency_key",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "orders",
                table: "orders");
        }
    }
}
