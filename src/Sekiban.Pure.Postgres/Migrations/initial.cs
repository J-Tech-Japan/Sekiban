using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekiban.Pure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PartitionKey = table.Column<string>(type: "text", nullable: false),
                    AggregateGroup = table.Column<string>(type: "text", nullable: false),
                    PayloadTypeName = table.Column<string>(type: "text", nullable: false),
                    CausationId = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    ExecutedUser = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
