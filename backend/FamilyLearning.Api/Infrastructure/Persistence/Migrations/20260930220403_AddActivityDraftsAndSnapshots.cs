using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddActivityDraftsAndSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

        migrationBuilder.CreateIndex(
            name: "IX_ActivityDrafts_FamilyId_UpdatedAtUtc",
            table: "ActivityDrafts",
            columns: new[] { "FamilyId", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskSnapshots_FamilyId_ReviewedAtUtc",
            table: "TaskSnapshots",
            columns: new[] { "FamilyId", "ReviewedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskSnapshots_SourceDraftId",
            table: "TaskSnapshots",
            column: "SourceDraftId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ActivityDrafts");

        migrationBuilder.DropTable(
            name: "TaskSnapshots");
    }
}
