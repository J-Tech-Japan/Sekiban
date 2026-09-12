using Microsoft.EntityFrameworkCore;

namespace Sekiban.Dcb.Postgres;

internal static class PostgresDurableSubscriptionSchema
{
    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS dcb_durable_subscriptions (
            service_id varchar(64) NOT NULL,
            subscription_name varchar(128) NOT NULL,
            initialized boolean NOT NULL,
            acknowledged_position varchar(30) NULL,
            phase varchar(32) NOT NULL,
            active_failure_position varchar(30) NULL,
            active_failure_count integer NOT NULL DEFAULT 0,
            active_failure_reason varchar(2048) NULL,
            next_attempt_at_utc timestamp with time zone NULL,
            last_halt_position varchar(30) NULL,
            last_halt_at_utc timestamp with time zone NULL,
            last_halt_reason varchar(2048) NULL,
            owner_id varchar(256) NULL,
            owner_generation bigint NOT NULL DEFAULT 0,
            lease_expires_at_utc timestamp with time zone NULL,
            created_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT pk_dcb_durable_subscriptions PRIMARY KEY (service_id, subscription_name)
        );
        CREATE INDEX IF NOT EXISTS ix_dcb_durable_subscriptions_lease
            ON dcb_durable_subscriptions (service_id, phase, lease_expires_at_utc);
        """;

    internal static Task ProvisionAsync(SekibanDcbDbContext context, CancellationToken cancellationToken = default) =>
        context.Database.ExecuteSqlRawAsync(Sql, cancellationToken);
}
