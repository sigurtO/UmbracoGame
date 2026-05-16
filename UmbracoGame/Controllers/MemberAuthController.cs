using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Models;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Cms.Web.Website.Controllers;
using Umbraco.Cms.Web.Website.Models;

namespace UmbracoGame.Controllers
{
    public class MemberAuthController : SurfaceController
    {
        private readonly IMemberSignInManager _memberSignInManager;

        // Dependency Injection: Grabbing the tools needed to authenticate against the SQLite DB
        public MemberAuthController(
            IUmbracoContextAccessor umbracoContextAccessor,
            IUmbracoDatabaseFactory databaseFactory,
            ServiceContext services,
            AppCaches appCaches,
            IProfilingLogger profilingLogger,
            IPublishedUrlProvider publishedUrlProvider,
            IMemberSignInManager memberSignInManager)
            : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
        {
            _memberSignInManager = memberSignInManager;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HandleLogin(LoginModel model)
        {
            if (!ModelState.IsValid) return CurrentUmbracoPage();

            // Attempt the login
            var result = await _memberSignInManager.PasswordSignInAsync(model.Username, model.Password, model.RememberMe, false);

            if (result.Succeeded)
            {
                // On success, redirect back to the home page (the Arena)
                return Redirect("/");
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt. Check your credentials.");
            return CurrentUmbracoPage();
        }

        [HttpGet]
        public async Task<IActionResult> HandleLogout()
        {
            // Nuke the session and redirect to home
            await _memberSignInManager.SignOutAsync();
            return Redirect("/");
        }
    }
}