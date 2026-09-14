using Npgsql;
using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// CRUD against the flow_definitions / flow_tags / flow_tag_transitions
    /// tables (see Db/*.sql), plus the reusable "tags" table. Each add/remove
    /// action is a simple, separate form submission — deliberately plain
    /// server-rendered CRUD rather than a client-side canvas, so it behaves
    /// predictably without relying on hand-written browser interaction code.
    /// </summary>
    public class FlowDefinitionRepository
    {
        private readonly string _connectionString;

        public FlowDefinitionRepository(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("FlowDefinitionsDb")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:FlowDefinitionsDb in appsettings.json");
        }

        public async Task<List<string>> GetAllFlowNamesAsync()
        {
            const string sql = "SELECT flow_name FROM flow_definitions ORDER BY flow_name;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names;
        }

        public async Task<List<FlowDefinitionSummary>> GetAllSummariesAsync()
        {
            const string sql = @"
                SELECT fd.id, fd.flow_name, fd.description,
                       COUNT(DISTINCT ft.id) AS tag_count,
                       COUNT(DISTINCT tr.id) AS transition_count
                FROM flow_definitions fd
                LEFT JOIN flow_tags ft ON ft.flow_definition_id = fd.id
                LEFT JOIN flow_tag_transitions tr ON tr.flow_definition_id = fd.id
                GROUP BY fd.id, fd.flow_name, fd.description
                ORDER BY fd.flow_name;";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var results = new List<FlowDefinitionSummary>();
            while (await reader.ReadAsync())
            {
                results.Add(new FlowDefinitionSummary
                {
                    Id = reader.GetInt32(0),
                    FlowName = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    TagCount = Convert.ToInt32(reader.GetInt64(3)),
                    TransitionCount = Convert.ToInt32(reader.GetInt64(4))
                });
            }
            return results;
        }

        public async Task<List<TagRow>> GetAllGlobalTagsAsync()
        {
            const string sql = "SELECT id, tag_name FROM tags ORDER BY tag_name;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var tags = new List<TagRow>();
            while (await reader.ReadAsync())
                tags.Add(new TagRow { Id = reader.GetInt32(0), TagName = reader.GetString(1) });
            return tags;
        }

        /// <summary>Creates a new reusable tag, or returns the existing one's id if the name is already taken.</summary>
        public async Task<TagRow> CreateGlobalTagAsync(string tagName)
        {
            const string sql = @"
                INSERT INTO tags (tag_name) VALUES (@tagName)
                ON CONFLICT (tag_name) DO UPDATE SET tag_name = EXCLUDED.tag_name
                RETURNING id, tag_name;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("tagName", tagName);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return new TagRow { Id = reader.GetInt32(0), TagName = reader.GetString(1) };
        }

        /// <summary>Deletes a global tag. Throws PostgresException (foreign key violation) if it's still used by any flow — caller should catch and show a friendly message.</summary>
        public async Task DeleteGlobalTagAsync(int tagId)
        {
            const string sql = "DELETE FROM tags WHERE id = @id;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", tagId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> CreateFlowAsync(string flowName, string? description)
        {
            const string sql = "INSERT INTO flow_definitions (flow_name, description) VALUES (@flowName, @description) RETURNING id;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("flowName", flowName);
            cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<FlowDefinitionDetailViewModel?> GetDetailAsync(string flowName)
        {
            const string flowSql = "SELECT id, flow_name, description FROM flow_definitions WHERE flow_name = @flowName;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            int flowId;
            var model = new FlowDefinitionDetailViewModel();

            await using (var cmd = new NpgsqlCommand(flowSql, conn))
            {
                cmd.Parameters.AddWithValue("flowName", flowName);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;
                flowId = reader.GetInt32(0);
                model.Id = flowId;
                model.FlowName = reader.GetString(1);
                model.Description = reader.IsDBNull(2) ? null : reader.GetString(2);
            }

            const string tagsSql = @"
                SELECT ft.id, t.tag_name, ft.is_terminal, ft.pos_x, ft.pos_y
                FROM flow_tags ft
                JOIN tags t ON t.id = ft.tag_id
                WHERE ft.flow_definition_id = @flowId
                ORDER BY ft.id;";
            await using (var cmd = new NpgsqlCommand(tagsSql, conn))
            {
                cmd.Parameters.AddWithValue("flowId", flowId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    model.Tags.Add(new FlowTagRow
                    {
                        Id = reader.GetInt32(0),
                        TagName = reader.GetString(1),
                        IsTerminal = reader.GetBoolean(2),
                        PosX = reader.GetInt32(3),
                        PosY = reader.GetInt32(4)
                    });
                }
            }

            const string transitionsSql = @"
                SELECT tr.id, t_from.tag_name, t_to.tag_name
                FROM flow_tag_transitions tr
                JOIN flow_tags ft_from ON ft_from.id = tr.from_tag_id
                JOIN tags t_from ON t_from.id = ft_from.tag_id
                JOIN flow_tags ft_to ON ft_to.id = tr.to_tag_id
                JOIN tags t_to ON t_to.id = ft_to.tag_id
                WHERE tr.flow_definition_id = @flowId
                ORDER BY tr.id;";
            await using (var cmd = new NpgsqlCommand(transitionsSql, conn))
            {
                cmd.Parameters.AddWithValue("flowId", flowId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    model.Transitions.Add(new FlowTransitionRow
                    {
                        Id = reader.GetInt32(0),
                        FromTagName = reader.GetString(1),
                        ToTagName = reader.GetString(2)
                    });
                }
            }

            return model;
        }

        /// <summary>Places an existing global tag onto this flow's tag list (or updates its terminal flag if already there).</summary>
        public async Task AddExistingTagToFlowAsync(int flowDefinitionId, int globalTagId, bool isTerminal)
        {
            const string sql = @"
                INSERT INTO flow_tags (flow_definition_id, tag_id, is_terminal, pos_x, pos_y)
                VALUES (@flowId, @tagId, @isTerminal, 40, 40)
                ON CONFLICT (flow_definition_id, tag_id)
                DO UPDATE SET is_terminal = EXCLUDED.is_terminal;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("flowId", flowDefinitionId);
            cmd.Parameters.AddWithValue("tagId", globalTagId);
            cmd.Parameters.AddWithValue("isTerminal", isTerminal);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Removes a tag from this flow's list (cascades to any transitions using it). Does NOT delete the global tag — it stays available for other flows.</summary>
        public async Task RemoveTagFromFlowAsync(int flowTagId)
        {
            const string sql = "DELETE FROM flow_tags WHERE id = @id;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", flowTagId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Defines that one tag-on-this-flow can transition to another tag-on-this-flow.</summary>
        public async Task AddTransitionAsync(int flowDefinitionId, int fromFlowTagId, int toFlowTagId)
        {
            const string sql = @"
                INSERT INTO flow_tag_transitions (flow_definition_id, from_tag_id, to_tag_id)
                VALUES (@flowId, @fromId, @toId)
                ON CONFLICT (from_tag_id, to_tag_id) DO NOTHING;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("flowId", flowDefinitionId);
            cmd.Parameters.AddWithValue("fromId", fromFlowTagId);
            cmd.Parameters.AddWithValue("toId", toFlowTagId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveTransitionAsync(int transitionId)
        {
            const string sql = "DELETE FROM flow_tag_transitions WHERE id = @id;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", transitionId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Replaces a flow's entire tag set and transition set in one
        /// transaction — this is what the visual builder's Save button
        /// calls. Global tags are upserted by name (reused if they already
        /// exist in the library), then placed onto this flow with the
        /// submitted position/terminal flag; any flow-tag NOT in the
        /// submitted set is removed from this flow (cascades to its
        /// transitions) — the global tag itself is never deleted here.
        /// </summary>
        public async Task SaveGraphAsync(int flowDefinitionId, List<GraphNodeDto> nodes, List<GraphEdgeDto> edges)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var flowTagIdByName = new Dictionary<string, int>();

            const string upsertGlobalTagSql = @"
                INSERT INTO tags (tag_name) VALUES (@tagName)
                ON CONFLICT (tag_name) DO UPDATE SET tag_name = EXCLUDED.tag_name
                RETURNING id;";

            const string upsertFlowTagSql = @"
                INSERT INTO flow_tags (flow_definition_id, tag_id, is_terminal, pos_x, pos_y)
                VALUES (@flowId, @tagId, @isTerminal, @x, @y)
                ON CONFLICT (flow_definition_id, tag_id)
                DO UPDATE SET is_terminal = EXCLUDED.is_terminal, pos_x = EXCLUDED.pos_x, pos_y = EXCLUDED.pos_y
                RETURNING id;";

            foreach (var node in nodes)
            {
                int globalTagId;
                await using (var cmd = new NpgsqlCommand(upsertGlobalTagSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("tagName", node.TagName);
                    globalTagId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                await using var flowTagCmd = new NpgsqlCommand(upsertFlowTagSql, conn, tx);
                flowTagCmd.Parameters.AddWithValue("flowId", flowDefinitionId);
                flowTagCmd.Parameters.AddWithValue("tagId", globalTagId);
                flowTagCmd.Parameters.AddWithValue("isTerminal", node.IsTerminal);
                flowTagCmd.Parameters.AddWithValue("x", node.X);
                flowTagCmd.Parameters.AddWithValue("y", node.Y);
                var flowTagId = await flowTagCmd.ExecuteScalarAsync();
                flowTagIdByName[node.TagName] = Convert.ToInt32(flowTagId);
            }

            const string deleteRemovedTagsSql = @"
                DELETE FROM flow_tags
                WHERE flow_definition_id = @flowId
                  AND NOT (id = ANY(@keepIds));";
            await using (var cmd = new NpgsqlCommand(deleteRemovedTagsSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("flowId", flowDefinitionId);
                cmd.Parameters.AddWithValue("keepIds", flowTagIdByName.Values.ToArray());
                await cmd.ExecuteNonQueryAsync();
            }

            const string deleteAllTransitionsSql = "DELETE FROM flow_tag_transitions WHERE flow_definition_id = @flowId;";
            await using (var cmd = new NpgsqlCommand(deleteAllTransitionsSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("flowId", flowDefinitionId);
                await cmd.ExecuteNonQueryAsync();
            }

            const string insertTransitionSql = @"
                INSERT INTO flow_tag_transitions (flow_definition_id, from_tag_id, to_tag_id)
                VALUES (@flowId, @fromId, @toId)
                ON CONFLICT (from_tag_id, to_tag_id) DO NOTHING;";
            foreach (var edge in edges)
            {
                if (!flowTagIdByName.TryGetValue(edge.From, out var fromId)) continue;
                if (!flowTagIdByName.TryGetValue(edge.To, out var toId)) continue;
                if (fromId == toId) continue;

                await using var cmd = new NpgsqlCommand(insertTransitionSql, conn, tx);
                cmd.Parameters.AddWithValue("flowId", flowDefinitionId);
                cmd.Parameters.AddWithValue("fromId", fromId);
                cmd.Parameters.AddWithValue("toId", toId);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }

        /// <summary>
        /// Fetches a flow's tags/transitions in the exact shape FlowValidator
        /// and FlowDiagramLayout already expect (same as the hardcoded
        /// FlowMaps.AllFlows entries) — so nothing downstream needs to know
        /// or care whether a flow came from Postgres or from C# code.
        /// Returns null if no DB-defined flow exists with this name.
        /// </summary>
        public async Task<(Dictionary<string, string[]> Map, HashSet<string> Terminal)?> GetFlowDefinitionAsync(string flowName)
        {
            var detail = await GetDetailAsync(flowName);
            if (detail == null) return null;

            var map = detail.Transitions
                .GroupBy(t => t.FromTagName)
                .ToDictionary(g => g.Key, g => g.Select(t => t.ToTagName).ToArray());

            var terminal = detail.Tags.Where(t => t.IsTerminal).Select(t => t.TagName).ToHashSet();

            return (map, terminal);
        }
    }
}
