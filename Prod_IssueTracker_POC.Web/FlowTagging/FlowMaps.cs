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

        // StatusUpdateFailed deliberately has NO entry here — that's what
        // makes it a dead end (never reaches a terminal tag).
       


    }
}
