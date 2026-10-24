using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Ras.Backend;

/// <summary>A file the source reader consumes as part of one map layer.</summary>
public sealed record MapSourceFileInput(string Path, string Role, bool Required = true);

/// <summary>The physical identity and content revision of one consumed file.</summary>
public sealed record MapSourceFileRevision(
    string InputPath,
    string Path,
    string Role,
    bool Required,
    bool Exists,
    string? Revision);

/// <summary>A deterministic snapshot of a primary source and every reported dependency.</summary>
public sealed record MapSourceFamilyCapture(
    string Family,
    string SourcePath,
    string Revision,
    MapSourceFileRevision[] Files);

/// <summary>The saved project and visualization owner of one map source operation.</summary>
public sealed record MapSourceMapCapture(
    string ProjectPath,
    string ProjectRevision,
    string? ProjectXmlSHA,
    string VisualizationPath,
    string? VisualizationRevision,
    string? MapID,
    int? MapIndex,
    string? MapName,
    string MapType,
    bool IsPersisted);

/// <summary>An exact persisted map-layer row resolved before its reader reports dependencies.</summary>
/// <remarks><see cref="MapSourceNodeLocation.SourcePath"/> is the canonical physical file identity.
/// <see cref="MapSourceNodeLocation.PresentationPath"/> preserves the path selected by the exact persisted .vis row for format-sensitive readers.</remarks>
public sealed record MapSourceNodeLocation(
    MapSourceMapCapture Map,
    CatalogTreeNodeLocator Locator,
    string SourcePath,
    string? GdbVectorType = null,
    string? GdbLayerName = null,
    string? PresentationPath = null);

/// <summary>An exact persisted map-layer row tied to its complete consumed-file family.</summary>
public sealed record MapSourceNodeCapture(
    MapSourceMapCapture Map,
    CatalogTreeNodeLocator Locator,
    MapSourceFamilyCapture Family);

/// <summary>
/// Captures source files and exact map ownership for real raster/vector readers.
/// This helper does not inspect or transform raster/vector content; readers must report
/// the files they actually consume and call the verify methods around their work.
/// </summary>
public static partial class MapSourceIdentity
{
    private const long MaximumProjectXmlBytes = 8L * 1024 * 1024;
    private const long MaximumVisualizationBytes = 32L * 1024 * 1024;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static MapSourceFamilyCapture CaptureFamily(
        string family,
        string sourcePath,
        IEnumerable<MapSourceFileInput> consumedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(consumedFiles);

        string sourceInputPath = Path.GetFullPath(sourcePath);
        string sourceCanonicalPath = SourceFilePaths.Canonical(sourceInputPath);
        if (!File.Exists(sourceCanonicalPath))
            throw new FileNotFoundException("The map source file was not found.", sourceInputPath);

        var inputs = new List<MapSourceFileInput>
        {
            new(sourceInputPath, "source", Required: true)
        };
        var inputByPath = new Dictionary<string, MapSourceFileInput>(PathComparer)
        {
            [sourceInputPath] = inputs[0]
        };
        foreach (MapSourceFileInput input in consumedFiles)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.Path);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.Role);
            string fullInputPath = Path.GetFullPath(input.Path);
            if (string.Equals(input.Role, "source", StringComparison.Ordinal) &&
                !PathComparer.Equals(fullInputPath, sourceInputPath))
                throw new ArgumentException("The source role is reserved for the primary map source.", nameof(consumedFiles));
            if (inputByPath.TryGetValue(fullInputPath, out MapSourceFileInput? previous))
            {
                if (PathComparer.Equals(fullInputPath, sourceInputPath)) continue;
                if (!string.Equals(previous.Role, input.Role, StringComparison.Ordinal) ||
                    previous.Required != input.Required)
                    throw new ArgumentException("One consumed path was reported with conflicting dependency metadata.", nameof(consumedFiles));
                continue;
            }
            MapSourceFileInput normalized = input with { Path = fullInputPath };
            inputByPath.Add(fullInputPath, normalized);
            inputs.Add(normalized);
        }

        MapSourceFileRevision[] files = inputs
            .Select(CaptureFile)
            .OrderBy(file => file.Path, PathComparer)
            .ThenBy(file => file.InputPath, PathComparer)
            .ThenBy(file => file.Role, StringComparer.Ordinal)
            .ToArray();

        MapSourceFileRevision source = files.SingleOrDefault(file =>
            string.Equals(file.Role, "source", StringComparison.Ordinal) &&
            PathComparer.Equals(file.InputPath, sourceInputPath))
            ?? throw new InvalidOperationException("The source family lost its primary file identity.");
        if (!source.Exists || source.Revision is null || !PathComparer.Equals(source.Path, sourceCanonicalPath))
            throw new SourceFileChangedException();

        string revision = ComputeFamilyRevision(family, sourceCanonicalPath, files);
        return new(family, sourceCanonicalPath, revision, files);
    }

    public static void VerifyFamily(MapSourceFamilyCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Files is null || capture.Files.Length == 0 ||
            string.IsNullOrWhiteSpace(capture.Family) || string.IsNullOrWhiteSpace(capture.SourcePath))
            throw new SourceFileChangedException();

        MapSourceFileRevision[] sourceFiles = capture.Files.Where(file =>
            string.Equals(file.Role, "source", StringComparison.Ordinal) && file.Required).ToArray();
        if (sourceFiles.Length != 1 || !PathComparer.Equals(sourceFiles[0].Path, capture.SourcePath))
            throw new SourceFileChangedException();

        MapSourceFamilyCapture current;
        try
        {
            current = CaptureFamily(capture.Family, sourceFiles[0].InputPath,
                capture.Files.Where(file => !(string.Equals(file.Role, "source", StringComparison.Ordinal) &&
                    PathComparer.Equals(file.InputPath, sourceFiles[0].InputPath)))
                    .Select(file => new MapSourceFileInput(file.InputPath, file.Role, file.Required)));
        }
        catch (FileNotFoundException)
        {
            throw new SourceFileChangedException();
        }
        catch (DirectoryNotFoundException)
        {
            throw new SourceFileChangedException();
        }
        if (!SameFamily(capture, current)) throw new SourceFileChangedException();
    }

    public static MapSourceMapCapture CaptureMap(MapTreeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MapID) || request.MapIndex < 0 ||
            string.IsNullOrWhiteSpace(request.MapName) || string.IsNullOrWhiteSpace(request.MapType))
            throw new ArgumentException("The saved map identity is incomplete.");
        return CaptureMapCore(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, request.MapID, request.MapIndex,
            request.MapName, request.MapType, allowUnpersistedDefaultProject: false);
    }

    public static MapSourceMapCapture CaptureMap(MapDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MapIndex is int mapIndex)
        {
            if (mapIndex < 0 || string.IsNullOrWhiteSpace(request.MapName) ||
                string.IsNullOrWhiteSpace(request.MapType))
                throw new ArgumentException("The saved map identity is incomplete.");
            return CaptureMapCore(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
                request.ExpectedVisualizationRevision, request.MapID, mapIndex,
                request.MapName, request.MapType, allowUnpersistedDefaultProject: false);
        }

        if (!string.Equals(request.Kind, "Project", StringComparison.Ordinal))
            throw new ArgumentException("Only the default Project map can be captured before it is persisted.");
        return CaptureMapCore(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ExpectedVisualizationRevision, request.MapID, null,
            request.MapName ?? request.Name, "Project", allowUnpersistedDefaultProject: true);
    }

    public static void VerifyMap(MapTreeRequest request, MapSourceMapCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!SameMap(capture, CaptureMap(request))) throw new SourceFileChangedException();
    }

    public static void VerifyMap(MapDocumentRequest request, MapSourceMapCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!SameMap(capture, CaptureMap(request))) throw new SourceFileChangedException();
    }

    /// <summary>Resolve the exact .vis row before its native reader reports dependencies.</summary>
    public static MapSourceNodeLocation ResolvePersistedNode(
        MapTreeRequest mapRequest,
        CatalogTreeNodeLocator locator)
    {
        ArgumentNullException.ThrowIfNull(mapRequest);
        ArgumentNullException.ThrowIfNull(locator);
        MapSourceMapCapture map = CaptureMap(mapRequest);
        string presentationPath = ResolveNodePresentationPath(mapRequest, map, locator);
        string sourcePath = SourceFilePaths.Canonical(presentationPath);
        VerifyMap(mapRequest, map);
        return new(map, locator, sourcePath, PresentationPath: presentationPath);
    }

    /// <summary>Resolve one exact serialized GDB vector child, whose source is a directory.</summary>
    public static MapSourceNodeLocation ResolvePersistedGdbNode(
        MapTreeRequest mapRequest,
        CatalogTreeNodeLocator locator)
    {
        ArgumentNullException.ThrowIfNull(mapRequest);
        ArgumentNullException.ThrowIfNull(locator);
        if (locator.NodeType != "GdbVectorLayer" || locator.MapType != "GdbVectorLayer")
            throw new SourceFileChangedException();
        MapSourceMapCapture map = CaptureMap(mapRequest);
        string sourcePath = ResolveGdbNodePath(mapRequest, map, locator, out string vectorType,
            out string layerName);
        VerifyMap(mapRequest, map);
        return new(map, locator, sourcePath, vectorType, layerName);
    }

    public static void VerifyPersistedGdbNode(MapTreeRequest mapRequest, MapSourceNodeLocation capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        MapSourceNodeLocation current = ResolvePersistedGdbNode(mapRequest, capture.Locator);
        if (!SameMap(capture.Map, current.Map) || !SameLocator(capture.Locator, current.Locator) ||
            !PathComparer.Equals(capture.SourcePath, current.SourcePath) ||
            !string.Equals(capture.GdbVectorType, current.GdbVectorType, StringComparison.Ordinal) ||
            !string.Equals(capture.GdbLayerName, current.GdbLayerName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    public static MapSourceNodeCapture ResolvePersistedNode(
        MapTreeRequest mapRequest,
        CatalogTreeNodeLocator locator,
        MapSourceFamilyCapture family)
    {
        ArgumentNullException.ThrowIfNull(family);
        MapSourceNodeLocation location = ResolvePersistedNode(mapRequest, locator);
        VerifyFamily(family);
        if (!PathComparer.Equals(location.SourcePath, family.SourcePath))
            throw new SourceFileChangedException();
        var capture = new MapSourceNodeCapture(location.Map, location.Locator, family);
        VerifyMap(mapRequest, capture.Map);
        VerifyFamily(capture.Family);
        return capture;
    }

    public static void VerifyPersistedNode(MapTreeRequest mapRequest, MapSourceNodeCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        MapSourceNodeCapture current = ResolvePersistedNode(mapRequest, capture.Locator, capture.Family);
        if (!SameMap(capture.Map, current.Map) || !SameLocator(capture.Locator, current.Locator) ||
            !SameFamily(capture.Family, current.Family))
            throw new SourceFileChangedException();
    }

    private static MapSourceMapCapture CaptureMapCore(
        string projectPath,
        string? projectXml,
        string? projectXmlSHA,
        string? expectedVisualizationRevision,
        string? mapID,
        int? mapIndex,
        string? mapName,
        string mapType,
        bool allowUnpersistedDefaultProject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        string projectAliasPath = Path.GetFullPath(projectPath);
        if (!Path.GetExtension(projectAliasPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The map source requires a saved HEC-RAS project file.");

        string projectPathCanonical = SourceFilePaths.Canonical(projectAliasPath);
        if (!File.Exists(projectPathCanonical))
            throw new FileNotFoundException("The current project file was not found.", projectPathCanonical);
        string projectRevision = HashFile(projectPathCanonical);
        string? projectXmlRevision = ValidateProjectXml(projectXml, projectXmlSHA);

        string visualizationAliasPath = Path.ChangeExtension(projectAliasPath, ".vis");
        string visualizationPath = SourceFilePaths.Canonical(visualizationAliasPath);
        bool visualizationExists = File.Exists(visualizationAliasPath);
        string? visualizationRevision = visualizationExists ? HashFile(visualizationPath) : null;
        if (!SameRevision(visualizationRevision, expectedVisualizationRevision))
            throw new SourceFileChangedException();

        XDocument? visualization = null;
        if (visualizationExists)
        {
            byte[] bytes = ReadBoundedFile(visualizationPath, MaximumVisualizationBytes);
            if (!string.Equals(Hash(bytes), visualizationRevision, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            visualization = ParseXml(bytes, MaximumVisualizationBytes);
            if (visualization.Root?.Name != "Ras")
                throw new InvalidDataException("The visualization file has an invalid Ras root element.");
        }

        bool isPersisted;
        if (mapIndex is int index)
        {
            XElement[] maps = visualization?.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
            if (index >= maps.Length) throw new SourceFileChangedException();
            XElement map = maps[index];
            if (!string.Equals((string?)map.Attribute("Name"), mapName, StringComparison.Ordinal) ||
                !string.Equals((string?)map.Attribute("Type"), mapType, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            if (mapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
                throw new NotSupportedException("The source map workflow supports Project, Buildable, Geometry and Plan maps.");
            isPersisted = true;
        }
        else
        {
            if (!allowUnpersistedDefaultProject || !string.Equals(mapType, "Project", StringComparison.Ordinal))
                throw new SourceFileChangedException();
            XElement[] maps = visualization?.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
            if (maps.Length != 0) throw new SourceFileChangedException();
            isPersisted = false;
        }

        if (!PathComparer.Equals(SourceFilePaths.Canonical(projectAliasPath), projectPathCanonical) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(visualizationAliasPath), visualizationPath) ||
            !File.Exists(projectPathCanonical) || !string.Equals(HashFile(projectPathCanonical), projectRevision, StringComparison.Ordinal) ||
            (visualizationRevision is null
                ? File.Exists(visualizationAliasPath)
                : !File.Exists(visualizationPath) || !string.Equals(HashFile(visualizationPath), visualizationRevision, StringComparison.Ordinal)))
            throw new SourceFileChangedException();

        return new(projectPathCanonical, projectRevision, projectXmlRevision, visualizationPath,
            visualizationRevision, mapID, mapIndex, mapName, mapType, isPersisted);
    }

    private static string ResolveNodePresentationPath(MapTreeRequest request,
        MapSourceMapCapture mapCapture, CatalogTreeNodeLocator locator)
    {
        if (locator.NodeType != "MapLayer" || locator.NodePath is not { Length: > 0 } nodePath ||
            string.IsNullOrWhiteSpace(locator.RootName) || string.IsNullOrWhiteSpace(locator.NodeName) ||
            string.IsNullOrWhiteSpace(locator.MapType) || string.IsNullOrWhiteSpace(locator.Filename) ||
            string.IsNullOrWhiteSpace(locator.FilenameRelative))
            throw new SourceFileChangedException();

        string visualizationAlias = Path.ChangeExtension(Path.GetFullPath(request.ProjectPath), ".vis");
        if (mapCapture.VisualizationRevision is null || !File.Exists(mapCapture.VisualizationPath))
            throw new SourceFileChangedException();
        byte[] bytes = ReadBoundedFile(mapCapture.VisualizationPath, MaximumVisualizationBytes);
        if (!string.Equals(Hash(bytes), mapCapture.VisualizationRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        XDocument document = ParseXml(bytes, MaximumVisualizationBytes);
        XElement? map = ResolveSerializedMap(document, request.MapIndex, request.MapName, request.MapType);
        XElement[] roots = map?.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        int firstIndex = nodePath[0];
        if (firstIndex < 0 || firstIndex >= roots.Length) throw new SourceFileChangedException();
        XElement root = roots[firstIndex];
        if ((string?)root.Attribute("Type") != "Root" ||
            !string.Equals((string?)root.Attribute("Name"), locator.RootName, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        XElement current = root;
        for (int index = 1; index < nodePath.Length; index++)
        {
            XElement[] children = current.Elements("Node").ToArray();
            int childIndex = nodePath[index];
            if (childIndex < 0 || childIndex >= children.Length) throw new SourceFileChangedException();
            current = children[childIndex];
        }

        if ((string?)current.Attribute("Type") != "MapLayer" ||
            !string.Equals((string?)current.Attribute("Name"), locator.NodeName, StringComparison.Ordinal) ||
            !string.Equals((string?)current.Attribute("MapType"), locator.MapType, StringComparison.Ordinal) ||
            !string.Equals((string?)current.Attribute("Filename"), locator.Filename, StringComparison.Ordinal) ||
            !string.Equals((string?)current.Attribute("FilenameRelative"), locator.FilenameRelative, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(request.ProjectPath))!;
        string? resolved = ResolveMapLayerPath(projectDirectory, locator.FilenameRelative, locator.Filename);
        if (resolved is null) throw new SourceFileChangedException();
        string sourcePath = SourceFilePaths.Canonical(resolved);
        if ((locator.AssetID is not null &&
                !PathComparer.Equals(SourceFilePaths.Canonical(locator.AssetID), sourcePath)) ||
            !File.Exists(sourcePath) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(visualizationAlias), mapCapture.VisualizationPath))
            throw new SourceFileChangedException();
        return resolved;
    }

    private static string ResolveGdbNodePath(MapTreeRequest request,
        MapSourceMapCapture mapCapture, CatalogTreeNodeLocator locator, out string vectorType,
        out string layerName)
    {
        vectorType = "";
        layerName = "";
        if (locator.NodePath is not { Length: > 1 } nodePath ||
            string.IsNullOrWhiteSpace(locator.RootName) || string.IsNullOrWhiteSpace(locator.NodeName) ||
            string.IsNullOrWhiteSpace(locator.Filename) || string.IsNullOrWhiteSpace(locator.FilenameRelative) ||
            string.IsNullOrWhiteSpace(locator.AssetID))
            throw new SourceFileChangedException();

        string visualizationAlias = Path.ChangeExtension(Path.GetFullPath(request.ProjectPath), ".vis");
        if (mapCapture.VisualizationRevision is null || !File.Exists(mapCapture.VisualizationPath))
            throw new SourceFileChangedException();
        byte[] bytes = ReadBoundedFile(mapCapture.VisualizationPath, MaximumVisualizationBytes);
        if (!string.Equals(Hash(bytes), mapCapture.VisualizationRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        XDocument document = ParseXml(bytes, MaximumVisualizationBytes);
        XElement? map = ResolveSerializedMap(document, request.MapIndex, request.MapName, request.MapType);
        XElement[] roots = map?.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        int firstIndex = nodePath[0];
        if (firstIndex < 0 || firstIndex >= roots.Length) throw new SourceFileChangedException();
        XElement root = roots[firstIndex];
        if ((string?)root.Attribute("Type") != "Root" ||
            !string.Equals((string?)root.Attribute("Name"), locator.RootName, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        XElement current = root;
        for (int index = 1; index < nodePath.Length; index++)
        {
            XElement[] children = current.Elements("Node").ToArray();
            int childIndex = nodePath[index];
            if (childIndex < 0 || childIndex >= children.Length) throw new SourceFileChangedException();
            current = children[childIndex];
        }

        string? persistedLayerName = (string?)current.Attribute("GdbLayerName");
        string? persistedVectorType = (string?)current.Attribute("GdbVectorType");
        if ((string?)current.Attribute("Type") != "GdbVectorLayer" ||
            !string.Equals((string?)current.Attribute("Name"), locator.NodeName, StringComparison.Ordinal) ||
            !string.Equals((string?)current.Attribute("MapType"), "GdbVectorLayer", StringComparison.Ordinal) ||
            !string.Equals((string?)current.Attribute("Filename"), locator.Filename, StringComparison.Ordinal) ||
            !string.Equals((string?)current.Attribute("FilenameRelative"), locator.FilenameRelative, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(persistedLayerName) ||
            string.IsNullOrWhiteSpace(persistedVectorType))
            throw new SourceFileChangedException();
        layerName = persistedLayerName;
        vectorType = persistedVectorType;

        XElement? parent = current.Parent;
        if (parent is null || (string?)parent.Attribute("Type") != "GdbDataset" ||
            !string.Equals((string?)parent.Attribute("Filename"), locator.Filename, StringComparison.Ordinal) ||
            !string.Equals((string?)parent.Attribute("FilenameRelative"), locator.FilenameRelative, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(request.ProjectPath))!;
        string relative = locator.FilenameRelative.Replace('\\', Path.DirectorySeparatorChar);
        string filename = locator.Filename.Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.IsPathRooted(relative)
            ? relative : Path.Combine(projectDirectory, relative));
        if (!Directory.Exists(resolved) && Path.IsPathFullyQualified(filename))
            resolved = Path.GetFullPath(filename);
        if (!Directory.Exists(resolved)) throw new SourceFileChangedException();
        string sourcePath = SourceFilePaths.Canonical(resolved);
        string assetPath = SourceFilePaths.Canonical(locator.AssetID);
        if (!PathComparer.Equals(sourcePath, assetPath) ||
            !string.Equals(Path.GetExtension(sourcePath), ".gdb", StringComparison.OrdinalIgnoreCase) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(visualizationAlias), mapCapture.VisualizationPath))
            throw new SourceFileChangedException();
        return sourcePath;
    }

    private static XElement? ResolveSerializedMap(XDocument visualization, int mapIndex,
        string mapName, string mapType)
    {
        XElement[] maps = visualization.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
        if (mapIndex < 0 || mapIndex >= maps.Length) throw new SourceFileChangedException();
        XElement map = maps[mapIndex];
        if (!string.Equals((string?)map.Attribute("Name"), mapName, StringComparison.Ordinal) ||
            !string.Equals((string?)map.Attribute("Type"), mapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return map;
    }

    private static string? ResolveMapLayerPath(string projectDirectory, string? relative, string? filename)
    {
        string? candidate = !string.IsNullOrWhiteSpace(relative)
            ? ResolvePath(projectDirectory, relative) : null;
        if (candidate is not null && File.Exists(candidate)) return candidate;
        candidate = !string.IsNullOrWhiteSpace(filename) ? ResolvePath(projectDirectory, filename) : null;
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    private static string ResolvePath(string directory, string value)
    {
        string normalized = value.Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized)
            ? normalized : Path.Combine(directory, normalized));
    }

    private static MapSourceFileRevision CaptureFile(MapSourceFileInput input)
    {
        string canonicalBefore = SourceFilePaths.Canonical(input.Path);
        bool exists = File.Exists(canonicalBefore);
        if (!exists && input.Required)
            throw new FileNotFoundException("A required map source dependency was not found.", input.Path);
        string? revision = exists ? HashFile(canonicalBefore) : null;
        string canonicalAfter = SourceFilePaths.Canonical(input.Path);
        if (!PathComparer.Equals(canonicalBefore, canonicalAfter) ||
            exists != File.Exists(canonicalAfter))
            throw new SourceFileChangedException();
        return new(input.Path, canonicalAfter, input.Role, input.Required, exists, revision);
    }

    private static string ComputeFamilyRevision(string family, string sourcePath,
        IEnumerable<MapSourceFileRevision> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, family);
        Append(hash, sourcePath);
        foreach (MapSourceFileRevision file in files)
        {
            Append(hash, file.InputPath);
            Append(hash, file.Path);
            Append(hash, file.Role);
            Append(hash, file.Required ? "1" : "0");
            Append(hash, file.Exists ? "1" : "0");
            Append(hash, file.Revision ?? "");
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string? ValidateProjectXml(string? projectXml, string? projectXmlSHA)
    {
        if (projectXml is null)
        {
            if (!string.IsNullOrEmpty(projectXmlSHA))
                throw new ArgumentException("The project XML revision has no project XML snapshot.");
            return null;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(projectXml);
        if (bytes.LongLength > MaximumProjectXmlBytes)
            throw new NotSupportedException("The current project XML snapshot is too large.");
        string revision = Hash(bytes);
        if (string.IsNullOrWhiteSpace(projectXmlSHA) ||
            !revision.Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (ParseXml(bytes, MaximumProjectXmlBytes).Root?.Name != "Project")
            throw new InvalidDataException("The current project XML snapshot has an invalid Project root element.");
        return revision;
    }

    private static byte[] ReadBoundedFile(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length > maximumBytes) throw new NotSupportedException("The map identity XML document is too large.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) throw new NotSupportedException("The map identity XML document is too large.");
        using var output = new MemoryStream((int)stream.Length);
        stream.CopyTo(output);
        if (output.Length != stream.Length) throw new SourceFileChangedException();
        return output.ToArray();
    }

    private static XDocument ParseXml(byte[] bytes, long maximumBytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes,
            IgnoreWhitespace = false
        };
        using var stream = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool SameRevision(string? left, string? right) => left is null
        ? right is null
        : right is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool SameFamily(MapSourceFamilyCapture left, MapSourceFamilyCapture right) =>
        string.Equals(left.Family, right.Family, StringComparison.Ordinal) &&
        PathComparer.Equals(left.SourcePath, right.SourcePath) &&
        string.Equals(left.Revision, right.Revision, StringComparison.OrdinalIgnoreCase) &&
        left.Files.SequenceEqual(right.Files);

    private static bool SameMap(MapSourceMapCapture left, MapSourceMapCapture right) =>
        PathComparer.Equals(left.ProjectPath, right.ProjectPath) &&
        string.Equals(left.ProjectRevision, right.ProjectRevision, StringComparison.OrdinalIgnoreCase) &&
        SameRevision(left.ProjectXmlSHA, right.ProjectXmlSHA) &&
        PathComparer.Equals(left.VisualizationPath, right.VisualizationPath) &&
        SameRevision(left.VisualizationRevision, right.VisualizationRevision) &&
        string.Equals(left.MapID, right.MapID, StringComparison.Ordinal) &&
        left.MapIndex == right.MapIndex &&
        string.Equals(left.MapName, right.MapName, StringComparison.Ordinal) &&
        string.Equals(left.MapType, right.MapType, StringComparison.Ordinal) &&
        left.IsPersisted == right.IsPersisted;

    private static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        string.Equals(left.RootName, right.RootName, StringComparison.Ordinal) &&
        string.Equals(left.NodeType, right.NodeType, StringComparison.Ordinal) &&
        string.Equals(left.NodeName, right.NodeName, StringComparison.Ordinal) &&
        SamePath(left.AssetID, right.AssetID) &&
        string.Equals(left.MapType, right.MapType, StringComparison.Ordinal) &&
        string.Equals(left.Filename, right.Filename, StringComparison.Ordinal) &&
        string.Equals(left.FilenameRelative, right.FilenameRelative, StringComparison.Ordinal) &&
        SameIntArray(left.NodePath, right.NodePath) &&
        SamePath(left.ParentGeometryAssetID, right.ParentGeometryAssetID) &&
        SameIntArray(left.ParentGeometryNodePath, right.ParentGeometryNodePath) &&
        string.Equals(left.ParentGeometryNodeName, right.ParentGeometryNodeName, StringComparison.Ordinal) &&
        SamePath(left.AssociationAssetID, right.AssociationAssetID) &&
        string.Equals(left.AssociationKind, right.AssociationKind, StringComparison.Ordinal) &&
        SamePath(left.ParentPlanAssetID, right.ParentPlanAssetID) &&
        SameIntArray(left.ParentPlanNodePath, right.ParentPlanNodePath) &&
        string.Equals(left.ParentPlanNodeName, right.ParentPlanNodeName, StringComparison.Ordinal) &&
        string.Equals(left.ParentResultNodeType, right.ParentResultNodeType, StringComparison.Ordinal) &&
        SamePath(left.ParentResultAssetID, right.ParentResultAssetID) &&
        SameIntArray(left.ParentResultNodePath, right.ParentResultNodePath) &&
        string.Equals(left.ParentResultNodeName, right.ParentResultNodeName, StringComparison.Ordinal) &&
        string.Equals(left.ParentResultRevision, right.ParentResultRevision, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string? left, string? right) => left is null
        ? right is null
        : right is not null && PathComparer.Equals(SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right));

    private static bool SameIntArray(int[]? left, int[]? right) => left is null
        ? right is null
        : right is not null && left.SequenceEqual(right);
}
