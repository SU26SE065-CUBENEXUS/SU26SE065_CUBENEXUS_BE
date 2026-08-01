-- Migration: Add role_code and assigned_station_number to tournament_judges

ALTER TABLE tournament_judges
ADD COLUMN IF NOT EXISTS role_code VARCHAR(50) NOT NULL DEFAULT 'STATION_JUDGE',
ADD COLUMN IF NOT EXISTS assigned_station_number INT NULL;
