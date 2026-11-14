using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations;

public partial class RenameAuthenticationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preserve table OIDs, grants, indexes, foreign keys and every authentication value.
        // A busy database fails quickly; the entire migration is transactional.
        migrationBuilder.Sql("""
            SET LOCAL lock_timeout = '5s';
            DO $$ BEGIN
                IF to_regclass('public.users') IS NOT NULL OR to_regclass('public.sessions') IS NOT NULL THEN
                    RAISE EXCEPTION 'Authentication rename targets are occupied; review before migrating.';
                END IF;
            END $$;
            LOCK TABLE public.ecocrop_users, public.ecocrop_sessions IN ACCESS EXCLUSIVE MODE;
            CREATE TEMP TABLE auth_users_before ON COMMIT DROP AS TABLE public.ecocrop_users;
            CREATE TEMP TABLE auth_sessions_before ON COMMIT DROP AS TABLE public.ecocrop_sessions;
            ALTER TABLE public.ecocrop_users RENAME TO users;
            ALTER TABLE public.ecocrop_sessions RENAME TO sessions;
            ALTER TABLE public.users RENAME CONSTRAINT pk_ecocrop_users TO pk_users;
            ALTER TABLE public.sessions RENAME CONSTRAINT pk_ecocrop_sessions TO pk_sessions;
            ALTER TABLE public.sessions RENAME CONSTRAINT fk_ecocrop_sessions_ecocrop_users_user_id TO fk_sessions_users_user_id;
            ALTER INDEX public.ix_ecocrop_users_email RENAME TO ix_users_email;
            ALTER INDEX public.ix_ecocrop_sessions_user_id RENAME TO ix_sessions_user_id;
            ALTER INDEX public.ix_ecocrop_sessions_expires_at_utc RENAME TO ix_sessions_expires_at_utc;
            DO $$ BEGIN
                IF EXISTS ((TABLE auth_users_before EXCEPT ALL TABLE public.users) UNION ALL (TABLE public.users EXCEPT ALL TABLE auth_users_before))
                   OR EXISTS ((TABLE auth_sessions_before EXCEPT ALL TABLE public.sessions) UNION ALL (TABLE public.sessions EXCEPT ALL TABLE auth_sessions_before)) THEN
                    RAISE EXCEPTION 'Authentication preservation check failed; transaction rolled back.';
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET LOCAL lock_timeout = '5s';
            ALTER TABLE public.sessions RENAME CONSTRAINT fk_sessions_users_user_id TO fk_ecocrop_sessions_ecocrop_users_user_id;
            ALTER TABLE public.sessions RENAME CONSTRAINT pk_sessions TO pk_ecocrop_sessions;
            ALTER TABLE public.users RENAME CONSTRAINT pk_users TO pk_ecocrop_users;
            ALTER INDEX public.ix_sessions_user_id RENAME TO ix_ecocrop_sessions_user_id;
            ALTER INDEX public.ix_sessions_expires_at_utc RENAME TO ix_ecocrop_sessions_expires_at_utc;
            ALTER INDEX public.ix_users_email RENAME TO ix_ecocrop_users_email;
            ALTER TABLE public.sessions RENAME TO ecocrop_sessions;
            ALTER TABLE public.users RENAME TO ecocrop_users;
            """);
    }
}
