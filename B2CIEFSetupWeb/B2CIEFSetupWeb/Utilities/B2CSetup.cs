using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace B2CIEFSetupWeb.Utilities
{
    public interface IB2CSetup
    {
        Task<IEFApps> CreateIEFAppsAsync(string domainId);
        Task CreateKeysAsync();
    }
    public class B2CSetup: IB2CSetup
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly ILogger<B2CSetup> _logger;
        public B2CSetup(ILogger<B2CSetup> logger, ITokenAcquisition tokenAcquisition)
        {
            _logger = logger;
            _tokenAcquisition = tokenAcquisition;
        }
        public async Task<IEFApps> CreateIEFAppsAsync(string domainId)
        {
            var AppName = "IdentityExperienceFramework";
            var ProxyAppName = "ProxyIdentityExperienceFramework";
            var appIds = new IEFApps();

            var token = await _tokenAcquisition.GetAccessTokenOnBehalfOfUserAsync(Constants.Scopes, domainId);
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var json = await http.GetStringAsync("https://graph.microsoft.com/beta/domains");
            var value = (JArray)JObject.Parse(json)["value"];
            var domainName = ((JObject)value.First())["id"].Value<string>();

            var requiredAADAccess = new
            {
                resourceAppId = "00000002-0000-0000-c000-000000000000",
                resourceAccess = new List<object>()
                {
                    new {
                        id = "311a71cc-e848-46a1-bdf8-97ff7156d8e6",
                        type = "Scope"
                    }
                }
            };
            var iefApiPermission = new
            {
                adminConsentDescription = $"Allow the application to access {AppName} on behalf of the signed-in user.",
                adminConsentDisplayName = $"Access {AppName}",
                id = Guid.NewGuid().ToString("D"),
                isEnabled = true,
                type = "User",
                userConsentDescription = $"Allow the application to access {AppName} on your behalf.",
                userConsentDisplayName = $"Access {AppName}",
                value = "user_impersonation"
            };

            var app = new
            {
                isFallbackPublicClient = false,
                displayName = AppName,
                identifierUris = new List<string>() { $"https://{domainName}/{AppName}" },
                signInAudience = "AzureADMyOrg",
                api = new { oauth2PermissionScopes = new List<object> { iefApiPermission } },
                web = new
                {
                    redirectUris = new List<string>() { $"https://login.microsoftonline.com/{domainName}" },
                    homePageUrl = $"https://login.microsoftonline.com/{domainName}",
                    implicitGrantSettings = new
                    {
                        enableIdTokenIssuance = true,
                        enableAccessTokenIssuance = false
                    }
                }
            };

            json = JsonConvert.SerializeObject(app);
            var resp = await http.PostAsync($"https://graph.microsoft.com/beta/applications",
                new StringContent(json, Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var appJSON = JObject.Parse(body);
                appIds.AppId = (string)appJSON["appId"];
                var spId = Guid.NewGuid().ToString("D");
                var sp = new
                {
                    accountEnabled = true,
                    appId = appIds.AppId,
                    appRoleAssignmentRequired = false,
                    displayName = AppName,
                    homepage = $"https://login.microsoftonline.com/{domainName}",
                    replyUrls = new List<string>() { $"https://login.microsoftonline.com/{domainName}" },
                    servicePrincipalNames = new List<string>() {
                        app.identifierUris[0],
                        appIds.AppId
                    },
                    tags = new string[] { "WindowsAzureActiveDirectoryIntegratedApp" },
                };
                resp = await http.PostAsync($"https://graph.microsoft.com/beta/servicePrincipals",
                    new StringContent(JsonConvert.SerializeObject(sp), Encoding.UTF8, "application/json"));
                if (!resp.IsSuccessStatusCode) throw new Exception(resp.ReasonPhrase);
                _logger.LogInformation($"IEF App {AppName} created.");
            }

            var proxyApp = new
            {
                isFallbackPublicClient = true,
                displayName = ProxyAppName,
                signInAudience = "AzureADMyOrg",
                publicClient = new { redirectUris = new List<string>() { $"https://login.microsoftonline.com/{domainName}" } },
                parentalControlSettings = new { legalAgeGroupRule = "Allow" },
                requiredResourceAccess = new List<object>() {
                    new {
                        resourceAppId = appIds.AppId,
                        resourceAccess = new List<object>()
                        {
                            new {
                                id = iefApiPermission.id,
                                type = "Scope"
                            }
                        }
                    },
                    new {
                        resourceAppId = "00000002-0000-0000-c000-000000000000",
                        resourceAccess = new List<object>()
                        {
                            new
                            {
                                id = "311a71cc-e848-46a1-bdf8-97ff7156d8e6",
                                type = "Scope"
                            }
                        }
                    }
                },
                web = new
                {
                    implicitGrantSettings = new
                    {
                        enableIdTokenIssuance = true,
                        enableAccessTokenIssuance = false
                    }
                }
            };

            json = JsonConvert.SerializeObject(proxyApp);
            resp = await http.PostAsync($"https://graph.microsoft.com/beta/applications",
                new StringContent(json, Encoding.UTF8, "application/json"));
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var appJSON = JObject.Parse(body);
                appIds.ProxyAppId = (string)appJSON["appId"];
                var sp = new
                {
                    accountEnabled = true,
                    appId = appIds.ProxyAppId,
                    appRoleAssignmentRequired = false,
                    displayName = ProxyAppName,
                    //homepage = $"https://login.microsoftonline.com/{domainName}",
                    //publisherName = domainNamePrefix,
                    replyUrls = new List<string>() { $"https://login.microsoftonline.com/{domainName}" },
                    servicePrincipalNames = new List<string>() {
                        appIds.ProxyAppId
                    },
                    tags = new string[] { "WindowsAzureActiveDirectoryIntegratedApp" },
                };
                resp = await http.PostAsync($"https://graph.microsoft.com/beta/servicePrincipals",
                    new StringContent(JsonConvert.SerializeObject(sp), Encoding.UTF8, "application/json"));
                if (!resp.IsSuccessStatusCode) throw new Exception(resp.ReasonPhrase);
                //AdminConsentUrl = new Uri($"https://login.microsoftonline.com/{tokens.TenantId}/oauth2/authorize?client_id={appIds.ProxyAppId}&prompt=admin_consent&response_type=code&nonce=defaultNonce");
                _logger.LogInformation($"IEF App {ProxyAppName} created.");
            }
            return appIds;
        }
        private List<string> _keys;
        public async Task CreateKeysAsync()
        {
            await CreateKeyIfNotExistsAsync("TokenSigningKeyContainer", "sig");
            await CreateKeyIfNotExistsAsync("TokenEncryptionKeyContainer", "enc");
        }
        private async Task CreateKeyIfNotExistsAsync(string name, string use)
        {
            var token = await _tokenAcquisition.GetAccessTokenOnBehalfOfUserAsync(Constants.Scopes);
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (_keys == null)
            {
                var resp = await http.GetStringAsync("https://graph.microsoft.com/beta/trustFramework/keySets");
                var keys = (JArray)JObject.Parse(resp)["value"];
                _keys = keys.Select(k => k["id"].Value<string>()).ToList();
            }
            if (!_keys.Contains($"B2C_1A_{name}"))
            {
                var httpResp = await http.PostAsync("https://graph.microsoft.com/beta/trustFramework/keySets",
                    new StringContent(JsonConvert.SerializeObject(new { id = name }), Encoding.UTF8, "application/json"));
                if (httpResp.IsSuccessStatusCode)
                {
                    var keyset = await httpResp.Content.ReadAsStringAsync();
                    var id = JObject.Parse(keyset)["id"].Value<string>();
                    var key = new
                    {
                        use,
                        kty = "RSA"
                    };
                    httpResp = await http.PostAsync($"https://graph.microsoft.com/beta/trustFramework/keySets/{id}/generateKey",
                        new StringContent(JsonConvert.SerializeObject(key), Encoding.UTF8, "application/json"));
                    if (!httpResp.IsSuccessStatusCode)
                    {
                        await http.DeleteAsync($"https://graph.microsoft.com/beta/trustFramework/keySets/{id}");
                        throw new Exception(httpResp.ReasonPhrase);
                    }
                }
                else
                    throw new Exception(httpResp.ReasonPhrase);
            }

        }
    }

    public class IEFApps
    {
        public string AppId;
        public string ProxyAppId;
    }
}
