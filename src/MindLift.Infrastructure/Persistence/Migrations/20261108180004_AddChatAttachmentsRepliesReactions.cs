using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAttachmentsRepliesReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attachment_content_type",
                table: "internal_chat_messages",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "attachment_duration_ms",
                table: "internal_chat_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_key",
                table: "internal_chat_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_kind",
                table: "internal_chat_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_name",
                table: "internal_chat_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "attachment_size",
                table: "internal_chat_messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reactions",
                table: "internal_chat_messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reply_to_id",
                table: "internal_chat_messages",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment_content_type",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "attachment_duration_ms",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "attachment_key",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "attachment_kind",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "attachment_name",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "attachment_size",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "reactions",
                table: "internal_chat_messages");

            migrationBuilder.DropColumn(
                name: "reply_to_id",
                table: "internal_chat_messages");
        }
    }
}
