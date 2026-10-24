using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Ras.Backend;
using Ras.Backend.Server;

if (args.Contains("--backend-worker", StringComparer.Ordinal))
    return await BackendWorker.RunAsync(["--duplex"]);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    string port = builder.Configuration["HECRAS_SERVER_PORT"] ?? "5080";
    if (!int.TryParse(port, out int parsedPort) || parsedPort is < 1 or > 65535)
        throw new InvalidOperationException("HECRAS_SERVER_PORT must be a valid TCP port.");
    builder.WebHost.UseUrls($"http://127.0.0.1:{parsedPort}");
}

string[] urls = (builder.Configuration["urls"] ?? "http://127.0.0.1:5080")
    .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
if (urls.Length == 0 || urls.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out Uri? address) ||
                                           (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
                                           !address.IsLoopback))
    throw new InvalidOperationException("HEC-RAS Server only accepts loopback URLs (127.0.0.1, ::1, or localhost).");

builder.Services.AddSingleton<OperationManager>();
builder.Services.AddHostedService(services => services.GetRequiredService<OperationManager>());

WebApplication app = builder.Build();
HashSet<string> developmentOrigins = ParseDevelopmentOrigins(builder.Configuration["HECRAS_DEV_ORIGINS"]);
app.Use(async (context, next) =>
{
    if (!IsLoopbackHost(context.Request.Host.Host) ||
        (context.Connection.RemoteIpAddress is IPAddress remoteAddress && !IPAddress.IsLoopback(remoteAddress)))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = new { code = "invalidHost", message = "The local server accepts loopback Host headers only." } }, BackendJson.Options, context.RequestAborted);
        return;
    }

    string requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
    string? origin = context.Request.Headers.Origin.FirstOrDefault();
    bool allowedOrigin = origin is not null && IsAllowedBrowserOrigin(origin, requestOrigin, developmentOrigins);
    if (IsMutation(context.Request.Method))
    {
        if (!string.IsNullOrWhiteSpace(origin) && !allowedOrigin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = new { code = "originDenied", message = "This browser origin is not authorized to modify the local server." } }, BackendJson.Options, context.RequestAborted);
            return;
        }
    }

    if (!string.IsNullOrWhiteSpace(origin) && allowedOrigin)
    {
        context.Response.Headers.AccessControlAllowOrigin = origin;
        context.Response.Headers.Vary = "Origin";
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
            context.Response.Headers.AccessControlAllowHeaders = "Content-Type";
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }
    }
    else if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next(context);
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ready", mode = "server" }));
app.MapGet("/api/runtime", () => Results.Ok(new
{
    mode = "server",
    runtime = RuntimeInformation.RuntimeIdentifier,
    operations = BackendWorker.OperationNames,
}));

app.MapPost("/api/operations", async (HttpContext context, OperationManager manager) =>
{
    OperationSubmission? submission;
    try
    {
        submission = await context.Request.ReadFromJsonAsync<OperationSubmission>(BackendJson.Options, context.RequestAborted);
    }
    catch (JsonException error)
    {
        return Results.BadRequest(new { error = new { code = "invalidInput", message = error.Message } });
    }
    if (submission is null || string.IsNullOrWhiteSpace(submission.Method) ||
        submission.Parameters.ValueKind != JsonValueKind.Object)
        return Results.BadRequest(new { error = new { code = "invalidInput", message = "Provide a method and an object-valued parameters field." } });
    if (!BackendWorker.OperationNames.Contains(submission.Method, StringComparer.Ordinal))
        return Results.BadRequest(new { error = new { code = "unsupported", message = "Unknown backend operation." } });

    if (!manager.TrySubmit(submission.Method, submission.Parameters, out string id))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    return Results.Accepted($"/api/operations/{id}", new { id });
});

app.MapGet("/api/operations/{id}", (string id, long? after, OperationManager manager) =>
{
    if (after is < 0) return Results.BadRequest(new { error = new { code = "invalidCursor", message = "The after cursor cannot be negative." } });
    OperationSnapshot? snapshot = manager.GetSnapshot(id, after ?? 0);
    if (snapshot is null) return Results.NotFound();
    if (snapshot.InvalidCursor)
        return Results.BadRequest(new { error = new { code = "invalidCursor", message = "The after cursor is ahead of the latest progress event." } });
    return Results.Ok(snapshot.Payload);
});

app.MapDelete("/api/operations/{id}", (string id, OperationManager manager) =>
{
    CancelRequestResult result = manager.RequestCancel(id);
    return result.Found ? Results.Ok(new { id, cancelRequested = result.CancelRequested }) : Results.NotFound();
});

app.MapPost("/api/operations/{id}/controls", async (string id, HttpContext context, OperationManager manager) =>
{
    ControlSubmission? submission;
    try
    {
        submission = await context.Request.ReadFromJsonAsync<ControlSubmission>(BackendJson.Options, context.RequestAborted);
    }
    catch (JsonException error)
    {
        return Results.BadRequest(new { error = new { code = "invalidInput", message = error.Message } });
    }
    if (submission is null || submission.Control.ValueKind != JsonValueKind.Object)
        return Results.BadRequest(new { error = new { code = "invalidInput", message = "The control value must be a JSON object." } });
    ControlSendResult result = manager.SendControl(id, submission.Control);
    return result switch
    {
        { Status: ControlSendStatus.NotFound } => Results.NotFound(),
        { Status: ControlSendStatus.Terminal } => Results.Conflict(new { error = new { code = "operationTerminal", message = "The operation has already finished." } }),
        { Status: ControlSendStatus.LimitReached } => Results.StatusCode(StatusCodes.Status429TooManyRequests),
        _ => Results.Accepted($"/api/operations/{id}", new { id, controlSequence = result.Sequence }),
    };
});

app.MapWorkspaceUploads();
app.MapSourceUploads();
app.MapGdbSourceUploads();

string? webRoot = ResolveWebRoot(builder.Configuration["HECRAS_WEB_ROOT"], app.Environment.ContentRootPath);
if (webRoot is not null)
{
    var provider = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = provider,
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
        ServeUnknownFileTypes = false,
    });
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api") ||
            (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
            Path.HasExtension(context.Request.Path.Value))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        IFileInfo index = provider.GetFileInfo("index.html");
        if (!index.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(index, context.RequestAborted);
    });
}

await app.StartAsync();
ICollection<string>? boundAddresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
    .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses;
if (boundAddresses is not null && boundAddresses.Any(address =>
        !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback))
{
    await app.StopAsync();
    throw new InvalidOperationException("The active HTTP server endpoint is not loopback; remote binding is disabled.");
}
await app.WaitForShutdownAsync();
return 0;

static bool IsMutation(string method) => HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
                                         HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

static bool IsLoopbackHost(string host) =>
    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
    (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));

static HashSet<string> ParseDevelopmentOrigins(string? configured)
{
    var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(configured)) return origins;
    foreach (string value in configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("HECRAS_DEV_ORIGINS entries must be explicit loopback origins such as http://127.0.0.1:5173.");
        origins.Add(uri.GetLeftPart(UriPartial.Authority));
    }
    return origins;
}

static bool IsAllowedBrowserOrigin(string origin, string requestOrigin, HashSet<string> developmentOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed) || !parsed.IsLoopback ||
        (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
        !string.IsNullOrEmpty(parsed.UserInfo) || parsed.AbsolutePath != "/" || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
        return false;
    return Uri.TryCreate(requestOrigin, UriKind.Absolute, out Uri? target) && SameOrigin(parsed, target) ||
           developmentOrigins.Contains(parsed.GetLeftPart(UriPartial.Authority));
}

static bool SameOrigin(Uri left, Uri right) =>
    string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

static string? ResolveWebRoot(string? configuredRoot, string contentRoot)
{
    if (!string.IsNullOrWhiteSpace(configuredRoot))
        return ValidateWebRoot(configuredRoot);
    if (File.Exists(Path.Combine(contentRoot, "index.html")))
        return Path.GetFullPath(contentRoot);

    var candidates = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
    foreach (string start in candidates)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(start));
        while (directory is not null)
        {
            bool repositoryRoot = File.Exists(Path.Combine(directory.FullName, "frontend", "index.html")) &&
                                  File.Exists(Path.Combine(directory.FullName, "backend", "Host", "Ras.Backend.Host.csproj"));
            if (repositoryRoot)
            {
                string dist = Path.Combine(directory.FullName, "dist");
                if (File.Exists(Path.Combine(dist, "index.html"))) return dist;
            }
            directory = directory.Parent;
        }
    }
    return null;
}

static string ValidateWebRoot(string path)
{
    string fullPath = Path.GetFullPath(path);
    if (!Directory.Exists(fullPath) || !File.Exists(Path.Combine(fullPath, "index.html")))
        throw new InvalidOperationException("HECRAS_WEB_ROOT must point to a built frontend directory containing index.html.");
    return fullPath;
}

internal sealed record OperationSubmission(string Method, JsonElement Parameters);
internal sealed record ControlSubmission(JsonElement Control);
