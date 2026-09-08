namespace Prod_IssueTracker_POC.FlowTagging
{
    /// <summary>
    /// One checkpoint emitted during a request's flow. KongId identifies a
    /// single API call (here: HttpContext.TraceIdentifier — in the full
    /// PaymentHub system this would be Kong's request id). ReferenceNumber is
    /// the business reference, which can repeat across multiple attempts
    /// (retries) — that's what lets the investigate tool group them together.
    /// </summary>
    public record FlowTagEntry(
        string KongId,
        string ReferenceNumber,
        string FlowName,
        string TagName,
        string? MetadataJson,
        DateTimeOffset Timestamp);
}
