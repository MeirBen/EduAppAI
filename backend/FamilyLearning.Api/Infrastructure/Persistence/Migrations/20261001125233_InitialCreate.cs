using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Families",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Families", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ActivityDrafts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                PlanJson = table.Column<string>(type: "TEXT", nullable: false),
                InputJson = table.Column<string>(type: "TEXT", nullable: false),
                DocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                ActiveOperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                TemplateVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                SourceSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                CreatedByParentId = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ReleasedSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                ReleasedSourceRevision = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActivityDrafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ActivityDrafts_Families_FamilyId",
                    column: x => x.FamilyId,
                    principalTable: "Families",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetUsers_Families_FamilyId",
                    column: x => x.FamilyId,
                    principalTable: "Families",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TaskSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                SourceDraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                SourceDraftRevision = table.Column<long>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                PlanJson = table.Column<string>(type: "TEXT", nullable: false),
                InputJson = table.Column<string>(type: "TEXT", nullable: false),
                ResolvedInputJson = table.Column<string>(type: "TEXT", nullable: false),
                DocumentJson = table.Column<string>(type: "TEXT", nullable: false),
                MeasurementsJson = table.Column<string>(type: "TEXT", nullable: false),
                EngineRevision = table.Column<int>(type: "INTEGER", nullable: false),
                TemplateVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                SourceSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                CreatedByParentId = table.Column<string>(type: "TEXT", nullable: false),
                DraftCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ReviewedByParentId = table.Column<string>(type: "TEXT", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskSnapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaskSnapshots_Families_FamilyId",
                    column: x => x.FamilyId,
                    principalTable: "Families",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TaskTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskTemplates", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaskTemplates_Families_FamilyId",
                    column: x => x.FamilyId,
                    principalTable: "Families",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GenerationOperations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                OperationKey = table.Column<Guid>(type: "TEXT", nullable: false),
                RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                InputFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                ProfileFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                EngineRevision = table.Column<int>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                OriginalRevision = table.Column<long>(type: "INTEGER", nullable: false),
                ExpectedRevision = table.Column<long>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", nullable: false),
                Stage = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                Failure = table.Column<string>(type: "TEXT", nullable: true),
                ArtifactsJson = table.Column<string>(type: "TEXT", nullable: true),
                StepsJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GenerationOperations", x => x.Id);
                table.ForeignKey(
                    name: "FK_GenerationOperations_ActivityDrafts_DraftId",
                    column: x => x.DraftId,
                    principalTable: "ActivityDrafts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey(
                    name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                UserId = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey(
                    name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserTokens",
            columns: table => new
            {
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Value = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey(
                    name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TaskTemplateVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false),
                DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskTemplateVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_TaskTemplateVersions_TaskTemplates_TemplateId",
                    column: x => x.TemplateId,
                    principalTable: "TaskTemplates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityDrafts_FamilyId_UpdatedAtUtc",
            table: "ActivityDrafts",
            columns: new[] { "FamilyId", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserClaims_UserId",
            table: "AspNetUserClaims",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUserLogins_UserId",
            table: "AspNetUserLogins",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "AspNetUsers",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "IX_AspNetUsers_FamilyId",
            table: "AspNetUsers",
            column: "FamilyId");

        migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "AspNetUsers",
            column: "NormalizedUserName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_DraftId",
            table: "GenerationOperations",
            column: "DraftId",
            unique: true,
            filter: "Status IN ('queued', 'calling')");

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_DraftId_CreatedAtUtc",
            table: "GenerationOperations",
            columns: new[] { "DraftId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_FamilyId_OperationKey",
            table: "GenerationOperations",
            columns: new[] { "FamilyId", "OperationKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_FinishedAtUtc",
            table: "GenerationOperations",
            column: "FinishedAtUtc",
            filter: "ArtifactsJson IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_Status_CreatedAtUtc",
            table: "GenerationOperations",
            columns: new[] { "Status", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskSnapshots_FamilyId_ReviewedAtUtc",
            table: "TaskSnapshots",
            columns: new[] { "FamilyId", "ReviewedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskSnapshots_SourceDraftId",
            table: "TaskSnapshots",
            column: "SourceDraftId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TaskTemplates_FamilyId_CreatedAtUtc",
            table: "TaskTemplates",
            columns: new[] { "FamilyId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskTemplateVersions_TemplateId_Version",
            table: "TaskTemplateVersions",
            columns: new[] { "TemplateId", "Version" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AspNetUserClaims");

        migrationBuilder.DropTable(
            name: "AspNetUserLogins");

        migrationBuilder.DropTable(
            name: "AspNetUserTokens");

        migrationBuilder.DropTable(
            name: "GenerationOperations");

        migrationBuilder.DropTable(
            name: "TaskSnapshots");

        migrationBuilder.DropTable(
            name: "TaskTemplateVersions");

        migrationBuilder.DropTable(
            name: "AspNetUsers");

        migrationBuilder.DropTable(
            name: "ActivityDrafts");

        migrationBuilder.DropTable(
            name: "TaskTemplates");

        migrationBuilder.DropTable(
            name: "Families");
    }
}
