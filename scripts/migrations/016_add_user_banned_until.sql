-- Migration: Add banned_at and banned_until columns to users table
ALTER TABLE users ADD COLUMN IF NOT EXISTS banned_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS banned_until TIMESTAMPTZ;
