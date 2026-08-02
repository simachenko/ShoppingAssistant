using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductAdvisor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSessionUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                schema: "advisor",
                table: "conversation_sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_sessions_UserId",
                schema: "advisor",
                table: "conversation_sessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversation_sessions_UserId",
                schema: "advisor",
                table: "conversation_sessions");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "advisor",
                table: "conversation_sessions");
        }
    }
}
