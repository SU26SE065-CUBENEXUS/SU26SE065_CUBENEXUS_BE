-- Migration: WCA Practice Attempt flow
-- Run against existing databases after init-db.sql

CREATE TABLE IF NOT EXISTS practice_attempts (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES practice_sessions(id),
    scramble_sequence TEXT NOT NULL,
    state VARCHAR(30) NOT NULL,
    hands_on_at TIMESTAMPTZ,
    ready_at TIMESTAMPTZ,
    started_at TIMESTAMPTZ,
    stopped_at TIMESTAMPTZ,
    time_ms INTEGER,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN NOT NULL DEFAULT false,
    abort_reason VARCHAR(50),
    created_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_practice_attempts_session
ON practice_attempts(session_id);

CREATE INDEX IF NOT EXISTS idx_practice_attempts_session_state
ON practice_attempts(session_id, state);

ALTER TABLE practice_solves
    ADD COLUMN IF NOT EXISTS attempt_id UUID REFERENCES practice_attempts(id);

CREATE INDEX IF NOT EXISTS idx_practice_solves_attempt
ON practice_solves(attempt_id);

ALTER TABLE practice_solves DROP CONSTRAINT IF EXISTS ck_practice_solves_time;

ALTER TABLE practice_solves
    ADD CONSTRAINT ck_practice_solves_time
        CHECK (is_dnf = true OR time_ms > 0);
