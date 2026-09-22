using System.Text;
using System.Text.Json;

namespace Prod_IssueTracker_POC.FlowTagging
{
    /// <summary>
    /// Real "logs DB" backing for flow tags — pushes each tag straight to Loki's
    /// push API when emitted, and queries Loki's query API to read them back.
    /// No Promtail/file-tailing needed for this POC — FlowTagger.Tag(...) calls
    /// this store directly, same as the in-memory version, so nothing else in
    /// the app changes when you switch FlowTags:DataSource to "Loki".
    ///
    /// IMPORTANT: only "app" is a Loki label below — KongId/ReferenceNumber/
    /// TagName live inside the JSON log line body and are filtered with LogQL's
    /// | json, never as labels (see the cardinality warning in the notes doc).
    /// </summary>
    public class LokiFlowTagStore : IFlowTagStore
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _appLabel;
        private readonly ILogger<LokiFlowTagStore> _logger;

        public LokiFlowTagStore(HttpClient http, IConfiguration config, ILogger<LokiFlowTagStore> logger)
        {
            _http = http;
            _baseUrl = config["Loki:BaseUrl"] ?? "http://localhost:3100";
            _appLabel = config["Loki:AppLabel"] ?? "poc-feeservice";
            _logger = logger;
        }

        public void Add(FlowTagEntry entry)
        {
            // Fire-and-forget push so a Loki hiccup never breaks the actual
            // GetFee request — this is investigation tooling, not critical path.
            _ = PushAsync(entry);
        }

        private async Task PushAsync(FlowTagEntry entry)
        {
            try
            {
                var logLine = JsonSerializer.Serialize(new
                {
                    entry.KongId,
                    entry.ReferenceNumber,
                    entry.FlowName,
                    entry.TagName,
                    Metadata = entry.MetadataJson,
                    entry.ServiceName
                });

                var tsNanos = entry.Timestamp.ToUnixTimeMilliseconds() * 1_000_000;

                var payload = new
                {
                    streams = new[]
                    {
                        new
                        {
                            stream = new Dictionary<string, string> { ["app"] = _appLabel },
                            values = new[] { new[] { tsNanos.ToString(), logLine } }
                        }
                    }
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{_baseUrl}/loki/api/v1/push", content);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Loki push failed ({Status}): {Body}", response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not push flow tag to Loki — is Loki running at {BaseUrl}?", _baseUrl);
            }
        }

        public List<FlowTagEntry> GetByKongId(string kongId)
        {
            // IFlowTagStore's read methods are synchronous (matching the
            // in-memory implementation), so we block on the async HTTP call
            // here. Fine for a POC's low query volume; if you keep the Loki
            // store long-term, change IFlowTagStore's read methods to be
            // async and update InvestigateController to await them instead.
            var query = $"{{app=\"{_appLabel}\"}} | json | KongId=\"{kongId}\"";
            return QueryAsync(query).GetAwaiter().GetResult().OrderBy(e => e.Timestamp).ToList();
        }

        public Dictionary<string, List<FlowTagEntry>> GetByReferenceNumber(string referenceNumber)
        {
            var query = $"{{app=\"{_appLabel}\"}} | json | ReferenceNumber=\"{referenceNumber}\"";
            var entries = QueryAsync(query).GetAwaiter().GetResult();

            return entries
                .GroupBy(e => e.KongId)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Timestamp).ToList());
        }

        private async Task<List<FlowTagEntry>> QueryAsync(string logQlQuery)
        {
            var results = new List<FlowTagEntry>();

            try
            {
                var start = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds() * 1_000_000;
                var end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

                var url = $"{_baseUrl}/loki/api/v1/query_range" +
                          $"?query={Uri.EscapeDataString(logQlQuery)}" +
                          $"&start={start}&end={end}&limit=1000";

                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Loki query failed ({Status}): {Body}", response.StatusCode, body);
                    return results;
                }

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

                        results.Add(new FlowTagEntry(
                            KongId: root.GetProperty("KongId").GetString()!,
                            ReferenceNumber: root.GetProperty("ReferenceNumber").GetString()!,
                            FlowName: root.GetProperty("FlowName").GetString()!,
                            TagName: root.GetProperty("TagName").GetString()!,
                            MetadataJson: root.TryGetProperty("Metadata", out var m) ? m.GetString() : null,
                            Timestamp: timestamp,
                            ServiceName: root.TryGetProperty("ServiceName", out var sn) ? sn.GetString() : null));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not query Loki — is Loki running at {BaseUrl}?", _baseUrl);
            }

            return results;
        }
    }
}
