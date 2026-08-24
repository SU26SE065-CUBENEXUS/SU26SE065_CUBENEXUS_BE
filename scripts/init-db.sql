-- =========================================================
-- CubeNexus Database Schema + Master Seed Data
-- PostgreSQL
--
-- Fresh install (recommended):
--   docker compose up -d
--   → Postgres mounts this file to /docker-entrypoint-initdb.d/
--   → runs once on empty volume (schema + seed below)
--
-- Manual install:
--   psql -h localhost -p 5432 -U cubenexus -d CubeNexus -f scripts/init-db.sql
--
-- This file contains the complete latest schema for a brand-new database
-- (including face_enrollments, face_verification_sessions, hieu2 tables).
-- Existing database (created before this version):
--   Apply only migrations newer than the schema version already deployed.
--   Or reset dev DB: docker compose down -v && docker compose up -d
-- =========================================================

BEGIN;

-- Optional: needed only if you want PostgreSQL to generate UUIDs with gen_random_uuid()
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- =========================================================
-- 1. MASTER DATA & IDENTITY
-- =========================================================

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY,
    user_code VARCHAR(255) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    phone VARCHAR(20) NOT NULL DEFAULT '',
    address TEXT NOT NULL DEFAULT '',
    avatar_url TEXT,
    user_role VARCHAR(30) NOT NULL DEFAULT 'COMPETITOR',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_banned BOOLEAN NOT NULL DEFAULT false,
    ban_reason TEXT,
    banned_at TIMESTAMPTZ,
    banned_until TIMESTAMPTZ,
    email_confirmed BOOLEAN NOT NULL DEFAULT true,
    email_confirmed_at TIMESTAMPTZ,
    auth_provider VARCHAR(20) NOT NULL DEFAULT 'LOCAL',
    google_sub VARCHAR(255),
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_users_role
        CHECK (user_role IN ('ADMIN', 'MANAGER', 'JUDGE', 'COMPETITOR')),

    CONSTRAINT ck_users_ban_reason
        CHECK (
            (is_banned = false AND ban_reason IS NULL)
            OR (is_banned = true)
        ),

    CONSTRAINT ck_users_auth_provider
        CHECK (auth_provider IN ('LOCAL', 'GOOGLE'))
);

CREATE INDEX IF NOT EXISTS idx_users_role
ON users(user_role);

CREATE INDEX IF NOT EXISTS idx_users_active
ON users(is_active, is_banned);

CREATE UNIQUE INDEX IF NOT EXISTS uq_users_google_sub
ON users(google_sub)
WHERE google_sub IS NOT NULL;


CREATE TABLE IF NOT EXISTS puzzle_types (
    id UUID PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    code VARCHAR(20) UNIQUE NOT NULL,
    scramble_length INTEGER,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_puzzle_types_scramble_length
        CHECK (scramble_length IS NULL OR scramble_length > 0)
);

CREATE INDEX IF NOT EXISTS idx_puzzle_types_active
ON puzzle_types(is_active);


CREATE TABLE IF NOT EXISTS penalty_types (
    id UUID PRIMARY KEY,
    code VARCHAR(10) UNIQUE NOT NULL,
    label VARCHAR(50) NOT NULL,
    time_addition_ms INTEGER NOT NULL DEFAULT 0,
    is_disqualified BOOLEAN NOT NULL DEFAULT false,

    CONSTRAINT ck_penalty_types_time
        CHECK (time_addition_ms >= 0)
);


CREATE TABLE IF NOT EXISTS elo_config (
    id UUID PRIMARY KEY,
    k_factor_placement INTEGER NOT NULL DEFAULT 100,
    k_factor_standard INTEGER NOT NULL DEFAULT 20,
    placement_match_count INTEGER NOT NULL DEFAULT 5,
    default_elo INTEGER NOT NULL DEFAULT 1000,
    updated_by UUID REFERENCES users(id),
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_elo_config_values
        CHECK (
            k_factor_placement > 0
            AND k_factor_standard > 0
            AND placement_match_count > 0
            AND default_elo >= 0
        )
);


-- =========================================================
-- 1.1 SCRAMBLE CONTROL CENTER
-- One technical store, isolated by competition mode.
-- =========================================================

CREATE TABLE IF NOT EXISTS scramble_pool_items (
    id UUID PRIMARY KEY,
    competition_mode VARCHAR(32) NOT NULL,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id) ON DELETE RESTRICT,
    sequence TEXT NOT NULL,
    sequence_hash VARCHAR(64) NOT NULL,
    expected_state_json TEXT,
    status VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    is_validated BOOLEAN NOT NULL DEFAULT false,
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

    CONSTRAINT ck_scramble_pool_mode
        CHECK (competition_mode IN ('ONLINE_MATCH', 'OFFLINE', 'ONLINE_ASYNC')),

    CONSTRAINT ck_scramble_pool_status
        CHECK (status IN ('DRAFT', 'AVAILABLE', 'RESERVED', 'USED', 'RETIRED', 'INVALID')),

    CONSTRAINT ck_scramble_pool_sequence
        CHECK (length(trim(sequence)) > 0),

    CONSTRAINT ck_scramble_pool_max_two_moves
        CHECK (
            status IN ('RETIRED', 'INVALID')
            OR cardinality(regexp_split_to_array(trim(sequence), E'\\s+')) <= 2
        ),

    CONSTRAINT uq_scramble_pool_mode_puzzle_hash
        UNIQUE (competition_mode, puzzle_type_id, sequence_hash)
);

CREATE INDEX IF NOT EXISTS ix_scramble_pool_assignment
ON scramble_pool_items(competition_mode, puzzle_type_id, status, created_at);

CREATE INDEX IF NOT EXISTS ix_scramble_pool_assigned_target
ON scramble_pool_items(assigned_target_type, assigned_target_id)
WHERE assigned_target_id IS NOT NULL;


CREATE TABLE IF NOT EXISTS scramble_pool_audit_logs (
    id UUID PRIMARY KEY,
    scramble_pool_item_id UUID NOT NULL REFERENCES scramble_pool_items(id) ON DELETE CASCADE,
    action TEXT NOT NULL,
    actor_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    target_type TEXT,
    target_id UUID,
    details_json TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_scramble_pool_audit_action
        CHECK (length(trim(action)) > 0)
);

CREATE INDEX IF NOT EXISTS ix_scramble_pool_audit_item_created
ON scramble_pool_audit_logs(scramble_pool_item_id, created_at);

CREATE INDEX IF NOT EXISTS ix_scramble_pool_audit_actor
ON scramble_pool_audit_logs(actor_user_id, created_at)
WHERE actor_user_id IS NOT NULL;


-- =========================================================
-- 1.2 SCRAMBLE GENERATION SETTINGS
-- One persisted MANUAL/AUTO setting per competition mode.
-- The setting applies to every active puzzle type in that mode.
-- =========================================================

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


-- =========================================================
-- 2. OFFLINE TOURNAMENT
-- =========================================================

CREATE TABLE IF NOT EXISTS tournaments (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    location VARCHAR(255),

    max_participants INTEGER,
    banner_url TEXT,

    registration_open_at TIMESTAMPTZ NOT NULL,
    registration_close_at TIMESTAMPTZ NOT NULL,

    start_date TIMESTAMPTZ NOT NULL,
    end_date TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(30) NOT NULL,
    tournament_type VARCHAR(32) NOT NULL DEFAULT 'OFFLINE',
    format_code VARCHAR(16) NOT NULL DEFAULT 'AO1',
    puzzle_type_id UUID REFERENCES puzzle_types(id) ON DELETE SET NULL,
    scramble_sequence TEXT,
    attempt_time_limit_ms INT NOT NULL DEFAULT 300000,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_tournaments_status
        CHECK (status_code IN ('DRAFT', 'PUBLISHED', 'REGISTRATION_OPEN', 'REGISTRATION_CLOSED', 'CHECKING_IN', 'ONGOING', 'COMPLETED', 'CANCELLED', 'DISABLED')),

    CONSTRAINT ck_tournaments_date
        CHECK (end_date > start_date),

    CONSTRAINT ck_tournaments_registration_date
        CHECK (
            registration_open_at < registration_close_at
            AND registration_close_at <= start_date
        )
);

CREATE INDEX IF NOT EXISTS idx_tournaments_status
ON tournaments(status_code);

CREATE INDEX IF NOT EXISTS idx_tournaments_created_by
ON tournaments(created_by);

CREATE INDEX IF NOT EXISTS idx_tournaments_date
ON tournaments(start_date, end_date);

CREATE INDEX IF NOT EXISTS idx_tournaments_registration_date
ON tournaments(registration_open_at, registration_close_at);


CREATE TABLE IF NOT EXISTS tournament_managers (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    assigned_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT uq_tournament_managers_tournament_user
        UNIQUE (tournament_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_tournament_managers_user
ON tournament_managers(user_id);


CREATE TABLE IF NOT EXISTS tournament_judges (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_code VARCHAR(50) NOT NULL DEFAULT 'STATION_JUDGE',
    assigned_station_number INT NULL,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_tournament_judges_tournament_user
        UNIQUE (tournament_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_tournament_judges_tournament
ON tournament_judges(tournament_id);

CREATE INDEX IF NOT EXISTS idx_tournament_judges_user
ON tournament_judges(user_id);


CREATE TABLE IF NOT EXISTS events (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    event_format_code VARCHAR(20) NOT NULL,
    time_limit_ms INTEGER,
    cutoff_time_ms INTEGER,
    solve_count INTEGER NOT NULL DEFAULT 5,
    total_rounds INTEGER NOT NULL DEFAULT 1,
    advance_top_n INTEGER NOT NULL DEFAULT 16,
    sort_order INTEGER,
    max_capacity INTEGER,
    registration_status_code VARCHAR(20) NOT NULL DEFAULT 'NOT_OPEN',
    created_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT uq_events_tournament_puzzle_format
        UNIQUE (tournament_id, puzzle_type_id, event_format_code),

    CONSTRAINT ck_events_values
        CHECK (
            solve_count > 0
            AND (time_limit_ms IS NULL OR time_limit_ms > 0)
            AND (cutoff_time_ms IS NULL OR cutoff_time_ms > 0)
            AND (
                time_limit_ms IS NULL
                OR cutoff_time_ms IS NULL
                OR cutoff_time_ms <= time_limit_ms
            )
        ),

    CONSTRAINT ck_events_format
        CHECK (event_format_code IN ('TRADITIONAL', 'MEDLEY')),

    CONSTRAINT ck_events_max_capacity
        CHECK (max_capacity IS NULL OR max_capacity > 0),

    CONSTRAINT ck_events_registration_status
        CHECK (registration_status_code IN ('NOT_OPEN', 'OPEN', 'CLOSED'))
);

CREATE INDEX IF NOT EXISTS idx_events_tournament
ON events(tournament_id);

CREATE INDEX IF NOT EXISTS idx_events_puzzle
ON events(puzzle_type_id);


CREATE TABLE IF NOT EXISTS medley_event_puzzles (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES events(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    sort_order INTEGER NOT NULL,

    CONSTRAINT uq_medley_event_puzzles_order
        UNIQUE (event_id, sort_order),

    CONSTRAINT uq_medley_event_puzzles_puzzle
        UNIQUE (event_id, puzzle_type_id),

    CONSTRAINT ck_medley_event_puzzles_order
        CHECK (sort_order > 0)
);


CREATE TABLE IF NOT EXISTS registrations (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    status_code VARCHAR(20) NOT NULL,
    qr_token TEXT UNIQUE NOT NULL,
    registered_at TIMESTAMPTZ NOT NULL,
    checked_in_at TIMESTAMPTZ,
    face_verified_at TIMESTAMPTZ,
    face_verification_session_id UUID,

    CONSTRAINT uq_registrations_tournament_user
        UNIQUE (tournament_id, user_id),

    CONSTRAINT ck_registrations_status
        CHECK (status_code IN ('PENDING', 'CONFIRMED', 'CANCELLED', 'CHECKED_IN'))
);

CREATE INDEX IF NOT EXISTS idx_registrations_user
ON registrations(user_id);

CREATE INDEX IF NOT EXISTS idx_registrations_tournament
ON registrations(tournament_id);

CREATE INDEX IF NOT EXISTS idx_registrations_status
ON registrations(status_code);

-- Composite index: speeds up COUNT(*) per tournament filtered by status_code (participant count query)
CREATE INDEX IF NOT EXISTS ix_registrations_tournament_status
ON registrations(tournament_id, status_code);


CREATE TABLE IF NOT EXISTS offline_registration_events (
    id UUID PRIMARY KEY,
    registration_id UUID NOT NULL REFERENCES registrations(id),
    event_id UUID NOT NULL REFERENCES events(id),
    status_code VARCHAR(20) NOT NULL DEFAULT 'REGISTERED',

    seed_time_ms INTEGER,
    seed_source_code VARCHAR(30),
    seed_generated_at TIMESTAMPTZ,

    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT uq_offline_registration_events_registration_event
        UNIQUE (registration_id, event_id),

    CONSTRAINT ck_offline_registration_events_status
        CHECK (status_code IN ('REGISTERED', 'WITHDRAWN', 'DISQUALIFIED')),

    CONSTRAINT ck_offline_registration_events_seed
        CHECK (seed_time_ms IS NULL OR seed_time_ms > 0),

    CONSTRAINT ck_offline_registration_events_seed_source
        CHECK (
            seed_source_code IS NULL
            OR seed_source_code IN (
                'OFFICIAL_RESULT',
                'PRACTICE_AO5',
                'DEFAULT',
                'MANUAL_OVERRIDE'
            )
        ),

    CONSTRAINT ck_offline_registration_events_seed_consistency
        CHECK (
            (seed_time_ms IS NULL AND seed_source_code IS NULL AND seed_generated_at IS NULL)
            OR
            (seed_time_ms IS NOT NULL AND seed_source_code IS NOT NULL AND seed_generated_at IS NOT NULL)
        )
);

CREATE INDEX IF NOT EXISTS idx_offline_registration_events_event
ON offline_registration_events(event_id);


CREATE TABLE IF NOT EXISTS groups (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES events(id),
    round_number INTEGER NOT NULL,
    group_name VARCHAR(50),
    status_code VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_groups_status
        CHECK (status_code IN ('PENDING', 'ONGOING', 'LOCKED', 'COMPLETED')),

    CONSTRAINT uq_groups_event_round_name
        UNIQUE (event_id, round_number, group_name),

    CONSTRAINT ck_groups_round
        CHECK (round_number > 0)
);

CREATE INDEX IF NOT EXISTS idx_groups_event_round
ON groups(event_id, round_number);


CREATE TABLE IF NOT EXISTS group_competitors (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL REFERENCES groups(id),
    registration_event_id UUID NOT NULL REFERENCES offline_registration_events(id),
    station_number INTEGER,
    status_code VARCHAR(20) NOT NULL DEFAULT 'PENDING',

    CONSTRAINT uq_group_competitors_group_registration_event
        UNIQUE (group_id, registration_event_id),

    CONSTRAINT ck_group_competitors_status
        CHECK (status_code IN ('PENDING', 'CALLED', 'COMPETING', 'COMPLETED', 'NO_SHOW')),

    CONSTRAINT ck_group_competitors_values
        CHECK (station_number IS NULL OR station_number > 0)
);


CREATE TABLE IF NOT EXISTS scramble_sets (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL REFERENCES groups(id),
    pdf_url TEXT,
    pdf_password_hash VARCHAR(255),
    generated_at TIMESTAMPTZ NOT NULL,
    generated_by UUID REFERENCES users(id),

    CONSTRAINT uq_scramble_sets_group
        UNIQUE (group_id)
);


CREATE TABLE IF NOT EXISTS scrambles (
    id UUID PRIMARY KEY,
    scramble_set_id UUID NOT NULL REFERENCES scramble_sets(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    solve_number INTEGER NOT NULL,
    sequence TEXT NOT NULL,
    sort_order INTEGER NOT NULL,
    source_scramble_pool_item_id UUID REFERENCES scramble_pool_items(id) ON DELETE SET NULL,

    CONSTRAINT uq_scrambles_set_solve_puzzle
        UNIQUE (scramble_set_id, solve_number, puzzle_type_id),

    CONSTRAINT ck_scrambles_values
        CHECK (
            solve_number > 0
            AND sort_order > 0
            AND length(trim(sequence)) > 0
        )
);

CREATE INDEX IF NOT EXISTS idx_scrambles_set
ON scrambles(scramble_set_id);

CREATE INDEX IF NOT EXISTS ix_scrambles_source_scramble_pool_item_id
ON scrambles(source_scramble_pool_item_id);


CREATE TABLE IF NOT EXISTS results (
    id UUID PRIMARY KEY,
    group_competitor_id UUID NOT NULL REFERENCES group_competitors(id),
    scramble_id UUID REFERENCES scrambles(id),
    judged_by UUID NOT NULL REFERENCES users(id),
    solve_number INTEGER NOT NULL,
    raw_time_ms INTEGER,
    final_time_ms INTEGER,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN NOT NULL DEFAULT false,
    esignature_data TEXT,
    evidence_photo_url TEXT,
    signed_at TIMESTAMPTZ,
    submitted_at TIMESTAMPTZ NOT NULL,
    is_locked BOOLEAN NOT NULL DEFAULT false,

    CONSTRAINT uq_results_competitor_solve
        UNIQUE (group_competitor_id, solve_number),

    CONSTRAINT ck_results_time
        CHECK (
            solve_number > 0
            AND (raw_time_ms IS NULL OR raw_time_ms > 0)
            AND (final_time_ms IS NULL OR final_time_ms > 0)
        ),
    CONSTRAINT ck_results_dnf_consistency
        CHECK (
            (is_dnf = true AND final_time_ms IS NULL)
            OR
            (is_dnf = false AND raw_time_ms IS NOT NULL AND final_time_ms IS NOT NULL)
        )
);

CREATE INDEX IF NOT EXISTS idx_results_group_competitor
ON results(group_competitor_id);

CREATE INDEX IF NOT EXISTS idx_results_judged_by
ON results(judged_by);

CREATE INDEX IF NOT EXISTS idx_results_locked
ON results(is_locked);


CREATE TABLE IF NOT EXISTS medley_result_details (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES results(id),
    medley_puzzle_id UUID NOT NULL REFERENCES medley_event_puzzles(id),
    scramble_id UUID NOT NULL REFERENCES scrambles(id),
    raw_time_ms INTEGER,
    final_time_ms INTEGER,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN NOT NULL DEFAULT false,
    sort_order INTEGER NOT NULL,

    CONSTRAINT uq_medley_result_details_result_puzzle
        UNIQUE (result_id, medley_puzzle_id),

    -- Medley Relay chỉ theo dõi TỔNG THỜI GIAN (lưu trong bảng results), không theo dõi thời gian riêng từng khối.
    -- raw_time_ms và final_time_ms trên từng dòng detail có thể NULL.
    CONSTRAINT ck_medley_result_details_values
        CHECK (
            sort_order > 0
            AND (raw_time_ms IS NULL OR raw_time_ms > 0)
            AND (final_time_ms IS NULL OR final_time_ms > 0)
        )
);


CREATE TABLE IF NOT EXISTS disputes (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES results(id),
    reported_by UUID NOT NULL REFERENCES users(id),
    reason TEXT NOT NULL,
    status_code VARCHAR(20) NOT NULL,
    resolved_by UUID REFERENCES users(id),
    resolution_note TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    resolved_at TIMESTAMPTZ,

    CONSTRAINT ck_disputes_status
        CHECK (status_code IN ('PENDING', 'RESOLVED', 'REJECTED'))
);

CREATE INDEX IF NOT EXISTS idx_disputes_result
ON disputes(result_id);

CREATE INDEX IF NOT EXISTS idx_disputes_status
ON disputes(status_code);

CREATE INDEX IF NOT EXISTS idx_disputes_reported_by
ON disputes(reported_by);


-- =========================================================
-- 2.1 ONLINE ASYNC TOURNAMENT ATTEMPTS
-- =========================================================

CREATE TABLE IF NOT EXISTS online_async_attempts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tournament_id UUID NOT NULL REFERENCES tournaments(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    scramble_sequence TEXT NOT NULL,
    scramble_pool_item_id UUID REFERENCES scramble_pool_items(id) ON DELETE SET NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'INITIALIZED',
    review_status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REVIEW',
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    hand_timer_started_at TIMESTAMPTZ,
    solve_started_at TIMESTAMPTZ,
    solve_finished_at TIMESTAMPTZ,
    attempt_deadline_at TIMESTAMPTZ,
    raw_time_ms INT,
    penalty_time_ms INT NOT NULL DEFAULT 0,
    penalty_code VARCHAR(16) NOT NULL DEFAULT 'NONE',
    is_dnf BOOLEAN NOT NULL DEFAULT FALSE,
    final_time_ms INT,
    scramble_check_status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
    finish_check_status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
    video_evidence_url TEXT,
    scramble_evidence_json TEXT,
    finish_evidence_json TEXT,
    reviewed_by UUID REFERENCES users(id) ON DELETE SET NULL,
    reviewed_at TIMESTAMPTZ,
    review_note TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_online_async_attempts_tournament_user
    ON online_async_attempts(tournament_id, user_id);

CREATE INDEX IF NOT EXISTS ix_online_async_attempts_leaderboard
    ON online_async_attempts(tournament_id, review_status, is_dnf, final_time_ms);

CREATE INDEX IF NOT EXISTS ix_online_async_attempts_deadline
    ON online_async_attempts(attempt_deadline_at)
    WHERE attempt_deadline_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_online_async_attempts_scramble_pool_item_id
    ON online_async_attempts(scramble_pool_item_id);


-- =========================================================
-- 3. ONLINE ARENA
-- =========================================================

CREATE TABLE IF NOT EXISTS online_profiles (
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

    -- Matchmaking cooldown (lưu tại profile, không phải MatchmakingQueue)
    matchmaking_cooldown_until TIMESTAMPTZ,
    setup_timeout_count INTEGER NOT NULL DEFAULT 0,
    setup_timeout_window_started_at TIMESTAMPTZ,
    last_setup_timeout_at TIMESTAMPTZ,

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

CREATE INDEX IF NOT EXISTS idx_online_profiles_user_id
ON online_profiles(user_id);

CREATE INDEX IF NOT EXISTS idx_online_profiles_leaderboard
ON online_profiles(elo_standard DESC)
WHERE is_placement_complete_standard = true;

CREATE INDEX IF NOT EXISTS idx_online_profiles_matchmaking
ON online_profiles(is_placement_complete_standard, elo_standard);

CREATE INDEX IF NOT EXISTS idx_online_profiles_cooldown
ON online_profiles(matchmaking_cooldown_until)
WHERE matchmaking_cooldown_until IS NOT NULL;


CREATE TABLE IF NOT EXISTS matchmaking_queue (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    online_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    queued_at TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(20) NOT NULL,

    CONSTRAINT ck_matchmaking_queue_status
        CHECK (status_code IN ('QUEUED', 'CONFIRMING', 'MATCHED', 'CANCELLED'))

);

CREATE UNIQUE INDEX IF NOT EXISTS uq_matchmaking_queue_active_user_puzzle
ON matchmaking_queue(user_id, puzzle_type_id)
WHERE status_code IN ('QUEUED', 'CONFIRMING');

CREATE INDEX IF NOT EXISTS idx_matchmaking_queue_search
ON matchmaking_queue(puzzle_type_id, status_code, queued_at);

CREATE INDEX IF NOT EXISTS idx_matchmaking_queue_profile
ON matchmaking_queue(online_profile_id);


CREATE TABLE IF NOT EXISTS online_matches (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    scramble_sequence TEXT NOT NULL,
    player1_scramble_sequence TEXT,
    player2_scramble_sequence TEXT,
    scramble_pool_item_id UUID REFERENCES scramble_pool_items(id) ON DELETE SET NULL,

    player1_id UUID NOT NULL REFERENCES users(id),
    player2_id UUID NOT NULL REFERENCES users(id),

    player1_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    player2_profile_id UUID NOT NULL REFERENCES online_profiles(id),

    winner_id UUID REFERENCES users(id),
    status_code VARCHAR(20) NOT NULL,

    -- Phase chuẩn hóa (granular state cho frontend)
    phase VARCHAR(30) NOT NULL DEFAULT 'ROOM_SETUP',

    room_token VARCHAR(255) UNIQUE NOT NULL,
    qr_session_code VARCHAR(255),
    player1_time_ms INTEGER,
    player2_time_ms INTEGER,
    player1_elo_before INTEGER,
    player2_elo_before INTEGER,
    player1_elo_after INTEGER,
    player2_elo_after INTEGER,
    outcome VARCHAR(30) NOT NULL DEFAULT 'INCONCLUSIVE',
    review_reason_json TEXT,
    video_evidence_upload_deadline_at TIMESTAMPTZ,
    player1_recording_started_at TIMESTAMPTZ,
    player2_recording_started_at TIMESTAMPTZ,
    time_limit_ms INTEGER NOT NULL DEFAULT 480000,

    -- Deadlines (UTC) — backend là nguồn sự thật, không để frontend tự tính
    setup_deadline_at TIMESTAMPTZ,
    ready_deadline_at TIMESTAMPTZ,
    countdown_ends_at TIMESTAMPTZ,
    inspection_deadline_at TIMESTAMPTZ,
    solve_deadline_at TIMESTAMPTZ,
    finish_check_deadline_at TIMESTAMPTZ,

    -- Cancellation info
    cancel_reason VARCHAR(100),
    timeout_player_id UUID REFERENCES users(id) ON DELETE SET NULL,
    elo_changed BOOLEAN NOT NULL DEFAULT false,
    -- Idempotency guard cho BackgroundService
    setup_timeout_penalty_applied_at TIMESTAMPTZ,

    -- Trạng thái sẵn sàng (Readiness Fields)
    player1_camera_ready BOOLEAN NOT NULL DEFAULT false,
    player2_camera_ready BOOLEAN NOT NULL DEFAULT false,
    player1_timer_ready BOOLEAN NOT NULL DEFAULT false,
    player2_timer_ready BOOLEAN NOT NULL DEFAULT false,
    player1_ready BOOLEAN NOT NULL DEFAULT false,
    player2_ready BOOLEAN NOT NULL DEFAULT false,
    player1_web_rtc_connected BOOLEAN NOT NULL DEFAULT false,
    player2_web_rtc_connected BOOLEAN NOT NULL DEFAULT false,
    player1_recording_started BOOLEAN NOT NULL DEFAULT false,
    player2_recording_started BOOLEAN NOT NULL DEFAULT false,
    player1_ai_pre_check_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    player2_ai_pre_check_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    player1_scramble_check_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    player2_scramble_check_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    player1_finish_check_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    player2_finish_check_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    player1_expected_state_json TEXT,
    player2_expected_state_json TEXT,
    player1_observed_state_json TEXT,
    player2_observed_state_json TEXT,
    player1_scanner_state_json TEXT,
    player2_scanner_state_json TEXT,

    -- Trạng thái kết quả (Result Fields)
    player1_is_dnf BOOLEAN NOT NULL DEFAULT false,
    player2_is_dnf BOOLEAN NOT NULL DEFAULT false,
    player1_result_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    player2_result_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',

    -- Mốc thời gian quan trọng (Realtime Timestamps)
    scramble_revealed_at TIMESTAMPTZ,
    player1_finished_at TIMESTAMPTZ,
    player2_finished_at TIMESTAMPTZ,

    started_at TIMESTAMPTZ,
    ended_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_online_matches_status
        CHECK (status_code IN ('CREATED', 'READY', 'ONGOING', 'PENDING_EVIDENCE', 'NEEDS_REVIEW', 'COMPLETED', 'CANCELLED', 'DRAW')),

    CONSTRAINT ck_online_matches_phase
        CHECK (phase IN (
            'ROOM_SETUP', 'WEBRTC_CONNECTING', 'MOBILE_TIMER_PAIRING', 'SCRAMBLE_CHECKING',
            'WAITING_READY', 'COUNTDOWN', 'INSPECTION', 'SOLVING', 'FINISH_CHECKING',
            'PENDING_EVIDENCE', 'COMPLETED', 'NEEDS_REVIEW', 'CANCELLED'
        )),

    CONSTRAINT ck_online_matches_players
        CHECK (player1_id <> player2_id),

    CONSTRAINT ck_online_matches_profiles
        CHECK (player1_profile_id <> player2_profile_id),

    CONSTRAINT ck_online_matches_winner
        CHECK (
            winner_id IS NULL
            OR winner_id = player1_id
            OR winner_id = player2_id
        ),

    CONSTRAINT ck_online_matches_times
        CHECK (
            (player1_time_ms IS NULL OR player1_time_ms > 0)
            AND (player2_time_ms IS NULL OR player2_time_ms > 0)
            AND time_limit_ms > 0
        ),

    CONSTRAINT ck_player1_result_status
        CHECK (player1_result_status IN ('PENDING', 'VALID', 'DNF', 'DISCONNECTED', 'REPORTED')),

    CONSTRAINT ck_player2_result_status
        CHECK (player2_result_status IN ('PENDING', 'VALID', 'DNF', 'DISCONNECTED', 'REPORTED')),

    CONSTRAINT ck_online_matches_outcome
        CHECK (outcome IN ('INCONCLUSIVE', 'PLAYER1_WIN', 'PLAYER2_WIN', 'DRAW', 'CANCELLED'))
);

CREATE INDEX IF NOT EXISTS idx_online_matches_player1
ON online_matches(player1_id);

CREATE INDEX IF NOT EXISTS idx_online_matches_player2
ON online_matches(player2_id);

CREATE INDEX IF NOT EXISTS idx_online_matches_puzzle_status
ON online_matches(puzzle_type_id, status_code);

CREATE INDEX IF NOT EXISTS idx_online_matches_created_at
ON online_matches(created_at);

CREATE INDEX IF NOT EXISTS idx_online_matches_player1_profile
ON online_matches(player1_profile_id);

CREATE INDEX IF NOT EXISTS idx_online_matches_player2_profile
ON online_matches(player2_profile_id);

CREATE INDEX IF NOT EXISTS ix_online_matches_scramble_pool_item_id
ON online_matches(scramble_pool_item_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_online_matches_qr_session_code
ON online_matches(qr_session_code)
WHERE qr_session_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_online_matches_active_phase
ON online_matches(status_code, phase)
WHERE status_code NOT IN ('COMPLETED', 'CANCELLED', 'DRAW');

CREATE INDEX IF NOT EXISTS idx_online_matches_setup_deadline
ON online_matches(setup_deadline_at)
WHERE status_code NOT IN ('COMPLETED', 'CANCELLED', 'DRAW') AND setup_deadline_at IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_online_matches_timeout_player
ON online_matches(timeout_player_id)
WHERE timeout_player_id IS NOT NULL;


CREATE TABLE IF NOT EXISTS online_match_confirmations (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id) ON DELETE CASCADE,

    player1_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    player2_user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    player1_confirmed BOOLEAN NOT NULL DEFAULT false,
    player2_confirmed BOOLEAN NOT NULL DEFAULT false,

    confirm_deadline_at TIMESTAMPTZ NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',

    created_at TIMESTAMPTZ NOT NULL,
    confirmed_at TIMESTAMPTZ,
    match_id UUID REFERENCES online_matches(id) ON DELETE SET NULL,

    CONSTRAINT ck_online_match_confirmations_status
        CHECK (status IN ('PENDING', 'CONFIRMED', 'EXPIRED', 'CANCELLED')),

    CONSTRAINT ck_online_match_confirmations_players
        CHECK (player1_user_id <> player2_user_id)
);

CREATE INDEX IF NOT EXISTS idx_online_match_confirmations_deadline
ON online_match_confirmations(confirm_deadline_at)
WHERE status = 'PENDING';

CREATE UNIQUE INDEX IF NOT EXISTS uq_online_match_confirmations_player1_active
ON online_match_confirmations(player1_user_id, puzzle_type_id)
WHERE status = 'PENDING';

CREATE UNIQUE INDEX IF NOT EXISTS uq_online_match_confirmations_player2_active
ON online_match_confirmations(player2_user_id, puzzle_type_id)
WHERE status = 'PENDING';

CREATE INDEX IF NOT EXISTS idx_online_match_confirmations_player1
ON online_match_confirmations(player1_user_id, status);

CREATE INDEX IF NOT EXISTS idx_online_match_confirmations_player2
ON online_match_confirmations(player2_user_id, status);


CREATE TABLE IF NOT EXISTS online_match_video_evidence (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id) ON DELETE CASCADE,
    player_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    object_key TEXT,
    content_type TEXT,
    file_size_bytes BIGINT,
    duration_seconds DOUBLE PRECISION,
    recording_status VARCHAR(30) NOT NULL DEFAULT 'Pending',
    recorded_at TIMESTAMPTZ,
    file_url TEXT NOT NULL,
    thumbnail_url TEXT,
    duration_ms BIGINT,
    recording_started_at TIMESTAMPTZ,
    recording_ended_at TIMESTAMPTZ,
    uploaded_at TIMESTAMPTZ,
    status VARCHAR(30) NOT NULL,
    checksum TEXT,
    source_type VARCHAR(30) NOT NULL DEFAULT 'LOCAL_CAMERA',
    mime_type TEXT
);

CREATE INDEX IF NOT EXISTS idx_online_match_video_evidence_match
ON online_match_video_evidence(match_id);

CREATE INDEX IF NOT EXISTS idx_online_match_video_evidence_player
ON online_match_video_evidence(player_id);

CREATE TABLE IF NOT EXISTS online_match_ai_checks (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id) ON DELETE CASCADE,
    player_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    check_type VARCHAR(30) NOT NULL,
    status VARCHAR(30) NOT NULL,
    confidence DOUBLE PRECISION,
    evidence_image_url TEXT,
    video_evidence_id UUID REFERENCES online_match_video_evidence(id) ON DELETE SET NULL,
    model_version TEXT,
    result_json TEXT,
    failure_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_online_match_ai_checks_match
ON online_match_ai_checks(match_id);

CREATE INDEX IF NOT EXISTS idx_online_match_ai_checks_player
ON online_match_ai_checks(player_id);

CREATE TABLE IF NOT EXISTS online_match_audit_logs (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id) ON DELETE CASCADE,
    player_id UUID REFERENCES users(id) ON DELETE SET NULL,
    event_type VARCHAR(50) NOT NULL,
    payload_json TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_online_match_audit_logs_match
ON online_match_audit_logs(match_id);

CREATE TABLE IF NOT EXISTS mobile_timer_sessions (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id),
    user_id UUID NOT NULL REFERENCES users(id),
    qr_session_code VARCHAR(255) NOT NULL,
    device_info TEXT,
    connected_at TIMESTAMPTZ,
    is_active BOOLEAN NOT NULL DEFAULT false,

    CONSTRAINT uq_mobile_timer_sessions_match_user
        UNIQUE (match_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_mobile_timer_sessions_match
ON mobile_timer_sessions(match_id);

CREATE INDEX IF NOT EXISTS idx_mobile_timer_sessions_qr
ON mobile_timer_sessions(qr_session_code);


CREATE TABLE IF NOT EXISTS elo_history (
    id UUID PRIMARY KEY,
    online_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    match_id UUID REFERENCES online_matches(id),
    elo_before INTEGER NOT NULL,
    elo_after INTEGER NOT NULL,
    delta INTEGER NOT NULL,
    k_factor_used INTEGER,
    actual_score NUMERIC(3,1),
    expected_score NUMERIC(6,4),
    is_placement_match BOOLEAN NOT NULL DEFAULT false,
    reason_code VARCHAR(50),
    elo_mode_code VARCHAR(20) NOT NULL DEFAULT 'STANDARD',
    changed_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_elo_history_values
        CHECK (
            elo_before >= 0
            AND elo_after >= 0
            AND (k_factor_used IS NULL OR k_factor_used > 0)
            AND (
                actual_score IS NULL
                OR actual_score IN (0.0, 0.5, 1.0)
            )
            AND (
                expected_score IS NULL
                OR (expected_score >= 0 AND expected_score <= 1)
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_elo_history_profile
ON elo_history(online_profile_id, changed_at DESC);

CREATE INDEX IF NOT EXISTS idx_elo_history_match
ON elo_history(match_id);


CREATE TABLE IF NOT EXISTS fraud_reports (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id),
    reporter_user_id UUID NOT NULL REFERENCES users(id),
    reported_user_id UUID NOT NULL REFERENCES users(id),
    fraud_type VARCHAR(50) NOT NULL DEFAULT 'OTHER',
    timestamp_text VARCHAR(20) DEFAULT '00:00',
    timestamp_seconds INTEGER DEFAULT 0,
    description TEXT,
    evidence_url TEXT,
    evidence_screenshot_url TEXT,
    status_code VARCHAR(20) NOT NULL,
    reason_code VARCHAR(50),
    review_scope VARCHAR(30) NOT NULL DEFAULT 'WHOLE_MATCH',
    decision VARCHAR(30),
    penalty_action VARCHAR(30),
    reviewed_by UUID REFERENCES users(id),
    resolved_by_admin_id UUID REFERENCES users(id),
    verdict_code VARCHAR(20),
    admin_note TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ,
    resolved_at TIMESTAMPTZ,

    CONSTRAINT ck_fraud_reports_status
        CHECK (status_code IN ('OPEN', 'REVIEWING', 'PENDING', 'REVIEWED', 'DISMISSED', 'RESOLVED', 'REJECTED')),

    CONSTRAINT ck_fraud_reports_verdict
        CHECK (verdict_code IS NULL OR verdict_code IN ('GUILTY', 'INNOCENT', 'INCONCLUSIVE')),

    CONSTRAINT ck_fraud_reports_users
        CHECK (reporter_user_id <> reported_user_id)
);

CREATE INDEX IF NOT EXISTS idx_fraud_reports_match
ON fraud_reports(match_id);

CREATE INDEX IF NOT EXISTS idx_fraud_reports_status
ON fraud_reports(status_code);

CREATE INDEX IF NOT EXISTS idx_fraud_reports_accused
ON fraud_reports(reported_user_id);


-- =========================================================
-- 5. PRACTICE
-- =========================================================

CREATE TABLE IF NOT EXISTS practice_sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    started_at TIMESTAMPTZ NOT NULL,
    ended_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_practice_sessions_user_puzzle
ON practice_sessions(user_id, puzzle_type_id);

CREATE INDEX IF NOT EXISTS idx_practice_sessions_started_at
ON practice_sessions(started_at DESC);


CREATE TABLE IF NOT EXISTS practice_attempts (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES practice_sessions(id),
    scramble_sequence TEXT NOT NULL,
    state VARCHAR(30) NOT NULL,
    hands_on_at TIMESTAMPTZ,
    ready_at TIMESTAMPTZ,
    started_at TIMESTAMPTZ,
    stopped_at TIMESTAMPTZ,
    time_ms INTEGER,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN NOT NULL DEFAULT false,
    abort_reason VARCHAR(50),
    created_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_practice_attempts_session
ON practice_attempts(session_id);

CREATE INDEX IF NOT EXISTS idx_practice_attempts_session_state
ON practice_attempts(session_id, state);


CREATE TABLE IF NOT EXISTS practice_solves (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES practice_sessions(id),
    attempt_id UUID REFERENCES practice_attempts(id),
    scramble_sequence TEXT NOT NULL,
    time_ms INTEGER NOT NULL,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN NOT NULL DEFAULT false,
    solved_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_practice_solves_time
        CHECK (is_dnf = true OR time_ms > 0)
);

CREATE INDEX IF NOT EXISTS idx_practice_solves_session
ON practice_solves(session_id);

CREATE INDEX IF NOT EXISTS idx_practice_solves_attempt
ON practice_solves(attempt_id);

CREATE INDEX IF NOT EXISTS idx_practice_solves_solved_at
ON practice_solves(solved_at DESC);


-- =========================================================
-- 6. NOTIFICATIONS
-- =========================================================

CREATE TABLE IF NOT EXISTS notifications (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type_code VARCHAR(50) NOT NULL,
    title VARCHAR(255) NOT NULL,
    body TEXT,
    payload JSONB,
    is_read BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL,
    read_at TIMESTAMPTZ
);

-- Supports the admin unread-notification query efficiently.
CREATE INDEX IF NOT EXISTS ix_notifications_user_unread_created
ON notifications(user_id, is_read, created_at DESC);


-- =========================================================
-- 7. REFRESH TOKENS
-- =========================================================

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    token_hash VARCHAR(255) UNIQUE NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    revoked_at TIMESTAMPTZ,
    replaced_by_token_hash VARCHAR(255),

    CONSTRAINT ck_refresh_tokens_expiry
        CHECK (expires_at > created_at)
);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user
ON refresh_tokens(user_id);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires
ON refresh_tokens(expires_at);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_revoked
ON refresh_tokens(revoked_at);

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


-- =========================================================
-- 7.1 FACE VERIFICATION (offline check-in / profile enroll)
-- Business enrollment + session state. Embeddings stay in FastAPI.
-- =========================================================

CREATE TABLE IF NOT EXISTS face_enrollments (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    status TEXT NOT NULL DEFAULT 'ENROLLED',
    model_version TEXT,
    quality_score DOUBLE PRECISION,
    templates_count INTEGER NOT NULL DEFAULT 0,
    last_external_session_id TEXT,
    enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_face_enrollments_status
        CHECK (status IN ('ENROLLED', 'REVOKED'))
);

CREATE INDEX IF NOT EXISTS ix_face_enrollments_status
ON face_enrollments(status);

CREATE TABLE IF NOT EXISTS face_verification_sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    purpose TEXT NOT NULL,
    context_type TEXT NOT NULL,
    tournament_id UUID REFERENCES tournaments(id) ON DELETE SET NULL,
    registration_id UUID REFERENCES registrations(id) ON DELETE SET NULL,
    initiated_by_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    external_session_id TEXT NOT NULL,
    upload_token TEXT NOT NULL,
    challenge_json TEXT,
    state TEXT NOT NULL DEFAULT 'POSITIONING',
    result_json TEXT,
    failure_reason TEXT,
    liveness_passed BOOLEAN,
    face_matched BOOLEAN,
    similarity DOUBLE PRECISION,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,

    CONSTRAINT ck_face_verification_sessions_purpose
        CHECK (purpose IN ('ENROLLMENT', 'VERIFICATION')),

    CONSTRAINT ck_face_verification_sessions_context
        CHECK (context_type IN ('PROFILE', 'CHECK_IN', 'STATION', 'LOGIN'))
);

CREATE INDEX IF NOT EXISTS ix_face_verification_sessions_user_id
ON face_verification_sessions(user_id);

CREATE INDEX IF NOT EXISTS ix_face_verification_sessions_external_session_id
ON face_verification_sessions(external_session_id);

CREATE INDEX IF NOT EXISTS ix_face_verification_sessions_registration_id
ON face_verification_sessions(registration_id);

CREATE INDEX IF NOT EXISTS ix_face_verification_sessions_state
ON face_verification_sessions(state);

ALTER TABLE registrations
    ADD CONSTRAINT fk_registrations_face_verification_session
    FOREIGN KEY (face_verification_session_id)
    REFERENCES face_verification_sessions(id)
    ON DELETE SET NULL;


CREATE INDEX IF NOT EXISTS idx_offline_registration_events_event_seed
ON offline_registration_events(event_id, seed_time_ms);

CREATE TABLE IF NOT EXISTS result_audit_logs (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES results(id),
    changed_by UUID NOT NULL REFERENCES users(id),
    old_raw_time_ms INTEGER,
    new_raw_time_ms INTEGER,
    old_final_time_ms INTEGER,
    new_final_time_ms INTEGER,
    old_penalty_type_id UUID REFERENCES penalty_types(id),
    new_penalty_type_id UUID REFERENCES penalty_types(id),
    old_is_dnf BOOLEAN NOT NULL DEFAULT false,
    new_is_dnf BOOLEAN NOT NULL DEFAULT false,
    reason TEXT NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_result_audit_logs_result
ON result_audit_logs(result_id);


-- =========================================================
-- 8. MASTER SEED DATA (idempotent — safe to re-run)
-- Required for register, practice, matchmaking, tournaments.
-- Stable UUIDs so Postman/docs can reference puzzleTypeId.
-- =========================================================

INSERT INTO puzzle_types (id, name, code, scramble_length, is_active, created_at)
VALUES
    ('7dd820d8-6be0-4197-bc29-0026c578cdf5', '2x2x2 Cube', '222', 10, true, NOW()),
    ('f4ddb522-426f-4dd0-a98d-20f21b192470', '3x3x3 Cube', '333', 20, true, NOW()),
    ('167d6142-48e9-436a-a42c-53427bcad8a7', '4x4x4 Cube', '444', 40, true, NOW()),
    ('1e36b408-c8d4-4e1a-9908-44fb7905e502', '5x5x5 Cube', '555', 60, true, NOW()),
    ('84b0f049-9b3e-4b40-8930-e764ea9d4121', '6x6x6 Cube', '666', 80, true, NOW())
ON CONFLICT (code) DO NOTHING;

INSERT INTO scramble_generation_settings (competition_mode, generation_mode)
VALUES
    ('ONLINE_MATCH', 'MANUAL'),
    ('OFFLINE', 'MANUAL'),
    ('ONLINE_ASYNC', 'MANUAL')
ON CONFLICT (competition_mode) DO NOTHING;

INSERT INTO penalty_types (id, code, label, time_addition_ms, is_disqualified)
VALUES
    ('a1000001-0000-4000-8000-000000000001', 'OK',     'OK',  0,    false),
    ('a1000001-0000-4000-8000-000000000002', 'PLUS_2', '+2',  2000, false),
    ('a1000001-0000-4000-8000-000000000003', 'DNF',    'DNF', 0,    true)
ON CONFLICT (code) DO NOTHING;

INSERT INTO elo_config (
    id,
    k_factor_placement,
    k_factor_standard,
    placement_match_count,
    default_elo,
    updated_at
)
SELECT
    'b2000001-0000-4000-8000-000000000001',
    100,
    20,
    5,
    1000,
    NOW()
WHERE NOT EXISTS (SELECT 1 FROM elo_config);

COMMIT;
