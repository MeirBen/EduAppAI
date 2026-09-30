using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SharedTaskInput : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Prototype learning data uses the retired blueprint and task-input contracts.
        // Parent accounts and family ownership remain intact.
        migrationBuilder.Sql("DELETE FROM TaskInstances; DELETE FROM TaskTemplateVersions; DELETE FROM TaskTemplates;");
        migrationBuilder.RenameColumn(
            name: "ParametersJson",
            table: "TaskInstances",
            newName: "InputJson");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "InputJson",
            table: "TaskInstances",
            newName: "ParametersJson");
    }
}
