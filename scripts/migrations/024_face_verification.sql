-- Face verification for offline tournament check-in (and profile enrollment).
-- NOTE: Fresh installs use scripts/init-db.sql (already includes this schema).
-- Apply this file ONLY when upgrading an existing database created before face verification.

CREATE TABLE IF NOT EXISTS face_enrollments (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    status text NOT NULL DEFAULT 'ENROLLED',
    model_version text NULL,
    quality_score double precision NULL,
    templates_count integer NOT NULL DEFAULT 0,
    last_external_session_id text NULL,
    enrolled_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT face_enrollments_status_chk CHECK (status IN ('ENROLLED', 'REVOKED'))
);

CREATE INDEX IF NOT EXISTS ix_face_enrollments_status ON face_enrollments(status);

CREATE TABLE IF NOT EXISTS face_verification_sessions (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    purpose text NOT NULL,
    context_type text NOT NULL,
    tournament_id uuid NULL REFERENCES tournaments(id) ON DELETE SET NULL,
    registration_id uuid NULL REFERENCES registrations(id) ON DELETE SET NULL,
    initiated_by_user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
    external_session_id text NOT NULL,
    upload_token text NOT NULL,
    challenge_json text NULL,
    state text NOT NULL DEFAULT 'POSITIONING',
    result_json text NULL,
    failure_reason text NULL,
    liveness_passed boolean NULL,
    face_matched boolean NULL,
    similarity double precision NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    completed_at timestamp with time zone NULL,
    CONSTRAINT face_verification_sessions_purpose_chk
        CHECK (purpose IN ('ENROLLMENT', 'VERIFICATION')),
    CONSTRAINT face_verification_sessions_context_chk
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
    ADD COLUMN IF NOT EXISTS face_verified_at timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS face_verification_session_id uuid NULL
        REFERENCES face_verification_sessions(id) ON DELETE SET NULL;
