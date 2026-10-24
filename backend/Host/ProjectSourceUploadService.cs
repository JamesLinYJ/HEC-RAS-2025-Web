using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Ras.Backend;

public sealed record ProjectSourceUploadTarget(
    string ProjectPath, string ProjectRevision, string ProjectSessionID, string SourcesRoot);

/// <summary>Filesystem-only upload guard; the HTTP process never opens native HDF or GDAL models.</summary>
public static class ProjectSourceUploadService
{
    public static ProjectSourceUploadTarget Prepare(string projectPath, string revision, string sessionID)
    {
        if (!Path.IsPathFullyQualified(projectPath) ||
            !Path.GetExtension(projectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(sessionID, out _) || revision.Length != 64 || !revision.All(Uri.IsHexDigit))
            throw new ArgumentException("A saved project path, SHA-256 revision and session are required.");
        string canonical = SourceFilePaths.Canonical(projectPath);
        byte[] bytes = ReadProject(canonical);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 };
        using (var stream = new MemoryStream(bytes, writable: false))
        using (XmlReader reader = XmlReader.Create(stream, settings))
        {
            if (XDocument.Load(reader).Root?.Name != "Project")
                throw new InvalidDataException("The selected .ras file has no Project root.");
        }
        string sourcesRoot = Path.Combine(Path.GetDirectoryName(canonical)!, "Map Sources");
        var target = new ProjectSourceUploadTarget(canonical, revision, sessionID, sourcesRoot);
        Verify(target);
        return target;
    }

    public static void Verify(ProjectSourceUploadTarget target)
    {
        if (!SourceFilePaths.Canonical(target.ProjectPath).Equals(target.ProjectPath, PathComparison) ||
            !Convert.ToHexString(SHA256.HashData(ReadProject(target.ProjectPath))).Equals(target.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !SourceFilePaths.Canonical(target.SourcesRoot).Equals(target.SourcesRoot, PathComparison))
            throw new SourceFileChangedException();
        if (File.Exists(target.SourcesRoot)) throw new IOException("The map sources path is a file.");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static byte[] ReadProject(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The saved project was not found.", path);
        if (info.Length > 8 * 1024 * 1024) throw new InvalidDataException("The project XML exceeds the upload guard's size limit.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            if (output.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("The project XML changed beyond the size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
