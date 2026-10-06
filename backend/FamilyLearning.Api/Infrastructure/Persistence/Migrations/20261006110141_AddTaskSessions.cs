using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTaskSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TaskSessions",
            columns: table => new
            {
                AssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                AnswersJson = table.Column<string>(type: "TEXT", nullable: false),
                EvaluationJson = table.Column<string>(type: "TEXT", nullable: true),
                ScoringPolicyVersion = table.Column<int>(type: "INTEGER", nullable: true),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                SavedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                ReviewedByParentId = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TaskSessions", x => x.AssignmentId);
                table.ForeignKey(
                    name: "FK_TaskSessions_Assignments_AssignmentId",
                    column: x => x.AssignmentId,
                    principalTable: "Assignments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TaskSessions");
    }
}
