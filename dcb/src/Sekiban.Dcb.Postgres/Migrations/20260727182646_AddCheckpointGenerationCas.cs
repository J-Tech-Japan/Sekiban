using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekiban.Dcb.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckpointGenerationCas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Generation",
                table: "dcb_multi_projection_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Lifecycle",
                table: "dcb_multi_projection_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "dcb_multi_projection_states",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Generation",
                table: "dcb_multi_projection_states");

            migrationBuilder.DropColumn(
                name: "Lifecycle",
                table: "dcb_multi_projection_states");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "dcb_multi_projection_states");
        }
    }
}
