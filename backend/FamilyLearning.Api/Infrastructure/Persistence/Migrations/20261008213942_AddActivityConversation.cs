using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddActivityConversation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ChatJson",
            table: "ActivityDrafts",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "UndoJson",
            table: "ActivityDrafts",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ChatJson",
            table: "ActivityDrafts");

        migrationBuilder.DropColumn(
            name: "UndoJson",
            table: "ActivityDrafts");
    }
}
