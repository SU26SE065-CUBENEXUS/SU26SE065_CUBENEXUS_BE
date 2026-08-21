-- ==========================================
-- 1. MASTER DATA & IDENTITY
-- ==========================================

CREATE TABLE users (
    id UUID PRIMARY KEY,
    user_code VARCHAR(255) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    avatar_url TEXT,
    user_role VARCHAR(20) NOT NULL,  -- 'admin', 'player', 'judge', 'organizer'
    is_active BOOLEAN DEFAULT true,
    is_banned BOOLEAN DEFAULT false,
    ban_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE puzzle_types (
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
    checked_in_at TIMESTAMPTZ
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
    user_id UUID NOT NULL REFERENCES users(id),
    type_code VARCHAR(50) NOT NULL,
    title VARCHAR(255) NOT NULL,
    body TEXT,
    payload JSONB,
    is_read BOOLEAN DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL,
    read_at TIMESTAMPTZ
);

-- ==========================================
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
