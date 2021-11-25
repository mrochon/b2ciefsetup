using B2CIEFSetupWeb.Models;
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
    public class IEFObject
    {
        public enum S { New, Existing, NotFound }
        public string Name;
        public string Id;
        public S Status;
    }
    public interface IB2CSetup
    {
        Task<List<IEFObject>> SetupAsync(string domainId, bool readOnly, bool dummyFB);
    }
    public class B2CSetup : IB2CSetup
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly ILogger<B2CSetup> _logger;
        private HttpClient _http;
        public B2CSetup(ILogger<B2CSetup> logger, ITokenAcquisition tokenAcquisition)
        {
            _logger = logger;
            _tokenAcquisition = tokenAcquisition;
        }
        public string DomainName { get; private set; }
        public string TenantName { get; set; }
        private bool _readOnly = false;
        public async Task<List<IEFObject>> SetupAsync(string domainId, bool readOnly, bool dummyFB)
        {
            using (_logger.BeginScope("SetupAsync: {0} - Read only: {1}", domainId, readOnly))
            {
                _readOnly = readOnly;
                try
                {
                    var token = await _tokenAcquisition.GetAccessTokenForUserAsync(
                        readOnly ? Constants.ReadOnlyScopes : Constants.ReadWriteScopes,
                        tenantId:domainId);
                    _http = new HttpClient();
                    _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                    _actions = new List<IEFObject>();
                    await SetupIEFAppsAsync(domainId);
                    await SetupKeysAsync();
                    var extAppId = await GetAppIdAsync("b2c-extensions-app");
                    _actions.Add(new IEFObject()
                    {
                        Name = "Extensions app: appId",
                        Id = extAppId,
                        Status = String.IsNullOrEmpty(extAppId) ? IEFObject.S.NotFound : IEFObject.S.Existing
                    });
                    extAppId = await GetAppIdAsync("b2c-extensions-app", true);
                    _actions.Add(new IEFObject()
                    {
                        Name = "Extensions app: objectId",
                        Id = extAppId,
                        Status = String.IsNullOrEmpty(extAppId) ? IEFObject.S.NotFound : IEFObject.S.Existing
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"SetupAsync failed: {ex.Message}");
                }
            }
            return _actions;
        }
        public List<IEFObject> _actions;
        public SetupState _state;

        private async Task SetupIEFAppsAsync(string domainId)
        {
            var AppName = "IdentityExperienceFramework";
            var ProxyAppName = "ProxyIdentityExperienceFramework";

            var json = await _http.GetStringAsync("https://graph.microsoft.com/v1.0/domains");
            var value = (JArray)JObject.Parse(json)["value"];
            DomainName = ((JObject)value.First())["id"].Value<string>();
            _logger.LogTrace("Domain: {0}", DomainName);
            TenantName = DomainName.Split('.')[0];
            //TODO: needs refactoring

            _actions.Add(new IEFObject()
            {
                Name = AppName,
                Status = IEFObject.S.NotFound
            });
            _actions[0].Id = await GetAppIdAsync(_actions[0].Name);
            if (!String.IsNullOrEmpty(_actions[0].Id)) _actions[0].Status = IEFObject.S.Existing;
            _actions.Add(new IEFObject()
            {
                Name = ProxyAppName,
                Status = IEFObject.S.NotFound
            });
            _actions[1].Id = await GetAppIdAsync(_actions[1].Name);
            if (!String.IsNullOrEmpty(_actions[1].Id)) _actions[1].Status = IEFObject.S.Existing;

            if (!String.IsNullOrEmpty(_actions[0].Id) && !String.IsNullOrEmpty(_actions[1].Id)) return; // Sorry! What if only one exists?
            //TODO: should verify whether the two apps are setup correctly
            if (_readOnly) return;

            // OIDC/signin permissions
            var OIDCAccess = new
            {
                resourceAppId = "00000003-0000-0000-c000-000000000000",
                resourceAccess = new List<object>()
                {
                    new
                    {
                        id = "37f7f235-527c-4136-accd-4a02d197296e",
                        type = "Scope"
                    },
                    new
                    {
                        id = "7427e0e9-2fba-42fe-b0c0-848c9e6a8182",
                        type = "Scope"
                    }
                }
            };

            // Create IEF App
            var app = new
            {
                displayName = AppName,
                signInAudience = "AzureADMyOrg",
                web = new
                {
                    redirectUris = new List<string>() { $"https://{TenantName}.b2clogin.com/{DomainName}" }
                },
                requiredResourceAccess = new List<object>() { OIDCAccess }
            };
            var iefAppPermissionId = Guid.NewGuid().ToString("D");
            json = JsonConvert.SerializeObject(app);
            var resp = await _http.PostAsync($"https://graph.microsoft.com/v1.0/applications",
                new StringContent(json, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
                throw new Exception(resp.ReasonPhrase);

            _logger.LogTrace("{0} application created", AppName);
            var body = await resp.Content.ReadAsStringAsync();
            var appJSON = JObject.Parse(body);
            _actions[0].Id = (string)appJSON["appId"];
            _actions[0].Status = IEFObject.S.New;
            var appObjectId = (string)appJSON["id"];

            var iefApiPermission = new
            {
                adminConsentDescription = $"Allow the application to access {AppName} on behalf of the signed-in user.",
                adminConsentDisplayName = $"Access {AppName}",
                id = iefAppPermissionId,
                isEnabled = true,
                type = "Admin",
                value = "user_impersonation"
            };
            // Expose API permission
            json = JsonConvert.SerializeObject(new { identifierUris = new List<string>() { $"https://{DomainName}/{_actions[0].Id}" }, api = new { oauth2PermissionScopes = new List<object> { iefApiPermission } } });
            resp = await _http.PatchAsync($"https://graph.microsoft.com/v1.0/applications/{appObjectId}",
                new StringContent(json, Encoding.UTF8, "application/json"));

            // Create SP
            if (!resp.IsSuccessStatusCode)
                throw new Exception(resp.ReasonPhrase);

            var sp = new
            {
                appId = _actions[0].Id,
                displayName = AppName
            };
            resp = await _http.PostAsync($"https://graph.microsoft.com/v1.0/servicePrincipals",
                new StringContent(JsonConvert.SerializeObject(sp), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) throw new Exception(resp.ReasonPhrase);
            _logger.LogTrace("{0} SP created", AppName);


            // Create Poxy IEF
            var proxyApp = new
            {
                displayName = ProxyAppName,
                signInAudience = "AzureADMyOrg",
                publicClient = new { redirectUris = new List<string>() { "myapp://auth" } },
                isFallbackPublicClient = true,
                requiredResourceAccess = new List<object>() 
                {
                    new 
                    {
                        resourceAppId = _actions[0].Id,
                        resourceAccess = new List<object>()
                        {
                            new 
                            {
                                id = iefAppPermissionId,
                                type = "Scope"
                            }
                        }
                    },
                    OIDCAccess
                }
            };
            json = JsonConvert.SerializeObject(proxyApp);
            resp = await _http.PostAsync($"https://graph.microsoft.com/v1.0/applications",
                new StringContent(json, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
                throw new Exception(resp.ReasonPhrase);

            _logger.LogTrace("{0} app created", ProxyAppName);
            body = await resp.Content.ReadAsStringAsync();
            appJSON = JObject.Parse(body);
            _actions[1].Id = (string)appJSON["appId"];
            _actions[1].Status = IEFObject.S.New;
            var spProxy = new
            {
                appId = _actions[1].Id,
                displayName = ProxyAppName,
                servicePrincipalNames = new List<string>() {
                    _actions[1].Id
                }
            };
            resp = await _http.PostAsync($"https://graph.microsoft.com/v1.0/servicePrincipals",
                new StringContent(JsonConvert.SerializeObject(spProxy), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) throw new Exception(resp.ReasonPhrase);
            //AdminConsentUrl = new Uri($"https://login.microsoftonline.com/{tokens.TenantId}/oauth2/authorize?client_id={appIds.ProxyAppId}&prompt=admin_consent&response_type=code&nonce=defaultNonce");
            _logger.LogTrace("{0} SP created", ProxyAppName);

            return;
        }
        private List<string> _keys;
        private async Task SetupKeysAsync()
        {
            await CreateKeyIfNotExistsAsync("TokenSigningKeyContainer", "sig");
            await CreateKeyIfNotExistsAsync("TokenEncryptionKeyContainer", "enc");
            await CreateKeyIfNotExistsAsync("FacebookSecret", "sig");
        }
        private async Task CreateKeyIfNotExistsAsync(string name, string use)
        {
            var keySetupState = new IEFObject() { Name = name, Status = IEFObject.S.NotFound };
            _actions.Add(keySetupState);
            if (_keys == null)
            {
                var resp = await _http.GetStringAsync("https://graph.microsoft.com/beta/trustFramework/keySets");
                var keys = (JArray)JObject.Parse(resp)["value"];
                _keys = keys.Select(k => k["id"].Value<string>()).ToList();
            }
            var kName = $"B2C_1A_{name}";
            if (_keys.Contains($"B2C_1A_{name}"))
            {
                keySetupState.Status = IEFObject.S.Existing;
                if (_readOnly) return;
            }
            else
            {
                if (_readOnly) return;
                var httpResp = await _http.PostAsync("https://graph.microsoft.com/beta/trustFramework/keySets",
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
                    httpResp = await _http.PostAsync($"https://graph.microsoft.com/beta/trustFramework/keySets/{id}/generateKey",
                        new StringContent(JsonConvert.SerializeObject(key), Encoding.UTF8, "application/json"));
                    if (!httpResp.IsSuccessStatusCode)
                    {
                        await _http.DeleteAsync($"https://graph.microsoft.com/beta/trustFramework/keySets/{id}");
                        throw new Exception(httpResp.ReasonPhrase);
                    }
                    keySetupState.Status = IEFObject.S.New;
                }
                else
                    throw new Exception(httpResp.ReasonPhrase);
            }
        }
        private async Task<string> GetAppIdAsync(string name, bool getObjectId = false)
        {
            var json = await _http.GetStringAsync($"https://graph.microsoft.com/v1.0/applications?$filter=startsWith(displayName,\'{name}\')");
            var value = (JArray)JObject.Parse(json)["value"];
            //TODO: what if someone created several apps?
            if (value.Count > 0)
            {
                if (getObjectId)
                    return ((JObject)value.First())["id"].Value<string>();
                else
                    return ((JObject)value.First())["appId"].Value<string>();
            }
            return String.Empty;
        }
    }
}
