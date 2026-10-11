using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmegaCloud.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConflictResolutionAndUploadShared : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConflictResolution",
                table: "UploadSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "UploadSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictResolution",
                table: "UploadSessions");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "UploadSessions");
        }
    }
}
