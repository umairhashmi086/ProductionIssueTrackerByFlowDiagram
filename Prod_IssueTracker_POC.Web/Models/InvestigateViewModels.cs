namespace Prod_IssueTracker_POC.Web.Models
{
    // These are the investigator's own domain models now — populated directly
    // from Loki query results + local FlowValidator output, not deserialized
    // from any producer API's response shape.

    public class TagDto
    {
        public string TagName { get; set; } = "";
        public DateTimeOffset Timestamp { get; set; }
        public string? MetadataJson { get; set; }
        public int RetryCount { get; set; } = 1;

        // True when THIS specific occurrence was reached via a transition
        // that wasn't expected/tolerated — lets the raw list and diagram
        // show exactly which step(s) went wrong, even if the flow recovers
        // to valid tags afterward.
        public bool IsUnexpected { get; set; }
    }

    public class AttemptDto
    {
        public int AttemptNumber { get; set; }
        public string AttemptId { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public bool IsHealthy { get; set; }
        public string? LastValidTag { get; set; }
        public bool DeadEnd { get; set; }
        public bool HasDeviations { get; set; }
        public string? CrossAttemptLink { get; set; }
        public List<TagDto> TagSequence { get; set; } = new();
        public int DeviationCount => TagSequence.Count(t => t.IsUnexpected);
    }

    // --- Server-computed diagram layout (built in the Controller, bound to the View) ---

    public class FlowNodeViewModel
    {
        public string Id { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsReached { get; set; }
        public bool IsDeadEnd { get; set; }
        public bool IsUnexpected { get; set; }
        // >1 shows a "retried:N" badge on the node; 1 (default) shows nothing.
        public int RetryCount { get; set; } = 1;
    }

    public class FlowEdgeViewModel
    {
        public string FromId { get; set; } = "";
        public string ToId { get; set; } = "";
        public bool IsTraversed { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool SameRow { get; set; }
    }

    public class DiagramViewModel
    {
        public List<FlowNodeViewModel> Nodes { get; set; } = new();
        public List<FlowEdgeViewModel> Edges { get; set; } = new();
        public double Width { get; set; }
        public double Height { get; set; }

        // Full SVG markup, built server-side in FlowDiagramLayout.BuildSvg and
        // rendered in the View via @Html.Raw(...). Built as a string rather
        // than looped over in Razor markup because SVG's <text> element name
        // collides with Razor's own reserved <text> transition tag — Razor
        // refuses to let a literal <text> tag carry attributes (RZ1023).
        public string SvgMarkup { get; set; } = "";
    }

    // --- The full page ViewModel the Controller builds and the View binds to ---

    public class ReferenceViewModel
    {
        public string ReferenceNumber { get; set; } = "";
        public string FlowName { get; set; } = "";
        public List<AttemptDto> Attempts { get; set; } = new();
        public int ActiveAttemptIndex { get; set; }
        public AttemptDto ActiveAttempt => Attempts[ActiveAttemptIndex];
        public DiagramViewModel Diagram { get; set; } = new();
    }
}
