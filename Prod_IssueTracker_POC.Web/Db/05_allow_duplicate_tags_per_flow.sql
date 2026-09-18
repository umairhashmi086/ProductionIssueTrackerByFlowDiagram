-- Run this after 04_link_flow_tags_to_catalog.sql. Lifts the "one tag per
-- flow" restriction in the visual flow builder — a single tag (e.g.
-- "Retry" or "Timeout") can now be dropped onto the canvas more than once
-- as its own independent node (its own position and its own in/out
-- connections), instead of being blocked as a duplicate.
--
-- flow_tags.id already is (and always was) the true node identity that
-- flow_tag_transitions.from_tag_id/to_tag_id point to; the only thing
-- preventing two nodes from sharing a tag_id was this uniqueness
-- constraint, so it can simply be dropped. flow_tags stays scoped to a
-- single flow_definition_id via its existing FK/index — nothing else
-- about the schema needs to change.
--
-- Safe to run once; guarded so re-running is a no-op.

ALTER TABLE flow_tags
    DROP CONSTRAINT IF EXISTS flow_tags_flow_definition_id_tag_id_key;
