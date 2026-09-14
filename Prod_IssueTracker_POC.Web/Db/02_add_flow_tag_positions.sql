-- Run this after 01_flow_definitions_schema.sql. Adds canvas coordinates so
-- the visual flow builder can save and restore where each tag box was placed.
ALTER TABLE flow_tags ADD COLUMN pos_x INT NOT NULL DEFAULT 40;
ALTER TABLE flow_tags ADD COLUMN pos_y INT NOT NULL DEFAULT 40;
