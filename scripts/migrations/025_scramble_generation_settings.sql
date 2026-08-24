BEGIN;

CREATE TABLE IF NOT EXISTS scramble_generation_settings (
    competition_mode VARCHAR(32) PRIMARY KEY,
    generation_mode VARCHAR(10) NOT NULL DEFAULT 'MANUAL',
    updated_by UUID REFERENCES users(id) ON DELETE SET NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_scramble_generation_settings_competition_mode
        CHECK (competition_mode IN ('ONLINE_MATCH', 'OFFLINE', 'ONLINE_ASYNC')),
    CONSTRAINT ck_scramble_generation_settings_generation_mode
        CHECK (generation_mode IN ('MANUAL', 'AUTO'))
);

INSERT INTO scramble_generation_settings (competition_mode, generation_mode)
VALUES
    ('ONLINE_MATCH', 'MANUAL'),
    ('OFFLINE', 'MANUAL'),
    ('ONLINE_ASYNC', 'MANUAL')
ON CONFLICT (competition_mode) DO NOTHING;

COMMIT;
