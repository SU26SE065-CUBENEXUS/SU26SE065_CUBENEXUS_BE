-- Backfill: users no longer need email confirmation to login
UPDATE users
SET email_confirmed = true,
    email_confirmed_at = COALESCE(email_confirmed_at, NOW())
WHERE email_confirmed = false;
