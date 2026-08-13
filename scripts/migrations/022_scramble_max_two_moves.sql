BEGIN;

-- Scrambles longer than two moves were created by the previous generator.
-- They must never enter the AVAILABLE assignment queue under the short-scan policy.
UPDATE scramble_pool_items
SET status = 'INVALID',
    is_validated = false
WHERE status IN ('DRAFT', 'AVAILABLE')
  AND cardinality(regexp_split_to_array(trim(sequence), E'\\s+')) > 2;

DO $$ BEGIN
    ALTER TABLE scramble_pool_items
        ADD CONSTRAINT ck_scramble_pool_max_two_moves
        CHECK (
            status IN ('RETIRED', 'INVALID')
            OR cardinality(regexp_split_to_array(trim(sequence), E'\\s+')) <= 2
        );
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

COMMIT;
