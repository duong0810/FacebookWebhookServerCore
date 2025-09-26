using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webhook_Message.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZaloCustomers",
                columns: table => new
                {
                    ZaloId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloCustomers", x => x.ZaloId);
                });

            migrationBuilder.CreateTable(
                name: "ZaloTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: false),
                    ExpireAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZaloMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SenderId = table.Column<string>(type: "text", nullable: false),
                    RecipientId = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StatusTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MsgId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZaloMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZaloMessages_ZaloCustomers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "ZaloCustomers",
                        principalColumn: "ZaloId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ZaloMessages_ZaloCustomers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "ZaloCustomers",
                        principalColumn: "ZaloId",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ZaloTokens");

            migrationBuilder.DropTable(
                name: "ZaloCustomers");
        }
    }
}
