using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveUnusedActivityState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "InputJson",
            table: "TaskSnapshots");

        migrationBuilder.DropColumn(
            name: "ResolvedInputJson",
            table: "TaskSnapshots");

        migrationBuilder.DropColumn(
            name: "InputFingerprint",
            table: "GenerationOperations");

        migrationBuilder.DropColumn(
            name: "InputJson",
            table: "ActivityDrafts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "InputJson",
            table: "TaskSnapshots",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ResolvedInputJson",
            table: "TaskSnapshots",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "InputFingerprint",
            table: "GenerationOperations",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "InputJson",
            table: "ActivityDrafts",
            type: "TEXT",
            nullable: false,
            defaultValue: "");
    }
}
