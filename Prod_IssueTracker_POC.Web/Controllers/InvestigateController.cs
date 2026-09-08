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

        // GET /Investigate/Index — search form: flow-type dropdown + a
        // reference number OR Kong ID field (whichever is filled in wins).
        [HttpGet]
        public IActionResult Index()
        {
            var model = new SearchIndexViewModel
            {
                FlowNames = FlowMaps.AllFlows.Keys.OrderBy(n => n).ToList()
            };
            return View(model);
        }

        // GET /Investigate/Reference?referenceNumber=MG-2026-00417&flowName=...&attemptIndex=0
        // GET /Investigate/Reference?kongId=abc123&flowName=...
        // Reads come exclusively from Loki — no dependency on any producer
        // service's own API. flowName (from the dropdown) is optional and,
        // when set, narrows the Loki query itself rather than just filtering
        // client-side — useful if reference numbers or Kong IDs could ever
        // collide across different flow types sharing one Loki instance.
        [HttpGet]
        public async Task<IActionResult> Reference(string? referenceNumber, string? kongId, string? flowName, int attemptIndex = 0)
        {
            // The dropdown submits the literal value "All" for "no filter" —
            // normalize it here so everything downstream can keep treating
            // "no flow filter" as null/empty, same as before.
            if (string.Equals(flowName, "All", StringComparison.OrdinalIgnoreCase))
                flowName = null;

            var searchingByKongId = string.IsNullOrWhiteSpace(referenceNumber) && !string.IsNullOrWhiteSpace(kongId);

            if (string.IsNullOrWhiteSpace(referenceNumber) && string.IsNullOrWhiteSpace(kongId))
                return RedirectToAction(nameof(Index));

            Dictionary<string, List<TagDto>> attemptsByKongId;
            string resolvedReferenceNumber;

            if (searchingByKongId)
            {
                var (foundReference, tags) = await _loki.GetByKongIdAsync(kongId!, flowName);
                if (tags.Count == 0 || foundReference == null)
                {
                    ViewBag.NotFoundReference = kongId;
                    return View("NotFound");
                }
                resolvedReferenceNumber = foundReference;
                attemptsByKongId = new Dictionary<string, List<TagDto>> { [kongId!] = tags };
            }
            else
            {
                resolvedReferenceNumber = referenceNumber!;
                attemptsByKongId = await _loki.GetAttemptsByReferenceAsync(resolvedReferenceNumber, flowName);
                if (attemptsByKongId.Count == 0)
                {
                    ViewBag.NotFoundReference = resolvedReferenceNumber;
                    return View("NotFound");
                }
            }

            var resolvedFlowName = flowName ?? await _loki.GetFlowNameAsync(resolvedReferenceNumber) ?? "";
            var (flowMap, terminalTags) = FlowMaps.AllFlows.TryGetValue(resolvedFlowName, out var flow)
                ? flow
                : (new Dictionary<string, string[]>(), new HashSet<string>());

            var attempts = attemptsByKongId
                .Select(kv =>
                {
                    var result = _validator.Validate(kv.Value, flowMap, terminalTags);
                    return (KongId: kv.Key, Result: result);
                })
                .OrderBy(a => a.Result.StartedAt)
                .ToList();

            var attemptDtos = new List<AttemptDto>();
            for (int i = 0; i < attempts.Count; i++)
            {
                var (kongIdValue, result) = attempts[i];
                var previous = i > 0 ? attempts[i - 1].Result : null;
                var previousKongId = i > 0 ? attempts[i - 1].KongId : null;
                var link = _validator.TryLinkToPriorAttempt(previous, previousKongId);

                attemptDtos.Add(new AttemptDto
                {
                    AttemptNumber = i + 1,
                    KongId = kongIdValue,
                    StartedAt = result.StartedAt,
                    IsHealthy = result.IsHealthy,
                    LastValidTag = result.LastTag,
                    DeadEnd = result.DeadEnd,
                    HasDeviations = result.HasDeviations,
                    CrossAttemptLink = link,
                    TagSequence = result.AnnotatedTags
                });
            }

            if (attemptIndex < 0 || attemptIndex >= attemptDtos.Count) attemptIndex = 0;
            var active = attemptDtos[attemptIndex];

            var diagram = flowMap.Count > 0
                ? FlowDiagramLayout.Build(flowMap, terminalTags, active.TagSequence, active.DeadEnd, active.LastValidTag)
                : new DiagramViewModel();

            var model = new ReferenceViewModel
            {
                ReferenceNumber = resolvedReferenceNumber,
                FlowName = resolvedFlowName,
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
