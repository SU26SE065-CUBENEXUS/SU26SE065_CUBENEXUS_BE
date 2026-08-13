BEGIN;

CREATE TABLE IF NOT EXISTS scramble_pool_items (
    id UUID PRIMARY KEY,
    competition_mode VARCHAR(32) NOT NULL,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id) ON DELETE RESTRICT,
    sequence TEXT NOT NULL,
    sequence_hash VARCHAR(64) NOT NULL,
    expected_state_json TEXT,
    status VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    is_validated BOOLEAN NOT NULL DEFAULT FALSE,
    generator_name TEXT NOT NULL DEFAULT 'ADMIN_IMPORT',
    notes TEXT,
    created_by UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    approved_by UUID REFERENCES users(id) ON DELETE SET NULL,
    approved_at TIMESTAMPTZ,
    assigned_target_type TEXT,
    assigned_target_id UUID,
    assigned_at TIMESTAMPTZ,
    used_at TIMESTAMPTZ,
    CONSTRAINT ck_scramble_pool_mode CHECK (competition_mode IN ('ONLINE_MATCH', 'OFFLINE', 'ONLINE_ASYNC')),
    CONSTRAINT ck_scramble_pool_status CHECK (status IN ('DRAFT', 'AVAILABLE', 'RESERVED', 'USED', 'RETIRED', 'INVALID')),
    CONSTRAINT uq_scramble_pool_mode_puzzle_hash UNIQUE (competition_mode, puzzle_type_id, sequence_hash)
);

CREATE INDEX IF NOT EXISTS ix_scramble_pool_assignment
    ON scramble_pool_items (competition_mode, puzzle_type_id, status, created_at);

CREATE TABLE IF NOT EXISTS scramble_pool_audit_logs (
    id UUID PRIMARY KEY,
    scramble_pool_item_id UUID NOT NULL REFERENCES scramble_pool_items(id) ON DELETE CASCADE,
    action TEXT NOT NULL,
    actor_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    target_type TEXT,
    target_id UUID,
    details_json TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_scramble_pool_audit_item_created
    ON scramble_pool_audit_logs (scramble_pool_item_id, created_at);

ALTER TABLE online_matches ADD COLUMN IF NOT EXISTS scramble_pool_item_id UUID;
ALTER TABLE online_async_attempts ADD COLUMN IF NOT EXISTS scramble_pool_item_id UUID;
ALTER TABLE scrambles ADD COLUMN IF NOT EXISTS source_scramble_pool_item_id UUID;

DO $$ BEGIN
    ALTER TABLE online_matches ADD CONSTRAINT fk_online_matches_scramble_pool
        FOREIGN KEY (scramble_pool_item_id) REFERENCES scramble_pool_items(id) ON DELETE SET NULL;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE online_async_attempts ADD CONSTRAINT fk_async_attempts_scramble_pool
        FOREIGN KEY (scramble_pool_item_id) REFERENCES scramble_pool_items(id) ON DELETE SET NULL;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
    ALTER TABLE scrambles ADD CONSTRAINT fk_offline_scrambles_scramble_pool
        FOREIGN KEY (source_scramble_pool_item_id) REFERENCES scramble_pool_items(id) ON DELETE SET NULL;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE INDEX IF NOT EXISTS ix_online_matches_scramble_pool_item_id ON online_matches(scramble_pool_item_id);
CREATE INDEX IF NOT EXISTS ix_online_async_attempts_scramble_pool_item_id ON online_async_attempts(scramble_pool_item_id);
CREATE INDEX IF NOT EXISTS ix_scrambles_source_scramble_pool_item_id ON scrambles(source_scramble_pool_item_id);

COMMIT;
