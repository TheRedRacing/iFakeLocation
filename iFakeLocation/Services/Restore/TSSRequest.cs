using System.Net.Http.Headers;
using System.Text;
using iFakeLocation.Interop;

namespace iFakeLocation.Services.Restore;

/// <summary>Wrapper for Apple's Tatsu Signing Server (TSS), used to authorize personalized (iOS 17+) image mounts.</summary>
internal sealed class TSSRequest {
    private const string TssControllerActionUrl = "http://gs.apple.com/TSS/controller?action=2";
    private const string TssClientVersionString = "libauthinstall-973.0.1";

    private readonly Dictionary<string, object?> _request = new() {
        {"@HostPlatformInfo", "mac"},
        {"@VersionInfo", TssClientVersionString},
        {"@UUID", Guid.NewGuid().ToString("D").ToUpperInvariant()},
    };

    public void Update(string key, object? value) {
        _request[key] = value;
    }

    public void Update(Dictionary<string, object> dict) {
        foreach (var kvp in dict) {
            _request[kvp.Key] = kvp.Value;
        }
    }

    public void ApplyRestoreRequestRules(Dictionary<string, object?> tssEntry, Dictionary<string, object?> parameters,
        IEnumerable<Dictionary<string, object?>> rules) {
        foreach (var rule in rules) {
            var conditionsFulfilled = true;
            foreach (var kvp in (Dictionary<string, object?>)rule["Conditions"]!) {
                if (!conditionsFulfilled)
                    break;
                object? value2 = kvp.Key switch {
                    "ApRawProductionMode" => parameters.GetValueOrDefault("ApProductionMode"),
                    "ApCurrentProductionMode" => parameters.GetValueOrDefault("ApProductionMode"),
                    "ApRawSecurityMode" => parameters.GetValueOrDefault("ApSecurityMode"),
                    "ApRequiresImage4" => parameters.GetValueOrDefault("ApSupportsImg4"),
                    "ApDemotionPolicyOverride" => parameters.GetValueOrDefault("DemotionPolicy"),
                    "ApInRomDFU" => parameters.GetValueOrDefault("ApInRomDFU"),
                    _ => null,
                };

                conditionsFulfilled = value2 != null && value2.Equals(kvp.Value);
            }

            if (!conditionsFulfilled)
                continue;

            foreach (var kvp in (Dictionary<string, object?>)rule["Actions"]!) {
                if (kvp.Value is null || !kvp.Value.GetType().IsValueType || kvp.Value.ToString() != "255") {
                    tssEntry[kvp.Key] = kvp.Value;
                }
            }
        }
    }

    public Dictionary<string, object?> SendAndReceive() {
        var handler = new HttpClientHandler {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("InetURL/1.0");
        client.DefaultRequestHeaders.Add("Expect", "");

        var plist = PlistHelper.ToPlistXml(_request);
        using var response = client.PostAsync(TssControllerActionUrl,
            new StringContent(plist, Encoding.UTF8, "text/xml")).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var responseMessage = responseBody.Split(["MESSAGE="], 2, StringSplitOptions.None)[1].Split('&')[0];
        if (responseMessage != "SUCCESS") {
            throw new Exception("TSS request received unexpected response: " + responseMessage);
        }

        var plistBody = responseBody.Split(["REQUEST_STRING="], 2, StringSplitOptions.None)[1];
        return PlistHelper.ReadPlistDictFromString(plistBody);
    }
}
