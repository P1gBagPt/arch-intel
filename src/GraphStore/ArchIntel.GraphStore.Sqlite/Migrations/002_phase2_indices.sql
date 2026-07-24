-- ============================================================
-- 002_phase2_indices.sql (Phase 2)
-- ============================================================

-- Composite index to speed up "give me all edges within project X" for subgraph rendering
CREATE INDEX idx_nodes_project_type ON nodes(project_id, node_type);

-- Covering index for neighborhood queries (both directions considered via UNION query, see §5)
CREATE INDEX idx_edges_full ON edges(source_id, target_id, relationship_type, edge_id);
