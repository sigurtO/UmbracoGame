using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Cms.Web.Website.Controllers;
using UmbracoGame.Models;

namespace UmbracoGame.Controllers
{
    public class MemberAuthController : SurfaceController
    {
        private readonly IMemberSignInManager _memberSignInManager;
        private readonly IMemberManager _memberManager;

        public MemberAuthController(
            IUmbracoContextAccessor umbracoContextAccessor,
            IUmbracoDatabaseFactory databaseFactory,
            ServiceContext services,
            AppCaches appCaches,
            IProfilingLogger profilingLogger,
            IPublishedUrlProvider publishedUrlProvider,
            IMemberSignInManager memberSignInManager,
            IMemberManager memberManager)
            : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
        {
            _memberSignInManager = memberSignInManager;
            _memberManager = memberManager;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HandleLogin(LoginViewModel loginModel)
        {
            // 1. Check if the user left the fields blank
            if (!ModelState.IsValid)
            {
                return CurrentUmbracoPage();
            }

            // 2. Try to log them in
            var result = await _memberSignInManager.PasswordSignInAsync(loginModel.Username, loginModel.Password, false, false);

            if (result.Succeeded)
            {
                // 3. Success! Send them to the homepage
                return Redirect("/");
            }

            // 4. Failure! Send an error message back to the UI
            ModelState.AddModelError(string.Empty, "Access Denied. Incorrect username or password.");
            return CurrentUmbracoPage();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HandleRegister(RegisterViewModel registerModel)
        {
            if (!ModelState.IsValid) return CurrentUmbracoPage();

            // 1. Check if the Username is already taken! (Crucial for a game)
            if (await _memberManager.FindByNameAsync(registerModel.Username) != null)
            {
                ModelState.AddModelError(string.Empty, "That username is already taken. Choose another.");
                return CurrentUmbracoPage();
            }

            // 2. Check if the Email is already used
            if (await _memberManager.FindByEmailAsync(registerModel.Email) != null)
            {
                ModelState.AddModelError(string.Empty, "An account with this email already exists.");
                return CurrentUmbracoPage();
            }

            // 3. Create the Identity User payload
            var identityUser = MemberIdentityUser.CreateNew(
                registerModel.Username, // Login Username
                registerModel.Email,    // Email Address
                "Member",
                true,
                registerModel.Username  // Display Name same as username
            );

            var createResult = await _memberManager.CreateAsync(identityUser, registerModel.Password);

            if (createResult.Succeeded)
            {

                // Log user in after register
                await _memberSignInManager.PasswordSignInAsync(registerModel.Username, registerModel.Password, false, false);
                return Redirect("/");
            }
            else
            {
                foreach (var error in createResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return CurrentUmbracoPage();
            }
        }

        [HttpGet]
        public async Task<IActionResult> HandleLogout()
        {
            await _memberSignInManager.SignOutAsync();
            return Redirect("/");
        }
    }
}