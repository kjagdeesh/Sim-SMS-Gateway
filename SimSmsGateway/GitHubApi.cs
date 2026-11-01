using System.Net.Http.Headers;
using System.Text.Json;

namespace SimSmsGateway;

public sealed record GitHubRepoInfo(
    string? Description,
    string? Language,
    int Stars,
    int Forks,
    DateTimeOffset? PushedAt,
    string? License,
    IReadOnlyList<string> Topics);

public sealed record GitHubUserInfo(string? Name, string? Bio, string? Location, string? AvatarUrl);

/// <summary>Reads public project information from the GitHub REST API.</summary>
public static class GitHubApi
{
    public const string Owner = "kjagdeesh";
    public const string Repo = "Sim-SMS-Gateway";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimSmsGateway", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<GitHubRepoInfo?> GetRepoAsync()
    {
        using var document = await GetJsonAsync($"https://api.github.com/repos/{Owner}/{Repo}");
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        return new GitHubRepoInfo(
            GetString(root, "description"),
            GetString(root, "language"),
            root.TryGetProperty("stargazers_count", out var stars) ? stars.GetInt32() : 0,
            root.TryGetProperty("forks_count", out var forks) ? forks.GetInt32() : 0,
            DateTimeOffset.TryParse(GetString(root, "pushed_at"), out var pushed) ? pushed : null,
            root.TryGetProperty("license", out var license) && license.ValueKind == JsonValueKind.Object
                ? GetString(license, "spdx_id")
                : null,
            root.TryGetProperty("topics", out var topics) && topics.ValueKind == JsonValueKind.Array
                ? topics.EnumerateArray().Select(topic => topic.GetString()).OfType<string>().ToArray()
                : []);
    }

    public static async Task<GitHubUserInfo?> GetUserAsync()
    {
        using var document = await GetJsonAsync($"https://api.github.com/users/{Owner}");
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        return new GitHubUserInfo(
            GetString(root, "name"),
            GetString(root, "bio"),
            GetString(root, "location"),
            GetString(root, "avatar_url"));
    }

    /// <summary>Returns the README's introduction as plain text, without headings, badges or code.</summary>
    public static async Task<string?> GetReadmeSummaryAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/readme");
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var markdown = await response.Content.ReadAsStringAsync();
            var paragraphs = new List<string>();
            var inCode = false;
            foreach (var rawLine in markdown.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inCode = !inCode;
                    continue;
                }

                if (inCode || line.Length == 0 || line.StartsWith('#') || line.StartsWith('!') ||
                    line.StartsWith('[') || line.StartsWith('<') || line.StartsWith('|'))
                {
                    continue;
                }

                paragraphs.Add(line.Replace("**", string.Empty).Replace("`", string.Empty).TrimStart('-', '*', ' '));
                if (paragraphs.Count == 3)
                {
                    break;
                }
            }

            return paragraphs.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<JsonDocument?> GetJsonAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
