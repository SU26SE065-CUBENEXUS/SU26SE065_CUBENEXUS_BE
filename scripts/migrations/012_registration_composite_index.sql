-- Migration: 012_registration_composite_index.sql
-- Purpose : Add composite index on registrations(tournament_id, status_code)
--           to speed up the participant-count query used in the public tournament list API.
--           Before this index, every call to GET /api/tournaments would do a full
--           sequential scan of the registrations table filtered by tournament_id list.
--
-- Apply to existing DB (without resetting):
--   psql -h localhost -p 5432 -U cubenexus -d CubeNexus \
--        -f scripts/migrations/012_registration_composite_index.sql

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_registrations_tournament_status
ON registrations(tournament_id, status_code);
