using System.Text.Json;

namespace Prod_IssueTracker_POC.FlowTagging
{
    public interface IFlowTagger
    {
        void Tag(string kongId, string referenceNumber, string flowName, string tagName, object? metadata = null, DateTimeOffset? timestamp = null, string? serviceName = null);
    }

    /// <summary>
    /// Call this at each checkpoint in a flow (validation succeeded, agent
    /// fetched, fee calculation failed, etc). Writes into IFlowTagStore, and
    /// also logs normally via ILogger so nothing about your existing logging
    /// changes — this is purely additive. ServiceName identifies which
    /// microservice emitted this tag.
    /// </summary>
    public class FlowTagger : IFlowTagger
    {
        private readonly IFlowTagStore _store;
        private readonly ILogger<FlowTagger> _logger;

        public FlowTagger(IFlowTagStore store, ILogger<FlowTagger> logger)
        {
            _store = store;
            _logger = logger;
        }

        public void Tag(string kongId, string referenceNumber, string flowName, string tagName, object? metadata = null, DateTimeOffset? timestamp = null, string? serviceName = null)
        {
            var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;
            var ts = timestamp ?? DateTimeOffset.UtcNow;

            _store.Add(new FlowTagEntry(kongId, referenceNumber, flowName, tagName, metadataJson, ts, serviceName));

            _logger.LogInformation(
                "FlowTag {FlowName} {TagName} [{ReferenceNumber}] [{KongId}] [{ServiceName}] {Metadata}",
                flowName, tagName, referenceNumber, kongId, serviceName ?? "unknown", metadataJson);
        }
    }
}
