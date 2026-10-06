using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddChildProfileDetails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Age",
            table: "Children",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "AgeConfirmedAtUtc",
            table: "Children",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Grade",
            table: "Children",
            type: "TEXT",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Age",
            table: "Children");

        migrationBuilder.DropColumn(
            name: "AgeConfirmedAtUtc",
            table: "Children");

        migrationBuilder.DropColumn(
            name: "Grade",
            table: "Children");
    }
}
