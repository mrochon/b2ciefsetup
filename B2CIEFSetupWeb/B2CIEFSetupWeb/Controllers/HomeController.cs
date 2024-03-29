﻿using System;
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
using B2CIEFSetupWeb.Utilities;
using Microsoft.IdentityModel.Tokens;

namespace B2CIEFSetupWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IAuthenticationService _authenticator;

        public HomeController(
            ILogger<HomeController> logger, 
            IAuthenticationService authenticator
            )
        {
            _logger = logger;
            _authenticator = authenticator;
        }

        public IActionResult Index()
        {
            return View(new SetupRequest());
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(SetupRequest req)
        {
            //TODO: allow user to requets read scopes only (no app creation)
            await _authenticator.ChallengeAsync(
                Request.HttpContext,
                "OpenIdConnect",
                new AuthenticationProperties(
                    new Dictionary<string, string>()
                    {
                        { ".redirect", $"/home/setup?readOnly={req.ValidateOnly.ToString()}&fb={req.CreateDummyFacebook}" }
                    },
                    new Dictionary<string, object>()
                    {
                        {"tenant", req.DomainName },
                        {"readOnly", req.ValidateOnly }
                        //{"admin_consent", true }
                    }));
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
        [Authorize]
        public async Task<IActionResult> Setup([FromServices] Utilities.B2CSetup setup)
        {
            var readOnlyStr = Request.Query["readOnly"].First();
            bool readOnly = true;
            bool.TryParse(readOnlyStr, out readOnly);
            var fbStr = Request.Query["fb"].First();
            bool fb = true;
            bool.TryParse(fbStr, out fb);

            var tenantId = User.Claims.First(c => c.Type == "http://schemas.microsoft.com/identity/claims/tenantid").Value;
            //var token = await tokenAcquisition.GetAccessTokenOnBehalfOfUserAsync(Constants.Scopes, tenantId);

            var res = await setup.SetupAsync(tenantId, readOnly, fb);
            var model = new SetupState();
            foreach(var item in res)
            {
                model.Items.Add(new ItemSetupState()
                {
                    Name = item.Name,
                    Id = (String.IsNullOrEmpty(item.Id)? "-": item.Id),
                    Status = item.Status == IEFObject.S.Existing? "Existing": item.Status == IEFObject.S.New ? "New": "Not found"
                });
            }
            model.ConsentUrl = $"https://login.microsoftonline.com/{tenantId}/adminconsent?client_id={res[1].Id}";

            _logger.LogInformation($"Update?: {!readOnly}; Modified?: {model.Items.Exists(item => item.Status == "New")}; Name?: {tenantId}");

            return View(model);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error(string msg = "")
        {
            _logger.LogError($"Error reported{Base64UrlEncoder.Decode(msg)}");
            return View(
                new ErrorViewModel 
                { 
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier ,
                    Message = Base64UrlEncoder.Decode(msg)
                });
        }
    }
}
