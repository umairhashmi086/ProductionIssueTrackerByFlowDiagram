namespace Prod_IssueTracker_POC.FlowTagging
{
    /// <summary>
    /// This project only LOGS tags — it deliberately does not know what a
    /// "valid" sequence looks like anymore. The adjacency maps / terminal-tag
    /// definitions used to live here, but interpreting flow shape is the
    /// investigator's job (Prod_IssueTracker_POC.Web), not any one producer's.
    /// That's what lets multiple independent services log to the same Loki
    /// instance and be investigated by one shared tool, without each of them
    /// needing to agree on or duplicate validation logic.
    ///
    /// Only the flow NAME constants stay here, since FeeService/DemoSeedController
    /// need a string to tag each emitted checkpoint with.
    /// </summary>
    public static class FlowMaps
    {
        public const string GetFeeTransaction = "GetFeeTransaction";
        public const string MoneyGramTransaction = "MoneyGramTransaction";
    }
}
