-- Countdown begins only after the tournament scramble has been verified.
ALTER TABLE online_async_attempts
    ADD COLUMN IF NOT EXISTS attempt_deadline_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS ix_online_async_attempts_deadline
    ON online_async_attempts(attempt_deadline_at)
    WHERE attempt_deadline_at IS NOT NULL;
