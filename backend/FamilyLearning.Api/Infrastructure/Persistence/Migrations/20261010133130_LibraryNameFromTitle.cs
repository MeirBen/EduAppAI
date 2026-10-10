using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <summary>Lists existing drafts by their learner title once content has one, as every later save does.</summary>
public partial class LibraryNameFromTitle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        "UPDATE ActivityDrafts SET Name = json_extract(DocumentJson, '$.title') WHERE trim(coalesce(json_extract(DocumentJson, '$.title'), '')) <> '';");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
