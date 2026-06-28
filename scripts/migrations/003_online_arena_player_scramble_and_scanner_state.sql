ALTER TABLE online_matches
    ADD COLUMN IF NOT EXISTS player1_scramble_sequence text NULL,
    ADD COLUMN IF NOT EXISTS player2_scramble_sequence text NULL,
    ADD COLUMN IF NOT EXISTS player1_expected_state_json text NULL,
    ADD COLUMN IF NOT EXISTS player2_expected_state_json text NULL,
    ADD COLUMN IF NOT EXISTS player1_observed_state_json text NULL,
    ADD COLUMN IF NOT EXISTS player2_observed_state_json text NULL,
    ADD COLUMN IF NOT EXISTS player1_scanner_state_json text NULL,
    ADD COLUMN IF NOT EXISTS player2_scanner_state_json text NULL;

UPDATE online_matches
SET player1_scramble_sequence = COALESCE(player1_scramble_sequence, scramble_sequence),
    player2_scramble_sequence = COALESCE(player2_scramble_sequence, scramble_sequence)
WHERE player1_scramble_sequence IS NULL
   OR player2_scramble_sequence IS NULL;
