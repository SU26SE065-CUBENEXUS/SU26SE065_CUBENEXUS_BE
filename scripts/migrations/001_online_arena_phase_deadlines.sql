-- =========================================================
-- Migration: Online Arena Phase & Deadline Fields
-- Version: 001
-- Description: Thêm phase, deadline fields vào online_matches.
--              Thêm matchmaking cooldown fields vào online_profiles.
-- Apply on: existing databases (fresh install uses init-db.sql)
-- =========================================================

BEGIN;

-- ===== online_matches =====

ALTER TABLE online_matches
    ADD COLUMN IF NOT EXISTS phase VARCHAR(30) NOT NULL DEFAULT 'ROOM_SETUP',
    ADD COLUMN IF NOT EXISTS setup_deadline_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS ready_deadline_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS countdown_ends_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS inspection_deadline_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS solve_deadline_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS finish_check_deadline_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS cancel_reason VARCHAR(100),
    ADD COLUMN IF NOT EXISTS timeout_player_id UUID REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS elo_changed BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS setup_timeout_penalty_applied_at TIMESTAMPTZ;

-- Add check constraint for phase values
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_online_matches_phase'
    ) THEN
        ALTER TABLE online_matches
            ADD CONSTRAINT ck_online_matches_phase
            CHECK (phase IN (
                'ROOM_SETUP', 'WEBRTC_CONNECTING', 'MOBILE_TIMER_PAIRING', 'SCRAMBLE_CHECKING',
                'WAITING_READY', 'COUNTDOWN', 'INSPECTION', 'SOLVING', 'FINISH_CHECKING',
                'PENDING_EVIDENCE', 'COMPLETED', 'NEEDS_REVIEW', 'CANCELLED'
            ));
    END IF;
END $$;

-- Index for BackgroundService to find non-terminal matches
CREATE INDEX IF NOT EXISTS idx_online_matches_active_phase
ON online_matches(status_code, phase)
WHERE status_code NOT IN ('COMPLETED', 'CANCELLED', 'DRAW');

CREATE INDEX IF NOT EXISTS idx_online_matches_setup_deadline
ON online_matches(setup_deadline_at)
WHERE status_code NOT IN ('COMPLETED', 'CANCELLED', 'DRAW') AND setup_deadline_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_online_matches_timeout_player
ON online_matches(timeout_player_id)
WHERE timeout_player_id IS NOT NULL;

-- ===== online_profiles — matchmaking cooldown =====

ALTER TABLE online_profiles
    ADD COLUMN IF NOT EXISTS matchmaking_cooldown_until TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS setup_timeout_count INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS setup_timeout_window_started_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS last_setup_timeout_at TIMESTAMPTZ;

-- Index to quickly check cooldown
CREATE INDEX IF NOT EXISTS idx_online_profiles_cooldown
ON online_profiles(matchmaking_cooldown_until)
WHERE matchmaking_cooldown_until IS NOT NULL;

COMMIT;
