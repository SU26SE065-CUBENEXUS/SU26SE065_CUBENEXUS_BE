ALTER TABLE online_match_video_evidence
    ADD COLUMN IF NOT EXISTS object_key text NULL,
    ADD COLUMN IF NOT EXISTS content_type text NULL,
    ADD COLUMN IF NOT EXISTS file_size_bytes bigint NULL,
    ADD COLUMN IF NOT EXISTS duration_seconds double precision NULL,
    ADD COLUMN IF NOT EXISTS recording_status text NOT NULL DEFAULT 'Pending',
    ADD COLUMN IF NOT EXISTS recorded_at timestamp with time zone NULL;

UPDATE online_match_video_evidence
SET object_key = COALESCE(object_key, NULLIF(file_url, '')),
    content_type = COALESCE(content_type, mime_type),
    file_size_bytes = COALESCE(file_size_bytes, 0),
    duration_seconds = COALESCE(duration_seconds, CASE WHEN duration_ms IS NULL THEN NULL ELSE duration_ms / 1000.0 END),
    recording_status = COALESCE(NULLIF(recording_status, ''), CASE WHEN status IS NULL OR status = '' THEN 'Pending' ELSE status END),
    recorded_at = COALESCE(recorded_at, recording_started_at)
WHERE object_key IS NULL
   OR content_type IS NULL
   OR duration_seconds IS NULL
   OR recorded_at IS NULL
   OR recording_status IS NULL
   OR recording_status = '';
