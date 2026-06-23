-- =============================================================================
-- Migration 002: Unified ELO profile (elo_standard / elo_medley)
--
-- GIỮ LẠI: users, puzzle_types
-- XÓA SẠCH: mọi dữ liệu bảng khác (practice, PVP, tournament, tokens, ...)
-- SCHEMA: online_profiles mới (1 row/user), bỏ practice_ao5_snapshots, elo_seed_thresholds
--
-- Chạy:
--   psql -U <user> -d <database> -f scripts/migrations/002-unified-elo-profile.sql
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 1. Xóa bảng seeding cũ (không còn dùng)
-- -----------------------------------------------------------------------------
DROP TABLE IF EXISTS practice_ao5_snapshots CASCADE;
DROP TABLE IF EXISTS elo_seed_thresholds CASCADE;

-- -----------------------------------------------------------------------------
-- 2. Xóa sạch dữ liệu (KHÔNG đụng users, puzzle_types)
--    Bỏ qua bảng chưa tồn tại bằng cách truncate từng nhóm an toàn.
-- -----------------------------------------------------------------------------
DO $truncate$
DECLARE
    t TEXT;
    tables TEXT[] := ARRAY[
        'result_audit_logs',
        'user_tokens',
        'refresh_tokens',
        'notifications',
        'practice_solves',
        'practice_attempts',
        'practice_sessions',
        'async_submissions',
        'async_tournaments',
        'fraud_reports',
        'elo_history',
        'mobile_timer_sessions',
        'matchmaking_queue',
        'online_matches',
        'disputes',
        'medley_result_details',
        'results',
        'scrambles',
        'scramble_sets',
        'group_competitors',
        'groups',
        'offline_registration_events',
        'registrations',
        'medley_event_puzzles',
        'events',
        'tournament_managers',
        'tournaments',
        'penalty_types',
        'elo_config'
    ];
BEGIN
    FOREACH t IN ARRAY tables
    LOOP
        IF EXISTS (
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = t
        ) THEN
            EXECUTE format('TRUNCATE TABLE %I RESTART IDENTITY CASCADE', t);
        END IF;
    END LOOP;
END;
$truncate$;

-- online_profiles: drop & recreate để chắc chắn đúng schema mới
DROP TABLE IF EXISTS online_profiles CASCADE;

-- -----------------------------------------------------------------------------
-- 3. Tạo lại online_profiles (schema mới)
-- -----------------------------------------------------------------------------
CREATE TABLE online_profiles (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),

    elo_standard INTEGER NOT NULL DEFAULT 1000,
    peak_elo_standard INTEGER NOT NULL DEFAULT 1000,
    placement_matches_done_standard INTEGER NOT NULL DEFAULT 0,
    is_placement_complete_standard BOOLEAN NOT NULL DEFAULT false,
    placement_completed_at_standard TIMESTAMPTZ,
    k_factor_current_standard INTEGER NOT NULL DEFAULT 100,
    total_wins_standard INTEGER NOT NULL DEFAULT 0,
    total_losses_standard INTEGER NOT NULL DEFAULT 0,
    total_draws_standard INTEGER NOT NULL DEFAULT 0,

    elo_medley INTEGER,
    peak_elo_medley INTEGER,
    placement_matches_done_medley INTEGER NOT NULL DEFAULT 0,
    is_placement_complete_medley BOOLEAN NOT NULL DEFAULT false,
    placement_completed_at_medley TIMESTAMPTZ,
    k_factor_current_medley INTEGER,
    total_wins_medley INTEGER NOT NULL DEFAULT 0,
    total_losses_medley INTEGER NOT NULL DEFAULT 0,
    total_draws_medley INTEGER NOT NULL DEFAULT 0,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_online_profiles_user UNIQUE (user_id),

    CONSTRAINT ck_online_profiles_elo_standard
        CHECK (elo_standard >= 0 AND peak_elo_standard >= 0),

    CONSTRAINT ck_online_profiles_stats_standard
        CHECK (
            placement_matches_done_standard >= 0
            AND k_factor_current_standard > 0
            AND total_wins_standard >= 0
            AND total_losses_standard >= 0
            AND total_draws_standard >= 0
        )
);

CREATE INDEX idx_online_profiles_user_id
    ON online_profiles(user_id);

CREATE INDEX idx_online_profiles_leaderboard
    ON online_profiles(elo_standard DESC)
    WHERE is_placement_complete_standard = true;

CREATE INDEX idx_online_profiles_matchmaking
    ON online_profiles(is_placement_complete_standard, elo_standard);

-- -----------------------------------------------------------------------------
-- 4. Khôi phục FK trỏ tới online_profiles (nếu bị DROP CASCADE)
-- -----------------------------------------------------------------------------
DO $fk$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'matchmaking_queue')
       AND NOT EXISTS (
           SELECT 1 FROM information_schema.table_constraints
           WHERE constraint_name = 'matchmaking_queue_online_profile_id_fkey'
       ) THEN
        ALTER TABLE matchmaking_queue
            ADD CONSTRAINT matchmaking_queue_online_profile_id_fkey
            FOREIGN KEY (online_profile_id) REFERENCES online_profiles(id);
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'elo_history')
       AND NOT EXISTS (
           SELECT 1 FROM information_schema.table_constraints
           WHERE constraint_name = 'elo_history_online_profile_id_fkey'
       ) THEN
        ALTER TABLE elo_history
            ADD CONSTRAINT elo_history_online_profile_id_fkey
            FOREIGN KEY (online_profile_id) REFERENCES online_profiles(id);
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'online_matches' AND column_name = 'player1_profile_id'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'online_matches_player1_profile_id_fkey'
    ) THEN
        ALTER TABLE online_matches
            ADD CONSTRAINT online_matches_player1_profile_id_fkey
            FOREIGN KEY (player1_profile_id) REFERENCES online_profiles(id);
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'online_matches' AND column_name = 'player2_profile_id'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'online_matches_player2_profile_id_fkey'
    ) THEN
        ALTER TABLE online_matches
            ADD CONSTRAINT online_matches_player2_profile_id_fkey
            FOREIGN KEY (player2_profile_id) REFERENCES online_profiles(id);
    END IF;
END;
$fk$;

-- -----------------------------------------------------------------------------
-- 5. Cập nhật elo_config (bỏ min_practice_solves) + seed mặc định
-- -----------------------------------------------------------------------------
ALTER TABLE elo_config DROP COLUMN IF EXISTS min_practice_solves;

ALTER TABLE elo_config DROP CONSTRAINT IF EXISTS ck_elo_config_values;
ALTER TABLE elo_config ADD CONSTRAINT ck_elo_config_values
    CHECK (
        k_factor_placement > 0
        AND k_factor_standard > 0
        AND placement_match_count > 0
        AND default_elo >= 0
    );

INSERT INTO elo_config (
    id,
    k_factor_placement,
    k_factor_standard,
    placement_match_count,
    default_elo,
    updated_at
)
VALUES (
    gen_random_uuid(),
    100,
    20,
    5,
    1000,
    NOW()
);

-- Master data tối thiểu cho Practice (lookup theo code OK / PLUS_2 / DNF)
INSERT INTO penalty_types (id, code, label, time_addition_ms, is_disqualified)
VALUES
    (gen_random_uuid(), 'OK',     'OK',  0,    false),
    (gen_random_uuid(), 'PLUS_2', '+2',  2000, false),
    (gen_random_uuid(), 'DNF',    'DNF', 0,    true);

-- -----------------------------------------------------------------------------
-- 6. elo_history: thêm elo_mode_code nếu chưa có
-- -----------------------------------------------------------------------------
ALTER TABLE elo_history
    ADD COLUMN IF NOT EXISTS elo_mode_code VARCHAR(20) NOT NULL DEFAULT 'STANDARD';

-- -----------------------------------------------------------------------------
-- 7. Tạo online_profile + elo_history DEFAULT_INIT cho MỌI user hiện có
-- -----------------------------------------------------------------------------
INSERT INTO online_profiles (
    id,
    user_id,
    elo_standard,
    peak_elo_standard,
    placement_matches_done_standard,
    is_placement_complete_standard,
    k_factor_current_standard,
    created_at,
    updated_at
)
SELECT
    gen_random_uuid(),
    u.id,
    ec.default_elo,
    ec.default_elo,
    0,
    false,
    ec.k_factor_placement,
    NOW(),
    NOW()
FROM users u
CROSS JOIN (SELECT * FROM elo_config LIMIT 1) ec;

INSERT INTO elo_history (
    id,
    online_profile_id,
    match_id,
    elo_before,
    elo_after,
    delta,
    k_factor_used,
    is_placement_match,
    reason_code,
    elo_mode_code,
    changed_at
)
SELECT
    gen_random_uuid(),
    op.id,
    NULL,
    0,
    op.elo_standard,
    op.elo_standard,
    op.k_factor_current_standard,
    false,
    'DEFAULT_INIT',
    'STANDARD',
    NOW()
FROM online_profiles op;

COMMIT;

-- Kiểm tra nhanh sau migration:
-- SELECT COUNT(*) FROM users;
-- SELECT COUNT(*) FROM puzzle_types;
-- SELECT user_id, elo_standard, elo_medley FROM online_profiles;
