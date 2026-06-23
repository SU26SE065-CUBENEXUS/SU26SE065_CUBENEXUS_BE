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
