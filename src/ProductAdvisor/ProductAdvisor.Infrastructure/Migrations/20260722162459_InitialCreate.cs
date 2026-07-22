using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductAdvisor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "advisor");

            migrationBuilder.CreateTable(
                name: "conversation_sessions",
                schema: "advisor",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    CurrentRequirement = table.Column<string>(type: "jsonb", nullable: false),
                    Messages = table.Column<string>(type: "jsonb", nullable: true),
                    PendingClarification = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_sessions", x => x.SessionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_sessions",
                schema: "advisor");
        }
    }
}
