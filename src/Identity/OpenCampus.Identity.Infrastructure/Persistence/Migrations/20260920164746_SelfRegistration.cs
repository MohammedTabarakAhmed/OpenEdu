using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCampus.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelfRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAtUtc",
                schema: "identity",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationStatus",
                schema: "identity",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "RequestedRole",
                schema: "identity",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationTokenExpiresAtUtc",
                schema: "identity",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationTokenHash",
                schema: "identity",
                table: "Users",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationTokenIssuedAtUtc",
                schema: "identity",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RegistrationStatus",
                schema: "identity",
                table: "Users",
                column: "RegistrationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Users_VerificationTokenHash",
                schema: "identity",
                table: "Users",
                column: "VerificationTokenHash",
                unique: true,
                filter: "[VerificationTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_RegistrationStatus",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_VerificationTokenHash",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAtUtc",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RegistrationStatus",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RequestedRole",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerificationTokenExpiresAtUtc",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerificationTokenHash",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerificationTokenIssuedAtUtc",
                schema: "identity",
                table: "Users");
        }
    }
}
