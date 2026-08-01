-- Add phone and address to users for registration / profile.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS phone VARCHAR(20) NOT NULL DEFAULT '';

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS address TEXT NOT NULL DEFAULT '';
