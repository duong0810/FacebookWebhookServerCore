using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webhook_Message.Migrations.ZaloDb
{
    /// <inheritdoc />
    public partial class UpdateZaloDbContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_RecipientId",
                table: "ZaloMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_SenderId",
                table: "ZaloMessages");

            migrationBuilder.AddForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_RecipientId",
                table: "ZaloMessages",
                column: "RecipientId",
                principalTable: "ZaloCustomers",
                principalColumn: "ZaloId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_SenderId",
                table: "ZaloMessages",
                column: "SenderId",
                principalTable: "ZaloCustomers",
                principalColumn: "ZaloId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_RecipientId",
                table: "ZaloMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_SenderId",
                table: "ZaloMessages");

            migrationBuilder.AddForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_RecipientId",
                table: "ZaloMessages",
                column: "RecipientId",
                principalTable: "ZaloCustomers",
                principalColumn: "ZaloId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ZaloMessages_ZaloCustomers_SenderId",
                table: "ZaloMessages",
                column: "SenderId",
                principalTable: "ZaloCustomers",
                principalColumn: "ZaloId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
