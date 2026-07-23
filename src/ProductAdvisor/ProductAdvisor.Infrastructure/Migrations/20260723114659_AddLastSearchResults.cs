using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductAdvisor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastSearchResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSearchResults",
                schema: "advisor",
                table: "conversation_sessions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSearchResults",
                schema: "advisor",
                table: "conversation_sessions");
        }
    }
}
