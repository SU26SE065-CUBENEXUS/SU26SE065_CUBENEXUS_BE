-- Adds columns present in init-db but not covered by 002-005 (legacy DB sync).
-- Safe when online_matches is empty; profile FKs are required for match creation.

ALTER TABLE online_matches
    ADD COLUMN IF NOT EXISTS player1_profile_id uuid NULL REFERENCES online_profiles(id),
    ADD COLUMN IF NOT EXISTS player2_profile_id uuid NULL REFERENCES online_profiles(id),
    ADD COLUMN IF NOT EXISTS player1_camera_ready boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player2_camera_ready boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player1_timer_ready boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player2_timer_ready boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player1_ready boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player2_ready boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player1_is_dnf boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player2_is_dnf boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player1_result_status varchar(20) NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS player2_result_status varchar(20) NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS scramble_revealed_at timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS player1_finished_at timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS player2_finished_at timestamp with time zone NULL;

-- Backfill profile ids from users when legacy rows exist without profiles.
UPDATE online_matches m
SET player1_profile_id = COALESCE(
        m.player1_profile_id,
        (SELECT p.id FROM online_profiles p WHERE p.user_id = m.player1_id LIMIT 1)
    ),
    player2_profile_id = COALESCE(
        m.player2_profile_id,
        (SELECT p.id FROM online_profiles p WHERE p.user_id = m.player2_id LIMIT 1)
    )
WHERE player1_profile_id IS NULL OR player2_profile_id IS NULL;

ALTER TABLE online_matches
    ALTER COLUMN player1_profile_id SET NOT NULL,
    ALTER COLUMN player2_profile_id SET NOT NULL;

ALTER TABLE online_matches
    DROP CONSTRAINT IF EXISTS ck_online_matches_profiles,
    DROP CONSTRAINT IF EXISTS ck_player1_result_status,
    DROP CONSTRAINT IF EXISTS ck_player2_result_status;

ALTER TABLE online_matches
    ADD CONSTRAINT ck_online_matches_profiles
        CHECK (player1_profile_id <> player2_profile_id),
    ADD CONSTRAINT ck_player1_result_status
        CHECK (player1_result_status IN ('PENDING', 'VALID', 'DNF', 'DISCONNECTED', 'REPORTED')),
    ADD CONSTRAINT ck_player2_result_status
        CHECK (player2_result_status IN ('PENDING', 'VALID', 'DNF', 'DISCONNECTED', 'REPORTED'));

CREATE INDEX IF NOT EXISTS idx_online_matches_player1_profile
ON online_matches(player1_profile_id);

CREATE INDEX IF NOT EXISTS idx_online_matches_player2_profile
ON online_matches(player2_profile_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_online_matches_qr_session_code
ON online_matches(qr_session_code)
WHERE qr_session_code IS NOT NULL;
