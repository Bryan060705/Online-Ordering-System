using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherToCartAndOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVoucher",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MemberVoucherId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_MemberVoucherId",
                table: "OrderItems",
                column: "MemberVoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MemberVouchers_MemberVoucherId",
                table: "OrderItems",
                column: "MemberVoucherId",
                principalTable: "MemberVouchers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MemberVouchers_MemberVoucherId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_MemberVoucherId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsVoucher",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "MemberVoucherId",
                table: "OrderItems");
        }
    }
}
