-- Run this after 03_add_tags_catalog.sql. Normalizes flow_tags to reference
-- the shared "tags" catalog by id instead of embedding a free-text
-- tag_name, so a tag defined once in the catalog can be reused across many
-- flows without duplicating/desyncing its spelling. This matches the schema
-- already live in production — this script is provided so other
-- environments (fresh dev/test databases) can reach the same state.
--
-- Safe to run once; guards are included so re-running is a no-op.

-- 1) Add the tag_id column (nullable at first so we can backfill).
ALTER TABLE flow_tags ADD COLUMN IF NOT EXISTS tag_id INT NULL;

-- 2) Backfill tag_id for existing rows by matching on tag_name, creating
--    any catalog tag that doesn't already exist.
INSERT INTO tags (tag_name)
SELECT DISTINCT ft.tag_name
FROM flow_tags ft
WHERE ft.tag_name IS NOT NULL
ON CONFLICT (tag_name) DO NOTHING;

UPDATE flow_tags ft
SET tag_id = t.id
FROM tags t
WHERE ft.tag_id IS NULL AND t.tag_name = ft.tag_name;

-- 3) Enforce the FK/NOT NULL/uniqueness now that all rows are backfilled.
ALTER TABLE flow_tags ALTER COLUMN tag_id SET NOT NULL;
ALTER TABLE flow_tags
    ADD CONSTRAINT flow_tags_tag_id_fkey FOREIGN KEY (tag_id) REFERENCES tags(id);

-- Old constraint was UNIQUE (flow_definition_id, tag_name); replace it with
-- the tag_id-based one the application code now relies on for ON CONFLICT.
ALTER TABLE flow_tags DROP CONSTRAINT IF EXISTS flow_tags_flow_definition_id_tag_name_key;
ALTER TABLE flow_tags
    ADD CONSTRAINT flow_tags_flow_definition_id_tag_id_key UNIQUE (flow_definition_id, tag_id);

CREATE INDEX IF NOT EXISTS idx_flow_tags_tag_id ON flow_tags (tag_id);

-- tag_name is left in place (now nullable, no longer authoritative) for
-- backward compatibility with any old reports/tooling that reads it
-- directly; the application always reads/writes the name via tags.tag_name.
ALTER TABLE flow_tags ALTER COLUMN tag_name DROP NOT NULL;
