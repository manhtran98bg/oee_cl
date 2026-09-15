using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(OeeDbContext))]
[Migration("202609140002_AddMachineStateEventsAndSyncOutbox")]
public partial class AddMachineStateEventsAndSyncOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create table if not exists machine_state_event (
                event_id TEXT not null primary key,
                gateway_id TEXT not null,
                machine TEXT not null,
                order_id TEXT not null,
                session_id TEXT not null,
                state TEXT not null,
                start_at INTEGER not null,
                end_at INTEGER not null,
                duration_sec INTEGER not null,
                is_open INTEGER not null,
                created_at INTEGER not null,
                updated_at INTEGER not null
            );

            create index if not exists ix_machine_state_event_machine_session_open
            on machine_state_event(machine, session_id, is_open);

            create index if not exists ix_machine_state_event_machine_start
            on machine_state_event(machine, start_at);

            create index if not exists ix_machine_state_event_order_start
            on machine_state_event(order_id, start_at);

            create table if not exists sync_outbox (
                id INTEGER not null primary key autoincrement,
                topic TEXT not null,
                dedupe_key TEXT not null,
                endpoint_path TEXT not null,
                payload_json TEXT not null,
                status TEXT not null,
                attempt_count INTEGER not null,
                next_attempt_at INTEGER not null,
                last_error TEXT null,
                created_at INTEGER not null,
                updated_at INTEGER not null,
                synced_at INTEGER null
            );

            create unique index if not exists ux_sync_outbox_dedupe_key
            on sync_outbox(dedupe_key);

            create index if not exists ix_sync_outbox_status_next_attempt
            on sync_outbox(status, next_attempt_at);

            create index if not exists ix_sync_outbox_topic_status
            on sync_outbox(topic, status);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop table if exists sync_outbox;
            drop table if exists machine_state_event;
            """);
    }
}
