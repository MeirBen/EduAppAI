using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAssignments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ArchivedAtUtc",
            table: "TaskSnapshots",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_TaskSnapshots_FamilyId_Id",
            table: "TaskSnapshots",
            columns: new[] { "FamilyId", "Id" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_Children_FamilyId_Id",
            table: "Children",
            columns: new[] { "FamilyId", "Id" });

        migrationBuilder.CreateTable(
            name: "Assignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                ChildId = table.Column<Guid>(type: "TEXT", nullable: false),
                SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                WithdrawnAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Assignments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Assignments_Children_FamilyId_ChildId",
                    columns: x => new { x.FamilyId, x.ChildId },
                    principalTable: "Children",
                    principalColumns: new[] { "FamilyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Assignments_TaskSnapshots_FamilyId_SnapshotId",
                    columns: x => new { x.FamilyId, x.SnapshotId },
                    principalTable: "TaskSnapshots",
                    principalColumns: new[] { "FamilyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_ChildId_CreatedAtUtc_Id",
            table: "Assignments",
            columns: new[] { "ChildId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_ChildId_SnapshotId",
            table: "Assignments",
            columns: new[] { "ChildId", "SnapshotId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_ChildId_Status_CreatedAtUtc_Id",
            table: "Assignments",
            columns: new[] { "ChildId", "Status", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_FamilyId_ChildId",
            table: "Assignments",
            columns: new[] { "FamilyId", "ChildId" });

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_FamilyId_CreatedAtUtc_Id",
            table: "Assignments",
            columns: new[] { "FamilyId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_FamilyId_SnapshotId",
            table: "Assignments",
            columns: new[] { "FamilyId", "SnapshotId" });

        migrationBuilder.CreateIndex(
            name: "IX_Assignments_FamilyId_Status_CreatedAtUtc_Id",
            table: "Assignments",
            columns: new[] { "FamilyId", "Status", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Assignments");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_TaskSnapshots_FamilyId_Id",
            table: "TaskSnapshots");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_Children_FamilyId_Id",
            table: "Children");

        migrationBuilder.DropColumn(
            name: "ArchivedAtUtc",
            table: "TaskSnapshots");
    }
}
