using System.Text.Json;
using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// Queries Loki's HTTP API directly. This is the key piece of the
    /// decoupling: the investigator no longer asks any producer service for
    /// its data — it reads straight from the shared log store. Any service
    /// that pushes tags into Loki under the same JSON shape (KongId,
    /// ReferenceNumber, FlowName, TagName, Metadata) is investigable here,
    /// with zero code changes to this client and zero dependency on that
    /// service's own API.
    /// </summary>
    public class LokiInvestigateClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _appLabel;

        public LokiInvestigateClient(HttpClient http, IConfiguration config)
        {
            _http = http;
            _baseUrl = config["Loki:BaseUrl"] ?? "http://localhost:3100";
            _appLabel = config["Loki:AppLabel"] ?? "poc-feeservice";
        }

        /// <summary>
        /// All attempts (grouped by KongId) sharing a reference number.
        /// Passing flowName narrows the Loki query itself (not just a UI
        /// filter) — useful if the same reference-number scheme could ever
        /// collide across different flow types sharing one Loki instance.
        /// </summary>
        public async Task<Dictionary<string, List<TagDto>>> GetAttemptsByReferenceAsync(string referenceNumber, string? flowName = null)
        {
            var query = BuildQuery($"ReferenceNumber=\"{referenceNumber}\"", flowName);
            var entries = await QueryAsync(query);

            return entries
                .GroupBy(e => e.KongId)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Timestamp)
                    .Select(e => new TagDto { TagName = e.TagName, Timestamp = e.Timestamp, MetadataJson = e.MetadataJson, ServiceName = e.ServiceName })
                    .ToList());
        }

        /// <summary>
        /// A single attempt's full tag sequence, looked up directly by its
        /// Kong ID rather than by reference number — useful when you already
        /// have a specific request's correlation ID (e.g. from a log line or
        /// an error report) and want to jump straight to it.
        /// </summary>
        public async Task<(string? ReferenceNumber, List<TagDto> Tags)> GetByKongIdAsync(string kongId, string? flowName = null)
        {
            var query = BuildQuery($"KongId=\"{kongId}\"", flowName);
            var entries = await QueryAsync(query);
            var ordered = entries.OrderBy(e => e.Timestamp).ToList();

            var referenceNumber = ordered.FirstOrDefault()?.ReferenceNumber;
            var tags = ordered.Select(e => new TagDto { TagName = e.TagName, Timestamp = e.Timestamp, MetadataJson = e.MetadataJson, ServiceName = e.ServiceName }).ToList();
            return (referenceNumber, tags);
        }

        /// <summary>
        /// Fetches all logs within a date range, grouped by Kong ID.
        /// Optionally filters by flow name if provided.
        /// Used for the logs browser page to display all transaction attempts.
        /// </summary>
        public async Task<Dictionary<string, LogGroupData>> GetAllLogsGroupedByKongIdAsync(string? flowName = null, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
        {
            var query = BuildQuery("", flowName);
            var start = startDate ?? DateTimeOffset.UtcNow.AddDays(-7);
            var end = endDate ?? DateTimeOffset.UtcNow;
            var entries = await QueryAsync(query, start, end);

            var grouped = entries
                .GroupBy(e => e.KongId)
                .ToDictionary(
                    g => g.Key,
                    g => new LogGroupData
                    {
                        KongId = g.Key,
                        ReferenceNumber = g.First().ReferenceNumber,
                        FlowName = g.First().FlowName,
                        Tags = g.OrderBy(e => e.Timestamp)
                            .Select(e => new TagDto { TagName = e.TagName, Timestamp = e.Timestamp, MetadataJson = e.MetadataJson, ServiceName = e.ServiceName })
                            .ToList()
                    }
                );

            return grouped;
        }

        /// <summary>
        /// Returns the FlowName for a reference number by looking at the first
        /// tag found (all attempts under one reference share a flow). Null if
        /// nothing was found at all.
        /// </summary>
        public async Task<string?> GetFlowNameAsync(string referenceNumber)
        {
            var query = BuildQuery($"ReferenceNumber=\"{referenceNumber}\"", null);
            var entries = await QueryAsync(query);
            return entries.OrderBy(e => e.Timestamp).FirstOrDefault()?.FlowName;
        }

        private string BuildQuery(string filterClause, string? flowName)
        {
            var flowClause = string.IsNullOrWhiteSpace(flowName) ? "" : $" | FlowName=\"{flowName}\"";
            var filter = string.IsNullOrWhiteSpace(filterClause) ? "" : $" | {filterClause}";
            return $"{{app=\"{_appLabel}\"}} | json{filter}{flowClause}";
        }

        private record RawEntry(string KongId, string ReferenceNumber, string FlowName, string TagName, string? MetadataJson, DateTimeOffset Timestamp, string? ServiceName = null);

        public class LogGroupData
        {
            public string KongId { get; set; } = "";
            public string ReferenceNumber { get; set; } = "";
            public string FlowName { get; set; } = "";
            public List<TagDto> Tags { get; set; } = new();
        }

        private async Task<List<RawEntry>> QueryAsync(string logQlQuery, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
        {
            var results = new List<RawEntry>();

            var start = (startDate ?? DateTimeOffset.UtcNow.AddHours(-24)).ToUnixTimeMilliseconds() * 1_000_000;
            var end = (endDate ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() * 1_000_000;

            var url = $"{_baseUrl}/loki/api/v1/query_range" +
                      $"?query={Uri.EscapeDataString(logQlQuery)}" +
                      $"&start={start}&end={end}&limit=1000";

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return results; // caller sees "no attempts found" — logged upstream if needed

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var streams = doc.RootElement.GetProperty("data").GetProperty("result");
            foreach (var stream_ in streams.EnumerateArray())
            {
                foreach (var value in stream_.GetProperty("values").EnumerateArray())
                {
                    var tsNanos = long.Parse(value[0].GetString()!);
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsNanos / 1_000_000);
                    var logLine = value[1].GetString()!;

                    using var lineDoc = JsonDocument.Parse(logLine);
                    var root = lineDoc.RootElement;

                    results.Add(new RawEntry(
                        KongId: root.GetProperty("KongId").GetString()!,
                        ReferenceNumber: root.GetProperty("ReferenceNumber").GetString()!,
                        FlowName: root.GetProperty("FlowName").GetString()!,
                        TagName: root.GetProperty("TagName").GetString()!,
                        MetadataJson: root.TryGetProperty("Metadata", out var m) ? m.GetString() : null,
                        Timestamp: timestamp,
                        ServiceName: root.TryGetProperty("ServiceName", out var sn) ? sn.GetString() : null));
                }
            }

            return results;
        }
    }
}
