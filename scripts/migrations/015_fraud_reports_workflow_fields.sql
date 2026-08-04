-- Migration 015: Add Fraud Report Workflow Fields
-- Ensures idempotency with IF NOT EXISTS clauses

ALTER TABLE fraud_reports
    ADD COLUMN IF NOT EXISTS fraud_type VARCHAR(50) NOT NULL DEFAULT 'OTHER',
    ADD COLUMN IF NOT EXISTS timestamp_text VARCHAR(20) DEFAULT '00:00',
    ADD COLUMN IF NOT EXISTS timestamp_seconds INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS evidence_screenshot_url TEXT;
