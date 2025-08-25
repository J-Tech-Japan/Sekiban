#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
namespace Sekiban.Dcb.Postgres.Migrations;

/// <inheritdoc />
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            "dcb_events",
            table => new
            {
                Id = table.Column<Guid>("uuid", nullable: false),
                SortableUniqueId = table.Column<string>("character varying(100)", maxLength: 100, nullable: false),
                EventType = table.Column<string>("text", nullable: false),
                Payload = table.Column<string>("json", nullable: false),
                Tags = table.Column<string>("jsonb", nullable: false),
                Timestamp = table.Column<DateTime>("timestamp with time zone", nullable: false),
                CausationId = table.Column<string>("text", nullable: true),
                CorrelationId = table.Column<string>("text", nullable: true),
                ExecutedUser = table.Column<string>("text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dcb_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            "dcb_tags",
            table => new
            {
                Id = table
                    .Column<long>("bigint", nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Tag = table.Column<string>("text", nullable: false),
                TagGroup = table.Column<string>("text", nullable: false),
                EventType = table.Column<string>("text", nullable: false),
                SortableUniqueId = table.Column<string>("character varying(100)", maxLength: 100, nullable: false),
                EventId = table.Column<Guid>("uuid", nullable: false),
                CreatedAt = table.Column<DateTime>("timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dcb_tags", x => x.Id);
            });

        migrationBuilder.CreateIndex("IX_dcb_events_EventType", "dcb_events", "EventType");

        migrationBuilder.CreateIndex("IX_dcb_events_Timestamp", "dcb_events", "Timestamp");

        migrationBuilder.CreateIndex("IX_Events_SortableUniqueId", "dcb_events", "SortableUniqueId", unique: true);

        migrationBuilder.CreateIndex("IX_Tags_EventId", "dcb_tags", "EventId");

        migrationBuilder.CreateIndex("IX_Tags_SortableUniqueId", "dcb_tags", "SortableUniqueId");

        migrationBuilder.CreateIndex("IX_Tags_Tag", "dcb_tags", "Tag");

        migrationBuilder.CreateIndex("IX_Tags_Tag_SortableUniqueId", "dcb_tags", new[] { "Tag", "SortableUniqueId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "dcb_events");

        migrationBuilder.DropTable(name: "dcb_tags");
    }
}
