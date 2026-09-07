namespace Prod_IssueTracker_POC.Web.FlowTagging
{
    /// <summary>
    /// This is the investigator's own registry of "what a valid flow looks
    /// like", keyed by the FlowName string that shows up in the logged tags.
    /// It deliberately has no dependency on any producer project's code —
    /// any number of independent services can log tags to the same Loki
    /// instance under a FlowName, and as long as an entry exists here, this
    /// one tool can investigate them. Add a new entry here to support a new
    /// producer service; nothing else in this project needs to change.
    /// </summary>
    public static class FlowMaps
    {
        public const string GetFeeTransaction = "GetFeeTransaction";
        public const string MoneyGramTransaction = "MoneyGramTransaction";

        public static readonly Dictionary<string, string[]> GetFeeFlow = new()
        {
            ["RequestReceived"]    = new[] { "ValidationSuccess", "ValidationFailed" },
            ["ValidationSuccess"]  = new[] { "AgentRetrieved", "AgentInvalid" },
            ["AgentRetrieved"]     = new[] { "BranchRetrieved", "BranchInvalid" },
            ["BranchRetrieved"]    = new[] { "FeeCalculated", "FeeCalculationFailed" },
            ["FeeCalculated"]      = new[] { "ProcessCompleted" },
        };

        public static readonly HashSet<string> GetFeeTerminalTags = new()
        {
            "ValidationFailed", "AgentInvalid", "BranchInvalid", "FeeCalculationFailed", "ProcessCompleted"
        };

        // StatusUpdateFailed deliberately has NO entry here — that's what
        // makes it a dead end (never reaches a terminal tag).
        public static readonly Dictionary<string, string[]> MoneyGramFlow = new()
        {
            ["ValidationSuccess"]       = new[] { "RemittanceCreated" },
            ["RemittanceCreated"]       = new[] { "AccountServiceInitiated" },
            ["AccountServiceInitiated"] = new[] { "CommitRequested" },
            ["CommitRequested"]         = new[] { "CommitSuccess", "CommitTimeout", "CommitFailed" },
            ["CommitSuccess"]           = new[] { "StatusUpdateAttempted" },
            ["CommitTimeout"]           = new[] { "StatusUpdateAttempted" },
            ["CommitFailed"]            = new[] { "StatusUpdateAttempted" },
            ["StatusUpdateAttempted"]   = new[] { "StatusUpdateSuccess", "StatusUpdateFailed" },
            ["StatusUpdateSuccess"]     = new[] { "TransactionCompleted", "TransactionFailed" },
        };

        public static readonly HashSet<string> MoneyGramTerminalTags = new()
        {
            "TransactionCompleted", "TransactionFailed"
        };

        public static readonly Dictionary<string, (Dictionary<string, string[]> Map, HashSet<string> Terminal)> AllFlows = new()
        {
            [GetFeeTransaction] = (GetFeeFlow, GetFeeTerminalTags),
            [MoneyGramTransaction] = (MoneyGramFlow, MoneyGramTerminalTags),
        };
    }
}
