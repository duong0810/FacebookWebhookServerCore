using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webhook_Message.Migrations.ZaloDb
{
    /// <inheritdoc />
    public partial class InitZaloDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZaloCustomers",
                columns: table => new
                {
                    ZaloId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloCustomers", x => x.ZaloId);
                });

            migrationBuilder.CreateTable(
                name: "ZaloMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenderId = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientId = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZaloMessages_ZaloCustomers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "ZaloCustomers",
                        principalColumn: "ZaloId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZaloMessages_ZaloCustomers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "ZaloCustomers",
                        principalColumn: "ZaloId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZaloMessages_RecipientId",
                table: "ZaloMessages",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_ZaloMessages_SenderId",
                table: "ZaloMessages",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZaloMessages");

            migrationBuilder.DropTable(
                name: "ZaloCustomers");
        }
    }
}
