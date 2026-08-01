-- Reorder users columns: phone, address after display_name and before avatar_url.
-- PostgreSQL cannot reorder columns in-place; recreate table and keep data/FKs.

BEGIN;

CREATE TABLE users_reordered (
    id UUID PRIMARY KEY,
    user_code VARCHAR(255) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255),
    display_name VARCHAR(100) NOT NULL,
    phone VARCHAR(20) NOT NULL DEFAULT '',
    address TEXT NOT NULL DEFAULT '',
    avatar_url TEXT,
    user_role VARCHAR(30) NOT NULL DEFAULT 'COMPETITOR',
    is_active BOOLEAN DEFAULT true,
    is_banned BOOLEAN DEFAULT false,
    ban_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    email_confirmed BOOLEAN NOT NULL DEFAULT false,
    email_confirmed_at TIMESTAMPTZ,
    auth_provider VARCHAR(20) NOT NULL DEFAULT 'LOCAL',
    google_sub VARCHAR(255),
    CONSTRAINT ck_users_auth_provider
        CHECK (auth_provider::text = ANY (ARRAY['LOCAL'::character varying, 'GOOGLE'::character varying]::text[]))
);

INSERT INTO users_reordered (
    id, user_code, email, password_hash, display_name, phone, address, avatar_url,
    user_role, is_active, is_banned, ban_reason, created_at, updated_at,
    email_confirmed, email_confirmed_at, auth_provider, google_sub
)
SELECT
    id, user_code, email, password_hash, display_name, phone, address, avatar_url,
    user_role, is_active, is_banned, ban_reason, created_at, updated_at,
    email_confirmed, email_confirmed_at, auth_provider, google_sub
FROM users;

-- Drop FKs that reference users
ALTER TABLE async_submissions DROP CONSTRAINT IF EXISTS async_submissions_reviewed_by_fkey;
ALTER TABLE async_submissions DROP CONSTRAINT IF EXISTS async_submissions_user_id_fkey;
ALTER TABLE async_tournaments DROP CONSTRAINT IF EXISTS async_tournaments_created_by_fkey;
ALTER TABLE disputes DROP CONSTRAINT IF EXISTS disputes_reported_by_fkey;
ALTER TABLE disputes DROP CONSTRAINT IF EXISTS disputes_resolved_by_fkey;
ALTER TABLE elo_config DROP CONSTRAINT IF EXISTS elo_config_updated_by_fkey;
ALTER TABLE fraud_reports DROP CONSTRAINT IF EXISTS fraud_reports_accused_user_id_fkey;
ALTER TABLE fraud_reports DROP CONSTRAINT IF EXISTS fraud_reports_reported_by_fkey;
ALTER TABLE fraud_reports DROP CONSTRAINT IF EXISTS fraud_reports_reported_user_id_fkey;
ALTER TABLE fraud_reports DROP CONSTRAINT IF EXISTS fraud_reports_reporter_user_id_fkey;
ALTER TABLE fraud_reports DROP CONSTRAINT IF EXISTS fraud_reports_resolved_by_admin_id_fkey;
ALTER TABLE fraud_reports DROP CONSTRAINT IF EXISTS fraud_reports_reviewed_by_fkey;
ALTER TABLE matchmaking_queue DROP CONSTRAINT IF EXISTS matchmaking_queue_user_id_fkey;
ALTER TABLE mobile_timer_sessions DROP CONSTRAINT IF EXISTS mobile_timer_sessions_user_id_fkey;
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_user_id_fkey;
ALTER TABLE online_match_ai_checks DROP CONSTRAINT IF EXISTS online_match_ai_checks_player_id_fkey;
ALTER TABLE online_match_audit_logs DROP CONSTRAINT IF EXISTS online_match_audit_logs_player_id_fkey;
ALTER TABLE online_match_confirmations DROP CONSTRAINT IF EXISTS online_match_confirmations_player1_user_id_fkey;
ALTER TABLE online_match_confirmations DROP CONSTRAINT IF EXISTS online_match_confirmations_player2_user_id_fkey;
ALTER TABLE online_match_video_evidence DROP CONSTRAINT IF EXISTS online_match_video_evidence_player_id_fkey;
ALTER TABLE online_matches DROP CONSTRAINT IF EXISTS online_matches_player1_id_fkey;
ALTER TABLE online_matches DROP CONSTRAINT IF EXISTS online_matches_player2_id_fkey;
ALTER TABLE online_matches DROP CONSTRAINT IF EXISTS online_matches_timeout_player_id_fkey;
ALTER TABLE online_matches DROP CONSTRAINT IF EXISTS online_matches_winner_id_fkey;
ALTER TABLE online_profiles DROP CONSTRAINT IF EXISTS online_profiles_user_id_fkey;
ALTER TABLE practice_sessions DROP CONSTRAINT IF EXISTS practice_sessions_user_id_fkey;
ALTER TABLE refresh_tokens DROP CONSTRAINT IF EXISTS refresh_tokens_user_id_fkey;
ALTER TABLE registrations DROP CONSTRAINT IF EXISTS registrations_user_id_fkey;
ALTER TABLE results DROP CONSTRAINT IF EXISTS results_judged_by_fkey;
ALTER TABLE scramble_sets DROP CONSTRAINT IF EXISTS scramble_sets_generated_by_fkey;
ALTER TABLE tournament_managers DROP CONSTRAINT IF EXISTS tournament_managers_user_id_fkey;
ALTER TABLE tournaments DROP CONSTRAINT IF EXISTS tournaments_created_by_fkey;
ALTER TABLE user_tokens DROP CONSTRAINT IF EXISTS user_tokens_user_id_fkey;

DROP TABLE users;
ALTER TABLE users_reordered RENAME TO users;

CREATE UNIQUE INDEX uq_users_google_sub ON users (google_sub) WHERE google_sub IS NOT NULL;

-- Recreate FKs
ALTER TABLE async_submissions ADD CONSTRAINT async_submissions_reviewed_by_fkey FOREIGN KEY (reviewed_by) REFERENCES users(id);
ALTER TABLE async_submissions ADD CONSTRAINT async_submissions_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE async_tournaments ADD CONSTRAINT async_tournaments_created_by_fkey FOREIGN KEY (created_by) REFERENCES users(id);
ALTER TABLE disputes ADD CONSTRAINT disputes_reported_by_fkey FOREIGN KEY (reported_by) REFERENCES users(id);
ALTER TABLE disputes ADD CONSTRAINT disputes_resolved_by_fkey FOREIGN KEY (resolved_by) REFERENCES users(id);
ALTER TABLE elo_config ADD CONSTRAINT elo_config_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES users(id);
ALTER TABLE fraud_reports ADD CONSTRAINT fraud_reports_accused_user_id_fkey FOREIGN KEY (accused_user_id) REFERENCES users(id);
ALTER TABLE fraud_reports ADD CONSTRAINT fraud_reports_reported_by_fkey FOREIGN KEY (reported_by) REFERENCES users(id);
ALTER TABLE fraud_reports ADD CONSTRAINT fraud_reports_reported_user_id_fkey FOREIGN KEY (reported_user_id) REFERENCES users(id) ON DELETE RESTRICT;
ALTER TABLE fraud_reports ADD CONSTRAINT fraud_reports_reporter_user_id_fkey FOREIGN KEY (reporter_user_id) REFERENCES users(id) ON DELETE RESTRICT;
ALTER TABLE fraud_reports ADD CONSTRAINT fraud_reports_resolved_by_admin_id_fkey FOREIGN KEY (resolved_by_admin_id) REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE fraud_reports ADD CONSTRAINT fraud_reports_reviewed_by_fkey FOREIGN KEY (reviewed_by) REFERENCES users(id);
ALTER TABLE matchmaking_queue ADD CONSTRAINT matchmaking_queue_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE mobile_timer_sessions ADD CONSTRAINT mobile_timer_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE notifications ADD CONSTRAINT notifications_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE online_match_ai_checks ADD CONSTRAINT online_match_ai_checks_player_id_fkey FOREIGN KEY (player_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE online_match_audit_logs ADD CONSTRAINT online_match_audit_logs_player_id_fkey FOREIGN KEY (player_id) REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE online_match_confirmations ADD CONSTRAINT online_match_confirmations_player1_user_id_fkey FOREIGN KEY (player1_user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE online_match_confirmations ADD CONSTRAINT online_match_confirmations_player2_user_id_fkey FOREIGN KEY (player2_user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE online_match_video_evidence ADD CONSTRAINT online_match_video_evidence_player_id_fkey FOREIGN KEY (player_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE online_matches ADD CONSTRAINT online_matches_player1_id_fkey FOREIGN KEY (player1_id) REFERENCES users(id);
ALTER TABLE online_matches ADD CONSTRAINT online_matches_player2_id_fkey FOREIGN KEY (player2_id) REFERENCES users(id);
ALTER TABLE online_matches ADD CONSTRAINT online_matches_timeout_player_id_fkey FOREIGN KEY (timeout_player_id) REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE online_matches ADD CONSTRAINT online_matches_winner_id_fkey FOREIGN KEY (winner_id) REFERENCES users(id);
ALTER TABLE online_profiles ADD CONSTRAINT online_profiles_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE practice_sessions ADD CONSTRAINT practice_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE refresh_tokens ADD CONSTRAINT refresh_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE registrations ADD CONSTRAINT registrations_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE results ADD CONSTRAINT results_judged_by_fkey FOREIGN KEY (judged_by) REFERENCES users(id);
ALTER TABLE scramble_sets ADD CONSTRAINT scramble_sets_generated_by_fkey FOREIGN KEY (generated_by) REFERENCES users(id);
ALTER TABLE tournament_managers ADD CONSTRAINT tournament_managers_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);
ALTER TABLE tournaments ADD CONSTRAINT tournaments_created_by_fkey FOREIGN KEY (created_by) REFERENCES users(id);
ALTER TABLE user_tokens ADD CONSTRAINT user_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES users(id);

COMMIT;
