-- Flow definitions, created/edited via the "Flow Definitions" admin page
-- (Controllers/FlowDefinitionController.cs) and read by the investigator
-- (InvestigateController) as an alternative/addition to the hardcoded
-- FlowMaps.cs entries. A flow found here takes priority over a same-named
-- hardcoded one, since a DB-defined flow is the one actively being authored.

CREATE TABLE flow_definitions (
    id            SERIAL PRIMARY KEY,
    flow_name     VARCHAR(200) NOT NULL UNIQUE,
    description   VARCHAR(500) NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- The vocabulary of tags for a flow, and which ones are terminal (a
-- transaction that reaches one of these is considered "finished").
--
-- NOTE: this table was later normalized (see Db/04_link_flow_tags_to_catalog.sql)
-- to reference the shared "tags" catalog via tag_id, so the same tag name
-- can be reused across multiple flows without duplicating rows. The
-- tag_name column below is superseded by tags.tag_name once that migration
-- runs; it's kept only for backward compatibility with old rows/tooling.
CREATE TABLE flow_tags (
    id                   SERIAL PRIMARY KEY,
    flow_definition_id   INT NOT NULL REFERENCES flow_definitions(id) ON DELETE CASCADE,
    tag_name             VARCHAR(200) NOT NULL,
    is_terminal          BOOLEAN NOT NULL DEFAULT false,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (flow_definition_id, tag_name)
);

-- The adjacency map: which tag(s) are allowed to follow which. A tag with
-- no outgoing rows here (and not marked terminal) is a "dead end" — exactly
-- the concept FlowValidator already uses for hardcoded flows.
CREATE TABLE flow_tag_transitions (
    id                   SERIAL PRIMARY KEY,
    flow_definition_id   INT NOT NULL REFERENCES flow_definitions(id) ON DELETE CASCADE,
    from_tag_id          INT NOT NULL REFERENCES flow_tags(id) ON DELETE CASCADE,
    to_tag_id            INT NOT NULL REFERENCES flow_tags(id) ON DELETE CASCADE,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (from_tag_id, to_tag_id)
);

CREATE INDEX idx_flow_tags_flow_definition ON flow_tags (flow_definition_id);
CREATE INDEX idx_flow_transitions_flow_definition ON flow_tag_transitions (flow_definition_id);
CREATE INDEX idx_flow_transitions_from_tag ON flow_tag_transitions (from_tag_id);
