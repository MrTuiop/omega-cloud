using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmegaCloud.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedSpaceAndNodeCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MaxCapacityBytes",
                table: "StorageNodes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "Folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "FileItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_IsShared",
                table: "Folders",
                column: "IsShared");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_IsShared_ParentFolderId_IsDeleted",
                table: "Folders",
                columns: new[] { "IsShared", "ParentFolderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_IsShared",
                table: "FileItems",
                column: "IsShared");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_IsShared_FolderId_IsDeleted",
                table: "FileItems",
                columns: new[] { "IsShared", "FolderId", "IsDeleted" });

            // Data migrations:
            // 1. Convert any existing Guest role users to regular User
            migrationBuilder.Sql("UPDATE \"Users\" SET \"Role\" = 'User' WHERE \"Role\" = 'Guest';");

            // 2. Initialize MaxCapacityBytes to TotalSpaceBytes for existing storage nodes
            migrationBuilder.Sql("UPDATE \"StorageNodes\" SET \"MaxCapacityBytes\" = \"TotalSpaceBytes\" WHERE \"MaxCapacityBytes\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folders_IsShared",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_Folders_IsShared_ParentFolderId_IsDeleted",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_FileItems_IsShared",
                table: "FileItems");

            migrationBuilder.DropIndex(
                name: "IX_FileItems_IsShared_FolderId_IsDeleted",
                table: "FileItems");

            migrationBuilder.DropColumn(
                name: "MaxCapacityBytes",
                table: "StorageNodes");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "FileItems");
        }
    }
}
