using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Checks GitHub's latest-release endpoint once at startup and reports
    /// back if it's newer than the running build. Entirely best-effort: no
    /// internet, a GitHub outage, rate limiting, or an unexpected response
    /// shape all just mean "no update found," never an error the user sees.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ReleasesApiUrl = "https://api.github.com/repos/CookieKwisp/RepoCosmeticsTracker/releases/latest";

        public record UpdateInfo(string Version, string Url);

        public static async Task<UpdateInfo?> CheckAsync(string currentVersion)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("RepoCosmeticTracker-UpdateChecker");
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using HttpResponseMessage response = await http.GetAsync(ReleasesApiUrl);
                if (!response.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("draft", out JsonElement draftEl) && draftEl.GetBoolean())
                    return null;
                if (root.TryGetProperty("prerelease", out JsonElement preEl) && preEl.GetBoolean())
                    return null;

                string? tag = root.TryGetProperty("tag_name", out JsonElement tagEl) ? tagEl.GetString() : null;
                string? url = root.TryGetProperty("html_url", out JsonElement urlEl) ? urlEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
                    return null;

                string latest = tag.TrimStart('v', 'V');
                return IsNewer(latest, currentVersion) ? new UpdateInfo(latest, url) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Compares dotted version strings component-by-component, tolerating different lengths (e.g. "1.1" vs "1.0.0.0").</summary>
        private static bool IsNewer(string latest, string current)
        {
            int[] latestParts = ParseVersionParts(latest);
            int[] currentParts = ParseVersionParts(current);
            int length = Math.Max(latestParts.Length, currentParts.Length);

            for (int i = 0; i < length; i++)
            {
                int l = i < latestParts.Length ? latestParts[i] : 0;
                int c = i < currentParts.Length ? currentParts[i] : 0;
                if (l != c)
                    return l > c;
            }
            return false;
        }

        private static int[] ParseVersionParts(string version) =>
            version.Split('.', '-', '+').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
    }
}
