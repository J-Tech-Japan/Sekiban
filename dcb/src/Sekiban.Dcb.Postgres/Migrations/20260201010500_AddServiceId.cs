using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekiban.Dcb.Postgres.Migrations;

/// <inheritdoc />
public partial class AddServiceId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ServiceId",
            table: "dcb_events",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.AddColumn<string>(
            name: "ServiceId",
            table: "dcb_tags",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.AddColumn<string>(
            name: "ServiceId",
            table: "dcb_multi_projection_states",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.DropPrimaryKey(
            name: "PK_dcb_events",
            table: "dcb_events");

        migrationBuilder.DropIndex(
            name: "IX_Events_SortableUniqueId",
            table: "dcb_events");

        migrationBuilder.DropPrimaryKey(
            name: "PK_dcb_multi_projection_states",
            table: "dcb_multi_projection_states");

        migrationBuilder.DropIndex(
            name: "IX_MultiProjectionStates_ProjectorName",
            table: "dcb_multi_projection_states");

        migrationBuilder.DropIndex(
            name: "IX_Tags_Tag",
            table: "dcb_tags");

        migrationBuilder.DropIndex(
            name: "IX_Tags_Tag_SortableUniqueId",
            table: "dcb_tags");

        migrationBuilder.AddPrimaryKey(
            name: "PK_dcb_events",
            table: "dcb_events",
            columns: new[] { "ServiceId", "Id" });

        migrationBuilder.AddPrimaryKey(
            name: "PK_dcb_multi_projection_states",
            table: "dcb_multi_projection_states",
            columns: new[] { "ServiceId", "ProjectorName", "ProjectorVersion" });

        migrationBuilder.CreateIndex(
            name: "IX_Events_ServiceId",
            table: "dcb_events",
            column: "ServiceId");

        migrationBuilder.CreateIndex(
            name: "IX_Events_Service_SortableUniqueId",
            table: "dcb_events",
            columns: new[] { "ServiceId", "SortableUniqueId" });

        migrationBuilder.CreateIndex(
            name: "IX_Tags_ServiceId",
            table: "dcb_tags",
            column: "ServiceId");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Service_Tag",
            table: "dcb_tags",
            columns: new[] { "ServiceId", "Tag" });

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Service_Tag_SortableUniqueId",
            table: "dcb_tags",
            columns: new[] { "ServiceId", "Tag", "SortableUniqueId" });

        migrationBuilder.CreateIndex(
            name: "IX_MultiProjectionStates_Service_ProjectorName",
            table: "dcb_multi_projection_states",
            columns: new[] { "ServiceId", "ProjectorName" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_dcb_events",
            table: "dcb_events");

        migrationBuilder.DropPrimaryKey(
            name: "PK_dcb_multi_projection_states",
            table: "dcb_multi_projection_states");

        migrationBuilder.DropIndex(
            name: "IX_Events_ServiceId",
            table: "dcb_events");

        migrationBuilder.DropIndex(
            name: "IX_Events_Service_SortableUniqueId",
            table: "dcb_events");

        migrationBuilder.DropIndex(
            name: "IX_Tags_ServiceId",
            table: "dcb_tags");

        migrationBuilder.DropIndex(
            name: "IX_Tags_Service_Tag",
            table: "dcb_tags");

        migrationBuilder.DropIndex(
            name: "IX_Tags_Service_Tag_SortableUniqueId",
            table: "dcb_tags");

        migrationBuilder.DropIndex(
            name: "IX_MultiProjectionStates_Service_ProjectorName",
            table: "dcb_multi_projection_states");

        migrationBuilder.AddPrimaryKey(
            name: "PK_dcb_events",
            table: "dcb_events",
            column: "Id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_dcb_multi_projection_states",
            table: "dcb_multi_projection_states",
            columns: new[] { "ProjectorName", "ProjectorVersion" });

        migrationBuilder.CreateIndex(
            name: "IX_Events_SortableUniqueId",
            table: "dcb_events",
            column: "SortableUniqueId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Tag",
            table: "dcb_tags",
            column: "Tag");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Tag_SortableUniqueId",
            table: "dcb_tags",
            columns: new[] { "Tag", "SortableUniqueId" });

        migrationBuilder.CreateIndex(
            name: "IX_MultiProjectionStates_ProjectorName",
            table: "dcb_multi_projection_states",
            column: "ProjectorName");

        migrationBuilder.DropColumn(
            name: "ServiceId",
            table: "dcb_events");

        migrationBuilder.DropColumn(
            name: "ServiceId",
            table: "dcb_tags");

        migrationBuilder.DropColumn(
            name: "ServiceId",
            table: "dcb_multi_projection_states");
    }
}
