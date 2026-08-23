-- =========================================================
-- CubeNexus demo seed data
-- Safe to run repeatedly: fixed IDs + conflict-safe inserts.
-- Target: PostgreSQL Railway / existing CubeNexus schema.
-- =========================================================

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- The shared hash below is the demo password hash supplied for these accounts.
-- Do not use these accounts for a real production deployment.
INSERT INTO users (
    id, user_code, email, password_hash, display_name,
    avatar_url, user_role, is_active, is_banned,
    ban_reason, email_confirmed, email_confirmed_at,
    auth_provider, created_at, updated_at
)
VALUES
    ('10000000-0000-0000-0000-000000000001', 'ADMIN001', 'admin@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'System Administrator', NULL, 'ADMIN', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000002', 'MANAGER001', 'manager1@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Tournament Manager 1', NULL, 'MANAGER', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000003', 'MANAGER002', 'manager2@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Tournament Manager 2', NULL, 'MANAGER', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000004', 'JUDGE001', 'judge1@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Head Judge', NULL, 'JUDGE', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000005', 'JUDGE002', 'judge2@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Judge Assistant', NULL, 'JUDGE', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000006', 'COMP001', 'competitor1@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Nguyen Van A', NULL, 'COMPETITOR', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000007', 'COMP002', 'competitor2@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Tran Van B', NULL, 'COMPETITOR', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000008', 'COMP003', 'competitor3@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Le Van C', NULL, 'COMPETITOR', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000009', 'COMP004', 'competitor4@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Pham Van D', NULL, 'COMPETITOR', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW()),
    ('10000000-0000-0000-0000-000000000010', 'COMP005', 'competitor5@cubenexus.com',
     '100000./VeF4GIRn7XX7GEAgaAdVg==.14fU+ipyygYGH5PIIGlZlO2q4dqjn0u+ZtPAZ379b0s=',
     'Hoang Van E', NULL, 'COMPETITOR', true, false, NULL, true, NOW(), 'LOCAL', NOW(), NOW())
ON CONFLICT (user_code) DO UPDATE SET
    email = EXCLUDED.email,
    display_name = EXCLUDED.display_name,
    user_role = EXCLUDED.user_role,
    is_active = true,
    is_banned = false,
    ban_reason = NULL,
    email_confirmed = true,
    email_confirmed_at = COALESCE(users.email_confirmed_at, NOW()),
    updated_at = NOW();

-- Manager 1: one published tournament in October 2026.
INSERT INTO tournaments (
    id, name, description, location, max_participants, banner_url,
    start_date, end_date, registration_open_at, registration_close_at,
    status_code, created_by, created_at, updated_at,
    tournament_type, format_code, attempt_time_limit_ms
)
VALUES (
    '20000000-0000-0000-0000-000000000001',
    'CubeNexus October Open 2026',
    'Demo tournament created by Tournament Manager 1.',
    'CubeNexus Arena', 64, NULL,
    '2026-10-10 08:00:00+07', '2026-10-10 18:00:00+07',
    '2026-09-01 00:00:00+07', '2026-10-09 23:59:59+07',
    'PUBLISHED', (SELECT id FROM users WHERE user_code = 'MANAGER001'), NOW(), NOW(),
    'OFFLINE', 'AO1', 300000
)
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    status_code = EXCLUDED.status_code,
    start_date = EXCLUDED.start_date,
    end_date = EXCLUDED.end_date,
    registration_open_at = EXCLUDED.registration_open_at,
    registration_close_at = EXCLUDED.registration_close_at,
    updated_at = NOW();

-- Manager 2: five published tournaments in November 2026.
INSERT INTO tournaments (
    id, name, description, location, max_participants, banner_url,
    start_date, end_date, registration_open_at, registration_close_at,
    status_code, created_by, created_at, updated_at,
    tournament_type, format_code, attempt_time_limit_ms
)
VALUES
    ('20000000-0000-0000-0000-000000000002', 'CubeNexus November Open 01', 'Demo tournament created by Tournament Manager 2.', 'CubeNexus Arena', 64, NULL, '2026-11-01 08:00:00+07', '2026-11-01 18:00:00+07', '2026-10-01 00:00:00+07', '2026-10-31 23:59:59+07', 'PUBLISHED', (SELECT id FROM users WHERE user_code = 'MANAGER002'), NOW(), NOW(), 'OFFLINE', 'AO1', 300000),
    ('20000000-0000-0000-0000-000000000003', 'CubeNexus November Open 02', 'Demo tournament created by Tournament Manager 2.', 'CubeNexus Arena', 64, NULL, '2026-11-08 08:00:00+07', '2026-11-08 18:00:00+07', '2026-10-01 00:00:00+07', '2026-11-07 23:59:59+07', 'PUBLISHED', (SELECT id FROM users WHERE user_code = 'MANAGER002'), NOW(), NOW(), 'OFFLINE', 'AO1', 300000),
    ('20000000-0000-0000-0000-000000000004', 'CubeNexus November Open 03', 'Demo tournament created by Tournament Manager 2.', 'CubeNexus Arena', 64, NULL, '2026-11-15 08:00:00+07', '2026-11-15 18:00:00+07', '2026-10-01 00:00:00+07', '2026-11-14 23:59:59+07', 'PUBLISHED', (SELECT id FROM users WHERE user_code = 'MANAGER002'), NOW(), NOW(), 'OFFLINE', 'AO1', 300000),
    ('20000000-0000-0000-0000-000000000005', 'CubeNexus November Open 04', 'Demo tournament created by Tournament Manager 2.', 'CubeNexus Arena', 64, NULL, '2026-11-22 08:00:00+07', '2026-11-22 18:00:00+07', '2026-10-01 00:00:00+07', '2026-11-21 23:59:59+07', 'PUBLISHED', (SELECT id FROM users WHERE user_code = 'MANAGER002'), NOW(), NOW(), 'OFFLINE', 'AO1', 300000),
    ('20000000-0000-0000-0000-000000000006', 'CubeNexus November Open 05', 'Demo tournament created by Tournament Manager 2.', 'CubeNexus Arena', 64, NULL, '2026-11-29 08:00:00+07', '2026-11-29 18:00:00+07', '2026-10-01 00:00:00+07', '2026-11-28 23:59:59+07', 'PUBLISHED', (SELECT id FROM users WHERE user_code = 'MANAGER002'), NOW(), NOW(), 'OFFLINE', 'AO1', 300000)
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    status_code = EXCLUDED.status_code,
    start_date = EXCLUDED.start_date,
    end_date = EXCLUDED.end_date,
    registration_open_at = EXCLUDED.registration_open_at,
    registration_close_at = EXCLUDED.registration_close_at,
    updated_at = NOW();

-- Explicit manager assignments.
INSERT INTO tournament_managers (id, tournament_id, user_id, assigned_at)
SELECT gen_random_uuid(), t.id, u.id, NOW()
FROM (VALUES
    ('20000000-0000-0000-0000-000000000001'::uuid, 'MANAGER001'),
    ('20000000-0000-0000-0000-000000000002'::uuid, 'MANAGER002'),
    ('20000000-0000-0000-0000-000000000003'::uuid, 'MANAGER002'),
    ('20000000-0000-0000-0000-000000000004'::uuid, 'MANAGER002'),
    ('20000000-0000-0000-0000-000000000005'::uuid, 'MANAGER002'),
    ('20000000-0000-0000-0000-000000000006'::uuid, 'MANAGER002')
) AS seed(tournament_id, user_code)
JOIN tournaments t ON t.id = seed.tournament_id
JOIN users u ON u.user_code = seed.user_code
WHERE NOT EXISTS (
    SELECT 1 FROM tournament_managers tm
    WHERE tm.tournament_id = seed.tournament_id AND tm.user_id = u.id
);

COMMIT;

-- Verification output.
SELECT user_code, email, user_role FROM users
WHERE user_code IN ('ADMIN001', 'MANAGER001', 'MANAGER002', 'JUDGE001', 'JUDGE002', 'COMP001', 'COMP002', 'COMP003', 'COMP004', 'COMP005')
ORDER BY user_code;

SELECT t.name, t.status_code, u.user_code AS manager_code,
       t.start_date::date AS start_date, t.end_date::date AS end_date
FROM tournaments t
JOIN users u ON u.id = t.created_by
WHERE t.id BETWEEN '20000000-0000-0000-0000-000000000001'::uuid
               AND '20000000-0000-0000-0000-000000000006'::uuid
ORDER BY t.start_date;
