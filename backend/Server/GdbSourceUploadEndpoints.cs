using Microsoft.AspNetCore.Http.Features;
using Ras.Backend;

namespace Ras.Backend.Server;

public static class GdbSourceUploadEndpoints
{
    private const int MaximumFileCount = 4096;
    private const int MaximumRelativePathLength = 4096;
    private const int MaximumPathDepth = 64;
    private const long DefaultUploadLimit = 2L * 1024 * 1024 * 1024;

    private sealed record SelectedFile(IFormFile File, string RelativePath, string[] Segments);
    private sealed record UploadedSource(string SourcePath, string SourceCanonicalPath,
        string SourceRevision, string Kind, string FileName);

    private sealed class PathNode(string name)
    {
        public string Name { get; } = name;
        public bool IsFile { get; set; }
        public Dictionary<string, PathNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static void MapGdbSourceUploads(this WebApplication app)
    {
        app.MapPost("/api/gdb-source-uploads", async (HttpContext context) =>
        {
            string? staging = null;
            try
            {
                if (!context.Request.HasFormContentType)
                    throw new ArgumentException("Select a file geodatabase as multipart form data.");

                long limit = long.TryParse(app.Configuration["HECRAS_UPLOAD_LIMIT_BYTES"], out long configured) && configured > 0
                    ? configured : DefaultUploadLimit;
                var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
                IFormCollection form = await context.Request.ReadFormAsync(new FormOptions
                {
                    MultipartBodyLengthLimit = limit,
                    ValueLengthLimit = 16384,
                }, context.RequestAborted);

                ProjectSourceUploadTarget target = ProjectSourceUploadService.Prepare(
                    form["projectPath"].ToString(), form["projectRevision"].ToString(),
                    form["projectSessionID"].ToString());
                SelectedFile[] files = ValidateFiles(form.Files, limit, out string gdbDirectoryName);

                ProjectSourceUploadService.Verify(target);
                Directory.CreateDirectory(target.SourcesRoot);
                ProjectSourceUploadService.Verify(target);

                string uploadID = Guid.NewGuid().ToString("N");
                staging = Path.Combine(target.SourcesRoot, "." + uploadID + ".partial");
                string destination = Path.Combine(target.SourcesRoot, uploadID);
                if (File.Exists(staging) || Directory.Exists(staging) ||
                    File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("The upload destination already exists.");
                Directory.CreateDirectory(staging);
                SetPrivateDirectoryMode(staging);

                foreach (SelectedFile file in files)
                {
                    string targetPath = Path.Combine(staging,
                        file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    string parent = Path.GetDirectoryName(targetPath)!;
                    CreatePrivateDirectory(parent);
                    await using Stream input = file.File.OpenReadStream();
                    await using var output = new FileStream(targetPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, context.RequestAborted);
                    if (output.Length != file.File.Length)
                        throw new InvalidDataException("An uploaded GDB file length changed.");
                }

                string stagedGdbPath = Path.Combine(staging, gdbDirectoryName);
                _ = GdbMapSourceService.CaptureSource(stagedGdbPath);
                context.RequestAborted.ThrowIfCancellationRequested();
                ProjectSourceUploadService.Verify(target);
                Directory.Move(staging, destination);
                staging = null;

                string sourcePath = Path.Combine(destination, gdbDirectoryName);
                GdbMapSourceIdentity captured = GdbMapSourceService.CaptureSource(sourcePath);
                string canonicalPath = captured.SourcePath;
                var sources = new[]
                {
                    new UploadedSource(canonicalPath, canonicalPath, captured.Revision, "gdb", gdbDirectoryName),
                };
                return Results.Created("/api/gdb-source-uploads/" + uploadID, new
                {
                    uploadID,
                    target.ProjectPath,
                    target.ProjectRevision,
                    target.ProjectSessionID,
                    sources,
                });
            }
            catch (SourceFileChangedException error)
            {
                return Results.Conflict(new { error = new { code = "sourceChanged", message = error.Message } });
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or
                                           OverflowException or System.Xml.XmlException)
            {
                return Results.BadRequest(new { error = new { code = "invalidInput", message = error.Message } });
            }
            finally
            {
                if (staging is not null && Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
        });
    }

    private static SelectedFile[] ValidateFiles(IFormFileCollection files, long limit,
        out string gdbDirectoryName)
    {
        if (files.Count is < 1 or > MaximumFileCount)
            throw new InvalidDataException($"Select between 1 and {MaximumFileCount} files from one file geodatabase.");

        long totalBytes = 0;
        var selected = new SelectedFile[files.Count];
        for (int index = 0; index < files.Count; index++)
        {
            IFormFile file = files[index];
            totalBytes = checked(totalBytes + file.Length);
            if (file.Length < 0 || totalBytes > limit)
                throw new InvalidDataException("The GDB upload exceeds the configured size limit.");

            string relativePath = file.FileName;
            if (string.IsNullOrEmpty(relativePath) || relativePath.Length > MaximumRelativePathLength ||
                Path.IsPathFullyQualified(relativePath) || relativePath.StartsWith('/') ||
                relativePath.StartsWith('\\') || relativePath.Contains('\\'))
                throw new InvalidDataException("GDB files must use relative webkitRelativePath names.");

            string[] segments = relativePath.Split('/');
            if (segments.Length is < 2 or > MaximumPathDepth || segments.Any(segment => segment.Length == 0))
                throw new InvalidDataException("GDB paths must contain a root directory and stay within the supported depth.");
            foreach (string segment in segments)
                SourceUploadNames.ValidatePortableSegment(segment,
                    "GDB paths must use portable directory and file names.",
                    "A GDB path contains a name reserved by supported desktop platforms.");

            selected[index] = new(file, relativePath, segments);
        }

        gdbDirectoryName = selected[0].Segments[0];
        if (!gdbDirectoryName.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select files from one .gdb directory.");
        foreach (SelectedFile file in selected)
        {
            if (!string.Equals(file.Segments[0], gdbDirectoryName, StringComparison.Ordinal))
                throw new InvalidDataException("Select files from exactly one .gdb root directory.");
            if (file.Segments.Skip(1).Take(file.Segments.Length - 2)
                .Any(segment => segment.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A GDB upload cannot contain a second .gdb root directory.");
        }

        ValidateNoPathCollisions(selected);
        return selected;
    }

    private static void ValidateNoPathCollisions(SelectedFile[] files)
    {
        var root = new PathNode(string.Empty);
        foreach (SelectedFile file in files)
        {
            PathNode current = root;
            for (int index = 0; index < file.Segments.Length; index++)
            {
                if (current.IsFile)
                    throw new InvalidDataException("The GDB upload contains a file and directory with the same path.");

                string segment = file.Segments[index];
                if (!current.Children.TryGetValue(segment, out PathNode? child))
                {
                    child = new(segment);
                    current.Children.Add(segment, child);
                }
                else if (!string.Equals(child.Name, segment, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The GDB upload contains names that differ only by letter case.");
                }

                bool isLeaf = index == file.Segments.Length - 1;
                if (!isLeaf)
                {
                    if (child.IsFile)
                        throw new InvalidDataException("The GDB upload contains a file and directory with the same path.");
                    current = child;
                    continue;
                }

                if (child.IsFile)
                    throw new InvalidDataException("The GDB upload contains duplicate file paths.");
                if (child.Children.Count > 0)
                    throw new InvalidDataException("The GDB upload contains a file and directory with the same path.");
                child.IsFile = true;
            }
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        SetPrivateDirectoryMode(path);
    }

    private static void SetPrivateDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
