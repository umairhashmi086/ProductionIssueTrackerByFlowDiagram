using Microsoft.AspNetCore.Mvc;
using Prod_IssueTracker_POC.Web.Models;
using Prod_IssueTracker_POC.Web.Services;

namespace Prod_IssueTracker_POC.Web.Controllers
{
    /// <summary>
    /// Admin UI for the reusable tag catalog (the "tags" table). Tags added
    /// here show up as pickable options in the flow builder's "+ Add Tag"
    /// mode (Views/FlowDefinition/Edit.cshtml), instead of the old behavior
    /// of re-typing a tag name from scratch every time.
    /// </summary>
    public class TagController : Controller
    {
        private readonly TagRepository _repo;

        public TagController(TagRepository repo)
        {
            _repo = repo;
        }

        // GET /Tag — list all catalog tags, with a form to add a new one.
        [HttpGet]
        public async Task<IActionResult> Index(string? search)
        {
            var tags = await _repo.GetAllAsync();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                tags = tags.Where(t => t.TagName.ToLower().Contains(searchLower)).ToList();
            }

            ViewBag.SearchQuery = search ?? "";
            return View(tags);
        }

        // POST /Tag/Create — add a new tag to the catalog (idempotent by name).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateTagViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.TagName))
            {
                ModelState.AddModelError(nameof(model.TagName), "Tag name is required.");
                var tags = await _repo.GetAllAsync();
                return View(nameof(Index), tags);
            }

            await _repo.GetOrCreateAsync(model.TagName.Trim());
            return RedirectToAction(nameof(Index));
        }

        // POST /Tag/Delete/5 — remove a tag from the catalog. Existing flows
        // that already use this tag name are unaffected (flow_tags is its
        // own table); this only affects what shows up as a pick option.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            await _repo.DeleteAsync(id);
            return RedirectToAction(nameof(Index));
        }

        // POST /Tag/CreateApi — same as Create, but for the flow builder's
        // "+ Add Tag" picker to call via fetch() and get the saved tag back
        // as JSON, without leaving the builder page.
        [HttpPost]
        public async Task<IActionResult> CreateApi([FromBody] CreateTagViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.TagName))
                return BadRequest(new { message = "Tag name is required." });

            var tag = await _repo.GetOrCreateAsync(model.TagName.Trim());
            return Ok(new { id = tag.Id, tagName = tag.TagName });
        }
    }
}
