using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmegaCloud.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeTelemetryAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuLoad",
                table: "StorageNodes",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiskStatus",
                table: "StorageNodes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "StorageNodes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "LatencyMs",
                table: "StorageNodes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuLoad",
                table: "StorageNodes");

            migrationBuilder.DropColumn(
                name: "DiskStatus",
                table: "StorageNodes");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "StorageNodes");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "StorageNodes");
        }
    }
}
