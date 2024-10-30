#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
namespace Sekiban.Infrastructure.Postgres.Migrations;

/// <inheritdoc />
public partial class PostgresInitial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            "Commands",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                AggregateContainerGroup = table.Column<int>("integer", nullable: false),
                Payload = table.Column<string>("json", nullable: false),
                ExecutedUser = table.Column<string>("text", nullable: true),
                Exception = table.Column<string>("text", nullable: true),
                CallHistories = table.Column<string>("json", nullable: false),
                AggregateId = table.Column<Guid>("uuid", nullable: false),
                PartitionKey = table.Column<string>("text", nullable: false),
                DocumentType = table.Column<int>("integer", nullable: false),
                DocumentTypeName = table.Column<string>("text", nullable: false),
                TimeStamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                SortableUniqueId = table.Column<string>("text", nullable: false),
                AggregateType = table.Column<string>("text", nullable: false),
                RootPartitionKey = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Commands", x => x.Id);
            });

        migrationBuilder.CreateTable(
            "DissolvableEvents",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                Payload = table.Column<string>("json", nullable: false),
                Version = table.Column<int>("integer", nullable: false),
                CallHistories = table.Column<string>("json", nullable: false),
                AggregateId = table.Column<Guid>("uuid", nullable: false),
                PartitionKey = table.Column<string>("text", nullable: false),
                DocumentType = table.Column<int>("integer", nullable: false),
                DocumentTypeName = table.Column<string>("text", nullable: false),
                TimeStamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                SortableUniqueId = table.Column<string>("text", nullable: false),
                AggregateType = table.Column<string>("text", nullable: false),
                RootPartitionKey = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DissolvableEvents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            "Events",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                Payload = table.Column<string>("json", nullable: false),
                Version = table.Column<int>("integer", nullable: false),
                CallHistories = table.Column<string>("json", nullable: false),
                AggregateId = table.Column<Guid>("uuid", nullable: false),
                PartitionKey = table.Column<string>("text", nullable: false),
                DocumentType = table.Column<int>("integer", nullable: false),
                DocumentTypeName = table.Column<string>("text", nullable: false),
                TimeStamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                SortableUniqueId = table.Column<string>("text", nullable: false),
                AggregateType = table.Column<string>("text", nullable: false),
                RootPartitionKey = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            "MultiProjectionSnapshots",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                AggregateContainerGroup = table.Column<int>("integer", nullable: false),
                PartitionKey = table.Column<string>("text", nullable: false),
                DocumentType = table.Column<int>("integer", nullable: false),
                DocumentTypeName = table.Column<string>("text", nullable: false),
                TimeStamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                SortableUniqueId = table.Column<string>("text", nullable: false),
                AggregateType = table.Column<string>("text", nullable: false),
                RootPartitionKey = table.Column<string>("text", nullable: false),
                LastEventId = table.Column<Guid>("uuid", nullable: false),
                LastSortableUniqueId = table.Column<string>("text", nullable: false),
                SavedVersion = table.Column<int>("integer", nullable: false),
                PayloadVersionIdentifier = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MultiProjectionSnapshots", x => x.Id);
            });

        migrationBuilder.CreateTable(
            "SingleProjectionSnapshots",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                AggregateContainerGroup = table.Column<int>("integer", nullable: false),
                Snapshot = table.Column<string>("json", nullable: true),
                LastEventId = table.Column<Guid>("uuid", nullable: false),
                LastSortableUniqueId = table.Column<string>("text", nullable: false),
                SavedVersion = table.Column<int>("integer", nullable: false),
                PayloadVersionIdentifier = table.Column<string>("text", nullable: false),
                AggregateId = table.Column<Guid>("uuid", nullable: false),
                PartitionKey = table.Column<string>("text", nullable: false),
                DocumentType = table.Column<int>("integer", nullable: false),
                DocumentTypeName = table.Column<string>("text", nullable: false),
                TimeStamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                SortableUniqueId = table.Column<string>("text", nullable: false),
                AggregateType = table.Column<string>("text", nullable: false),
                RootPartitionKey = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SingleProjectionSnapshots", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Commands");

        migrationBuilder.DropTable(name: "DissolvableEvents");

        migrationBuilder.DropTable(name: "Events");

        migrationBuilder.DropTable(name: "MultiProjectionSnapshots");

        migrationBuilder.DropTable(name: "SingleProjectionSnapshots");
    }
}
