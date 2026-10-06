using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddChildProfileTimestamps : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAtUtc",
            table: "Children",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "UpdatedAtUtc",
            table: "Children");
    }
}
