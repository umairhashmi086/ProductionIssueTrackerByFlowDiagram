using Microsoft.AspNetCore.Mvc;
using Prod_IssueTracker_POC.Web.FlowTagging;
using Prod_IssueTracker_POC.Web.Models;
using Prod_IssueTracker_POC.Web.Services;

namespace Prod_IssueTracker_POC.Web.Controllers
{
    public class InvestigateController : Controller
    {
        private readonly LokiInvestigateClient _loki;
        private readonly DemoSeedClient _demoSeed;
        private readonly FlowValidator _validator = new();

        public InvestigateController(LokiInvestigateClient loki, DemoSeedClient demoSeed)
        {
            _loki = loki;
            _demoSeed = demoSeed;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        // GET /Investigate/Reference?referenceNumber=MG-2026-00417&attemptIndex=0
        // Reads come exclusively from Loki now — no dependency on any
        // producer service's own API. Any service tagging into the same
        // Loki instance under this shape is investigable here.
        [HttpGet]
        public async Task<IActionResult> Reference(string referenceNumber, int attemptIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(referenceNumber))
                return RedirectToAction(nameof(Index));

            var attemptsByAttemptId = await _loki.GetAttemptsByReferenceAsync(referenceNumber);
            if (attemptsByAttemptId.Count == 0)
            {
                ViewBag.NotFoundReference = referenceNumber;
                return View("NotFound");
            }

            var flowName = await _loki.GetFlowNameAsync(referenceNumber) ?? "";
            var (flowMap, terminalTags) = FlowMaps.AllFlows.TryGetValue(flowName, out var flow)
                ? flow
                : (new Dictionary<string, string[]>(), new HashSet<string>());

            // Validate walks the WHOLE raw sequence and annotates every tag
            // occurrence individually (IsUnexpected) — no pre-collapsing
            // needed, the validator tolerates retries on its own, and it
            // never stops early, so the flow can be shown "recovering" to
            // green after a red deviation instead of just halting.
            var attempts = attemptsByAttemptId
                .Select(kv =>
                {
                    var result = _validator.Validate(kv.Value, flowMap, terminalTags);
                    return (AttemptId: kv.Key, Result: result);
                })
                .OrderBy(a => a.Result.StartedAt)
                .ToList();

            var attemptDtos = new List<AttemptDto>();
            for (int i = 0; i < attempts.Count; i++)
            {
                var (attemptId, result) = attempts[i];
                var previous = i > 0 ? attempts[i - 1].Result : null;
                var previousAttemptId = i > 0 ? attempts[i - 1].AttemptId : null;
                var link = _validator.TryLinkToPriorAttempt(previous, previousAttemptId);

                attemptDtos.Add(new AttemptDto
                {
                    AttemptNumber = i + 1,
                    AttemptId = attemptId,
                    StartedAt = result.StartedAt,
                    IsHealthy = result.IsHealthy,
                    LastValidTag = result.LastTag,
                    DeadEnd = result.DeadEnd,
                    HasDeviations = result.HasDeviations,
                    CrossAttemptLink = link,
                    TagSequence = result.AnnotatedTags // raw order, each tag flagged individually
                });
            }

            if (attemptIndex < 0 || attemptIndex >= attemptDtos.Count) attemptIndex = 0;
            var active = attemptDtos[attemptIndex];

            var diagram = flowMap.Count > 0
                ? FlowDiagramLayout.Build(flowMap, terminalTags, active.TagSequence, active.DeadEnd, active.LastValidTag)
                : new DiagramViewModel();

            var model = new ReferenceViewModel
            {
                ReferenceNumber = referenceNumber,
                FlowName = flowName,
                Attempts = attemptDtos,
                ActiveAttemptIndex = attemptIndex,
                Diagram = diagram
            };

            return View(model);
        }

        // POST /Investigate/SeedDemo — this is the one place Web still talks
        // to the API project, because seeding sample data is a WRITE action
        // (it asks the producer to log something), not a read.
        [HttpPost]
        public async Task<IActionResult> SeedDemo()
        {
            var referenceNumber = await _demoSeed.SeedMoneyGramScenarioAsync();
            return RedirectToAction(nameof(Reference), new { referenceNumber = referenceNumber ?? "MG-2026-00417" });
        }
    }
}
