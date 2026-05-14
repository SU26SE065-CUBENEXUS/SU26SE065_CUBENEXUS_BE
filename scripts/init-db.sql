-- ==========================================
-- 1. MASTER DATA & IDENTITY
-- ==========================================

-- Bảng tài khoản người dùng
CREATE TABLE users (
    id UUID PRIMARY KEY,
    user_code VARCHAR(255) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    avatar_url TEXT,
    is_active BOOLEAN DEFAULT true,
    is_banned BOOLEAN DEFAULT false,
    ban_reason TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

-- Bảng vai trò hệ thống
CREATE TABLE roles (
    id UUID PRIMARY KEY,
    name VARCHAR(50) UNIQUE NOT NULL,
    description TEXT
);

-- Gán vai trò cho người dùng (n-n)
CREATE TABLE user_roles (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    role_id UUID NOT NULL REFERENCES roles(id),
    granted_by UUID REFERENCES users(id),
    granted_at TIMESTAMPTZ NOT NULL
);

-- Loại puzzle (3x3, 2x2, Megaminx...)
CREATE TABLE puzzle_types (
    id UUID PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    code VARCHAR(20) UNIQUE NOT NULL,
    scramble_length INTEGER,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL
);

-- Luật phạt chuẩn WCA (+2, DNF, OK)
CREATE TABLE penalty_types (
    id UUID PRIMARY KEY,
    code VARCHAR(10) UNIQUE NOT NULL,
    label VARCHAR(50) NOT NULL,
    time_addition_ms INTEGER DEFAULT 0,
    is_disqualified BOOLEAN DEFAULT false
);

-- Tham số Elo do Admin quản lý
CREATE TABLE elo_config (
    id UUID PRIMARY KEY,
    k_factor_placement INTEGER NOT NULL DEFAULT 100,
    k_factor_standard INTEGER NOT NULL DEFAULT 20,
    placement_match_count INTEGER NOT NULL DEFAULT 5,
    default_elo INTEGER NOT NULL DEFAULT 1000,
    updated_by UUID REFERENCES users(id),
    updated_at TIMESTAMPTZ NOT NULL
);

-- Ngưỡng Ao5 -> Elo khởi điểm
CREATE TABLE elo_seed_thresholds (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    max_time_ms INTEGER,
    min_time_ms INTEGER,
    elo_value INTEGER NOT NULL,
    sort_order INTEGER NOT NULL
);

-- ==========================================
-- 2. OFFLINE TOURNAMENT
-- ==========================================

-- Giải đấu offline
CREATE TABLE tournaments (
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

CREATE TABLE tournament_managers (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    assigned_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE events (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    event_format_code VARCHAR(20) NOT NULL,
    time_limit_ms INTEGER,
    cutoff_time_ms INTEGER,
    solve_count INTEGER DEFAULT 5,
    sort_order INTEGER,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE medley_event_puzzles (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES events(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    sort_order INTEGER NOT NULL
);

CREATE TABLE registrations (
    id UUID PRIMARY KEY,
    tournament_id UUID NOT NULL REFERENCES tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    status_code VARCHAR(20) NOT NULL,
    qr_token VARCHAR(255) UNIQUE NOT NULL,
    registered_at TIMESTAMPTZ NOT NULL,
    checked_in_at TIMESTAMPTZ
);

CREATE TABLE groups (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES events(id),
    round_number INTEGER NOT NULL,
    group_name VARCHAR(50),
    status_code VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE group_competitors (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL REFERENCES groups(id),
    registration_id UUID NOT NULL REFERENCES registrations(id),
    seed_time_ms INTEGER,
    station_number INTEGER
);

CREATE TABLE scramble_sets (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL REFERENCES groups(id),
    pdf_url TEXT,
    pdf_password_hash VARCHAR(255),
    generated_at TIMESTAMPTZ NOT NULL,
    generated_by UUID REFERENCES users(id)
);

CREATE TABLE scrambles (
    id UUID PRIMARY KEY,
    scramble_set_id UUID NOT NULL REFERENCES scramble_sets(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    solve_number INTEGER NOT NULL,
    sequence TEXT NOT NULL,
    sort_order INTEGER NOT NULL
);

CREATE TABLE results (
    id UUID PRIMARY KEY,
    group_competitor_id UUID NOT NULL REFERENCES group_competitors(id),
    scramble_id UUID NOT NULL REFERENCES scrambles(id),
    judged_by UUID NOT NULL REFERENCES users(id),
    solve_number INTEGER NOT NULL,
    raw_time_ms INTEGER,
    final_time_ms INTEGER,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN DEFAULT false,
    esignature_data TEXT,
    signed_at TIMESTAMPTZ,
    submitted_at TIMESTAMPTZ NOT NULL,
    is_locked BOOLEAN DEFAULT false
);

CREATE TABLE medley_result_details (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES results(id),
    medley_puzzle_id UUID NOT NULL REFERENCES medley_event_puzzles(id),
    raw_time_ms INTEGER,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN DEFAULT false,
    sort_order INTEGER NOT NULL
);

CREATE TABLE disputes (
    id UUID PRIMARY KEY,
    result_id UUID NOT NULL REFERENCES results(id),
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
    user_id UUID UNIQUE NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    elo INTEGER NOT NULL DEFAULT 1000,
    peak_elo INTEGER,
    placement_matches_done INTEGER DEFAULT 0,
    is_placement_complete BOOLEAN DEFAULT false,
    seed_source_code VARCHAR(20),
    total_wins INTEGER DEFAULT 0,
    total_losses INTEGER DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE matchmaking_queue (
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
    qr_session_code VARCHAR(255),
    player1_time_ms INTEGER,
    player2_time_ms INTEGER,
    player1_elo_before INTEGER,
    player2_elo_before INTEGER,
    player1_elo_after INTEGER,
    player2_elo_after INTEGER,
    started_at TIMESTAMPTZ,
    ended_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE mobile_timer_sessions (
    id UUID PRIMARY KEY,
    match_id UUID NOT NULL REFERENCES online_matches(id),
    user_id UUID NOT NULL REFERENCES users(id),
    qr_session_code VARCHAR(255) NOT NULL,
    device_info TEXT,
    connected_at TIMESTAMPTZ,
    is_active BOOLEAN DEFAULT false
);

CREATE TABLE elo_history (
    id UUID PRIMARY KEY,
    online_profile_id UUID NOT NULL REFERENCES online_profiles(id),
    match_id UUID REFERENCES online_matches(id),
    elo_before INTEGER NOT NULL,
    elo_after INTEGER NOT NULL,
    delta INTEGER NOT NULL,
    reason_code VARCHAR(50),
    changed_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE fraud_reports (
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
-- 4. ASYNC TOURNAMENT
-- ==========================================

CREATE TABLE async_tournaments (
    id UUID PRIMARY KEY,
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    title VARCHAR(255) NOT NULL,
    description TEXT,
    scramble_sequence TEXT NOT NULL,
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    status_code VARCHAR(20) NOT NULL,
    created_by UUID NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE async_submissions (
    id UUID PRIMARY KEY,
    async_tournament_id UUID NOT NULL REFERENCES async_tournaments(id),
    user_id UUID NOT NULL REFERENCES users(id),
    video_url TEXT NOT NULL,
    claimed_time_ms INTEGER NOT NULL,
    status_code VARCHAR(20) NOT NULL,
    reviewed_by UUID REFERENCES users(id),
    admin_note TEXT,
    submitted_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ
);

-- ==========================================
-- 5. PRACTICE
-- ==========================================

CREATE TABLE practice_sessions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id),
    puzzle_type_id UUID NOT NULL REFERENCES puzzle_types(id),
    started_at TIMESTAMPTZ NOT NULL,
    ended_at TIMESTAMPTZ
);

CREATE TABLE practice_solves (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES practice_sessions(id),
    scramble_sequence TEXT NOT NULL,
    time_ms INTEGER NOT NULL,
    penalty_type_id UUID REFERENCES penalty_types(id),
    is_dnf BOOLEAN DEFAULT false,
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
