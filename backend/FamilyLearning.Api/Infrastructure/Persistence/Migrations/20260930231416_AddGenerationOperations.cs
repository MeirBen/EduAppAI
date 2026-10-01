using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddGenerationOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GenerationOperations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                DraftId = table.Column<Guid>(type: "TEXT", nullable: false),
                OperationKey = table.Column<Guid>(type: "TEXT", nullable: false),
                RequestFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                InputFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                ProfileFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                EngineRevision = table.Column<int>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                OriginalRevision = table.Column<long>(type: "INTEGER", nullable: false),
                ExpectedRevision = table.Column<long>(type: "INTEGER", nullable: false),
                Kind = table.Column<string>(type: "TEXT", nullable: false),
                Stage = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                Failure = table.Column<string>(type: "TEXT", nullable: true),
                ArtifactsJson = table.Column<string>(type: "TEXT", nullable: true),
                StepsJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GenerationOperations", x => x.Id);
                table.ForeignKey(
                    name: "FK_GenerationOperations_ActivityDrafts_DraftId",
                    column: x => x.DraftId,
                    principalTable: "ActivityDrafts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_DraftId",
            table: "GenerationOperations",
            column: "DraftId",
            unique: true,
            filter: "Status IN ('queued', 'calling')");

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_DraftId_CreatedAtUtc",
            table: "GenerationOperations",
            columns: new[] { "DraftId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_FamilyId_OperationKey",
            table: "GenerationOperations",
            columns: new[] { "FamilyId", "OperationKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_FinishedAtUtc",
            table: "GenerationOperations",
            column: "FinishedAtUtc",
            filter: "ArtifactsJson IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_GenerationOperations_Status_CreatedAtUtc",
            table: "GenerationOperations",
            columns: new[] { "Status", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "GenerationOperations");
    }
}
