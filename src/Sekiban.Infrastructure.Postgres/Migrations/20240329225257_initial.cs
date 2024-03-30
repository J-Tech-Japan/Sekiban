using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekiban.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateContainerGroup = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    ExecutedUser = table.Column<string>(type: "text", nullable: true),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    CallHistories = table.Column<string>(type: "json", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartitionKey = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentTypeName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DissolvableEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CallHistories = table.Column<string>(type: "json", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartitionKey = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentTypeName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DissolvableEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CallHistories = table.Column<string>(type: "json", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartitionKey = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentTypeName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MultiProjectionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateContainerGroup = table.Column<int>(type: "integer", nullable: false),
                    PartitionKey = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentTypeName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    SavedVersion = table.Column<int>(type: "integer", nullable: false),
                    PayloadVersionIdentifier = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiProjectionSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SingleProjectionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateContainerGroup = table.Column<int>(type: "integer", nullable: false),
                    Snapshot = table.Column<string>(type: "json", nullable: true),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    SavedVersion = table.Column<int>(type: "integer", nullable: false),
                    PayloadVersionIdentifier = table.Column<string>(type: "text", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartitionKey = table.Column<string>(type: "text", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentTypeName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    RootPartitionKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SingleProjectionSnapshots", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Commands");

            migrationBuilder.DropTable(
                name: "DissolvableEvents");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "MultiProjectionSnapshots");

            migrationBuilder.DropTable(
                name: "SingleProjectionSnapshots");
        }
    }
}
