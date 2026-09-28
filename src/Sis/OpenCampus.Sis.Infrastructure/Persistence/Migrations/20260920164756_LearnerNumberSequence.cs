using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCampus.Sis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LearnerNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "LearnerNumberSequence",
                schema: "sis",
                startValue: 1000L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "LearnerNumberSequence",
                schema: "sis");
        }
    }
}
