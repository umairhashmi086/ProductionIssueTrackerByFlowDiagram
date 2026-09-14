-- Clean rebuild of the flow-definitions schema. Drops any existing tables
-- from earlier versions first, then creates everything fresh in one go —
-- use this INSTEAD of 01/02/03, not alongside them.

DROP TABLE IF EXISTS flow_tag_transitions;
DROP TABLE IF EXISTS flow_tags;
DROP TABLE IF EXISTS flow_definitions;
DROP TABLE IF EXISTS tags;

-- The reusable tag vocabulary — add a tag once, reuse it across any flow.
CREATE TABLE tags (
    id            SERIAL PRIMARY KEY,
    tag_name      VARCHAR(200) NOT NULL UNIQUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- One row per flow (e.g. "GetFeeTransaction").
CREATE TABLE flow_definitions (
    id            SERIAL PRIMARY KEY,
    flow_name     VARCHAR(200) NOT NULL UNIQUE,
    description   VARCHAR(500) NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Which global tags are placed on a given flow's canvas, and where —
-- this is a USAGE of a tag within one flow, not the tag itself.
CREATE TABLE flow_tags (
    id                   SERIAL PRIMARY KEY,
    flow_definition_id   INT NOT NULL REFERENCES flow_definitions(id) ON DELETE CASCADE,
    tag_id               INT NOT NULL REFERENCES tags(id),
    is_terminal          BOOLEAN NOT NULL DEFAULT false,
    pos_x                INT NOT NULL DEFAULT 40,
    pos_y                INT NOT NULL DEFAULT 40,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (flow_definition_id, tag_id)
);

-- The adjacency map: which tag-usage can transition to which, within one
-- flow. A tag with no outgoing rows here (and not marked terminal) is a
-- "dead end" — the concept FlowValidator uses to flag stuck transactions.
CREATE TABLE flow_tag_transitions (
    id                   SERIAL PRIMARY KEY,
    flow_definition_id   INT NOT NULL REFERENCES flow_definitions(id) ON DELETE CASCADE,
    from_tag_id          INT NOT NULL REFERENCES flow_tags(id) ON DELETE CASCADE,
    to_tag_id            INT NOT NULL REFERENCES flow_tags(id) ON DELETE CASCADE,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (from_tag_id, to_tag_id)
);

CREATE INDEX idx_flow_tags_flow_definition ON flow_tags (flow_definition_id);
CREATE INDEX idx_flow_tags_tag_id ON flow_tags (tag_id);
CREATE INDEX idx_flow_transitions_flow_definition ON flow_tag_transitions (flow_definition_id);
CREATE INDEX idx_flow_transitions_from_tag ON flow_tag_transitions (from_tag_id);
