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
        private readonly FlowDefinitionRepository _flowDefinitions;
        private readonly RazorViewRenderer _viewRenderer;
        private readonly FlowValidator _validator = new();

        public InvestigateController(
            LokiInvestigateClient loki,
            FlowDefinitionRepository flowDefinitions,
            RazorViewRenderer viewRenderer)
        {
            _loki = loki;
            _flowDefinitions = flowDefinitions;
            _viewRenderer = viewRenderer;
        }

        // GET /Investigate/Index — search form: flow-type dropdown + a
        // reference number OR Kong ID field (whichever is filled in wins).
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var dbFlowNames = await _flowDefinitions.GetAllFlowNamesAsync();

            var model = new SearchIndexViewModel { FlowNames = dbFlowNames };
            return View(model);
        }

        // GET /Investigate/Logs — display all logs grouped by Kong ID
        [HttpGet]
        public async Task<IActionResult> Logs(string? flowName, DateTime? fromDate, DateTime? toDate)
        {
            var dbFlowNames = await _flowDefinitions.GetAllFlowNamesAsync();
            ViewBag.FlowNames = dbFlowNames;

            // Default to today (00:00 to 23:59) if no dates provided
            var now = DateTime.Now;
            var startDate = fromDate ?? new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
            var endDate = toDate ?? new DateTime(now.Year, now.Month, now.Day, 23, 59, 59);

            // Fetch all logs from Loki grouped by Kong ID with date range
            var logsGroupedByKongId = await _loki.GetAllLogsGroupedByKongIdAsync(flowName, new DateTimeOffset(startDate), new DateTimeOffset(endDate));

            // Convert to LogGroupDto for the view
            var logGroups = logsGroupedByKongId.Values
                .Select(g => new LogGroupDto
                {
                    KongId = g.KongId,
                    ReferenceNumber = g.ReferenceNumber,
                    FlowName = g.FlowName,
                    LogCount = g.Tags.Count,
                    FirstLogTime = g.Tags.FirstOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow,
                    LastLogTime = g.Tags.LastOrDefault()?.Timestamp ?? DateTimeOffset.UtcNow,
                    Logs = g.Tags
                })
                .OrderByDescending(g => g.LastLogTime)
                .ToList();

            var model = new LogsPageViewModel
            {
                FlowName = flowName,
                LogGroups = logGroups,
                FromDate = fromDate,
                ToDate = toDate
            };

            return View(model);
        }

        // POST /Investigate/GetLogsJson — fetch logs grouped by Kong ID (AJAX endpoint)
        [HttpPost]
        public async Task<IActionResult> GetLogsJson(string? flowName)
        {
            try
            {
                // Query to get all logs from last 24 hours
                var query = string.IsNullOrWhiteSpace(flowName)
                    ? "{app=\"poc-feeservice\"} | json"
                    : $"{{app=\"poc-feeservice\"}} | json | FlowName=\"{flowName}\"";

                var logsDict = await _loki.GetAttemptsByReferenceAsync("", flowName);

                var logGroups = new List<LogGroupDto>();
                // This is a simplified version - real implementation would query all logs
                // For demo purposes, return empty list

                return Json(new { success = true, logs = logGroups });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
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


        /// <summary>
        /// Shared lookup + validation logic used by both Reference (renders
        /// the page) and DownloadReport (renders the same data into a
        /// downloadable file) — keeps them from drifting out of sync.
        /// </summary>
        private async Task<ReferenceViewModel?> BuildReferenceViewModelAsync(string? referenceNumber, string? kongId, string? flowName, int attemptIndex)
        {
            if (string.Equals(flowName, "All", StringComparison.OrdinalIgnoreCase))
                flowName = null;

            // Prioritize Kong ID over reference number since:
            // 1. Every transaction has a Kong ID
            // 2. Reference number can be null
            // 3. Multiple Kong IDs can share the same reference number
            var searchingByKongId = !string.IsNullOrWhiteSpace(kongId);
            if (string.IsNullOrWhiteSpace(kongId) && string.IsNullOrWhiteSpace(referenceNumber))
                return null;

            Dictionary<string, List<TagDto>> attemptsByKongId;
            string resolvedReferenceNumber;

            if (searchingByKongId)
            {
                var (foundReference, tags) = await _loki.GetByKongIdAsync(kongId!, flowName);
                if (tags.Count == 0 || foundReference == null) return null;
                resolvedReferenceNumber = foundReference ?? "Unknown";
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
                ??  (new Dictionary<string, string[]>(), new HashSet<string>());

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

                // Calculate latency for each tag
                var tagsWithLatency = CalculateLatencies(result.AnnotatedTags);

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
                    TagSequence = tagsWithLatency,
                    ServiceGroups = BuildServiceGroups(tagsWithLatency)
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

        /// <summary>
        /// Calculates the latency (in milliseconds) between consecutive tags.
        /// First tag has 0ms latency. Subsequent tags show the time delta from previous tag.
        /// </summary>
        private static List<TagDto> CalculateLatencies(List<TagDto> tags)
        {
            if (tags.Count == 0) return tags;

            var result = new List<TagDto>();
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                var latencyMs = i == 0 ? 0 : (long)(tags[i].Timestamp - tags[i - 1].Timestamp).TotalMilliseconds;

                result.Add(new TagDto
                {
                    TagName = tag.TagName,
                    Timestamp = tag.Timestamp,
                    MetadataJson = tag.MetadataJson,
                    RetryCount = tag.RetryCount,
                    IsUnexpected = tag.IsUnexpected,
                    LatencyMs = latencyMs,
                    ServiceName = tag.ServiceName
                });
            }
            return result;
        }

        /// <summary>
        /// Groups tags by service name and calculates total latency per service.
        /// </summary>
        private static List<ServiceGroupDto> BuildServiceGroups(List<TagDto> tags)
        {
            if (tags.Count == 0) return new();

            var groups = new Dictionary<string, ServiceGroupDto>();
            var groupOrder = new List<string>();

            foreach (var tag in tags)
            {
                var serviceName = string.IsNullOrWhiteSpace(tag.ServiceName) ? "Unknown" : tag.ServiceName;

                if (!groups.ContainsKey(serviceName))
                {
                    groups[serviceName] = new ServiceGroupDto { ServiceName = serviceName };
                    groupOrder.Add(serviceName);
                }

                groups[serviceName].Tags.Add(tag);
            }

            var result = groupOrder.Select(svc =>
            {
                var group = groups[svc];
                group.CalculateTotalLatency();
                return group;
            }).ToList();

            return result;
        }
    }
}
