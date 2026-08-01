-- Migration 010: Add evidence_photo_url to results table
ALTER TABLE results ADD COLUMN IF NOT EXISTS evidence_photo_url TEXT;
