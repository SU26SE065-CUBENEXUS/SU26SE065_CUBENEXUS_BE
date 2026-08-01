-- Migration: Create tournament_judges table
BEGIN;

CREATE TABLE IF NOT EXISTS tournament_judges (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_tournament_judges_tournament_user UNIQUE (tournament_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_tournament_judges_tournament
ON tournament_judges(tournament_id);

CREATE INDEX IF NOT EXISTS idx_tournament_judges_user
ON tournament_judges(user_id);

COMMIT;
