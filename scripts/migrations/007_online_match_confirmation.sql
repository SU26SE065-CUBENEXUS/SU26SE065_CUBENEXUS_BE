-- =========================================================
-- Migration 007: Online Match Confirmation (60s confirm flow)
-- =========================================================
-- Run after 006_online_matches_profile_and_readiness.sql
-- =========================================================

BEGIN;

-- ===== 1. matchmaking_queue: add CONFIRMING status =====
-- Drop the old check constraint and recreate with CONFIRMING
ALTER TABLE matchmaking_queue
    DROP CONSTRAINT IF EXISTS ck_matchmaking_queue_status;

ALTER TABLE matchmaking_queue
    ADD CONSTRAINT ck_matchmaking_queue_status
        CHECK (status_code IN ('QUEUED', 'CONFIRMING', 'MATCHED', 'CANCELLED'));

-- Drop old unique index that only covered QUEUED
DROP INDEX IF EXISTS uq_matchmaking_queue_active_user_puzzle;

-- New index: one active entry per (user, puzzle) when QUEUED or CONFIRMING
CREATE UNIQUE INDEX uq_matchmaking_queue_active_user_puzzle
ON matchmaking_queue(user_id, puzzle_type_id)
WHERE status_code IN ('QUEUED', 'CONFIRMING');


-- ===== 2. Create online_match_confirmations table =====
CREATE TABLE IF NOT EXISTS online_match_confirmations (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id) ON DELETE CASCADE,

    player1_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    player2_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    player1_confirmed BOOLEAN NOT NULL DEFAULT false,
    player2_confirmed BOOLEAN NOT NULL DEFAULT false,

    confirm_deadline_at TIMESTAMPTZ NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',

    created_at TIMESTAMPTZ NOT NULL,
    confirmed_at TIMESTAMPTZ,
    match_id UUID REFERENCES online_matches(id) ON DELETE SET NULL,

    CONSTRAINT ck_online_match_confirmations_status
        CHECK (status IN ('PENDING', 'CONFIRMED', 'EXPIRED', 'CANCELLED')),

    CONSTRAINT ck_online_match_confirmations_players
        CHECK (player1_user_id <> player2_user_id)
);

-- Index for BackgroundService: quickly find expired PENDING confirmations
CREATE INDEX IF NOT EXISTS idx_online_match_confirmations_deadline
ON online_match_confirmations(confirm_deadline_at)
WHERE status = 'PENDING';

-- Unique partial index: one active PENDING confirmation per player1 per puzzle type
-- Prevents a player from having multiple simultaneous PENDING confirmations
CREATE UNIQUE INDEX IF NOT EXISTS uq_online_match_confirmations_player1_active
ON online_match_confirmations(player1_user_id, puzzle_type_id)
WHERE status = 'PENDING';

CREATE UNIQUE INDEX IF NOT EXISTS uq_online_match_confirmations_player2_active
ON online_match_confirmations(player2_user_id, puzzle_type_id)
WHERE status = 'PENDING';

-- Lookup index: find confirmation by player
CREATE INDEX IF NOT EXISTS idx_online_match_confirmations_player1
ON online_match_confirmations(player1_user_id, status);

CREATE INDEX IF NOT EXISTS idx_online_match_confirmations_player2
ON online_match_confirmations(player2_user_id, status);

COMMIT;
