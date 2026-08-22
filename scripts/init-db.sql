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
-- ==========================================

CREATE TABLE users (
    id UUID PRIMARY KEY,
    user_code VARCHAR(255) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    phone VARCHAR(20) NOT NULL DEFAULT '',
    address TEXT NOT NULL DEFAULT '',
    avatar_url TEXT,
    user_role VARCHAR(20) NOT NULL DEFAULT 'COMPETITOR',  -- 'ADMIN', 'MANAGER', 'JUDGE', 'COMPETITOR'
    is_active BOOLEAN DEFAULT true,
    is_banned BOOLEAN DEFAULT false,
    ban_reason TEXT,
    banned_at TIMESTAMPTZ,
    banned_until TIMESTAMPTZ,
    email_confirmed BOOLEAN NOT NULL DEFAULT true,
    email_confirmed_at TIMESTAMPTZ DEFAULT NOW(),
    auth_provider VARCHAR(20) NOT NULL DEFAULT 'LOCAL',
    google_sub VARCHAR(255),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

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
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE elo_config (
    id UUID PRIMARY KEY,
    k_factor_placement INTEGER NOT NULL DEFAULT 100,
    k_factor_standard INTEGER NOT NULL DEFAULT 20,
    placement_match_count INTEGER NOT NULL DEFAULT 5,
    default_elo INTEGER NOT NULL DEFAULT 1000,
    seed_thresholds JSONB,
    updated_by UUID REFERENCES users(id),
    updated_at TIMESTAMPTZ NOT NULL
);

-- ==========================================
-- 2. OFFLINE TOURNAMENT
-- ==========================================

CREATE TABLE offline_tournaments (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    location VARCHAR(255),
    start_date TIMESTAMPTZ NOT NULL,
    end_date TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(30) NOT NULL,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE offline_tournament_managers (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES offline_tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    assigned_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE offline_events (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES offline_tournaments(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    event_format_code VARCHAR(20) NOT NULL,
    time_limit_ms INTEGER,
    cutoff_time_ms INTEGER,
    solve_count INTEGER DEFAULT 5,
    sort_order INTEGER,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE offline_medley_puzzles (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES offline_events(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    sort_order INTEGER NOT NULL
);

CREATE TABLE offline_registrations (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES offline_tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    status_code VARCHAR(20) NOT NULL,
    qr_token VARCHAR(255) UNIQUE NOT NULL,
    registered_at TIMESTAMPTZ NOT NULL,
<<<<<<< HEAD
    checked_in_at TIMESTAMPTZ
=======
    checked_in_at TIMESTAMPTZ,
    face_verified_at TIMESTAMPTZ,
    face_verification_session_id UUID,

    CONSTRAINT uq_registrations_tournament_user
        UNIQUE (tournament_id, user_id),

    CONSTRAINT ck_registrations_status
        CHECK (status_code IN ('PENDING', 'CONFIRMED', 'CANCELLED', 'CHECKED_IN'))
>>>>>>> 42cd0430219596e4936ffedcfed65e3dc4437053
);

CREATE TABLE offline_groups (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES offline_events(id),
    round_number INTEGER NOT NULL,
    group_name VARCHAR(50),
    status_code VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE offline_group_competitors (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL REFERENCES offline_groups(id),
    registration_id UUID NOT NULL REFERENCES offline_registrations(id),
    seed_time_ms INTEGER,
    station_number INTEGER
);

CREATE TABLE offline_scramble_sets (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL REFERENCES offline_groups(id),
    pdf_url TEXT,
    pdf_password_hash VARCHAR(255),
    generated_at TIMESTAMPTZ NOT NULL,
    generated_by UUID REFERENCES users(id)
);

CREATE TABLE offline_scrambles (
    id UUID PRIMARY KEY,
    scramble_set_id UUID NOT NULL REFERENCES offline_scramble_sets(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    solve_number INTEGER NOT NULL,
    sequence TEXT NOT NULL,
    sort_order INTEGER NOT NULL
);

CREATE TABLE offline_results (
    id UUID PRIMARY KEY,
    group_competitor_id UUID NOT NULL REFERENCES offline_group_competitors(id),
    scramble_id UUID NOT NULL REFERENCES offline_scrambles(id),
    judged_by UUID NOT NULL REFERENCES users(id),
    solve_number INTEGER NOT NULL,
    raw_time_ms INTEGER,
    final_time_ms INTEGER,
    penalty VARCHAR(10) DEFAULT 'ok',
    esignature_data TEXT,
    signed_at TIMESTAMPTZ,
    submitted_at TIMESTAMPTZ NOT NULL,
    is_locked BOOLEAN DEFAULT false
);

CREATE TABLE offline_medley_result_details (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES offline_results(id),
    medley_puzzle_id UUID NOT NULL REFERENCES offline_medley_puzzles(id),
    raw_time_ms INTEGER,
    penalty VARCHAR(10) DEFAULT 'ok',
    sort_order INTEGER NOT NULL
);

CREATE TABLE offline_disputes (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES offline_results(id),
    reported_by UUID NOT NULL REFERENCES users(id),
    reason TEXT NOT NULL,
    status_code VARCHAR(20) NOT NULL,
    resolved_by UUID REFERENCES users(id),
    resolution_note TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    resolved_at TIMESTAMPTZ
);

-- ==========================================
-- 3. ONLINE ARENA
-- ==========================================

CREATE TABLE online_profiles (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    elo INTEGER NOT NULL DEFAULT 1000,
    peak_elo INTEGER,
    placement_matches_done INTEGER DEFAULT 0,
    is_placement_complete BOOLEAN DEFAULT false,
    seed_source_code VARCHAR(20),
    total_wins INTEGER DEFAULT 0,
    total_losses INTEGER DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    UNIQUE(user_id, puzzle_type_id)
);

CREATE TABLE online_matchmaking_queue (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    online_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    queued_at TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(20) NOT NULL
);

CREATE TABLE online_matches (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    scramble_sequence TEXT NOT NULL,
<<<<<<< HEAD
    player1_scramble_sequence TEXT,
    player2_scramble_sequence TEXT,
    scramble_pool_item_id UUID REFERENCES scramble_pool_items(id) ON DELETE SET NULL,

=======
>>>>>>> practice
    player1_id UUID NOT NULL REFERENCES users(id),
    player2_id UUID NOT NULL REFERENCES users(id),
    winner_id UUID REFERENCES users(id),
    status_code VARCHAR(20) NOT NULL,
    room_token VARCHAR(255) UNIQUE NOT NULL,
    player1_time_ms INTEGER,
    player1_elo_before INTEGER,
    player1_elo_after INTEGER,
    player1_device_info TEXT,
    player1_connected_at TIMESTAMPTZ,
    player2_time_ms INTEGER,
    player2_elo_before INTEGER,
    player2_elo_after INTEGER,
    player2_device_info TEXT,
    player2_connected_at TIMESTAMPTZ,
    started_at TIMESTAMPTZ,
    ended_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE online_elo_history (
    id UUID PRIMARY KEY,
    online_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    match_id UUID REFERENCES online_matches(id),
    elo_before INTEGER NOT NULL,
    elo_after INTEGER NOT NULL,
    delta INTEGER NOT NULL,
    reason_code VARCHAR(50),
    changed_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE online_fraud_reports (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id),
    reported_by UUID NOT NULL REFERENCES users(id),
    accused_user_id UUID NOT NULL REFERENCES users(id),
    description TEXT,
    evidence_url TEXT,
    status_code VARCHAR(20) NOT NULL,
    reviewed_by UUID REFERENCES users(id),
    verdict_code VARCHAR(20),
    admin_note TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ
);

-- ==========================================
-- 4. VIDEO CHALLENGES (giải đấu bất đồng bộ - user tự giải ở nhà, quay video nộp)
-- ==========================================

CREATE TABLE video_challenges (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    title VARCHAR(255) NOT NULL,
    description TEXT,
    scramble_sequence TEXT NOT NULL,
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(20) NOT NULL,  -- 'upcoming','active','ended'
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE video_challenge_submissions (
    id UUID PRIMARY KEY,
    challenge_id UUID NOT NULL REFERENCES video_challenges(id),
    user_id UUID NOT NULL REFERENCES users(id),
    video_url TEXT NOT NULL,
    claimed_time_ms INTEGER NOT NULL,
    status_code VARCHAR(20) NOT NULL,  -- 'pending','approved','rejected'
    reviewed_by UUID REFERENCES users(id),
    admin_note TEXT,
    submitted_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ
);

-- ==========================================
-- 5. PRACTICE
-- ==========================================

CREATE TABLE practice_solves (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    scramble_sequence TEXT NOT NULL,
    time_ms INTEGER NOT NULL,
    penalty VARCHAR(10) DEFAULT 'ok',
    solved_at TIMESTAMPTZ NOT NULL
);

-- ==========================================
-- 6. NOTIFICATIONS
-- ==========================================

CREATE TABLE notifications (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type_code VARCHAR(50) NOT NULL,
    title VARCHAR(255) NOT NULL,
    body TEXT,
    payload JSONB,
    is_read BOOLEAN DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL,
    read_at TIMESTAMPTZ
);

<<<<<<< HEAD
CREATE INDEX IF NOT EXISTS idx_notifications_user_read
ON notifications(user_id, is_read, created_at DESC);

-- Supports the admin unread-notification query efficiently.
CREATE INDEX IF NOT EXISTS ix_notifications_user_unread_created
ON notifications(user_id, is_read, created_at DESC);


-- =========================================================
=======
-- ==========================================
>>>>>>> practice
-- 7. REFRESH TOKENS
-- ==========================================

CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    token VARCHAR(255) UNIQUE NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    replaced_by VARCHAR(255)
);

-- ==========================================
-- INDEXES
-- ==========================================

<<<<<<< HEAD
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_role ON users(role);
CREATE INDEX idx_online_profiles_user_puzzle ON online_profiles(user_id, puzzle_type_id);
CREATE INDEX idx_online_matches_players ON online_matches(player1_id, player2_id);
CREATE INDEX idx_online_elo_history_profile ON online_elo_history(online_profile_id);
CREATE INDEX idx_online_matchmaking_status ON online_matchmaking_queue(status_code, puzzle_type_id);
CREATE INDEX idx_offline_registrations_tournament ON offline_registrations(tournament_id);
CREATE INDEX idx_offline_results_competitor ON offline_results(group_competitor_id);
CREATE INDEX idx_practice_solves_user_puzzle ON practice_solves(user_id, puzzle_type_id);
CREATE INDEX idx_notifications_user_unread ON notifications(user_id, is_read);
CREATE INDEX idx_video_challenge_submissions_challenge ON video_challenge_submissions(challenge_id);
=======
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
