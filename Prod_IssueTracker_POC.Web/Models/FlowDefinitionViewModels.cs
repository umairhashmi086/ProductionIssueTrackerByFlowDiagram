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

    public class TagRow
    {
        public int Id { get; set; }
        public string TagName { get; set; } = "";
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
        // The full reusable tag library, for the "add existing tag" dropdown
        // — separate from Tags above, which is just what's on THIS flow.
        public List<TagRow> AvailableTags { get; set; } = new();
    }

    public class CreateFlowDefinitionViewModel
    {
        public string FlowName { get; set; } = "";
        public string? Description { get; set; }
    }

    // --- Request shape for the visual builder's single "Save" call ---

    public class GraphNodeDto
    {
        public string TagName { get; set; } = "";
        public bool IsTerminal { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class GraphEdgeDto
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }

    public class SaveGraphRequest
    {
        public int FlowDefinitionId { get; set; }
        public List<GraphNodeDto> Nodes { get; set; } = new();
        public List<GraphEdgeDto> Edges { get; set; } = new();
    }
}
