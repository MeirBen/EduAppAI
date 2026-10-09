using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ActivityOnly : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Normal startup/migration must never silently perform the coordinated learning reset.
        migrationBuilder.Sql("""
            CREATE TABLE __ActivityOnlyCutoverGuard (
                LearningRecords INTEGER CONSTRAINT "Stop the app and run --activity-only-cutover before migrating" CHECK (LearningRecords = 0)
            );
            INSERT INTO __ActivityOnlyCutoverGuard
            SELECT EXISTS(SELECT 1 FROM TaskSessions) OR EXISTS(SELECT 1 FROM Assignments)
                OR EXISTS(SELECT 1 FROM ChildDeviceGrants) OR EXISTS(SELECT 1 FROM ChildActivations)
                OR EXISTS(SELECT 1 FROM Children) OR EXISTS(SELECT 1 FROM GenerationOperations)
                OR EXISTS(SELECT 1 FROM ActivityDrafts) OR EXISTS(SELECT 1 FROM TaskSnapshots)
                OR EXISTS(SELECT 1 FROM TaskTemplateVersions) OR EXISTS(SELECT 1 FROM TaskTemplates);
            DROP TABLE __ActivityOnlyCutoverGuard;
            """);

        migrationBuilder.DropTable(
            name: "TaskTemplateVersions");

        migrationBuilder.DropTable(
            name: "TaskTemplates");

        migrationBuilder.DropColumn(
            name: "TemplateVersionId",
            table: "TaskSnapshots");

        migrationBuilder.DropColumn(
            name: "TemplateVersionId",
            table: "ActivityDrafts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TemplateVersionId",
            table: "TaskSnapshots",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TemplateVersionId",
            table: "ActivityDrafts",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "TaskTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
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
            name: "TaskTemplateVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
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
            name: "IX_TaskTemplates_FamilyId_CreatedAtUtc",
            table: "TaskTemplates",
            columns: new[] { "FamilyId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskTemplateVersions_TemplateId_Version",
            table: "TaskTemplateVersions",
            columns: new[] { "TemplateId", "Version" },
            unique: true);
    }
}
