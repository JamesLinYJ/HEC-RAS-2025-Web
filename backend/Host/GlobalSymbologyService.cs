using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Geospatial.Rendering;
using Geospatial.Rendering.Systems.Vector;
using Visual.Mapping.Observables;
using Visual.Mapping.Observables.Symbology;

namespace Ras.Backend;

/// <summary>
/// Persists user-level vector style defaults in the same sparse XML form as the
/// original GlobalVectorSymbologyRegistry. Paths are selected by the server,
/// never supplied by an operation request.
/// </summary>
public static class GlobalSymbologyService
{
    private const int MaximumSettingsBytes = 4 * 1024 * 1024;
    private const string SettingsDirectoryEnvironmentVariable = "HECRAS_SETTINGS_DIRECTORY";
    private const string SettingsDirectoryName = "Settings";
    private const string SettingsFileName = "VectorSymbology.xml";
    private static readonly object DefinitionLock = new();
    private static bool definitionsRegistered;
    private static readonly string[] KnownStyleAttributes =
    [
        "ID", "PenColor", "PenWidth", "FillColor", "PointSize",
        "HaloColor", "HaloThickness", "TextEmphasis"
    ];

    public static GlobalSymbologyInspectResponse Inspect()
    {
        Snapshot snapshot = ReadSnapshot();
        return ToResponse(snapshot);
    }

    public static GlobalSymbologySaveResponse Save(GlobalSymbologySaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsRevision(request.ExpectedRevision))
            throw new ArgumentException("The expected global symbology revision is invalid.");
        if (request.Styles is null)
            throw new ArgumentException("The global symbology style list is missing.");

        IReadOnlyList<VectorSymbologyDefinition> definitions = GetDefinitions();
        Dictionary<string, VectorSymbologyDefinition> definitionsByID = definitions
            .ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var stylesByID = new Dictionary<string, GlobalSymbologyValues>(StringComparer.Ordinal);
        foreach (GlobalSymbologyStyleInput? style in request.Styles)
        {
            if (style is null || string.IsNullOrWhiteSpace(style.DefinitionID) || style.Values is null)
                throw new ArgumentException("A global symbology style entry is incomplete.");
            if (!definitionsByID.ContainsKey(style.DefinitionID))
                throw new ArgumentException("A global symbology style references an unknown definition.");
            if (!stylesByID.TryAdd(style.DefinitionID, style.Values))
                throw new ArgumentException("A global symbology definition was supplied more than once.");
        }
        if (stylesByID.Count != definitionsByID.Count || definitionsByID.Keys.Any(id => !stylesByID.ContainsKey(id)))
            throw new ArgumentException("The save request must contain every current global symbology definition.");

        string path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream processLock = AcquireProcessLock(path + ".lock");
        byte[] currentBytes = ReadBytes(path);
        string currentRevision = Revision(currentBytes);
        if (!currentRevision.Equals(request.ExpectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new GlobalSymbologyRevisionConflictException(
                "全局符号设置已在其他操作中更新，请重新读取后再保存。" +
                " Global symbology changed elsewhere; reload it before saving.");

        XElement currentRoot = ParseSettings(currentBytes);
        Dictionary<string, XElement> extensions = ReadDefinitionExtensions(currentRoot, definitionsByID.Keys);
        XElement output = new(currentRoot);
        output.Elements(GlobalVectorSymbologyRegistry.XMLTag)
            .Where(element => definitionsByID.ContainsKey((string?)element.Attribute("ID") ?? string.Empty))
            .Remove();

        foreach (VectorSymbologyDefinition definition in definitions)
        {
            GlobalSymbologyValues values = ValidateValues(stylesByID[definition.Id]);
            VectorSymbology symbology = CloneSymbology(definition.DefaultSymbology);
            ApplyValues(definition.Type, values, symbology);
            bool differsFromDefault = !AreEqual(definition.Type, symbology, definition.DefaultSymbology);
            XElement? styleElement = differsFromDefault ? SerializeStyle(definition, symbology) : null;
            if (extensions.TryGetValue(definition.Id, out XElement? extension))
            {
                styleElement ??= new XElement(GlobalVectorSymbologyRegistry.XMLTag,
                    new XAttribute("ID", definition.Id));
                MergeExtensions(styleElement, extension);
            }
            if (styleElement is not null)
                output.Add(styleElement);
        }

        byte[] outputBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(output.ToString(SaveOptions.DisableFormatting));
        AtomicWrite(path, outputBytes);
        Snapshot saved = new(Revision(outputBytes), output, definitions,
            LoadValues(output, definitions));
        return ToSaveResponse(saved);
    }

    /// <summary>Loads one consistent settings revision for explicit default-style fallbacks.</summary>
    public static IReadOnlyDictionary<string, GlobalSymbologyValues> LoadRenderDefaults(IEnumerable<string> definitionIDs)
    {
        ArgumentNullException.ThrowIfNull(definitionIDs);
        Snapshot snapshot = ReadSnapshot();
        var requested = definitionIDs.Distinct(StringComparer.Ordinal).ToArray();
        var available = snapshot.Definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var result = new Dictionary<string, GlobalSymbologyValues>(StringComparer.Ordinal);
        foreach (string id in requested)
        {
            if (!available.TryGetValue(id, out VectorSymbologyDefinition? definition))
                throw new NotSupportedException("The requested global vector symbology definition is not registered.");
            result.Add(id, ToValues(snapshot.Styles[id]));
        }
        return result;
    }

    private static Snapshot ReadSnapshot()
    {
        IReadOnlyList<VectorSymbologyDefinition> definitions = GetDefinitions();
        string path = SettingsPath();
        byte[] bytes = ReadBytes(path);
        XElement root = ParseSettings(bytes);
        return new Snapshot(Revision(bytes), root, definitions, LoadValues(root, definitions));
    }

    private static IReadOnlyList<VectorSymbologyDefinition> GetDefinitions()
    {
        lock (DefinitionLock)
        {
            if (!definitionsRegistered)
            {
                GlobalVectorSymbologyRegistry.Register().GetAwaiter().GetResult();
                definitionsRegistered = true;
            }
        }

        return GlobalVectorSymbologyRegistry.DefaultDefinitions
            .Where(definition => IsSupportedType(definition.Type))
            .GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsSupportedType(VectorType type) =>
        type is VectorType.Point or VectorType.Line or VectorType.Polygon or VectorType.Text;

    private static Dictionary<string, VectorSymbology> LoadValues(
        XElement root, IReadOnlyList<VectorSymbologyDefinition> definitions)
    {
        var result = new Dictionary<string, VectorSymbology>(StringComparer.Ordinal);
        foreach (VectorSymbologyDefinition definition in definitions)
        {
            VectorSymbology style = CloneSymbology(definition.DefaultSymbology);
            XElement[] matches = root.Elements(GlobalVectorSymbologyRegistry.XMLTag)
                .Where(element => string.Equals((string?)element.Attribute("ID"), definition.Id,
                    StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"Global symbology contains duplicate definition '{definition.Id}'.");
            if (matches.Length == 1)
            {
                try { VectorSymbologyHelpers.DeserializeVectorSymbology(matches[0], style); }
                catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
                {
                    throw new InvalidDataException($"Global symbology definition '{definition.Id}' is invalid.", exception);
                }
            }
            ValidateValues(ToValues(style));
            result.Add(definition.Id, style);
        }
        return result;
    }

    private static GlobalSymbologyInspectResponse ToResponse(Snapshot snapshot) => new(snapshot.Revision,
        snapshot.Definitions
            .GroupBy(definition => definition.LayerName ?? string.Empty, StringComparer.Ordinal)
            .Select(group => new GlobalSymbologyGroupSnapshot(group.Key,
                group.Select(definition => ToDefinitionSnapshot(definition, snapshot.Styles[definition.Id]))
                    .ToArray()))
            .ToArray());

    private static GlobalSymbologySaveResponse ToSaveResponse(Snapshot snapshot)
    {
        GlobalSymbologyInspectResponse inspected = ToResponse(snapshot);
        return new GlobalSymbologySaveResponse(inspected.Revision, inspected.Groups);
    }

    private static GlobalSymbologyDefinitionSnapshot ToDefinitionSnapshot(
        VectorSymbologyDefinition definition, VectorSymbology current)
    {
        string[] properties = EditableProperties(definition.Type);
        return new GlobalSymbologyDefinitionSnapshot(definition.Id, definition.Name,
            definition.LayerName ?? string.Empty, definition.Type.ToString(), properties,
            ToValues(definition.DefaultSymbology), ToValues(current),
            !AreEqual(definition.Type, definition.DefaultSymbology, current));
    }

    private static string[] EditableProperties(VectorType type) => type switch
    {
        VectorType.Point => ["PenColor", "PenWidth", "FillColor", "PointSize"],
        VectorType.Line => ["PenColor", "PenWidth"],
        VectorType.Polygon => ["PenColor", "PenWidth", "FillColor"],
        VectorType.Text => ["FillColor", "PenWidth", "HaloColor", "HaloThickness", "TextEmphasis"],
        _ => []
    };

    private static GlobalSymbologyValues ToValues(VectorSymbology style) => new(
        ToArgb(style.PenColor), style.PenWidth, ToArgb(style.FillColor), style.PointSize,
        ToArgb(style.HaloColor), style.HaloThickness, style.TextEmphasis);

    private static GlobalSymbologyValues ValidateValues(GlobalSymbologyValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _ = ParseArgb(values.PenColor);
        _ = ParseArgb(values.FillColor);
        _ = ParseArgb(values.HaloColor);
        if (!double.IsFinite(values.PenWidth) || values.PenWidth < 0 || values.PenWidth > 10_000 ||
            !double.IsFinite(values.HaloThickness) || values.HaloThickness < 0 || values.HaloThickness > 10_000 ||
            values.PointSize < 0 || values.PointSize > 10_000)
            throw new ArgumentException("Global vector symbology contains an invalid size or width.");
        return values;
    }

    private static void ApplyValues(VectorType type, GlobalSymbologyValues values, VectorSymbology style)
    {
        ValidateValues(values);
        style.PenColor = ParseArgb(values.PenColor);
        style.PenWidth = values.PenWidth;
        if (type is VectorType.Point or VectorType.Polygon or VectorType.Text)
            style.FillColor = ParseArgb(values.FillColor);
        if (type == VectorType.Point)
            style.PointSize = values.PointSize;
        if (type == VectorType.Text)
        {
            style.HaloColor = ParseArgb(values.HaloColor);
            style.HaloThickness = checked((float)values.HaloThickness);
            style.TextEmphasis = values.TextEmphasis;
        }
    }

    private static bool AreEqual(VectorType type, VectorSymbology left, VectorSymbology right) => type switch
    {
        VectorType.Line => left.PenColor.ToArgb() == right.PenColor.ToArgb() && left.PenWidth == right.PenWidth,
        VectorType.Polygon => left.PenColor.ToArgb() == right.PenColor.ToArgb() && left.PenWidth == right.PenWidth &&
                              left.FillColor.ToArgb() == right.FillColor.ToArgb(),
        VectorType.Point => left.PenColor.ToArgb() == right.PenColor.ToArgb() && left.PenWidth == right.PenWidth &&
                            left.FillColor.ToArgb() == right.FillColor.ToArgb() && left.PointSize == right.PointSize,
        VectorType.Text => left.FillColor.ToArgb() == right.FillColor.ToArgb() && left.PenWidth == right.PenWidth &&
                           left.HaloColor.ToArgb() == right.HaloColor.ToArgb() &&
                           left.HaloThickness == right.HaloThickness && left.TextEmphasis == right.TextEmphasis,
        _ => false
    };

    private static XElement? SerializeStyle(VectorSymbologyDefinition definition, VectorSymbology style)
    {
        XElement element = new(GlobalVectorSymbologyRegistry.XMLTag,
            new XAttribute("ID", definition.Id));
        switch (definition.Type)
        {
            case VectorType.Line:
                VectorSymbologyHelpers.SerializeLineSymbology(element, style);
                break;
            case VectorType.Polygon:
                VectorSymbologyHelpers.SerializePolygonSymbology(element, style);
                break;
            case VectorType.Point:
                VectorSymbologyHelpers.SerializePointSymbology(element, style);
                break;
            case VectorType.Text:
                VectorSymbologyHelpers.SerializeTextSymbology(element, style);
                break;
            default:
                return null;
        }
        return element;
    }

    private static VectorSymbology CloneSymbology(VectorSymbology source)
    {
        var clone = new VectorSymbology(source.PenColor, source.PenWidth, source.FillColor)
        {
            PointSize = source.PointSize,
            HasShadow = source.HasShadow,
            IsDotted = source.IsDotted,
            IsTranslated = source.IsTranslated,
            HaloColor = source.HaloColor,
            HaloThickness = source.HaloThickness,
            Font = source.Font,
            TextEmphasis = source.TextEmphasis,
            TextMaxWidth = source.TextMaxWidth,
            TextKerning = source.TextKerning,
            BlendMode = source.BlendMode
        };
        if (source.DottedStrokeStyle is { } strokes)
            clone.DottedStrokeStyle = strokes.ToArray();
        if (source.Translation is { } translation)
            clone.Translation = translation.ToArray();
        return clone;
    }

    private static string ToArgb(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color ParseArgb(string value)
    {
        if (value is null || value.Length != 9 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            throw new ArgumentException("Vector colors must use #AARRGGBB format.");
        return Color.FromArgb(unchecked((int)argb));
    }

    private static XElement ParseSettings(byte[] bytes)
    {
        if (bytes.Length == 0)
            return new XElement(GlobalVectorSymbologyRegistry.XMLCollectionTag);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumSettingsBytes,
                IgnoreComments = false,
                IgnoreWhitespace = false
            });
            XElement root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
            if (root.Name != GlobalVectorSymbologyRegistry.XMLCollectionTag)
                throw new InvalidDataException("Global vector symbology has an unexpected XML root.");
            return root;
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Global vector symbology XML could not be read.", exception);
        }
    }

    private static Dictionary<string, XElement> ReadDefinitionExtensions(
        XElement root, IEnumerable<string> knownDefinitionIDs)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var known = new HashSet<string>(knownDefinitionIDs, StringComparer.Ordinal);
        foreach (XElement style in root.Elements(GlobalVectorSymbologyRegistry.XMLTag))
        {
            string? id = (string?)style.Attribute("ID");
            if (id is null || !known.Contains(id)) continue;
            var extension = new XElement(GlobalVectorSymbologyRegistry.XMLTag,
                new XAttribute("ID", id));
            foreach (XAttribute attribute in style.Attributes().Where(attribute =>
                         attribute.Name != "ID" && !KnownStyleAttributes.Contains(attribute.Name.LocalName,
                             StringComparer.Ordinal)))
                extension.Add(new XAttribute(attribute));
            foreach (XNode node in style.Nodes().Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
                extension.Add(CloneNode(node));
            if (extension.Attributes().Count() > 1 || extension.Nodes().Any())
                result[id] = extension;
        }
        return result;
    }

    private static void MergeExtensions(XElement target, XElement extension)
    {
        foreach (XAttribute attribute in extension.Attributes().Where(attribute => attribute.Name != "ID"))
            if (target.Attribute(attribute.Name) is null)
                target.Add(new XAttribute(attribute));
        foreach (XNode node in extension.Nodes())
            target.Add(CloneNode(node));
    }

    private static XNode CloneNode(XNode node) => node switch
    {
        XElement element => new XElement(element),
        XComment comment => new XComment(comment.Value),
        XProcessingInstruction instruction => new XProcessingInstruction(instruction.Target, instruction.Data),
        XText text => new XText(text.Value),
        _ => new XText(node.ToString())
    };

    private static string SettingsPath()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable(SettingsDirectoryEnvironmentVariable);
        string root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HEC-RAS 2025")
            : Path.GetFullPath(configuredRoot);
        return Path.Combine(root, SettingsDirectoryName, SettingsFileName);
    }

    private static byte[] ReadBytes(string path)
    {
        if (!File.Exists(path)) return [];
        var info = new FileInfo(path);
        if (info.Length < 0 || info.Length > MaximumSettingsBytes)
            throw new InvalidDataException("Global vector symbology file exceeds its size limit.");
        return File.ReadAllBytes(path);
    }

    private static FileStream AcquireProcessLock(string lockPath)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 199) { Thread.Sleep(25); }
        }
        throw new IOException("Timed out waiting for the global vector symbology settings lock.");
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(directory, ".VectorSymbology." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       16 * 1024, FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool IsRevision(string? value) => value is not null && value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private sealed record Snapshot(string Revision, XElement Root,
        IReadOnlyList<VectorSymbologyDefinition> Definitions,
        Dictionary<string, VectorSymbology> Styles);
}
