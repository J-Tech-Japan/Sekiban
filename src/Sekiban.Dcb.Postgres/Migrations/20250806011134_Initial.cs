using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sekiban.Dcb.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dcb_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CausationId = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    ExecutedUser = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dcb_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dcb_tags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    SortableUniqueId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dcb_tags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dcb_events_EventType",
                table: "dcb_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_dcb_events_Timestamp",
                table: "dcb_events",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Events_SortableUniqueId",
                table: "dcb_events",
                column: "SortableUniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_EventId",
                table: "dcb_tags",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SortableUniqueId",
                table: "dcb_tags",
                column: "SortableUniqueId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Tag",
                table: "dcb_tags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Tag_SortableUniqueId",
                table: "dcb_tags",
                columns: new[] { "Tag", "SortableUniqueId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dcb_events");

            migrationBuilder.DropTable(
                name: "dcb_tags");
        }
    }
}
