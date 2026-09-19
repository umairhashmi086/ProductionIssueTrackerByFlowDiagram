using Prod_IssueTracker_POC.Web.Models;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// Analyzes logs from Loki (by Kong ID or Reference Number) to suggest
    /// flow diagrams. Extracts tag sequences, identifies transitions, and
    /// generates GraphNodeDto/GraphEdgeDto that can be directly imported
    /// into the visual flow builder or saved as a new flow definition.
    /// </summary>
    public class SuggestedFlowService
    {
        private readonly LokiInvestigateClient _loki;

        public SuggestedFlowService(LokiInvestigateClient loki)
        {
            _loki = loki;
        }

        /// <summary>
        /// Analyzes a single Kong ID's tag sequence and suggests a flow diagram.
        /// Returns nodes (tags) and edges (transitions observed in the sequence).
        /// If layoutNodes=true, applies simple grid layout; otherwise nodes have
        /// default positions (caller can use the visual builder to arrange them).
        /// </summary>
        public async Task<SuggestedFlowModel?> SuggestFromKongIdAsync(string kongId, string? flowName = null, bool layoutNodes = false)
        {
            var (referenceNumber, tags) = await _loki.GetByKongIdAsync(kongId, flowName);
            if (tags.Count == 0) return null;

            return BuildSuggestedFlow(tags, referenceNumber ?? "Unknown", flowName, layoutNodes);
        }

        /// <summary>
        /// Analyzes all attempts (Kong IDs) for a reference number and suggests
        /// a flow diagram based on the combined tag sequences. This helps discover
        /// flows from multiple request attempts sharing one logical operation.
        /// </summary>
        public async Task<SuggestedFlowModel?> SuggestFromReferenceAsync(string referenceNumber, string? flowName = null, bool layoutNodes = false)
        {
            var attemptsByKongId = await _loki.GetAttemptsByReferenceAsync(referenceNumber, flowName);
            if (attemptsByKongId.Count == 0) return null;

            var allTags = new List<TagDto>();
            foreach (var tags in attemptsByKongId.Values.OrderBy(t => t.FirstOrDefault()?.Timestamp))
                allTags.AddRange(tags);

            if (allTags.Count == 0) return null;

            return BuildSuggestedFlow(allTags, referenceNumber, flowName, layoutNodes);
        }

        private SuggestedFlowModel BuildSuggestedFlow(List<TagDto> tags, string sourceName, string? flowName, bool layoutNodes)
        {
            var uniqueTags = tags.Select(t => t.TagName).Distinct().ToList();
            var transitions = ExtractTransitions(tags);

            var nodes = uniqueTags.Select((tag, index) =>
            {
                var (x, y) = layoutNodes ? GetGridPosition(index) : (0, 0);
                return new GraphNodeDto
                {
                    ClientId = $"suggested-{Guid.NewGuid():N}",
                    TagName = tag,
                    IsTerminal = IsTerminalTag(tag, transitions),
                    X = x,
                    Y = y
                };
            }).ToList();

            var nodesByTag = nodes.ToDictionary(n => n.TagName);

            var edges = transitions
                .Distinct()
                .Where(t => nodesByTag.ContainsKey(t.From) && nodesByTag.ContainsKey(t.To))
                .Select(t => new GraphEdgeDto
                {
                    From = nodesByTag[t.From].ClientId,
                    To = nodesByTag[t.To].ClientId
                })
                .ToList();

            return new SuggestedFlowModel
            {
                SourceKongIdOrReference = sourceName,
                SuggestedFlowName = flowName ?? $"Auto-{DateTime.Now:MMddHHmmss}",
                TagCount = uniqueTags.Count,
                TransitionCount = transitions.Distinct().Count(),
                Nodes = nodes,
                Edges = edges,
                TagSequence = tags.Select(t => t.TagName).ToList()
            };
        }

        private List<(string From, string To)> ExtractTransitions(List<TagDto> tags)
        {
            var transitions = new List<(string From, string To)>();
            for (int i = 0; i < tags.Count - 1; i++)
            {
                var from = tags[i].TagName;
                var to = tags[i + 1].TagName;
                if (from != to) // skip self-loops
                    transitions.Add((from, to));
            }
            return transitions;
        }

        private bool IsTerminalTag(string tag, List<(string From, string To)> transitions)
        {
            return !transitions.Any(t => t.From == tag);
        }

        private (int X, int Y) GetGridPosition(int index)
        {
            int cols = 5;
            int row = index / cols;
            int col = index % cols;
            return (col * 150, row * 150);
        }
    }

    public class SuggestedFlowModel
    {
        public string SourceKongIdOrReference { get; set; } = "";
        public string SuggestedFlowName { get; set; } = "";
        public int TagCount { get; set; }
        public int TransitionCount { get; set; }
        public List<GraphNodeDto> Nodes { get; set; } = new();
        public List<GraphEdgeDto> Edges { get; set; } = new();
        public List<string> TagSequence { get; set; } = new();
    }
}
