using Microsoft.AspNetCore.Mvc;
using Prod_IssueTracker_POC.FlowTagging;

namespace Prod_IssueTracker_POC.Controllers
{
    /// <summary>
    /// Seeds sample data so you can see the investigate page working without
    /// wiring up a real MoneyGram integration. Not part of the "real" app —
    /// safe to delete this controller once you're tagging real flows.
    /// </summary>
    [ApiController]
    [Route("api/demo")]
    public class DemoSeedController : ControllerBase
    {
        private readonly IFlowTagger _flowTagger;

        public DemoSeedController(IFlowTagger flowTagger)
        {
            _flowTagger = flowTagger;
        }

        /// <summary>
        /// Reproduces the original MoneyGram incident: attempt 1 times out on
        /// commit and gets stuck (StatusUpdateFailed, no terminal tag reached);
        /// attempt 2 retries under the same reference number shortly after and
        /// deviates immediately because the system tried to skip straight to
        /// CommitRequested (representing the duplicate-reference short-circuit).
        /// </summary>
        [HttpPost("seed-moneygram-scenario")]
        public IActionResult SeedMoneyGramScenario()
        {
            var reference = "MG-2026-00417";
            var now = DateTimeOffset.UtcNow;

            // --- Attempt 1: commit is retried after transient exceptions —
            // a non-consecutive loop-back pattern (CommitRequested ->
            // CommitFailed -> CommitRequested -> CommitFailed -> ...), which
            // is exactly the "A->B->A->B->A->C" shape the retry-loop-back
            // rule in FlowValidator is meant to handle without flagging a
            // false deviation, before finally timing out for good.
            var kongId1 = "demo-attempt-1-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var t0 = now.AddMinutes(-10);
            Emit(kongId1, reference, "ValidationSuccess", t0);
            Emit(kongId1, reference, "RemittanceCreated", t0.AddSeconds(1));
            Emit(kongId1, reference, "AccountServiceInitiated", t0.AddSeconds(2));
            Emit(kongId1, reference, "CommitRequested", t0.AddSeconds(3), new { attempt = 1 });
            Emit(kongId1, reference, "CommitFailed", t0.AddSeconds(5), new { reason = "transient network error" });
            Emit(kongId1, reference, "CommitRequested", t0.AddSeconds(8), new { attempt = 2 });
            Emit(kongId1, reference, "CommitFailed", t0.AddSeconds(10), new { reason = "transient network error" });
            Emit(kongId1, reference, "CommitRequested", t0.AddSeconds(13), new { attempt = 3 });
            Emit(kongId1, reference, "CommitTimeout", t0.AddSeconds(28), new { waitedSeconds = 15 });
            Emit(kongId1, reference, "StatusUpdateAttempted", t0.AddSeconds(29));
            Emit(kongId1, reference, "StatusUpdateFailed", t0.AddSeconds(29.5),
                new { reason = "ArgumentException: reasonCode cannot be null for status 'Failed'" });
            // (deliberately no further tag — this is the dead end)

            // --- Attempt 2: retried ~7 minutes later, same reference ---
            var kongId2 = "demo-attempt-2-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var t1 = now.AddMinutes(-3);
            Emit(kongId2, reference, "ValidationSuccess", t1);
            // Jumps straight to CommitRequested — representing the system
            // short-circuiting because the provider still held the reference
            // active from attempt 1. This is an unexpected transition against
            // the flow map (ValidationSuccess should be followed by
            // RemittanceCreated), which the validator will correctly flag.
            Emit(kongId2, reference, "CommitRequested", t1.AddSeconds(1),
                new { reason = "MoneyGram returned Duplicate Reference error" });

            return Ok(new
            {
                message = "Seeded MoneyGram scenario",
                referenceNumber = reference
                // Investigation now happens in Prod_IssueTracker_POC.Web, not
                // here — this API no longer serves an investigate endpoint.
            });
        }

        private void Emit(string kongId, string referenceNumber, string tagName, DateTimeOffset timestamp, object? metadata = null)
        {
            _flowTagger.Tag(kongId, referenceNumber, FlowMaps.MoneyGramTransaction, tagName, metadata, timestamp);
        }
    }
}
