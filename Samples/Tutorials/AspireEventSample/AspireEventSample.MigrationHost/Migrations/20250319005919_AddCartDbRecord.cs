using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireEventSample.MigrationHost.Migrations
{
    /// <inheritdoc />
    public partial class AddCartDbRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false),
                    AggregateGroup = table.Column<string>(type: "text", nullable: false),
                    LastSortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalAmount = table.Column<int>(type: "integer", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carts_RootPartitionKey_AggregateGroup_TargetId",
                table: "Carts",
                columns: new[] { "RootPartitionKey", "AggregateGroup", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Carts");
        }
    }
}
