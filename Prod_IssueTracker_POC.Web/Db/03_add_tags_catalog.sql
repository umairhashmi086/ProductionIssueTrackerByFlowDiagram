-- Run this after 02_add_flow_tag_positions.sql. Adds a reusable catalog of
-- tag names (managed via the "Manage Tags" admin page) so that building a
-- flow no longer requires re-typing a brand-new tag name every time in the
-- flow builder's "+ Add Tag" prompt — instead, the builder lets you pick
-- from tags already defined here, and any new one you type there is saved
-- back into this catalog automatically for reuse next time.
CREATE TABLE tags (
    id           SERIAL PRIMARY KEY,
    tag_name     VARCHAR(200) NOT NULL UNIQUE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
