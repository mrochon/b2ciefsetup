using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using B2CIEFSetupWeb.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace B2CIEFSetupWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IAuthenticationService _authenticator;
        private readonly Utilities.IB2CSetup _setup;

        public HomeController(
            //Utilities.IB2CSetup setup,
            ILogger<HomeController> logger, 
            IAuthenticationService authenticator
            )
        {
            _logger = logger;
            _authenticator = authenticator;
            //_setup = setup;
        }

        public async Task<IActionResult> Index()
        {
            await _authenticator.ChallengeAsync(
                Request.HttpContext,
                "AzureADOpenID",
                new AuthenticationProperties(
                    new Dictionary<string, string>()
                    {
                        { ".redirect", "/home/setup" }
                    },
                    new Dictionary<string, object>()
                    {
                        {"tenant", "mrochonb2cprod" }
                    }));
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
        [Authorize]
        public async Task<IActionResult> Setup([FromServices] Utilities.B2CSetup setup, [FromServices] ITokenAcquisition tokenAcquisition)
        {
            var tenantId = User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/tenantid").Value;
            //var token = await tokenAcquisition.GetAccessTokenOnBehalfOfUserAsync(Constants.Scopes, tenantId);

            var res = await setup.SetupAsync(tenantId);
            var model = new List<ItemSetupState>();
            foreach(var item in res)
            {
                model.Add(new ItemSetupState()
                {
                    Name = item.Name,
                    Id = (String.IsNullOrEmpty(item.Id)? "-": item.Id),
                    Status = item.IsNew? "Created new": "Existed already"
                });
            }
            //AdminConsentUrl = new Uri($"https://login.microsoftonline.com/{tokens.TenantId}/oauth2/authorize?client_id={appIds.ProxyAppId}&prompt=admin_consent&response_type=code&nonce=defaultNonce");

            return View(model);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
