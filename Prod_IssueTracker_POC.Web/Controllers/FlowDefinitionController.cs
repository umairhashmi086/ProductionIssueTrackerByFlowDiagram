using Microsoft.AspNetCore.Mvc;
using Npgsql;
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

        public FlowDefinitionController(FlowDefinitionRepository repo)
        {
            _repo = repo;
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

            var trimmedName = model.FlowName.Trim();

            var existing = await _repo.GetDetailAsync(trimmedName);
            if (existing != null)
            {
                ModelState.AddModelError(nameof(model.FlowName), $"A flow named \"{trimmedName}\" already exists — pick a different name or edit the existing one.");
                return View(model);
            }

            try
            {
                await _repo.CreateFlowAsync(trimmedName, model.Description?.Trim());
            }
            catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation
            {
                ModelState.AddModelError(nameof(model.FlowName), $"A flow named \"{trimmedName}\" already exists — pick a different name or edit the existing one.");
                return View(model);
            }

            return RedirectToAction(nameof(Edit), new { flowName = trimmedName });
        }

        // GET /FlowDefinition/Edit?flowName=X — plain list-based editor:
        // pick existing tags from the reusable library, define transitions
        // between the ones on this flow. Simple server-rendered forms only.
        [HttpGet]
        public async Task<IActionResult> Edit(string flowName)
        {
            var detail = await _repo.GetDetailAsync(flowName);
            if (detail == null) return NotFound();
            detail.AvailableTags = await _repo.GetAllGlobalTagsAsync();
            return View(detail);
        }

        // POST /FlowDefinition/AddExistingTag — pick a tag from the library
        // and place it on this flow.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddExistingTag(int flowDefinitionId, string flowName, int tagId, bool isTerminal)
        {
            if (tagId > 0)
                await _repo.AddExistingTagToFlowAsync(flowDefinitionId, tagId, isTerminal);
            return RedirectToAction(nameof(Edit), new { flowName });
        }

        // POST /FlowDefinition/RemoveTag — removes the tag from THIS flow
        // only; the global tag stays in the library for reuse elsewhere.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveTag(int flowTagId, string flowName)
        {
            await _repo.RemoveTagFromFlowAsync(flowTagId);
            return RedirectToAction(nameof(Edit), new { flowName });
        }

        // POST /FlowDefinition/AddTransition
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTransition(int flowDefinitionId, string flowName, int fromFlowTagId, int toFlowTagId)
        {
            if (fromFlowTagId != toFlowTagId) // self-loops are meaningless — retries are handled automatically by FlowValidator
                await _repo.AddTransitionAsync(flowDefinitionId, fromFlowTagId, toFlowTagId);
            return RedirectToAction(nameof(Edit), new { flowName });
        }

        // POST /FlowDefinition/RemoveTransition
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveTransition(int transitionId, string flowName)
        {
            await _repo.RemoveTransitionAsync(transitionId);
            return RedirectToAction(nameof(Edit), new { flowName });
        }

        // POST /FlowDefinition/SaveGraph — the canvas builder's Save button
        // posts the WHOLE graph (all nodes + all edges) here in one call.
        [HttpPost]
        public async Task<IActionResult> SaveGraph([FromBody] SaveGraphRequest request)
        {
            if (request.FlowDefinitionId <= 0)
                return BadRequest(new { message = "Missing flowDefinitionId" });

            await _repo.SaveGraphAsync(request.FlowDefinitionId, request.Nodes, request.Edges);
            return Ok(new { message = "Saved", tagCount = request.Nodes.Count, transitionCount = request.Edges.Count });
        }
    }
}
