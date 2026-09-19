using Microsoft.AspNetCore.Mvc;
using Prod_IssueTracker_POC.Web.Models;
using Prod_IssueTracker_POC.Web.Services;

namespace Prod_IssueTracker_POC.Web.Controllers
{
    /// <summary>
    /// Admin UI for creating and editing flow definitions in Postgres, as an
    /// alternative to hand-editing FlowMaps.cs. InvestigateController reads
    /// whatever is created here automatically — no code changes or redeploy
    /// needed to add a new flow.
    /// </summary>
    public class FlowDefinitionController : Controller
    {
        private readonly FlowDefinitionRepository _repo;
        private readonly TagRepository _tagRepo;
        private readonly SuggestedFlowService _suggestedFlow;

        public FlowDefinitionController(FlowDefinitionRepository repo, TagRepository tagRepo, SuggestedFlowService suggestedFlow)
        {
            _repo = repo;
            _tagRepo = tagRepo;
            _suggestedFlow = suggestedFlow;
        }

        // GET /FlowDefinition — list all DB-defined flows
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var summaries = await _repo.GetAllSummariesAsync();
            return View(summaries);
        }

        // GET /FlowDefinition/Create
        [HttpGet]
        public IActionResult Create() => View(new CreateFlowDefinitionViewModel());

        // GET /FlowDefinition/Suggest — form to suggest a flow from logs
        [HttpGet]
        public async Task<IActionResult> Suggest()
        {
            var dbFlowNames = await _repo.GetAllFlowNamesAsync();
            ViewBag.FlowNames = dbFlowNames;
            return View(new SuggestFlowViewModel());
        }

        // POST /FlowDefinition/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateFlowDefinitionViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.FlowName))
            {
                ModelState.AddModelError(nameof(model.FlowName), "Flow name is required.");
                return View(model);
            }

            await _repo.CreateFlowAsync(model.FlowName.Trim(), model.Description?.Trim());
            return RedirectToAction(nameof(Edit), new { flowName = model.FlowName.Trim() });
        }

        // GET /FlowDefinition/CreateFromSuggestion — creates a new flow and
        // redirects to Edit with suggested graph pre-populated from sessionStorage.
        [HttpGet]
        public IActionResult CreateFromSuggestion()
        {
            return View("Create", new CreateFlowDefinitionViewModel
            {
                Description = "Auto-generated from log analysis"
            });
        }

        // GET /FlowDefinition/Edit?flowName=X — the visual flow builder.
        // Existing tags/transitions are passed to the page as JSON to seed
        // the canvas; everything else happens client-side until Save.
        [HttpGet]
        public async Task<IActionResult> Edit(string flowName)
        {
            var detail = await _repo.GetDetailAsync(flowName);
            if (detail == null) return NotFound();
            ViewBag.AvailableTags = await _tagRepo.GetAllNamesAsync();
            return View(detail);
        }

        // POST /FlowDefinition/SaveGraph — the builder's Save button posts
        // the WHOLE graph (all nodes + all edges) here in one call, rather
        // than one request per tag/transition like the old form-based UI.
        [HttpPost]
        public async Task<IActionResult> SaveGraph([FromBody] SaveGraphRequest? request)
        {
            // Without [ApiController], model binding doesn't auto-reject a
            // missing/malformed JSON body — it just leaves request null, so
            // this null check must come before touching any of its members.
            if (request == null)
                return BadRequest(new { message = "Missing or invalid request body" });

            if (request.FlowDefinitionId <= 0)
                return BadRequest(new { message = "Missing flowDefinitionId" });

            var idMap = await _repo.SaveGraphAsync(request.FlowDefinitionId, request.Nodes, request.Edges);
            return Ok(new { message = "Saved", tagCount = request.Nodes.Count, transitionCount = request.Edges.Count, nodeIdMap = idMap });
        }

        // POST /FlowDefinition/SuggestFromKongId — analyzes a single Kong ID's logs
        // and suggests a flow diagram based on its tag sequence.
        [HttpPost]
        public async Task<IActionResult> SuggestFromKongId([FromBody] SuggestFlowRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.KongId))
                return BadRequest(new { message = "Missing or invalid kongId" });

            var suggestion = await _suggestedFlow.SuggestFromKongIdAsync(request.KongId, request.FlowName, layoutNodes: true);
            if (suggestion == null)
                return NotFound(new { message = $"No logs found for Kong ID: {request.KongId}" });

            return Ok(suggestion);
        }

        // POST /FlowDefinition/SuggestFromReference — analyzes all attempts for a
        // reference number and suggests a flow diagram combining all tag sequences.
        [HttpPost]
        public async Task<IActionResult> SuggestFromReference([FromBody] SuggestFlowRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ReferenceNumber))
                return BadRequest(new { message = "Missing or invalid referenceNumber" });

            var suggestion = await _suggestedFlow.SuggestFromReferenceAsync(request.ReferenceNumber, request.FlowName, layoutNodes: true);
            if (suggestion == null)
                return NotFound(new { message = $"No logs found for reference: {request.ReferenceNumber}" });

            return Ok(suggestion);
        }
    }
}

public class SuggestFlowRequest
{
    public string? KongId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? FlowName { get; set; }
}
