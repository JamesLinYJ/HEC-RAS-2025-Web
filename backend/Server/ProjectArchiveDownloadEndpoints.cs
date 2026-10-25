using Ras.Backend;

namespace Ras.Backend.Server;

internal static class ProjectArchiveDownloadEndpoints
{
    public static void MapProjectArchiveDownloads(this WebApplication app)
    {
        app.MapGet("/api/project-archives/{token}", (string token, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            FileStream? archive = DeveloperProjectArchiveService.OpenCompletedArchive(token);
            return archive is null ? Results.NotFound() : Results.File(archive,
                "application/zip", "project-" + token + ".zip", enableRangeProcessing: false);
        });
    }
}
