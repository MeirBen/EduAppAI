using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <summary>Orders library pages by timestamp, then identifier, straight from each family's index.</summary>
public partial class LibraryPageOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaskSnapshots_FamilyId_ReviewedAtUtc",
            table: "TaskSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_ActivityDrafts_FamilyId_UpdatedAtUtc",
            table: "ActivityDrafts");

        migrationBuilder.CreateIndex(
            name: "IX_TaskSnapshots_FamilyId_ReviewedAtUtc_Id",
            table: "TaskSnapshots",
            columns: new[] { "FamilyId", "ReviewedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityDrafts_FamilyId_UpdatedAtUtc_Id",
            table: "ActivityDrafts",
            columns: new[] { "FamilyId", "UpdatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaskSnapshots_FamilyId_ReviewedAtUtc_Id",
            table: "TaskSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_ActivityDrafts_FamilyId_UpdatedAtUtc_Id",
            table: "ActivityDrafts");

        migrationBuilder.CreateIndex(
            name: "IX_TaskSnapshots_FamilyId_ReviewedAtUtc",
            table: "TaskSnapshots",
            columns: new[] { "FamilyId", "ReviewedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityDrafts_FamilyId_UpdatedAtUtc",
            table: "ActivityDrafts",
            columns: new[] { "FamilyId", "UpdatedAtUtc" });
    }
}
