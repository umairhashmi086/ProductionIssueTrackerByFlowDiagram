using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Prod_IssueTracker_POC.Web.Services
{
    /// <summary>
    /// Renders a .cshtml view to a plain HTML string instead of writing it to
    /// the HTTP response — used to build the downloadable report file.
    /// Standard ASP.NET Core pattern; no new NuGet packages needed, these
    /// services are already registered by AddControllersWithViews().
    /// </summary>
    public class RazorViewRenderer
    {
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IServiceProvider _serviceProvider;

        public RazorViewRenderer(IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider)
        {
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _serviceProvider = serviceProvider;
        }

        public async Task<string> RenderViewToStringAsync(ControllerContext controllerContext, string viewName, object model)
        {
            var actionContext = controllerContext;

            var viewResult = _viewEngine.FindView(actionContext, viewName, isMainPage: true);
            if (!viewResult.Success)
                throw new InvalidOperationException($"View '{viewName}' not found. Searched: {string.Join(", ", viewResult.SearchedLocations)}");

            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };
            var tempData = new TempDataDictionary(actionContext.HttpContext, _tempDataProvider);

            await using var writer = new StringWriter();
            var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());

            await viewResult.View.RenderAsync(viewContext);
            return writer.ToString();
        }
    }
}
