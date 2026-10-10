using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <summary>Moves every stored plan, including undo checkpoints and operation evidence, to schema 3 with empty title/instruction guidance.</summary>
public partial class DocumentGuidance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE ActivityDrafts SET PlanJson = json_set(PlanJson, '$.documentGuidance', '', '$.schemaVersion', 3);");
        migrationBuilder.Sql("UPDATE ActivityDrafts SET UndoJson = json_set(UndoJson, '$.plan.documentGuidance', '', '$.plan.schemaVersion', 3) WHERE UndoJson IS NOT NULL;");
        migrationBuilder.Sql("UPDATE TaskSnapshots SET PlanJson = json_set(PlanJson, '$.documentGuidance', '', '$.schemaVersion', 3);");
        migrationBuilder.Sql("UPDATE GenerationOperations SET ArtifactsJson = json_set(ArtifactsJson, '$.plan.documentGuidance', '', '$.plan.schemaVersion', 3) WHERE json_type(ArtifactsJson, '$.plan') = 'object';");
        migrationBuilder.Sql("UPDATE GenerationOperations SET ArtifactsJson = json_set(ArtifactsJson, '$.input.documentGuidance', '', '$.input.schemaVersion', 3) WHERE json_type(ArtifactsJson, '$.input') = 'object';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
