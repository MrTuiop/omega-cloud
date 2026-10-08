using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmegaCloud.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVfsAndStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Folders_Folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Folders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StorageNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RegistrationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NodeSecretHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsBehindNat = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalSpaceBytes = table.Column<long>(type: "bigint", nullable: false),
                    AvailableSpaceBytes = table.Column<long>(type: "bigint", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageRelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ReferenceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageBlobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageBlobs_StorageNodes_StorageNodeId",
                        column: x => x.StorageNodeId,
                        principalTable: "StorageNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UploadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChunkSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    TotalChunks = table.Column<int>(type: "integer", nullable: false),
                    ExpectedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TempStoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadSessions_Folders_TargetFolderId",
                        column: x => x.TargetFolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UploadSessions_StorageNodes_StorageNodeId",
                        column: x => x.StorageNodeId,
                        principalTable: "StorageNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UploadSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FileItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageBlobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Extension = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileItems_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FileItems_StorageBlobs_StorageBlobId",
                        column: x => x.StorageBlobId,
                        principalTable: "StorageBlobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsUploaded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadChunks_UploadSessions_UploadSessionId",
                        column: x => x.UploadSessionId,
                        principalTable: "UploadSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_FolderId",
                table: "FileItems",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_IsDeleted",
                table: "FileItems",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_StorageBlobId",
                table: "FileItems",
                column: "StorageBlobId");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_UserId",
                table: "FileItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FileItems_UserId_FolderId_Name_IsDeleted",
                table: "FileItems",
                columns: new[] { "UserId", "FolderId", "Name", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Folders_IsDeleted",
                table: "Folders",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentFolderId",
                table: "Folders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_UserId",
                table: "Folders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_UserId_ParentFolderId_Name_IsDeleted",
                table: "Folders",
                columns: new[] { "UserId", "ParentFolderId", "Name", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageBlobs_Sha256Hash",
                table: "StorageBlobs",
                column: "Sha256Hash");

            migrationBuilder.CreateIndex(
                name: "IX_StorageBlobs_StorageNodeId",
                table: "StorageBlobs",
                column: "StorageNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageNodes_LastHeartbeatAt",
                table: "StorageNodes",
                column: "LastHeartbeatAt");

            migrationBuilder.CreateIndex(
                name: "IX_StorageNodes_RegistrationKey",
                table: "StorageNodes",
                column: "RegistrationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadChunks_UploadSessionId_ChunkIndex",
                table: "UploadChunks",
                columns: new[] { "UploadSessionId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_ExpiresAt",
                table: "UploadSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_StorageNodeId",
                table: "UploadSessions",
                column: "StorageNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_TargetFolderId",
                table: "UploadSessions",
                column: "TargetFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_UserId",
                table: "UploadSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileItems");

            migrationBuilder.DropTable(
                name: "UploadChunks");

            migrationBuilder.DropTable(
                name: "StorageBlobs");

            migrationBuilder.DropTable(
                name: "UploadSessions");

            migrationBuilder.DropTable(
                name: "Folders");

            migrationBuilder.DropTable(
                name: "StorageNodes");
        }
    }
}
