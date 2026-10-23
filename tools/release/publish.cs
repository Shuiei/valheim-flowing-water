// A release's steps that talk to GitHub's release notes and the mod sites, for CircleCI's release
// workflow (.circleci/continue.yml; a .NET file-based app: dotnet run --file tools/release/publish.cs -- ...;
// --file because the repository's root holds the plugin's project, which dotnet run would run instead).
// Run it from the repository's root: the sites' settings are in tools/release/sites.json.
// Adapted from the Valheim World Editor's tools/release/publish.cs (by way of Moder Warehouse Plus): one
// package here, the plugin.
//   notes <CHANGELOG.md> <version> <last tag, or -> <out.md> [<notes.md>]
//       The release notes: notes.md (docs/releases/v<version>.md) when it is there, else the CHANGELOG's
//       sections newer than the last released tag, up to version.
//   thunderstore|hexium <dist> [--dry-run]
//       Publishes FlowingWater-<version>.zip in dist (made by thunderstore/build.sh; the version
//       is its manifest's) to one site. --dry-run checks all it can without publishing: the token, that
//       the version is not on the site yet, the categories, the files (the release runs it for both sites
//       before it tags anything). A version already on the site fails: no site takes a version twice. On
//       Hexium, a version an earlier run submitted (hidden, so not seen as there) whose metadata it then
//       failed to set is taken as submitted: its metadata is set.
// The tokens come from the environment: THUNDERSTORE_TOKEN, HEXIUM_TOKEN.
// No Nexus Mods yet: the mod has no page there (made by hand on the website). Once it has one, its
// publishing comes from Moder Warehouse Plus's publish.cs (PublishNexus), with a nexus entry in sites.json.
#:property PublishAot=false
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const string Repo = "https://github.com/Shuiei/valheim-flowing-water";
const string SitesFile = "tools/release/sites.json";

try
{
    switch (args.FirstOrDefault())
    {
        case "notes" when args.Length is 5 or 6:
            File.WriteAllText(args[4], Notes(File.ReadAllText(args[1]), args[2], args[3], args.ElementAtOrDefault(5)), new UTF8Encoding(false));
            return 0;
        case "thunderstore" or "hexium" when args.Length >= 2:
            bool dryRun = args.AsSpan(2).Contains("--dry-run");
            var site = JsonNode.Parse(File.ReadAllText(SitesFile))![args[0]]!;
            var release = FindRelease(args[1]);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"flowing-water-release/{release.Version}");
                if (args[0] == "thunderstore")
                {
                    await PublishThunderstore(http, site, release, dryRun);
                }
                else
                {
                    await PublishHexium(http, site, release, dryRun);
                }
            }

            return 0;
        default:
            Console.Error.WriteLine("usage: publish.cs notes <CHANGELOG.md> <version> <last tag|-> <out.md> [notes.md]");
            Console.Error.WriteLine("       publish.cs thunderstore|hexium <dist> [--dry-run]");
            return 2;
    }
}
catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or IOException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// The notes command: notesFile's text when it exists, else CHANGELOG.md's sections after lastTag (v1.2.3, or "-"
// for none) up to version, newest first, as they are.
static string Notes(string changelog, string version, string lastTag, string? notesFile)
{
    if (notesFile is not null && File.Exists(notesFile))
    {
        return File.ReadAllText(notesFile);
    }

    var current = Version.Parse(version);
    Version? last = lastTag == "-" ? null : Version.Parse(lastTag.TrimStart('v'));
    var kept = new List<string>();
    foreach (string section in Regex.Split(changelog, "^(?=## )", RegexOptions.Multiline))
    {
        var m = Regex.Match(section, @"^## v?([0-9]+\.[0-9]+\.[0-9]+)\b");
        if (!m.Success)
        {
            continue;
        }

        var v = Version.Parse(m.Groups[1].Value);
        if (v <= current && (last is null || v > last))
        {
            kept.Add(section.Trim());
        }
    }

    if (kept.Count == 0)
    {
        throw new InvalidDataException($"CHANGELOG.md has no section for v{version}.");
    }

    return string.Join("\n\n", kept) + $"\n\nFull list: [CHANGELOG.md]({Repo}/blob/main/CHANGELOG.md).\n";
}

// The release's package in dist: the plugin's zip.
static Release FindRelease(string dist)
{
    string[] plugins = Directory.GetFiles(dist, "FlowingWater-*.zip");
    if (plugins.Length != 1)
    {
        throw new InvalidDataException($"{dist} must hold one FlowingWater-<version>.zip (it holds {plugins.Length}).");
    }

    JsonNode manifest;
    byte[] manifestBytes, icon;
    using (var zip = ZipFile.OpenRead(plugins[0]))
    {
        manifestBytes = ReadEntry(zip, "manifest.json");
        icon = ReadEntry(zip, "icon.png");
        manifest = JsonNode.Parse(manifestBytes)!;
    }

    string version = (string)manifest["version_number"]!;
    var release = new Release(version, (string)manifest["name"]!, plugins[0], manifestBytes, icon);
    Console.WriteLine($"{release.Name} {version}: {plugins[0]}");
    return release;
}

static byte[] ReadEntry(ZipArchive zip, string name)
{
    var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"The plugin zip has no {name}.");
    using var stream = entry.Open();
    using var copy = new MemoryStream();
    stream.CopyTo(copy);
    return copy.ToArray();
}

// ---- Thunderstore and Hexium (Hexium follows Thunderstore's API).

static async Task PublishThunderstore(HttpClient http, JsonNode site, Release release, bool dryRun)
{
    const string Name = "Thunderstore";
    string url = Text(site, "url"), team = Text(site, "team"), community = Text(site, "community");
    string[] categories = Texts(site, "categories");
    string token = Token("THUNDERSTORE_TOKEN");
    await NotThereYet(http, Name, $"{url}/api/experimental/package/{team}/{release.Name}/{release.Version}/");
    var user = await Call(http, Name, Get($"{url}/api/experimental/current-user/", Bearer(token)));
    if (user?["teams"]?.AsArray().Any(t => (string?)t == team) != true)
    {
        throw new InvalidOperationException($"{Name}: the token is not a service account of team {team}.");
    }

    await CategoriesExist(http, Name, $"{url}/api/experimental/community/{community}/category/", "slug", categories);
    // Thunderstore's own checks of the manifest and icon (its README check is broken on its side: always 500;
    // tools/thunderstore/package.cs checks the README by the same rules).
    var checks = new (string What, string Path, JsonObject Body)[]
    {
        ("manifest", "manifest-v1", new JsonObject { ["namespace"] = team, ["manifest_data"] = Convert.ToBase64String(release.Manifest) }),
        ("icon", "icon", new JsonObject { ["icon_data"] = Convert.ToBase64String(release.Icon) }),
    };
    foreach (var (what, path, body) in checks)
    {
        try
        {
            var answer = await Call(http, Name, Post($"{url}/api/experimental/submission/validate/{path}/", body, Bearer(token)));
            if (answer?["success"]?.GetValue<bool>() != true)
            {
                throw new InvalidOperationException($"{Name} refuses the package's {what}: {answer?.ToJsonString()}");
            }
        }
        catch (SiteException ex) when (ex.Status >= 500)
        {
            Console.WriteLine($"warning: {Name}'s {what} check is not working right now ({ex.Status}); not a verdict on the package.");
        }
    }

    Console.WriteLine($"{Name}: {team}-{release.Name} {release.Version} can be published (categories {string.Join(", ", categories)}).");
    if (dryRun)
    {
        return;
    }

    string upload = await Upload(http, Name, url, token, release.Plugin, tokenToParts: false);
    var result = await Submit(http, Name, url, token, upload, team, community, categories);
    Console.WriteLine($"{Name}: published {result?["package_version"]?["full_name"]}, {url}/c/{community}/p/{team}/{release.Name}/");
}

static async Task PublishHexium(HttpClient http, JsonNode site, Release release, bool dryRun)
{
    const string Name = "Hexium";
    string url = Text(site, "url"), team = Text(site, "team"), community = Text(site, "community"), install = Text(site, "installLocation");
    string[] categories = Texts(site, "categories");
    string token = Token("HEXIUM_TOKEN");
    // Hidden versions (every upload until it is unhidden on hexium.gg) do not show here: the upload is refused then.
    await NotThereYet(http, Name, $"{url}/api/experimental/package/{team}/{release.Name}/{release.Version}/");
    await CategoriesExist(http, Name, $"{url}/api/experimental/community/{community}/category/", "name", categories);
    if (dryRun)
    {
        // Hexium has no endpoint that only checks a token: an upload begun and called off checks it.
        var begun = await Call(http, Name, Post($"{url}/api/experimental/usermedia/initiate-upload/",
            new JsonObject { ["filename"] = Path.GetFileName(release.Plugin), ["file_size_bytes"] = new FileInfo(release.Plugin).Length }, Bearer(token)));
        await Call(http, Name, Post($"{url}/api/experimental/usermedia/{begun!["user_media"]!["uuid"]}/abort-upload/", new JsonObject(), Bearer(token)));
        Console.WriteLine($"{Name}: {team}-{release.Name} {release.Version} can be published (install location {install}, categories {string.Join(", ", categories)}).");
        return;
    }

    string upload = await Upload(http, Name, url, token, release.Plugin, tokenToParts: true);
    try
    {
        var result = await Submit(http, Name, url, token, upload, team, community, categories);
        Console.WriteLine($"{Name}: published {result?["package_version"]?["full_name"]} (hidden: unhide it on its Versions page on hexium.gg).");
    }
    catch (SiteException ex) when (ex.Status is >= 400 and < 500 && Says(ex.Answer, "already") && Says(ex.Answer, "version"))
    {
        // The site has this zip's name and version already (Thunderstore's words, which Hexium follows: "Package of
        // the same namespace, name and version already exists"), and NotThereYet did not see it: hidden, submitted by
        // an earlier run of this job that failed after (setting the metadata). It is this release's: on with the
        // metadata. The upload just made is not used.
        Console.WriteLine($"{Name}: {team}-{release.Name} {release.Version} was submitted already, by an earlier run (hidden: unhide it on its Versions page on hexium.gg); its metadata is set now. The site said: {ex.Answer}");
        try
        {
            await Call(http, Name, Post($"{url}/api/experimental/usermedia/{upload}/abort-upload/", new JsonObject(), Bearer(token)));
        }
        catch (Exception abort) when (abort is SiteException or HttpRequestException)
        {
            Console.WriteLine($"warning: {Name}: calling off the unused upload failed: {abort.Message}");
        }
    }

    var meta = new JsonObject { ["install_location"] = install, ["categories"] = new JsonArray([.. categories.Select(c => JsonValue.Create(c))]) };
    await Call(http, Name, Post($"{url}/api/mod/{team}/{release.Name}/set-metadata", meta, Bearer(token)));
    Console.WriteLine($"{Name}: install location {install}, categories {string.Join(", ", categories)}.");
}

// The version must not be on the site: its page answers 404.
static async Task NotThereYet(HttpClient http, string site, string url)
{
    if (await Call(http, site, Get(url), missingIsNull: true) is not null)
    {
        throw new InvalidOperationException($"{site}: this version is published already ({url}). Raise VERSION: no site takes a version twice.");
    }
}

static async Task CategoriesExist(HttpClient http, string site, string url, string key, string[] wanted)
{
    var known = new HashSet<string>();
    for (string? page = url; page is not null;)
    {
        var answer = await Call(http, site, Get(page));
        foreach (var category in answer!["results"]!.AsArray())
        {
            known.Add((string)category![key]!);
        }

        page = (string?)(answer["pagination"]?["next_link"] ?? answer["next"]);
    }

    string[] missing = [.. wanted.Where(c => !known.Contains(c))];
    if (missing.Length > 0)
    {
        throw new InvalidOperationException($"{site} has no category {string.Join(", ", missing)} (tools/release/sites.json).");
    }
}

// Uploads the zip in the parts the site asks for and returns the upload's id; an upload that fails is called off.
// tokenToParts: the token goes with each part sent to the site itself (Hexium's parts may be its own endpoint);
// never to a storage URL.
static async Task<string> Upload(HttpClient http, string site, string url, string token, string zip, bool tokenToParts)
{
    var begun = await Call(http, site, Post($"{url}/api/experimental/usermedia/initiate-upload/",
        new JsonObject { ["filename"] = Path.GetFileName(zip), ["file_size_bytes"] = new FileInfo(zip).Length }, Bearer(token)));
    string uuid = (string)begun!["user_media"]!["uuid"]!;
    try
    {
        var parts = new JsonArray();
        using (var file = File.OpenRead(zip))
        {
            foreach (var part in begun["upload_urls"]!.AsArray())
            {
                byte[] data = new byte[(int)part!["length"]!];
                file.Seek((long)part["offset"]!, SeekOrigin.Begin);
                file.ReadExactly(data);
                var target = new Uri(new Uri(url), (string)part["url"]!);
                using var put = new HttpRequestMessage(HttpMethod.Put, target) { Content = new ByteArrayContent(data) };
                if (tokenToParts && target.Host == new Uri(url).Host)
                {
                    put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using var answer = await http.SendAsync(put);
                if (!answer.IsSuccessStatusCode)
                {
                    throw new SiteException($"{site}: uploading part {part["part_number"]} failed ({(int)answer.StatusCode}).", (int)answer.StatusCode);
                }

                string etag = answer.Headers.TryGetValues("ETag", out var tags) ? tags.First() : "";
                parts.Add(new JsonObject { ["ETag"] = etag, ["PartNumber"] = (int)part["part_number"]! });
            }
        }

        await Call(http, site, Post($"{url}/api/experimental/usermedia/{uuid}/finish-upload/", new JsonObject { ["parts"] = parts }, Bearer(token)));
        return uuid;
    }
    catch (Exception ex) when (ex is SiteException or HttpRequestException or IOException)
    {
        try
        {
            await Call(http, site, Post($"{url}/api/experimental/usermedia/{uuid}/abort-upload/", new JsonObject(), Bearer(token)));
        }
        catch (Exception abort) when (abort is SiteException or HttpRequestException)
        {
            Console.WriteLine($"warning: {site}: calling off the upload failed: {abort.Message}");
        }

        throw;
    }
}

// Submits an uploaded zip: the site checks and publishes it before answering.
static async Task<JsonNode?> Submit(HttpClient http, string site, string url, string token, string upload, string team, string community, string[] categories)
{
    var body = new JsonObject
    {
        ["upload_uuid"] = upload,
        ["author_name"] = team,
        ["communities"] = new JsonArray(community),
        ["community_categories"] = new JsonObject { [community] = new JsonArray([.. categories.Select(c => JsonValue.Create(c))]) },
        ["has_nsfw_content"] = false,
    };
    return await Call(http, site, Post($"{url}/api/experimental/submission/submit/", body, Bearer(token)));
}

// ---- Requests.

static HttpRequestMessage Get(string url, params (string Name, string Value)[] headers) => Request(HttpMethod.Get, url, null, headers);

static HttpRequestMessage Post(string url, JsonNode body, params (string Name, string Value)[] headers) => Request(HttpMethod.Post, url, body, headers);

static HttpRequestMessage Request(HttpMethod method, string url, JsonNode? body, (string Name, string Value)[] headers)
{
    var request = new HttpRequestMessage(method, url);
    if (body is not null)
    {
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    foreach (var (name, value) in headers)
    {
        request.Headers.TryAddWithoutValidation(name, value);
    }

    return request;
}

static (string, string) Bearer(string token) => ("Authorization", $"Bearer {token}");

// Sends a request and returns the JSON it answers (null for none). An error answer throws SiteException with
// what the site said; missingIsNull: a 404 answers null instead.
static async Task<JsonNode?> Call(HttpClient http, string site, HttpRequestMessage request, bool missingIsNull = false)
{
    using (request)
    {
        using var answer = await http.SendAsync(request);
        string text = await answer.Content.ReadAsStringAsync();
        if (missingIsNull && answer.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!answer.IsSuccessStatusCode)
        {
            // The URL without its query: a presigned URL's query is a credential.
            string where = request.RequestUri!.GetLeftPart(UriPartial.Path);
            throw new SiteException($"{site} answered {(int)answer.StatusCode} to {request.Method} {where}: {Shorten(text)}", (int)answer.StatusCode, Shorten(text));
        }

        return text.Length == 0 ? null : JsonNode.Parse(text);
    }
}

static string Shorten(string text) => text.Length > 500 ? text[..500] + "…" : text;

static bool Says(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

static string Token(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } token
    ? token
    : throw new InvalidOperationException($"{name} is not set (CircleCI: the mod-token context).");

static string Text(JsonNode node, string key) => (string?)node[key] ?? throw new InvalidDataException($"tools/release/sites.json: no {key}.");

static string[] Texts(JsonNode node, string key) => [.. node[key]!.AsArray().Select(v => (string)v!)];

// One release's package: the plugin's zip, with its manifest and icon (for the sites' checks).
internal sealed record Release(string Version, string Name, string Plugin, byte[] Manifest, byte[] Icon);

// A site's error answer, with its HTTP status and what it said (shortened).
internal sealed class SiteException(string message, int status, string answer = "") : InvalidOperationException(message)
{
    public int Status { get; } = status;

    public string Answer { get; } = answer;
}
