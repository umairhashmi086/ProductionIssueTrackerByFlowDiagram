using Npgsql;
using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// CRUD against the "tags" catalog table (see Db/03_add_tags_catalog.sql).
    /// This is a flat, reusable list of tag names — independent of any one
    /// flow — managed via the "Manage Tags" admin page and consumed by the
    /// flow builder (Views/FlowDefinition/Edit.cshtml) so that adding a tag
    /// to a flow means picking an existing name instead of retyping it.
    /// </summary>
    public class TagRepository
    {
        private readonly string _connectionString;

        public TagRepository(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("FlowDefinitionsDb")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:FlowDefinitionsDb in appsettings.json");
        }

        public async Task<List<TagRow>> GetAllAsync()
        {
            const string sql = "SELECT id, tag_name, created_at FROM tags ORDER BY tag_name;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var results = new List<TagRow>();
            while (await reader.ReadAsync())
            {
                results.Add(new TagRow
                {
                    Id = reader.GetInt32(0),
                    TagName = reader.GetString(1),
                    CreatedAt = reader.GetFieldValue<DateTime>(2)
                });
            }
            return results;
        }

        public async Task<List<string>> GetAllNamesAsync()
        {
            const string sql = "SELECT tag_name FROM tags ORDER BY tag_name;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names;
        }

        /// <summary>
        /// Adds a tag to the catalog if it doesn't already exist (by name,
        /// case-sensitive match on the exact text). Returns the tag whether
        /// it was just created or already existed, so callers can safely
        /// call this every time a tag is used without worrying about
        /// duplicates.
        /// </summary>
        public async Task<TagRow> GetOrCreateAsync(string tagName)
        {
            const string upsertSql = @"
                INSERT INTO tags (tag_name) VALUES (@tagName)
                ON CONFLICT (tag_name) DO UPDATE SET tag_name = EXCLUDED.tag_name
                RETURNING id, tag_name, created_at;";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(upsertSql, conn);
            cmd.Parameters.AddWithValue("tagName", tagName);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return new TagRow
            {
                Id = reader.GetInt32(0),
                TagName = reader.GetString(1),
                CreatedAt = reader.GetFieldValue<DateTime>(2)
            };
        }

        public async Task DeleteAsync(int id)
        {
            const string sql = "DELETE FROM tags WHERE id = @id;";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
