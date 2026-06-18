-- Migration: add email auth support for existing databases

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS email_confirmed BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS email_confirmed_at TIMESTAMPTZ;

CREATE TABLE IF NOT EXISTS user_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    token_type VARCHAR(30) NOT NULL,
    token_hash VARCHAR(128) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_user_tokens_type
        CHECK (token_type IN ('EMAIL_CONFIRMATION', 'PASSWORD_RESET')),

    CONSTRAINT ck_user_tokens_expiry
        CHECK (expires_at > created_at)
);

CREATE INDEX IF NOT EXISTS idx_user_tokens_user_type
ON user_tokens(user_id, token_type);

CREATE INDEX IF NOT EXISTS idx_user_tokens_hash
ON user_tokens(token_hash);

CREATE INDEX IF NOT EXISTS idx_user_tokens_expires
ON user_tokens(expires_at);

-- Fix refresh_tokens schema for older databases
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'refresh_tokens' AND column_name = 'token'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'refresh_tokens' AND column_name = 'token_hash'
    ) THEN
        ALTER TABLE refresh_tokens RENAME COLUMN token TO token_hash;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'refresh_tokens' AND column_name = 'replaced_by'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'refresh_tokens' AND column_name = 'replaced_by_token_hash'
    ) THEN
        ALTER TABLE refresh_tokens RENAME COLUMN replaced_by TO replaced_by_token_hash;
    END IF;
END $$;

ALTER TABLE refresh_tokens
    ADD COLUMN IF NOT EXISTS replaced_by_token_hash VARCHAR(255);

ALTER TABLE refresh_tokens
    ADD COLUMN IF NOT EXISTS token_hash VARCHAR(255);
