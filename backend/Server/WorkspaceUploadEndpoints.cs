using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;

namespace Ras.Backend.Server;

/// <summary>Imports a complete browser-selected project directory into server-owned storage.</summary>
public static class WorkspaceUploadEndpoints
{
    private const long DefaultUploadLimit = 2L * 1024 * 1024 * 1024;
    private const int MaximumFiles = 20000;

    public static void MapWorkspaceUploads(this WebApplication app)
    {
        string root = app.Configuration["HECRAS_WORKSPACE_ROOT"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HEC-RAS 2025", "Workspaces");
        root = Path.GetFullPath(root);
        long limit = long.TryParse(app.Configuration["HECRAS_UPLOAD_LIMIT_BYTES"], out long configured)
            && configured > 0 ? configured : DefaultUploadLimit;

        app.MapPost("/api/workspaces", async (HttpContext context) =>
        {
            if (!context.Request.HasFormContentType)
                return Results.BadRequest(new { error = new { code = "invalidInput", message = "Upload project files as multipart form data." } });

            var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(root, id);
            bool created = false;
            try
            {
                IFormCollection form = await context.Request.ReadFormAsync(new FormOptions
                {
                    MultipartBodyLengthLimit = limit,
                    ValueLengthLimit = 4096,
                    MultipartHeadersLengthLimit = 16384,
                }, context.RequestAborted);
                if (form.Files.Count == 0 || form.Files.Count > MaximumFiles)
                    throw new InvalidDataException($"Select between 1 and {MaximumFiles} project files.");
                if (form.Files.Sum(file => file.Length) > limit)
                    throw new InvalidDataException("The upload exceeds the configured size limit.");

                Directory.CreateDirectory(directory);
                created = true;
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                int count = 0;

                if (form.Files.Count == 1 && form.Files[0].FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await using Stream input = form.Files[0].OpenReadStream();
                    using var archive = new ZipArchive(input, ZipArchiveMode.Read);
                    if (archive.Entries.Count > MaximumFiles)
                        throw new InvalidDataException("The archive contains too many entries.");
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        context.RequestAborted.ThrowIfCancellationRequested();
                        // Only regular files/directories are accepted; never create archive symlinks.
                        int fileType = (entry.ExternalAttributes >> 16) & 0xF000;
                        if (fileType != 0 && fileType != 0x8000 && fileType != 0x4000)
                            throw new InvalidDataException("The archive contains an unsupported link or special file.");
                        string relative = SafeRelativePath(entry.FullName.TrimEnd('/'));
                        if (entry.FullName.EndsWith('/')) continue;
                        if (!paths.Add(relative)) throw new InvalidDataException("The archive contains duplicate file paths.");
                        total = checked(total + entry.Length);
                        if (total > limit) throw new InvalidDataException("The expanded archive exceeds the configured size limit.");
                        await using Stream source = entry.Open();
                        await CopyFileAsync(source, directory, relative, entry.Length, context.RequestAborted);
                        count++;
                    }
                }
                else
                {
                    foreach (IFormFile file in form.Files)
                    {
                        string relative = SafeRelativePath(file.FileName);
                        if (!paths.Add(relative)) throw new InvalidDataException("The upload contains duplicate file paths.");
                        total = checked(total + file.Length);
                        if (total > limit) throw new InvalidDataException("The upload exceeds the configured size limit.");
                        await using Stream source = file.OpenReadStream();
                        await CopyFileAsync(source, directory, relative, file.Length, context.RequestAborted);
                        count++;
                    }
                }

                var projects = WorkspaceFiles(directory)
                    .Where(path => path.EndsWith(".ras", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal)
                    .Select(path => new { path, name = Path.GetFileNameWithoutExtension(path) }).ToArray();
                if (projects.Length == 0) throw new InvalidDataException("The selected files contain no .ras project.");
                return Results.Created($"/api/workspaces/{id}/archive", new
                {
                    workspaceId = id, rootPath = directory, projects, fileCount = count,
                });
            }
            catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or OverflowException)
            {
                if (created) DeleteFailedUpload(directory, app.Logger);
                return Results.BadRequest(new { error = new { code = "invalidUpload", message = error.Message } });
            }
            catch
            {
                if (created) DeleteFailedUpload(directory, app.Logger);
                throw;
            }
        });

        app.MapGet("/api/workspaces/{id}/archive", async (string id, HttpContext context) =>
        {
            if (!Guid.TryParseExact(id, "N", out Guid parsed) || parsed.ToString("N") != id)
                return Results.NotFound();
            string directory = Path.Combine(root, id);
            if (!Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null)
                return Results.NotFound();
            // ZIP entry finalization may write synchronously even through OpenAsync.
            // Complete the archive on a private file before streaming it over HTTP.
            string temporary = Path.Combine(Path.GetTempPath(), $"hecras-download-{Guid.NewGuid():N}.zip");
            var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            try
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await using (var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create,
                    leaveOpen: true, entryNameEncoding: null, cancellationToken: context.RequestAborted))
                {
                    foreach (string path in WorkspaceFiles(directory).Order(StringComparer.Ordinal))
                    {
                        context.RequestAborted.ThrowIfCancellationRequested();
                        string relative = Path.GetRelativePath(directory, path);
                        EnsureNoLinks(directory, relative);
                        ZipArchiveEntry entry = archive.CreateEntry(relative.Replace(Path.DirectorySeparatorChar, '/'), CompressionLevel.Fastest);
                        await using Stream destination = await entry.OpenAsync(context.RequestAborted);
                        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        await source.CopyToAsync(destination, context.RequestAborted);
                    }
                }
                output.Position = 0;
                return Results.File(output, "application/zip", $"HEC-RAS-{id}.zip");
            }
            catch
            {
                await output.DisposeAsync();
                throw;
            }
        });
    }

    private static string SafeRelativePath(string value)
    {
        string path = value.Replace('\\', '/');
        string[] parts = path.Split('/');
        if (parts.Length > 64 || parts.Any(part => string.IsNullOrEmpty(part) || part is "." or ".."
            || part.Length > 255 || part.Any(character => character < 32 || "<>:\"|?*".Contains(character))
            || part.EndsWith(' ') || part.EndsWith('.')
            || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new InvalidDataException("A project file has an invalid or unsafe relative path.");
        return Path.Combine(parts);
    }

    private static async Task CopyFileAsync(Stream source, string root, string relative, long length, CancellationToken cancellation)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureNoLinks(root, relative);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            copied = checked(copied + read);
            if (copied > length) throw new InvalidDataException("A project file exceeds its declared size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellation);
        }
        if (copied != length) throw new InvalidDataException("A project file was truncated during upload.");
    }

    private static void EnsureNoLinks(string root, string relative)
    {
        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (new FileInfo(current).LinkTarget is not null)
                throw new InvalidDataException("Project workspace links cannot be read or overwritten.");
        }
    }

    private static IEnumerable<string> WorkspaceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (new FileInfo(entry).LinkTarget is not null)
                    throw new InvalidDataException("Project workspace links cannot be read or overwritten.");
                if (Directory.Exists(entry)) pending.Push(entry);
                else yield return entry;
            }
        }
    }

    private static void DeleteFailedUpload(string directory, ILogger logger)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Could not remove incomplete project upload {Directory}", directory);
        }
    }
}
