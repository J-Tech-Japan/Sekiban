using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekiban.Dcb.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiProjectionStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dcb_multi_projection_states",
                columns: table => new
                {
                    ProjectorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProjectorVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadType = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    LastSortableUniqueId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    StateData = table.Column<byte[]>(type: "bytea", nullable: true),
                    IsOffloaded = table.Column<bool>(type: "boolean", nullable: false),
                    OffloadKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OffloadProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OriginalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CompressedSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SafeWindowThreshold = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BuildSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BuildHost = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dcb_multi_projection_states", x => new { x.ProjectorName, x.ProjectorVersion });
                    table.CheckConstraint("CK_MultiProjectionStates_OffloadConsistency", "(\"IsOffloaded\" = false AND \"StateData\" IS NOT NULL) OR (\"IsOffloaded\" = true AND \"OffloadKey\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MultiProjectionStates_ProjectorName",
                table: "dcb_multi_projection_states",
                column: "ProjectorName");

            migrationBuilder.CreateIndex(
                name: "IX_MultiProjectionStates_UpdatedAt",
                table: "dcb_multi_projection_states",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dcb_multi_projection_states");
        }
    }
}
