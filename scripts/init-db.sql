-- =========================================================
-- CubeNexus Database Schema
-- PostgreSQL
-- Merged CREATE TABLE + Constraints + Indexes
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
    avatar_url TEXT,
    user_role VARCHAR(30) NOT NULL DEFAULT 'COMPETITOR',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_banned BOOLEAN NOT NULL DEFAULT false,
    ban_reason TEXT,
    email_confirmed BOOLEAN NOT NULL DEFAULT false,
    email_confirmed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_users_role
        CHECK (user_role IN ('ADMIN', 'MANAGER', 'JUDGE', 'COMPETITOR')),

    CONSTRAINT ck_users_ban_reason
        CHECK (
            (is_banned = false AND ban_reason IS NULL)
            OR (is_banned = true)
        )
);

CREATE INDEX IF NOT EXISTS idx_users_role
ON users(user_role);

CREATE INDEX IF NOT EXISTS idx_users_active
ON users(is_active, is_banned);


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
    min_practice_solves INTEGER NOT NULL DEFAULT 5,
    updated_by UUID REFERENCES users(id),
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_elo_config_values
        CHECK (
            k_factor_placement > 0
            AND k_factor_standard > 0
            AND placement_match_count > 0
            AND default_elo >= 0
            AND min_practice_solves >= 5
        )
);


CREATE TABLE IF NOT EXISTS elo_seed_thresholds (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    label VARCHAR(100),
    min_time_ms INTEGER,
    max_time_ms INTEGER,
    elo_value INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,

    CONSTRAINT uq_elo_seed_thresholds_puzzle_order
        UNIQUE (puzzle_type_id, sort_order),

    CONSTRAINT ck_elo_seed_thresholds_range
        CHECK (
            elo_value >= 0
            AND sort_order > 0
            AND (min_time_ms IS NULL OR min_time_ms >= 0)
            AND (max_time_ms IS NULL OR max_time_ms > 0)
            AND (
                min_time_ms IS NULL
                OR max_time_ms IS NULL
                OR min_time_ms < max_time_ms
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_elo_seed_thresholds_puzzle
ON elo_seed_thresholds(puzzle_type_id);


-- =========================================================
-- 2. OFFLINE TOURNAMENT
-- =========================================================

CREATE TABLE IF NOT EXISTS tournaments (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    location VARCHAR(255),

    registration_open_at TIMESTAMPTZ NOT NULL,
    registration_close_at TIMESTAMPTZ NOT NULL,

    start_date TIMESTAMPTZ NOT NULL,
    end_date TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(30) NOT NULL,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_tournaments_status
        CHECK (status_code IN ('DRAFT', 'PUBLISHED', 'ONGOING', 'COMPLETED', 'CANCELLED')),

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


CREATE TABLE IF NOT EXISTS events (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    event_format_code VARCHAR(20) NOT NULL,
    time_limit_ms INTEGER,
    cutoff_time_ms INTEGER,
    solve_count INTEGER NOT NULL DEFAULT 5,
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

    CONSTRAINT ck_medley_result_details_values
        CHECK (
            sort_order > 0
            AND (raw_time_ms IS NULL OR raw_time_ms > 0)
            AND (final_time_ms IS NULL OR final_time_ms > 0)
        ),

    CONSTRAINT ck_medley_result_details_dnf_consistency
        CHECK (
            (is_dnf = true AND final_time_ms IS NULL)
            OR
            (is_dnf = false AND raw_time_ms IS NOT NULL AND final_time_ms IS NOT NULL)
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
-- 3. ONLINE ARENA
-- =========================================================

CREATE TABLE IF NOT EXISTS practice_ao5_snapshots (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    ao5_time_ms INTEGER NOT NULL,
    assigned_elo INTEGER NOT NULL,
    seed_threshold_id UUID REFERENCES elo_seed_thresholds(id),
    calculated_at TIMESTAMPTZ NOT NULL,
    is_used_for_seeding BOOLEAN NOT NULL DEFAULT false,

    CONSTRAINT ck_practice_ao5_snapshots_values
        CHECK (
            ao5_time_ms > 0
            AND assigned_elo >= 0
        )
);

CREATE INDEX IF NOT EXISTS idx_practice_ao5_user_puzzle
ON practice_ao5_snapshots(user_id, puzzle_type_id);


CREATE TABLE IF NOT EXISTS online_profiles (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),

    elo INTEGER NOT NULL DEFAULT 1000,
    peak_elo INTEGER NOT NULL DEFAULT 1000,

    seed_elo INTEGER,
    seed_source_code VARCHAR(20),
    practice_ao5_ms INTEGER,
    practice_ao5_snapshot_id UUID REFERENCES practice_ao5_snapshots(id),

    placement_matches_done INTEGER NOT NULL DEFAULT 0,
    is_placement_complete BOOLEAN NOT NULL DEFAULT false,
    placement_completed_at TIMESTAMPTZ,

    k_factor_current INTEGER NOT NULL DEFAULT 100,

    total_wins INTEGER NOT NULL DEFAULT 0,
    total_losses INTEGER NOT NULL DEFAULT 0,
    total_draws INTEGER NOT NULL DEFAULT 0,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_online_profiles_user_puzzle
        UNIQUE (user_id, puzzle_type_id),

    CONSTRAINT ck_online_profiles_elo
        CHECK (elo >= 0),

    CONSTRAINT ck_online_profiles_peak_elo
        CHECK (peak_elo >= 0),

    CONSTRAINT ck_online_profiles_seed_source
        CHECK (
            seed_source_code IS NULL
            OR seed_source_code IN ('PRACTICE', 'DEFAULT')
        ),

    CONSTRAINT ck_online_profiles_stats
        CHECK (
            placement_matches_done >= 0
            AND k_factor_current > 0
            AND total_wins >= 0
            AND total_losses >= 0
            AND total_draws >= 0
        )
);

CREATE INDEX IF NOT EXISTS idx_online_profiles_user_id
ON online_profiles(user_id);

CREATE INDEX IF NOT EXISTS idx_online_profiles_puzzle_type_id
ON online_profiles(puzzle_type_id);

CREATE INDEX IF NOT EXISTS idx_online_profiles_leaderboard
ON online_profiles(puzzle_type_id, elo DESC);

CREATE INDEX IF NOT EXISTS idx_online_profiles_matchmaking
ON online_profiles(puzzle_type_id, is_placement_complete, elo);


CREATE TABLE IF NOT EXISTS matchmaking_queue (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    online_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    queued_at TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(20) NOT NULL,

    CONSTRAINT ck_matchmaking_queue_status
        CHECK (status_code IN ('QUEUED', 'MATCHED', 'CANCELLED'))
    
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_matchmaking_queue_active_user_puzzle
ON matchmaking_queue(user_id, puzzle_type_id)
WHERE status_code = 'QUEUED';

CREATE INDEX IF NOT EXISTS idx_matchmaking_queue_search
ON matchmaking_queue(puzzle_type_id, status_code, queued_at);

CREATE INDEX IF NOT EXISTS idx_matchmaking_queue_profile
ON matchmaking_queue(online_profile_id);


CREATE TABLE IF NOT EXISTS online_matches (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    scramble_sequence TEXT NOT NULL,

    player1_id UUID NOT NULL REFERENCES users(id),
    player2_id UUID NOT NULL REFERENCES users(id),

    player1_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    player2_profile_id UUID NOT NULL REFERENCES online_profiles(id),

    winner_id UUID REFERENCES users(id),
    status_code VARCHAR(20) NOT NULL,
    room_token VARCHAR(255) UNIQUE NOT NULL,
    qr_session_code VARCHAR(255),
    player1_time_ms INTEGER,
    player2_time_ms INTEGER,
    player1_elo_before INTEGER,
    player2_elo_before INTEGER,
    player1_elo_after INTEGER,
    player2_elo_after INTEGER,
    started_at TIMESTAMPTZ,
    ended_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_online_matches_status
        CHECK (status_code IN ('ONGOING', 'COMPLETED', 'CANCELLED', 'DRAW')),

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
        )
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
    reported_by UUID NOT NULL REFERENCES users(id),
    accused_user_id UUID NOT NULL REFERENCES users(id),
    description TEXT,
    evidence_url TEXT,
    status_code VARCHAR(20) NOT NULL,
    reviewed_by UUID REFERENCES users(id),
    verdict_code VARCHAR(20),
    admin_note TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ,

    CONSTRAINT ck_fraud_reports_status
        CHECK (status_code IN ('PENDING', 'REVIEWED', 'DISMISSED')),

    CONSTRAINT ck_fraud_reports_verdict
        CHECK (verdict_code IS NULL OR verdict_code IN ('GUILTY', 'INNOCENT', 'INCONCLUSIVE')),

    CONSTRAINT ck_fraud_reports_users
        CHECK (reported_by <> accused_user_id)
);

CREATE INDEX IF NOT EXISTS idx_fraud_reports_match
ON fraud_reports(match_id);

CREATE INDEX IF NOT EXISTS idx_fraud_reports_status
ON fraud_reports(status_code);

CREATE INDEX IF NOT EXISTS idx_fraud_reports_accused
ON fraud_reports(accused_user_id);


-- =========================================================
-- 4. ASYNC TOURNAMENT
-- =========================================================

CREATE TABLE IF NOT EXISTS async_tournaments (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    title VARCHAR(255) NOT NULL,
    description TEXT,
    scramble_sequence TEXT NOT NULL,
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(20) NOT NULL,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL,

    CONSTRAINT ck_async_tournaments_status
        CHECK (status_code IN ('UPCOMING', 'ONGOING', 'COMPLETED', 'CANCELLED')),

    CONSTRAINT ck_async_tournaments_date
        CHECK (end_at > start_at)
);

CREATE INDEX IF NOT EXISTS idx_async_tournaments_puzzle
ON async_tournaments(puzzle_type_id);

CREATE INDEX IF NOT EXISTS idx_async_tournaments_status
ON async_tournaments(status_code);


CREATE TABLE IF NOT EXISTS async_submissions (
    id UUID PRIMARY KEY,
    async_tournament_id UUID NOT NULL REFERENCES async_tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    video_url TEXT NOT NULL,
    claimed_time_ms INTEGER NOT NULL,
    status_code VARCHAR(20) NOT NULL,
    reviewed_by UUID REFERENCES users(id),
    admin_note TEXT,
    submitted_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ,

    CONSTRAINT ck_async_submissions_status
        CHECK (status_code IN ('PENDING', 'APPROVED', 'REJECTED')),

    CONSTRAINT uq_async_submissions_tournament_user
        UNIQUE (async_tournament_id, user_id),

    CONSTRAINT ck_async_submissions_time
        CHECK (claimed_time_ms > 0)
);

CREATE INDEX IF NOT EXISTS idx_async_submissions_user
ON async_submissions(user_id);

CREATE INDEX IF NOT EXISTS idx_async_submissions_status
ON async_submissions(status_code);


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
    user_id UUID NOT NULL REFERENCES users(id),
    type_code VARCHAR(50) NOT NULL,
    title VARCHAR(255) NOT NULL,
    body TEXT,
    payload JSONB,
    is_read BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL,
    read_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_notifications_user_read
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

COMMIT;
