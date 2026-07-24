-- ============================================================
-- 001_init.sql (Phase 1)
-- ============================================================

CREATE TABLE projects (
    project_id      TEXT PRIMARY KEY,
    repo_id         TEXT NOT NULL DEFAULT 'default',
    name            TEXT NOT NULL,
    path            TEXT NOT NULL,
    target_framework TEXT,
    project_type    TEXT,
    layer           TEXT,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    scan_version    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE nodes (
    node_id         TEXT PRIMARY KEY,
    repo_id         TEXT NOT NULL DEFAULT 'default',
    project_id      TEXT NOT NULL REFERENCES projects(project_id),
    node_type       TEXT NOT NULL,
    name            TEXT NOT NULL,
    full_name       TEXT NOT NULL,
    namespace       TEXT,
    file_path       TEXT,
    line_start      INTEGER,
    line_end        INTEGER,
    metadata_json    TEXT NOT NULL DEFAULT '{}',
    is_external      INTEGER NOT NULL DEFAULT 0,
    is_deleted       INTEGER NOT NULL DEFAULT 0,
    valid_from       TEXT NOT NULL,
    valid_to         TEXT,
    created_at       TEXT NOT NULL,
    updated_at       TEXT NOT NULL,
    scan_version     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE edges (
    edge_id          TEXT PRIMARY KEY,
    repo_id          TEXT NOT NULL DEFAULT 'default',
    source_id        TEXT NOT NULL REFERENCES nodes(node_id),
    target_id        TEXT NOT NULL REFERENCES nodes(node_id),
    relationship_type TEXT NOT NULL,
    metadata_json     TEXT NOT NULL DEFAULT '{}',
    is_deleted        INTEGER NOT NULL DEFAULT 0,
    valid_from        TEXT NOT NULL,
    valid_to          TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    scan_version      INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE scan_runs (
    scan_run_id      INTEGER PRIMARY KEY AUTOINCREMENT,
    repo_id          TEXT NOT NULL DEFAULT 'default',
    started_at       TEXT NOT NULL,
    completed_at     TEXT,
    scan_type        TEXT NOT NULL,
    triggered_by     TEXT,
    changed_files_json TEXT,
    status           TEXT NOT NULL DEFAULT 'Running',
    error_message    TEXT
);

-- Indices (Phase 1)
CREATE INDEX idx_nodes_project        ON nodes(project_id);
CREATE INDEX idx_nodes_type           ON nodes(node_type);
CREATE INDEX idx_nodes_name           ON nodes(name);
CREATE INDEX idx_nodes_full_name      ON nodes(full_name);
CREATE INDEX idx_nodes_file_path      ON nodes(file_path);
CREATE INDEX idx_edges_source         ON edges(source_id);
CREATE INDEX idx_edges_target         ON edges(target_id);
CREATE INDEX idx_edges_relationship   ON edges(relationship_type);
CREATE INDEX idx_edges_source_rel     ON edges(source_id, relationship_type);
CREATE INDEX idx_edges_target_rel     ON edges(target_id, relationship_type);
