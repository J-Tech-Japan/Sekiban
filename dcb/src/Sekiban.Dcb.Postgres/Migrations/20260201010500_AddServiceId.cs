using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekiban.Dcb.Postgres.Migrations;

/// <inheritdoc />
public partial class AddServiceId : Migration
{
    private const string ServiceIdColumn = "ServiceId";
    private const string EventsTable = "dcb_events";
    private const string TagsTable = "dcb_tags";
    private const string StatesTable = "dcb_multi_projection_states";
    private const string EventsPrimaryKey = "PK_dcb_events";
    private const string StatesPrimaryKey = "PK_dcb_multi_projection_states";
    private const string ProjectorNameColumn = "ProjectorName";
    private const string SortableUniqueIdColumn = "SortableUniqueId";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: ServiceIdColumn,
            table: EventsTable,
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.AddColumn<string>(
            name: ServiceIdColumn,
            table: TagsTable,
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.AddColumn<string>(
            name: ServiceIdColumn,
            table: StatesTable,
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.DropPrimaryKey(
            name: EventsPrimaryKey,
            table: EventsTable);

        migrationBuilder.DropIndex(
            name: "IX_Events_SortableUniqueId",
            table: EventsTable);

        migrationBuilder.DropPrimaryKey(
            name: StatesPrimaryKey,
            table: StatesTable);

        migrationBuilder.DropIndex(
            name: "IX_MultiProjectionStates_ProjectorName",
            table: StatesTable);

        migrationBuilder.DropIndex(
            name: "IX_Tags_Tag",
            table: TagsTable);

        migrationBuilder.DropIndex(
            name: "IX_Tags_Tag_SortableUniqueId",
            table: TagsTable);

        migrationBuilder.AddPrimaryKey(
            name: EventsPrimaryKey,
            table: EventsTable,
            columns: new[] { ServiceIdColumn, "Id" });

        migrationBuilder.AddPrimaryKey(
            name: StatesPrimaryKey,
            table: StatesTable,
            columns: new[] { ServiceIdColumn, ProjectorNameColumn, "ProjectorVersion" });

        migrationBuilder.CreateIndex(
            name: "IX_Events_ServiceId",
            table: EventsTable,
            column: ServiceIdColumn);

        migrationBuilder.CreateIndex(
            name: "IX_Events_Service_SortableUniqueId",
            table: EventsTable,
            columns: new[] { ServiceIdColumn, SortableUniqueIdColumn });

        migrationBuilder.CreateIndex(
            name: "IX_Tags_ServiceId",
            table: TagsTable,
            column: ServiceIdColumn);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Service_Tag",
            table: TagsTable,
            columns: new[] { ServiceIdColumn, "Tag" });

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Service_Tag_SortableUniqueId",
            table: TagsTable,
            columns: new[] { ServiceIdColumn, "Tag", SortableUniqueIdColumn });

        migrationBuilder.CreateIndex(
            name: "IX_MultiProjectionStates_Service_ProjectorName",
            table: StatesTable,
            columns: new[] { ServiceIdColumn, ProjectorNameColumn });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: EventsPrimaryKey,
            table: EventsTable);

        migrationBuilder.DropPrimaryKey(
            name: StatesPrimaryKey,
            table: StatesTable);

        migrationBuilder.DropIndex(
            name: "IX_Events_ServiceId",
            table: EventsTable);

        migrationBuilder.DropIndex(
            name: "IX_Events_Service_SortableUniqueId",
            table: EventsTable);

        migrationBuilder.DropIndex(
            name: "IX_Tags_ServiceId",
            table: TagsTable);

        migrationBuilder.DropIndex(
            name: "IX_Tags_Service_Tag",
            table: TagsTable);

        migrationBuilder.DropIndex(
            name: "IX_Tags_Service_Tag_SortableUniqueId",
            table: TagsTable);

        migrationBuilder.DropIndex(
            name: "IX_MultiProjectionStates_Service_ProjectorName",
            table: StatesTable);

        migrationBuilder.AddPrimaryKey(
            name: EventsPrimaryKey,
            table: EventsTable,
            column: "Id");

        migrationBuilder.AddPrimaryKey(
            name: StatesPrimaryKey,
            table: StatesTable,
            columns: new[] { ProjectorNameColumn, "ProjectorVersion" });

        migrationBuilder.CreateIndex(
            name: "IX_Events_SortableUniqueId",
            table: EventsTable,
            column: SortableUniqueIdColumn,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Tag",
            table: TagsTable,
            column: "Tag");

        migrationBuilder.CreateIndex(
            name: "IX_Tags_Tag_SortableUniqueId",
            table: TagsTable,
            columns: new[] { "Tag", SortableUniqueIdColumn });

        migrationBuilder.CreateIndex(
            name: "IX_MultiProjectionStates_ProjectorName",
            table: StatesTable,
            column: ProjectorNameColumn);

        migrationBuilder.DropColumn(
            name: ServiceIdColumn,
            table: EventsTable);

        migrationBuilder.DropColumn(
            name: ServiceIdColumn,
            table: TagsTable);

        migrationBuilder.DropColumn(
            name: ServiceIdColumn,
            table: StatesTable);
    }
}
