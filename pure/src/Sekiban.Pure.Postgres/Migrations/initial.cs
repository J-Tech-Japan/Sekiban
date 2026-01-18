#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
#pragma warning disable CS8981
namespace Sekiban.Pure.Postgres.Migrations;

/// <inheritdoc />
public partial class initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            "Events",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                Payload = table.Column<string>("json", nullable: false),
                SortableUniqueId = table.Column<string>("text", nullable: false),
                Version = table.Column<int>("integer", nullable: false),
                AggregateId = table.Column<Guid>("uuid", nullable: false),
                RootPartitionKey = table.Column<string>("text", nullable: false),
                TimeStamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                PartitionKey = table.Column<string>("text", nullable: false),
                AggregateGroup = table.Column<string>("text", nullable: false),
                PayloadTypeName = table.Column<string>("text", nullable: false),
                CausationId = table.Column<string>("text", nullable: false),
                CorrelationId = table.Column<string>("text", nullable: false),
                ExecutedUser = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Events", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Events");
    }
}
