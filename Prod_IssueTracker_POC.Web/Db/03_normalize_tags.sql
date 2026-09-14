-- Run after 01_flow_definitions_schema.sql and 02_add_flow_tag_positions.sql.
-- Splits "a tag" (a reusable name, e.g. "ValidationSuccess") from "a tag's
-- USE within one specific flow" (its terminal flag + canvas position there).
-- The same global tag can now be reused across many flows without retyping
-- its name each time.

-- 1. The reusable tag vocabulary.
CREATE TABLE tags (
    id            SERIAL PRIMARY KEY,
    tag_name      VARCHAR(200) NOT NULL UNIQUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- 2. Backfill from whatever tag names already exist in flow_tags.
INSERT INTO tags (tag_name)
SELECT DISTINCT tag_name FROM flow_tags
ON CONFLICT (tag_name) DO NOTHING;

-- 3. Point flow_tags at the global tag instead of storing the name directly.
ALTER TABLE flow_tags ADD COLUMN tag_id INT REFERENCES tags(id);
UPDATE flow_tags ft SET tag_id = t.id FROM tags t WHERE t.tag_name = ft.tag_name;
ALTER TABLE flow_tags ALTER COLUMN tag_id SET NOT NULL;

-- 4. tag_name is now redundant (sourced via tag_id -> tags.tag_name) — drop it,
--    and re-key the per-flow uniqueness constraint onto tag_id instead.
--    If this DROP CONSTRAINT errors because the auto-generated name differs
--    on your server, run \d flow_tags in psql to find the real constraint
--    name and substitute it here.
ALTER TABLE flow_tags DROP CONSTRAINT IF EXISTS flow_tags_flow_definition_id_tag_name_key;
ALTER TABLE flow_tags DROP COLUMN tag_name;
ALTER TABLE flow_tags ADD CONSTRAINT flow_tags_flow_definition_id_tag_id_key UNIQUE (flow_definition_id, tag_id);

CREATE INDEX idx_flow_tags_tag_id ON flow_tags (tag_id);
