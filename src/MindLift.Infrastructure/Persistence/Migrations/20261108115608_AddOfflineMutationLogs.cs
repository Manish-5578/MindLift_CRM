using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindLift.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the tables the model has but no migration ever created: internal chat messages (an earlier migration file for it had no migration
    /// attributes, so it was never applied), mobile devices and offline mutation logs. Written by hand rather than generated, because the generated
    /// version also dropped and re-added columns the database already has. Every statement is IF NOT EXISTS, so it is safe on a database where
    /// someone already created these tables.
    /// </summary>
    public partial class AddOfflineMutationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE lead_notes ALTER COLUMN updated_at DROP NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS internal_chat_messages (
                    id uuid NOT NULL,
                    sender_id uuid NOT NULL,
                    sender_name character varying(200) NOT NULL,
                    sender_email character varying(320),
                    recipient_id uuid,
                    channel_name character varying(100),
                    message character varying(4000) NOT NULL,
                    created_at_utc timestamp with time zone NOT NULL,
                    is_read boolean NOT NULL,
                    tenant_id uuid NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    created_by_id uuid,
                    updated_at timestamp with time zone,
                    updated_by_id uuid,
                    is_deleted boolean NOT NULL,
                    deleted_at timestamp with time zone,
                    version integer NOT NULL,
                    CONSTRAINT pk_internal_chat_messages PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS mobile_devices (
                    id uuid NOT NULL,
                    user_id uuid NOT NULL,
                    installation_id character varying(100) NOT NULL,
                    platform character varying(20) NOT NULL,
                    environment character varying(20) NOT NULL,
                    provider character varying(20) NOT NULL,
                    provider_version character varying(20) NOT NULL,
                    token_ciphertext character varying(4000) NOT NULL,
                    token_hash character varying(128) NOT NULL,
                    app_version character varying(50) NOT NULL,
                    locale character varying(20) NOT NULL,
                    time_zone_id character varying(100) NOT NULL,
                    notifications_allowed boolean NOT NULL,
                    last_seen_at_utc timestamp with time zone NOT NULL,
                    revoked_at_utc timestamp with time zone,
                    last_push_succeeded_at_utc timestamp with time zone,
                    last_push_failure_code text,
                    failure_count integer NOT NULL,
                    tenant_id uuid NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    created_by_id uuid,
                    updated_at timestamp with time zone,
                    updated_by_id uuid,
                    is_deleted boolean NOT NULL,
                    deleted_at timestamp with time zone,
                    version integer NOT NULL,
                    CONSTRAINT pk_mobile_devices PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS offline_mutation_logs (
                    id uuid NOT NULL,
                    client_mutation_id character varying(100) NOT NULL,
                    batch_id character varying(100) NOT NULL,
                    entity_type character varying(50) NOT NULL,
                    operation character varying(50) NOT NULL,
                    status character varying(30) NOT NULL,
                    server_entity_id uuid,
                    new_version integer,
                    error_message text,
                    result_json text,
                    completed_at_utc timestamp with time zone NOT NULL,
                    tenant_id uuid NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    created_by_id uuid,
                    updated_at timestamp with time zone,
                    updated_by_id uuid,
                    is_deleted boolean NOT NULL,
                    deleted_at timestamp with time zone,
                    version integer NOT NULL,
                    CONSTRAINT pk_offline_mutation_logs PRIMARY KEY (id)
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_lead_notes_tenant_id ON lead_notes (tenant_id);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_internal_chat_messages_tenant_id ON internal_chat_messages (tenant_id);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_internal_chat_messages_tenant_id_channel_name_created_at_utc ON internal_chat_messages (tenant_id, channel_name, created_at_utc);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_internal_chat_messages_tenant_id_recipient_id_sender_id_cre ON internal_chat_messages (tenant_id, recipient_id, sender_id, created_at_utc);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_mobile_devices_tenant_id ON mobile_devices (tenant_id);
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_mobile_devices_tenant_id_user_id_installation_id ON mobile_devices (tenant_id, user_id, installation_id);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_mobile_devices_token_hash ON mobile_devices (token_hash);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_offline_mutation_logs_tenant_id ON offline_mutation_logs (tenant_id);
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_offline_mutation_logs_tenant_id_client_mutation_id ON offline_mutation_logs (tenant_id, client_mutation_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS offline_mutation_logs;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS mobile_devices;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS internal_chat_messages;");
        }
    }
}
