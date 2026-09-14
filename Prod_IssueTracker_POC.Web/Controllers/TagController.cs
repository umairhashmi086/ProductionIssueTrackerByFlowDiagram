using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Prod_IssueTracker_POC.Web.Models;
using Prod_IssueTracker_POC.Web.Services;

namespace Prod_IssueTracker_POC.Web.Controllers
{
    /// <summary>
    /// Manage the reusable tag vocabulary (the "tags" table) — add a tag
    /// once here, then reuse it across any number of flows in the visual
    /// builder instead of retyping the same name for every flow.
    /// </summary>
    public class TagController : Controller
    {
        private readonly FlowDefinitionRepository _repo;

        public TagController(FlowDefinitionRepository repo)
        {
            _repo = repo;
        }

        // GET /Tag — list all reusable tags
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tags = await _repo.GetAllGlobalTagsAsync();
            return View(tags);
        }

        // POST /Tag/Create — add a new reusable tag
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string tagName)
        {
            if (!string.IsNullOrWhiteSpace(tagName))
                await _repo.CreateGlobalTagAsync(tagName.Trim());
            return RedirectToAction(nameof(Index));
        }

        // POST /Tag/QuickCreate — used by the flow builder's inline "new
        // tag" flow. Returns JSON so the builder can add it to the canvas
        // without a full page reload.
        [HttpPost]
        public async Task<IActionResult> QuickCreate([FromBody] QuickCreateTagRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TagName))
                return BadRequest(new { message = "Tag name is required." });

            var tag = await _repo.CreateGlobalTagAsync(request.TagName.Trim());
            return Ok(new { id = tag.Id, tagName = tag.TagName });
        }

        // POST /Tag/Delete — only succeeds if the tag isn't used by any flow
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _repo.DeleteGlobalTagAsync(id);
            }
            catch (PostgresException ex) when (ex.SqlState == "23503") // foreign_key_violation
            {
                TempData["DeleteError"] = "That tag is still used in one or more flows — remove it from those flows first.";
            }
            return RedirectToAction(nameof(Index));
        }
    }

    public class QuickCreateTagRequest
    {
        public string TagName { get; set; } = "";
    }
}
