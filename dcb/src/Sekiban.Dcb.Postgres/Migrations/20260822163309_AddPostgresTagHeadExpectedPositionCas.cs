using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sekiban.Dcb.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgresTagHeadExpectedPositionCas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dcb_tag_head_enablement_epochs",
                columns: table => new
                {
                    ServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EnabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dcb_tag_head_enablement_epochs", x => x.ServiceId);
                });

            migrationBuilder.CreateTable(
                name: "dcb_tag_head_violations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    PreviousHeadWasEmpty = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousHeadPosition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObservedPosition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetectingWriter = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dcb_tag_head_violations", x => x.Id);
                    table.CheckConstraint("CK_TagHeadViolations_Observed_NotEmpty", "length(\"ObservedPosition\") > 0");
                });

            migrationBuilder.CreateTable(
                name: "dcb_tag_heads",
                columns: table => new
                {
                    ServiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    HeadPosition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dcb_tag_heads", x => new { x.ServiceId, x.Tag });
                    table.CheckConstraint("CK_TagHeads_Position_NotEmpty", "\"HeadPosition\" IS NULL OR length(\"HeadPosition\") > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagHeadViolations_Service_Tag_Detected",
                table: "dcb_tag_head_violations",
                columns: new[] { "ServiceId", "Tag", "DetectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_TagHeadViolations_IdempotentRepair",
                table: "dcb_tag_head_violations",
                columns: new[] { "ServiceId", "Tag", "PreviousHeadWasEmpty", "PreviousHeadPosition", "ObservedPosition" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dcb_tag_head_enablement_epochs");

            migrationBuilder.DropTable(
                name: "dcb_tag_head_violations");

            migrationBuilder.DropTable(
                name: "dcb_tag_heads");
        }
    }
}
