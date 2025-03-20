using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireEventSample.MigrationHost.Migrations
{
    /// <inheritdoc />
    public partial class AddCartItemTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ItemsJson",
                table: "Carts");

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false),
                    AggregateGroup = table.Column<string>(type: "text", nullable: false),
                    LastSortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId",
                table: "CartItems",
                column: "CartId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_RootPartitionKey_AggregateGroup_TargetId",
                table: "CartItems",
                columns: new[] { "RootPartitionKey", "AggregateGroup", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.AddColumn<string>(
                name: "ItemsJson",
                table: "Carts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
