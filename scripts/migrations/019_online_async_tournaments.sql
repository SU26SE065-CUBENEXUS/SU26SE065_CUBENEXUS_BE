-- 019_online_async_tournaments.sql
-- Migration script to support Asynchronous Online Tournaments (AO1 format)

-- 1. Extend tournaments table with columns for online async format
ALTER TABLE tournaments
    ADD COLUMN IF NOT EXISTS tournament_type VARCHAR(32) NOT NULL DEFAULT 'OFFLINE',
    ADD COLUMN IF NOT EXISTS format_code VARCHAR(16) NOT NULL DEFAULT 'AO1',
    ADD COLUMN IF NOT EXISTS puzzle_type_id UUID REFERENCES puzzle_types(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS scramble_sequence TEXT,
    ADD COLUMN IF NOT EXISTS attempt_time_limit_ms INT NOT NULL DEFAULT 300000;

-- 2. Create online_async_attempts table
CREATE TABLE IF NOT EXISTS online_async_attempts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tournament_id UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    scramble_sequence TEXT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'INITIALIZED', -- INITIALIZED | SCRAMBLE_VERIFIED | SOLVING | COMPLETED | EXPIRED
    review_status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REVIEW', -- PENDING_REVIEW | APPROVED | REJECTED
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    hand_timer_started_at TIMESTAMPTZ,
    solve_started_at TIMESTAMPTZ,
    solve_finished_at TIMESTAMPTZ,
    raw_time_ms INT,
    penalty_time_ms INT NOT NULL DEFAULT 0,
    penalty_code VARCHAR(16) NOT NULL DEFAULT 'NONE', -- NONE | PLUS2 | DNF
    is_dnf BOOLEAN NOT NULL DEFAULT FALSE,
    final_time_ms INT,
    scramble_check_status VARCHAR(16) NOT NULL DEFAULT 'PENDING', -- PENDING | PASSED | FAILED
    finish_check_status VARCHAR(16) NOT NULL DEFAULT 'PENDING', -- PENDING | PASSED | FAILED
    video_evidence_url TEXT,
    scramble_evidence_json TEXT,
    finish_evidence_json TEXT,
    reviewed_by UUID REFERENCES users(id) ON DELETE SET NULL,
    reviewed_at TIMESTAMPTZ,
    review_note TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 3. Unique Index to enforce Format AO1: Exactly 1 attempt per competitor per tournament
CREATE UNIQUE INDEX IF NOT EXISTS ix_online_async_attempts_tournament_user 
    ON online_async_attempts(tournament_id, user_id);

-- 4. Covering Leaderboard Index for fast O(1) ranking queries
CREATE INDEX IF NOT EXISTS ix_online_async_attempts_leaderboard 
    ON online_async_attempts(tournament_id, review_status, is_dnf, final_time_ms);
