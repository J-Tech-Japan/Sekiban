using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireEventSample.MigrationHost.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryToBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Branches",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Branches");
        }
    }
}
