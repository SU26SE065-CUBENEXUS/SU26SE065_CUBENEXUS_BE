-- Migration 011: Add max_participants and banner_url to tournaments table
ALTER TABLE tournaments ADD COLUMN IF NOT EXISTS max_participants INTEGER;
ALTER TABLE tournaments ADD COLUMN IF NOT EXISTS banner_url TEXT;

-- Drop and recreate ck_tournaments_status constraint to include REGISTRATION_OPEN and REGISTRATION_CLOSED
ALTER TABLE tournaments DROP CONSTRAINT IF EXISTS ck_tournaments_status;
ALTER TABLE tournaments ADD CONSTRAINT ck_tournaments_status 
    CHECK (status_code IN ('DRAFT', 'PUBLISHED', 'REGISTRATION_OPEN', 'REGISTRATION_CLOSED', 'ONGOING', 'COMPLETED', 'CANCELLED'));
