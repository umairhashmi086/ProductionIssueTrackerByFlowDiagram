using System.Text;
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
        private readonly FlowDefinitionRepository _flowDefinitions;
        private readonly RazorViewRenderer _viewRenderer;
        private readonly FlowValidator _validator = new();

        public InvestigateController(
            LokiInvestigateClient loki,
            DemoSeedClient demoSeed,
            FlowDefinitionRepository flowDefinitions,
            RazorViewRenderer viewRenderer)
        {
            _loki = loki;
            _demoSeed = demoSeed;
            _flowDefinitions = flowDefinitions;
            _viewRenderer = viewRenderer;
        }

        // GET /Investigate/Index — search form: flow-type dropdown + a
        // reference number OR Kong ID field (whichever is filled in wins).
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var dbFlowNames = await _flowDefinitions.GetAllFlowNamesAsync();
            var allFlowNames = FlowMaps.AllFlows.Keys.Concat(dbFlowNames).Distinct().OrderBy(n => n).ToList();

            var model = new SearchIndexViewModel { FlowNames = allFlowNames };
            return View(model);
        }

        // GET /Investigate/Reference?referenceNumber=MG-2026-00417&flowName=...&attemptIndex=0
        // GET /Investigate/Reference?kongId=abc123&flowName=...
        [HttpGet]
        public async Task<IActionResult> Reference(string? referenceNumber, string? kongId, string? flowName, int attemptIndex = 0)
        {
            var model = await BuildReferenceViewModelAsync(referenceNumber, kongId, flowName, attemptIndex);
            if (model == null)
            {
                ViewBag.NotFoundReference = string.IsNullOrWhiteSpace(kongId) ? referenceNumber : kongId;
                return View("NotFound");
            }
            return View(model);
        }

        // GET /Investigate/DownloadReport?... — same lookup as Reference, but
        // renders the diagram + raw tag sequence into one self-contained HTML
        // file the browser downloads directly (works fully offline once
        // downloaded — the SVG is inline, not a separate image reference).
        [HttpGet]
        public async Task<IActionResult> DownloadReport(string? referenceNumber, string? kongId, string? flowName, int attemptIndex = 0)
        {
            var model = await BuildReferenceViewModelAsync(referenceNumber, kongId, flowName, attemptIndex);
            if (model == null) return NotFound();

            var html = await _viewRenderer.RenderViewToStringAsync(ControllerContext, "DownloadReport", model);
            var bytes = Encoding.UTF8.GetBytes(html);
            var fileName = $"investigate-{model.ReferenceNumber}-attempt{model.ActiveAttemptIndex + 1}.html";
            return File(bytes, "text/html", fileName);
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

        /// <summary>
        /// Shared lookup + validation logic used by both Reference (renders
        /// the page) and DownloadReport (renders the same data into a
        /// downloadable file) — keeps them from drifting out of sync.
        /// </summary>
        private async Task<ReferenceViewModel?> BuildReferenceViewModelAsync(string? referenceNumber, string? kongId, string? flowName, int attemptIndex)
        {
            if (string.Equals(flowName, "All", StringComparison.OrdinalIgnoreCase))
                flowName = null;

            var searchingByKongId = string.IsNullOrWhiteSpace(referenceNumber) && !string.IsNullOrWhiteSpace(kongId);
            if (string.IsNullOrWhiteSpace(referenceNumber) && string.IsNullOrWhiteSpace(kongId))
                return null;

            Dictionary<string, List<TagDto>> attemptsByKongId;
            string resolvedReferenceNumber;

            if (searchingByKongId)
            {
                var (foundReference, tags) = await _loki.GetByKongIdAsync(kongId!, flowName);
                if (tags.Count == 0 || foundReference == null) return null;
                resolvedReferenceNumber = foundReference;
                attemptsByKongId = new Dictionary<string, List<TagDto>> { [kongId!] = tags };
            }
            else
            {
                resolvedReferenceNumber = referenceNumber!;
                attemptsByKongId = await _loki.GetAttemptsByReferenceAsync(resolvedReferenceNumber, flowName);
                if (attemptsByKongId.Count == 0) return null;
            }

            var resolvedFlowName = flowName ?? await _loki.GetFlowNameAsync(resolvedReferenceNumber) ?? "";

            // Flow definitions can come from either source: the hardcoded
            // FlowMaps (original POC flows) or ones created via the
            // Flow Definitions admin page and stored in Postgres. DB wins if
            // a flow with the same name exists in both, since DB-defined
            // flows are the ones actively being authored/edited.
            var (flowMap, terminalTags) = await _flowDefinitions.GetFlowDefinitionAsync(resolvedFlowName)
                ?? (FlowMaps.AllFlows.TryGetValue(resolvedFlowName, out var flow) ? flow : (new Dictionary<string, string[]>(), new HashSet<string>()));

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

            return new ReferenceViewModel
            {
                ReferenceNumber = resolvedReferenceNumber,
                FlowName = resolvedFlowName,
                Attempts = attemptDtos,
                ActiveAttemptIndex = attemptIndex,
                Diagram = diagram
            };
        }
    }
}
