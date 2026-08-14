-- =========================================================
-- Migration 023: Add total_rounds & advance_top_n to events table
-- =========================================================

ALTER TABLE events ADD COLUMN IF NOT EXISTS total_rounds INTEGER NOT NULL DEFAULT 1;
ALTER TABLE events ADD COLUMN IF NOT EXISTS advance_top_n INTEGER NOT NULL DEFAULT 16;
