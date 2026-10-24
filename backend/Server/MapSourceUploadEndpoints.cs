using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Features;
using Ras.Backend;

namespace Ras.Backend.Server;

public static class MapSourceUploadEndpoints
{
    private sealed record SelectedFile(IFormFile File, string StoredName, string Extension, string Stem);
    private sealed record UploadedSource(string SourcePath, string SourceCanonicalPath, string SourceRevision, string Kind, string FileName);

    public static void MapSourceUploads(this WebApplication app)
    {
        app.MapPost("/api/source-uploads", async (HttpContext context) =>
        {
            string? staging = null;
            try
            {
                if (!context.Request.HasFormContentType) throw new ArgumentException("Select map source files as multipart form data.");
                long limit = long.TryParse(app.Configuration["HECRAS_UPLOAD_LIMIT_BYTES"], out long configured) && configured > 0
                    ? configured : 2L * 1024 * 1024 * 1024;
                var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limit;
                IFormCollection form = await context.Request.ReadFormAsync(new FormOptions
                {
                    MultipartBodyLengthLimit = limit, ValueLengthLimit = 16384,
                }, context.RequestAborted);
                ProjectSourceUploadTarget target = ProjectSourceUploadService.Prepare(
                    form["projectPath"].ToString(), form["projectRevision"].ToString(), form["projectSessionID"].ToString());
                if (form.Files.Count is < 1 or > 256 || form.Files.Sum(file => file.Length) > limit)
                    throw new InvalidDataException("Select at most 256 map source files within the configured upload size limit.");

                SelectedFile[] selected = form.Files.Select(ValidateFile).ToArray();
                // Sidecar case varies in Windows-origin families. Keep the .shp
                // stem and normalize only copied file names, never source bytes.
                selected = selected.Select(file =>
                {
                    SelectedFile? main = file.Extension is ".shx" or ".dbf" or ".prj"
                        ? selected.FirstOrDefault(item => item.Extension == ".shp" && item.Stem.Equals(file.Stem, StringComparison.OrdinalIgnoreCase))
                        : null;
                    return main is null ? file : file with { Stem = main.Stem, StoredName = main.Stem + file.Extension };
                }).ToArray();
                if (selected.Select(file => file.StoredName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
                    throw new InvalidDataException("The source selection contains duplicate file names.");
                SelectedFile[] primary = selected.Where(file => file.Extension is ".shp" or ".geojson" or ".json" or ".tif" or ".tiff").ToArray();
                if (primary.Length == 0) throw new InvalidDataException("The selection contains no supported primary map source.");
                foreach (SelectedFile file in selected)
                {
                    if (file.Extension is ".shx" or ".dbf" or ".prj")
                    {
                        if (!primary.Any(main => main.Extension == ".shp" && main.Stem.Equals(file.Stem, StringComparison.Ordinal)))
                            throw new InvalidDataException("Select Shapefile sidecars together with their matching .shp file.");
                    }
                    if (file.Extension == ".shp" && new[] { ".shx", ".dbf" }.Any(extension =>
                        !selected.Any(sidecar => sidecar.Stem == file.Stem && sidecar.Extension == extension)))
                        throw new InvalidDataException("A Shapefile upload requires its matching .shp, .shx and .dbf files.");
                }

                ProjectSourceUploadService.Verify(target);
                Directory.CreateDirectory(target.SourcesRoot);
                ProjectSourceUploadService.Verify(target);
                string id = Guid.NewGuid().ToString("N");
                staging = Path.Combine(target.SourcesRoot, "." + id + ".partial");
                string destination = Path.Combine(target.SourcesRoot, id);
                Directory.CreateDirectory(staging);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                foreach (SelectedFile file in selected)
                {
                    await using Stream input = file.File.OpenReadStream();
                    await using var output = new FileStream(Path.Combine(staging, file.StoredName), FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, context.RequestAborted);
                    if (output.Length != file.File.Length) throw new InvalidDataException("An uploaded source file length changed.");
                }
                var sources = new List<UploadedSource>();
                foreach (SelectedFile file in primary)
                {
                    await using var input = new FileStream(Path.Combine(staging, file.StoredName), FileMode.Open, FileAccess.Read, FileShare.Read);
                    string revision = Convert.ToHexString(await SHA256.HashDataAsync(input, context.RequestAborted));
                    string path = Path.Combine(destination, file.StoredName);
                    sources.Add(new(path, path, revision, file.Extension == ".shp" ? "shapefile" :
                        file.Extension is ".tif" or ".tiff" ? "raster" : "geojson", file.File.FileName));
                }
                context.RequestAborted.ThrowIfCancellationRequested();
                ProjectSourceUploadService.Verify(target);
                Directory.Move(staging, destination);
                staging = null;
                return Results.Created("/api/source-uploads/" + id, new
                {
                    uploadID = id, target.ProjectPath, target.ProjectRevision, target.ProjectSessionID, sources,
                });
            }
            catch (SourceFileChangedException error)
            {
                return Results.Conflict(new { error = new { code = "sourceChanged", message = error.Message } });
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or OverflowException or System.Xml.XmlException)
            {
                return Results.BadRequest(new { error = new { code = "invalidInput", message = error.Message } });
            }
            finally
            {
                if (staging is not null && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        });
    }

    private static SelectedFile ValidateFile(IFormFile file)
    {
        string name = file.FileName;
        SourceUploadNames.ValidatePortableSegment(name);
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".geojson" or ".json" or ".shp" or ".shx" or ".dbf" or ".prj" or ".tif" or ".tiff"))
            throw new InvalidDataException("Choose GeoJSON, a Shapefile family, or a self-contained GeoTIFF source.");
        string stem = Path.GetFileNameWithoutExtension(name);
        return new(file, stem + extension, extension, stem);
    }
}
