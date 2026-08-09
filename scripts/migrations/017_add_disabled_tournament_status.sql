-- Migration 017: Allow 'DISABLED' status code in tournaments table constraint

ALTER TABLE tournaments DROP CONSTRAINT IF EXISTS ck_tournaments_status;
ALTER TABLE tournaments DROP CONSTRAINT IF EXISTS tournaments_status_code_check;

ALTER TABLE tournaments ADD CONSTRAINT ck_tournaments_status
    CHECK (status_code IN ('DRAFT', 'PUBLISHED', 'REGISTRATION_OPEN', 'REGISTRATION_CLOSED', 'ONGOING', 'COMPLETED', 'CANCELLED', 'DISABLED'));
