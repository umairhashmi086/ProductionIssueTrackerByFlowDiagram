using System.Text.Json;
using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// Queries Loki's HTTP API directly. This is the key piece of the
    /// decoupling: the investigator no longer asks any producer service for
    /// its data — it reads straight from the shared log store. Any service
    /// that pushes tags into Loki under the same JSON shape (AttemptId,
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

        public async Task<Dictionary<string, List<TagDto>>> GetAttemptsByReferenceAsync(string referenceNumber)
        {
            var query = $"{{app=\"{_appLabel}\"}} | json | ReferenceNumber=\"{referenceNumber}\"";
            var entries = await QueryAsync(query);

            return entries
                .GroupBy(e => e.AttemptId)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Timestamp)
                    .Select(e => new TagDto { TagName = e.TagName, Timestamp = e.Timestamp, MetadataJson = e.MetadataJson })
                    .ToList());
        }

        /// <summary>
        /// Returns the FlowName for a reference number by looking at the first
        /// tag found (all attempts under one reference share a flow). Null if
        /// nothing was found at all.
        /// </summary>
        public async Task<string?> GetFlowNameAsync(string referenceNumber)
        {
            var query = $"{{app=\"{_appLabel}\"}} | json | ReferenceNumber=\"{referenceNumber}\"";
            var entries = await QueryAsync(query);
            return entries.OrderBy(e => e.Timestamp).FirstOrDefault()?.FlowName;
        }

        private record RawEntry(string AttemptId, string ReferenceNumber, string FlowName, string TagName, string? MetadataJson, DateTimeOffset Timestamp);

        private async Task<List<RawEntry>> QueryAsync(string logQlQuery)
        {
            var results = new List<RawEntry>();

            var start = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds() * 1_000_000;
            var end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

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
                        AttemptId: root.GetProperty("AttemptId").GetString()!,
                        ReferenceNumber: root.GetProperty("ReferenceNumber").GetString()!,
                        FlowName: root.GetProperty("FlowName").GetString()!,
                        TagName: root.GetProperty("TagName").GetString()!,
                        MetadataJson: root.TryGetProperty("Metadata", out var m) ? m.GetString() : null,
                        Timestamp: timestamp));
                }
            }

            return results;
        }
    }
}
