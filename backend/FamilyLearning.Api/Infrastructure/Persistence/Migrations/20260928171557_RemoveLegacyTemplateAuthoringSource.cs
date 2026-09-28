using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveLegacyTemplateAuthoringSource : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Some databases applied AiOnlyTemplates before it removed this column.
        // SQLite rebuilds from the target model, so this also works when the column is already absent.
        migrationBuilder.DropColumn(name: "AuthoringSource", table: "TaskTemplateVersions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The preceding model also omits AuthoringSource; rollback must not restore schema drift.
    }
}
