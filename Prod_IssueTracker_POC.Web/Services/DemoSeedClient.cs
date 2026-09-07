using System.Net.Http.Json;
using System.Text.Json;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// This is the ONE remaining call from Web to the API project — and it's
    /// a write-trigger (POST), not a read. Seeding sample data is fundamentally
    /// a logging action ("go log this scenario"), so it correctly belongs
    /// behind the producer's own API, same as any real write would. Reads
    /// (LokiInvestigateClient) never go through this or any other producer API.
    /// </summary>
    public class DemoSeedClient
    {
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public DemoSeedClient(HttpClient http) => _http = http;

        public async Task<string?> SeedMoneyGramScenarioAsync()
        {
            var response = await _http.PostAsync("/api/demo/seed-moneygram-scenario", null);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(JsonOptions);
            return result != null && result.TryGetValue("referenceNumber", out var refNum) ? refNum.ToString() : null;
        }
    }
}
