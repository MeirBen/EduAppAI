using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyLearning.Api.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddChildAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Children",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Children", x => x.Id);
                table.ForeignKey(
                    name: "FK_Children_Families_FamilyId",
                    column: x => x.FamilyId,
                    principalTable: "Families",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ChildActivations",
            columns: table => new
            {
                ChildId = table.Column<Guid>(type: "TEXT", nullable: false),
                CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DeviceLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ConsumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChildActivations", x => x.ChildId);
                table.ForeignKey(
                    name: "FK_ChildActivations_Children_ChildId",
                    column: x => x.ChildId,
                    principalTable: "Children",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ChildDeviceGrants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ChildId = table.Column<Guid>(type: "TEXT", nullable: false),
                DeviceLabel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChildDeviceGrants", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChildDeviceGrants_Children_ChildId",
                    column: x => x.ChildId,
                    principalTable: "Children",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChildActivations_CodeHash",
            table: "ChildActivations",
            column: "CodeHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ChildDeviceGrants_ChildId_CreatedAtUtc_Id",
            table: "ChildDeviceGrants",
            columns: new[] { "ChildId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Children_FamilyId_CreatedAtUtc_Id",
            table: "Children",
            columns: new[] { "FamilyId", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChildActivations");

        migrationBuilder.DropTable(
            name: "ChildDeviceGrants");

        migrationBuilder.DropTable(
            name: "Children");
    }
}
