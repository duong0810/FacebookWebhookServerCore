using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webhook_Message.Migrations.ZaloDb
{
    /// <inheritdoc />
    public partial class AddStatusAndDeliveredTimeToZaloMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredTime",
                table: "ZaloMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ZaloMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredTime",
                table: "ZaloMessages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ZaloMessages");
        }
    }
}
