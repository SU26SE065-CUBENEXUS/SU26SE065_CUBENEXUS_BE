ALTER TABLE online_matches
    ADD COLUMN IF NOT EXISTS player1_web_rtc_connected boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player2_web_rtc_connected boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player1_recording_started boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player2_recording_started boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS player1_ai_pre_check_status text NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS player2_ai_pre_check_status text NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS player1_scramble_check_status text NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS player2_scramble_check_status text NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS player1_finish_check_status text NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS player2_finish_check_status text NOT NULL DEFAULT 'PENDING',
    ADD COLUMN IF NOT EXISTS outcome text NOT NULL DEFAULT 'INCONCLUSIVE',
    ADD COLUMN IF NOT EXISTS review_reason_json text NULL,
    ADD COLUMN IF NOT EXISTS video_evidence_upload_deadline_at timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS player1_recording_started_at timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS player2_recording_started_at timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS time_limit_ms integer NOT NULL DEFAULT 480000;

CREATE TABLE IF NOT EXISTS online_match_video_evidence (
    id uuid PRIMARY KEY,
    match_id uuid NOT NULL REFERENCES online_matches(id) ON DELETE CASCADE,
    player_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    file_url text NOT NULL,
    thumbnail_url text NULL,
    duration_ms bigint NULL,
    recording_started_at timestamp with time zone NULL,
    recording_ended_at timestamp with time zone NULL,
    uploaded_at timestamp with time zone NULL,
    status text NOT NULL,
    checksum text NULL,
    source_type text NOT NULL DEFAULT 'LOCAL_CAMERA',
    mime_type text NULL
);

CREATE TABLE IF NOT EXISTS online_match_ai_checks (
    id uuid PRIMARY KEY,
    match_id uuid NOT NULL REFERENCES online_matches(id) ON DELETE CASCADE,
    player_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    check_type text NOT NULL,
    status text NOT NULL,
    confidence double precision NULL,
    evidence_image_url text NULL,
    video_evidence_id uuid NULL REFERENCES online_match_video_evidence(id) ON DELETE SET NULL,
    model_version text NULL,
    result_json text NULL,
    failure_reason text NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS online_match_audit_logs (
    id uuid PRIMARY KEY,
    match_id uuid NOT NULL REFERENCES online_matches(id) ON DELETE CASCADE,
    player_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    event_type text NOT NULL,
    payload_json text NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now()
);

ALTER TABLE fraud_reports
    ADD COLUMN IF NOT EXISTS reporter_user_id uuid NULL REFERENCES users(id) ON DELETE RESTRICT,
    ADD COLUMN IF NOT EXISTS reported_user_id uuid NULL REFERENCES users(id) ON DELETE RESTRICT,
    ADD COLUMN IF NOT EXISTS reason_code text NULL,
    ADD COLUMN IF NOT EXISTS review_scope text NOT NULL DEFAULT 'WHOLE_MATCH',
    ADD COLUMN IF NOT EXISTS decision text NULL,
    ADD COLUMN IF NOT EXISTS penalty_action text NULL,
    ADD COLUMN IF NOT EXISTS resolved_by_admin_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS resolved_at timestamp with time zone NULL;

UPDATE fraud_reports
SET reporter_user_id = COALESCE(reporter_user_id, reported_by),
    reported_user_id = COALESCE(reported_user_id, accused_user_id)
WHERE reporter_user_id IS NULL OR reported_user_id IS NULL;
