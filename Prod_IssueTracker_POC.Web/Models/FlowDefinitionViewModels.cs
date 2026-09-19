namespace Prod_IssueTracker_POC.Web.Models
{
    public class FlowDefinitionSummary
    {
        public int Id { get; set; }
        public string FlowName { get; set; } = "";
        public string? Description { get; set; }
        public int TagCount { get; set; }
        public int TransitionCount { get; set; }
    }

    public class FlowTagRow
    {
        public int Id { get; set; }
        public string TagName { get; set; } = "";
        public bool IsTerminal { get; set; }
        public int PosX { get; set; }
        public int PosY { get; set; }
    }

    public class FlowTransitionRow
    {
        public int Id { get; set; }
        // FromTagId/ToTagId are flow_tags.id — the actual node identity on
        // the canvas (a tag can now have more than one node/id in the same
        // flow), used to rebuild the builder's edges exactly. FromTagName/
        // ToTagName are kept for GetFlowDefinitionAsync's tag-name-keyed
        // adjacency map, which FlowValidator consumes.
        public int FromTagId { get; set; }
        public int ToTagId { get; set; }
        public string FromTagName { get; set; } = "";
        public string ToTagName { get; set; } = "";
    }

    /// <summary>Everything needed to render/edit one flow's tags and transitions.</summary>
    public class FlowDefinitionDetailViewModel
    {
        public int Id { get; set; }
        public string FlowName { get; set; } = "";
        public string? Description { get; set; }
        public List<FlowTagRow> Tags { get; set; } = new();
        public List<FlowTransitionRow> Transitions { get; set; } = new();
    }

    public class CreateFlowDefinitionViewModel
    {
        public string FlowName { get; set; } = "";
        public string? Description { get; set; }
    }

    // --- Request shape for the visual builder's single "Save" call ---

    public class GraphNodeDto
    {
        // Node identity independent of TagName so the same tag can appear
        // as more than one node on the canvas. For a node loaded from the
        // DB this is its flow_tags.id (as a string); for a node the user
        // just added in the browser it's a client-generated placeholder
        // (e.g. "new-3") that SaveGraphAsync recognizes as "not yet in the
        // DB" and inserts as a brand-new row.
        public string ClientId { get; set; } = "";
        public string TagName { get; set; } = "";
        public bool IsTerminal { get; set; }
        // double (not int): the canvas sends pixel coordinates that can be
        // fractional (getBoundingClientRect() sub-pixel values). Using int
        // here would throw a JsonException on any non-whole number and
        // silently null out the entire SaveGraphRequest during model
        // binding — see FlowDefinitionController.SaveGraph.
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class GraphEdgeDto
    {
        // References GraphNodeDto.ClientId, not a tag name — an edge
        // always connects two specific node instances, which matters once
        // a tag can have multiple nodes in the same flow.
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }

    public class SaveGraphRequest
    {
        public int FlowDefinitionId { get; set; }
        public List<GraphNodeDto> Nodes { get; set; } = new();
        public List<GraphEdgeDto> Edges { get; set; } = new();
    }

    public class SuggestFlowViewModel
    {
        public string SourceInput { get; set; } = "";
        public string? FlowName { get; set; }
        public SuggestedFlowModel? Suggestion { get; set; }
    }
}
