BEGIN;

-- Keep database-created online matches aligned with the five-minute solve
-- deadline enforced by the application. Existing match history is unchanged.
ALTER TABLE online_matches
    ALTER COLUMN time_limit_ms SET DEFAULT 300000;

COMMIT;
