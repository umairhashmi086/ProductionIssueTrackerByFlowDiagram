using System.Text.Json;

namespace Prod_IssueTracker_POC.FlowTagging
{
    public interface IFlowTagger
    {
        void Tag(string attemptId, string referenceNumber, string flowName, string tagName, object? metadata = null, DateTimeOffset? timestamp = null);
    }

    /// <summary>
    /// Call this at each checkpoint in a flow (validation succeeded, agent
    /// fetched, fee calculation failed, etc). Writes into IFlowTagStore, and
    /// also logs normally via ILogger so nothing about your existing logging
    /// changes — this is purely additive.
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

        public void Tag(string attemptId, string referenceNumber, string flowName, string tagName, object? metadata = null, DateTimeOffset? timestamp = null)
        {
            var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;
            var ts = timestamp ?? DateTimeOffset.UtcNow;

            _store.Add(new FlowTagEntry(attemptId, referenceNumber, flowName, tagName, metadataJson, ts));

            _logger.LogInformation(
                "FlowTag {FlowName} {TagName} [{ReferenceNumber}] [{AttemptId}] {Metadata}",
                flowName, tagName, referenceNumber, attemptId, metadataJson);
        }
    }
}
