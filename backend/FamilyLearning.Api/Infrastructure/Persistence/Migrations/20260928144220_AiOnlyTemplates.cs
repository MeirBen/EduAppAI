using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AiOnlyTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The AI-only schema replaces earlier prototypes. Remove incompatible learning data,
        // including all revisions of an affected template, while retaining Identity and families.
        migrationBuilder.Sql("""
            DELETE FROM TaskInstances WHERE TemplateVersionId IN (
                SELECT Id FROM TaskTemplateVersions WHERE TemplateId IN (
                    SELECT TemplateId FROM TaskTemplateVersions WHERE json_extract(DefinitionJson, '$.schemaVersion') <> 2
                )
            );
            DELETE FROM TaskTemplateVersions WHERE TemplateId IN (
                SELECT TemplateId FROM TaskTemplateVersions WHERE json_extract(DefinitionJson, '$.schemaVersion') <> 2
            );
            DELETE FROM TaskTemplates WHERE NOT EXISTS (
                SELECT 1 FROM TaskTemplateVersions WHERE TemplateId = TaskTemplates.Id
            );
            """);

        migrationBuilder.DropColumn(
            name: "GenerationMethod",
            table: "TaskInstances");

        migrationBuilder.DropColumn(name: "AuthoringSource", table: "TaskTemplateVersions");

        migrationBuilder.DropColumn(
            name: "Seed",
            table: "TaskInstances");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AuthoringSource", table: "TaskTemplateVersions", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "GenerationMethod",
            table: "TaskInstances",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "Seed",
            table: "TaskInstances",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
    }
}
