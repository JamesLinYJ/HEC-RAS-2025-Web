using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Ras.Backend;

public sealed record SavedMapViewExtent(double Left, double Top, double Right, double Bottom);

public sealed record MapViewsInspectRequest(
    string ProjectPath,
    string ProjectXml,
    [property: JsonPropertyName("projectXmlSHA")] string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    string Action,
    string? TargetName = null,
    string? Name = null,
    SavedMapViewExtent? Extent = null,
    string? Shortcut = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record MapViewsCommitRequest(
    string ProjectPath,
    string ProjectXml,
    [property: JsonPropertyName("projectXmlSHA")] string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    string Action,
    [property: JsonPropertyName("expectedVisualizationRevision")] string? ExpectedVisualizationRevision,
    [property: JsonPropertyName("expectedViewsSHA256")] string ExpectedViewsSHA256,
    string? TargetName = null,
    [property: JsonPropertyName("expectedEntrySHA256")] string? ExpectedEntrySHA256 = null,
    string? Name = null,
    SavedMapViewExtent? Extent = null,
    string? Shortcut = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record SavedMapView(
    string Name,
    SavedMapViewExtent Extent,
    string Shortcut,
    [property: JsonPropertyName("entrySHA256")] string EntrySHA256);

public sealed record MapViewsInspection(
    bool CanCommit,
    string? ValidationMessage,
    string Action,
    string ProjectPath,
    string ProjectXml,
    [property: JsonPropertyName("projectXmlSHA")] string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    [property: JsonPropertyName("visualizationRevision")] string? VisualizationRevision,
    [property: JsonPropertyName("viewsSHA256")] string ViewsSHA256,
    SavedMapView[] Views,
    SavedMapView? Target);

public sealed record MapViewsCommitReceipt(
    bool Success,
    string Action,
    string ProjectPath,
    string ProjectXml,
    [property: JsonPropertyName("projectXmlSHA")] string ProjectXmlSHA,
    string ProjectRevision,
    Guid ProjectSessionID,
    [property: JsonPropertyName("previousVisualizationRevision")] string? PreviousVisualizationRevision,
    [property: JsonPropertyName("visualizationRevision")] string VisualizationRevision,
    [property: JsonPropertyName("previousViewsSHA256")] string PreviousViewsSHA256,
    [property: JsonPropertyName("viewsSHA256")] string ViewsSHA256,
    SavedMapView[] Views,
    SavedMapView? Before,
    SavedMapView? After);

/// <summary>
/// Reads and edits the global saved map-view collection in a project's .vis
/// file. It never opens or modifies model/HDF sources.
/// </summary>
public static partial class MapTreeService
{
    private const string EmptyViewsHashMaterial = "hecras-map-views/v1:absent";

    private static readonly Dictionary<string, string> VirtualKeyNames = BuildVirtualKeyNames();

    public static MapViewsInspection InspectViews(MapViewsInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateViewsProjectIdentity(request.ProjectPath, request.ProjectXml,
            request.ProjectXmlSHA, request.ProjectRevision, request.ProjectSessionID,
            request.CoreLayerContext);

        using OperationContext context = Open(request.ProjectPath, request.ProjectXml,
            request.ProjectXmlSHA, expectedVisualizationRevision: null, allowMissing: true,
            projectSessionID: request.ProjectSessionID.ToString("D"),
            coreLayerContext: request.CoreLayerContext, discoverVisualizationRevision: true);
        EnsureViewsProjectRevision(context, request.ProjectRevision, request.ProjectSessionID,
            request.CoreLayerContext);
        return InspectViewsCore(request, context);
    }

    public static MapViewsCommitReceipt CommitView(MapViewsCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateViewsProjectIdentity(request.ProjectPath, request.ProjectXml,
            request.ProjectXmlSHA, request.ProjectRevision, request.ProjectSessionID,
            request.CoreLayerContext);
        ValidateCommitCapture(request);

        using OperationContext context = Open(request.ProjectPath, request.ProjectXml,
            request.ProjectXmlSHA, request.ExpectedVisualizationRevision, allowMissing: true,
            projectSessionID: request.ProjectSessionID.ToString("D"),
            coreLayerContext: request.CoreLayerContext);
        EnsureViewsProjectRevision(context, request.ProjectRevision, request.ProjectSessionID,
            request.CoreLayerContext);

        MapViewsInspectRequest inspectRequest = ToInspectRequest(request);
        MapViewsInspection inspection = InspectViewsCore(inspectRequest, context);
        if (!string.Equals(inspection.ViewsSHA256, request.ExpectedViewsSHA256,
                StringComparison.OrdinalIgnoreCase) ||
            !SameEntryRevision(inspection.Target, request.ExpectedEntrySHA256, request.Action))
            throw new SourceFileChangedException();
        if (!inspection.CanCommit)
            throw new ArgumentException(inspection.ValidationMessage ?? "The saved map-view change is invalid.");

        SavedMapView? before = inspection.Target;
        XElement root = context.Visualization.Root
            ?? throw new InvalidDataException("The visualization file has no Ras root element.");
        XElement views = EnsureViewsElement(root);
        ApplyViewChange(request, views, before);

        SavedMapView[] savedViews = ReadSavedViews(views);
        SavedMapView? after = request.Action switch
        {
            "create" => savedViews.Single(view => string.Equals(view.Name, NormalizeViewName(request.Name), StringComparison.Ordinal)),
            "rename" => savedViews.Single(view => string.Equals(view.Name, NormalizeViewName(request.Name), StringComparison.Ordinal)),
            "resetExtent" or "shortcut" => savedViews.Single(view => string.Equals(view.Name, request.TargetName, StringComparison.Ordinal)),
            "delete" => null,
            _ => throw new InvalidOperationException("The saved view action was not validated.")
        };
        string nextViewsSHA256 = HashViewsElement(views);
        string newVisualizationRevision = Commit(context);
        return new MapViewsCommitReceipt(true, request.Action, context.ProjectPath,
            request.ProjectXml, request.ProjectXmlSHA.ToUpperInvariant(), context.ProjectRevision,
            request.ProjectSessionID, context.PreviousVisualizationRevision, newVisualizationRevision,
            inspection.ViewsSHA256, nextViewsSHA256, savedViews, before, after);
    }

    private static MapViewsInspection InspectViewsCore(MapViewsInspectRequest request, OperationContext context)
    {
        XElement root = context.Visualization.Root
            ?? throw new InvalidDataException("The visualization file has no Ras root element.");
        XElement? viewsElement = FindViewsElement(root);
        SavedMapView[] views = ReadSavedViews(viewsElement);
        string viewsSHA256 = HashViewsElement(viewsElement);
        SavedMapView? target = null;
        string? validation = null;
        bool canCommit = false;
        string action = request.Action;

        if (action is not ("list" or "create" or "rename" or "resetExtent" or "delete" or "shortcut"))
        {
            validation = "Choose list, create, rename, resetExtent, delete, or shortcut.";
        }
        else if (action == "list")
        {
            validation = null;
        }
        else
        {
            if (action != "create")
            {
                if (string.IsNullOrWhiteSpace(request.TargetName))
                    validation = "Choose the saved view to change.";
                else
                {
                    SavedMapView[] targets = views.Where(view =>
                        string.Equals(view.Name, request.TargetName, StringComparison.Ordinal)).ToArray();
                    if (targets.Length != 1)
                        validation = "The selected saved view is missing or ambiguous. Refresh the saved-view list.";
                    else target = targets[0];
                }
            }

            string? newName = null;
            if (validation is null && action is ("create" or "rename"))
            {
                try { newName = NormalizeViewName(request.Name); }
                catch (ArgumentException error) { validation = error.Message; }
                if (validation is null && views.Any(view =>
                        !ReferenceEquals(view, target) &&
                        string.Equals(view.Name, newName, StringComparison.OrdinalIgnoreCase)))
                {
                    validation = "A saved view with that name already exists.";
                }
            }

            if (validation is null && action is ("create" or "resetExtent"))
            {
                try { ValidateExtent(request.Extent); }
                catch (ArgumentException error) { validation = error.Message; }
            }

            if (validation is null && action == "shortcut")
            {
                string? normalizedShortcut = null;
                try { normalizedShortcut = NormalizeShortcut(request.Shortcut); }
                catch (ArgumentException error) { validation = error.Message; }
                if (validation is null && normalizedShortcut is not null && normalizedShortcut != "None" && views.Any(view =>
                        !string.Equals(view.Name, target?.Name, StringComparison.Ordinal) &&
                        ShortcutConflicts(view.Shortcut, normalizedShortcut)))
                    validation = "That keyboard shortcut is already assigned to another saved view.";
            }

            if (validation is null && action == "create" &&
                request.Shortcut is { Length: > 0 } createShortcut)
            {
                try
                {
                    string normalizedShortcut = NormalizeShortcut(createShortcut);
                    if (normalizedShortcut != "None" && views.Any(view =>
                            ShortcutConflicts(view.Shortcut, normalizedShortcut)))
                        validation = "That keyboard shortcut is already assigned to another saved view.";
                }
                catch (ArgumentException error) { validation = error.Message; }
            }

            canCommit = validation is null;
        }

        return new MapViewsInspection(canCommit, validation, action, context.ProjectPath,
            request.ProjectXml, request.ProjectXmlSHA.ToUpperInvariant(), context.ProjectRevision,
            request.ProjectSessionID, context.PreviousVisualizationRevision, viewsSHA256, views, target);
    }

    private static MapViewsInspectRequest ToInspectRequest(MapViewsCommitRequest request) => new(
        request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA, request.ProjectRevision,
        request.ProjectSessionID, request.Action, request.TargetName, request.Name,
        request.Extent, request.Shortcut, request.CoreLayerContext);

    private static void ValidateCommitCapture(MapViewsCommitRequest request)
    {
        if (request.Action is not ("create" or "rename" or "resetExtent" or "delete" or "shortcut"))
            throw new ArgumentException("A saved-view commit must create, rename, resetExtent, delete, or change a shortcut.");
        if (!IsSha256(request.ExpectedViewsSHA256))
            throw new ArgumentException("Inspect the saved views before committing.");
        bool hasTarget = request.Action != "create";
        if (hasTarget && (string.IsNullOrWhiteSpace(request.TargetName) || !IsSha256(request.ExpectedEntrySHA256)))
            throw new ArgumentException("Inspect the selected saved view before changing it.");
        if (!hasTarget && (request.TargetName is not null || request.ExpectedEntrySHA256 is not null))
            throw new ArgumentException("Creating a saved view must not include a target identity.");
    }

    private static void ValidateViewsProjectIdentity(string projectPath, string projectXml,
        string projectXmlSHA, string projectRevision, Guid projectSessionID,
        CoreLayerRequestContext? coreLayerContext)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathFullyQualified(projectPath) ||
            !Path.GetExtension(projectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Open a saved .ras project before editing saved map views.");
        if (projectSessionID == Guid.Empty)
            throw new ArgumentException("A non-empty project session ID is required.");
        if (!IsSha256(projectRevision) || !IsSha256(projectXmlSHA) || string.IsNullOrEmpty(projectXml))
            throw new ArgumentException("The saved project XML and revision are required.");
        if (coreLayerContext is not null &&
            (!string.Equals(coreLayerContext.ProjectSessionID, projectSessionID.ToString("D"), StringComparison.Ordinal) ||
             !string.Equals(coreLayerContext.ProjectRevision, projectRevision, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();
    }

    private static void EnsureViewsProjectRevision(OperationContext context, string projectRevision,
        Guid projectSessionID, CoreLayerRequestContext? coreLayerContext)
    {
        if (!string.Equals(context.ProjectRevision, projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.ProjectSessionID, projectSessionID.ToString("D"), StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (coreLayerContext is not null &&
            (!string.Equals(coreLayerContext.ProjectSessionID, context.ProjectSessionID, StringComparison.Ordinal) ||
             !PathEquals(coreLayerContext.ProjectPath, context.ProjectPath) ||
             !string.Equals(coreLayerContext.ProjectRevision, context.ProjectRevision, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();
    }

    private static bool SameEntryRevision(SavedMapView? target, string? expectedEntryRevision, string action)
    {
        if (action == "create") return target is null && expectedEntryRevision is null;
        return target is not null && expectedEntryRevision is not null &&
               target.EntrySHA256.Equals(expectedEntryRevision, StringComparison.OrdinalIgnoreCase);
    }

    private static XElement? FindViewsElement(XElement root)
    {
        if (root.Name != "Ras") throw new InvalidDataException("The visualization file has an invalid Ras root element.");
        XElement[] views = root.Elements("Views").ToArray();
        if (views.Length > 1) throw new InvalidDataException("The visualization file contains duplicate Views collections.");
        return views.SingleOrDefault();
    }

    private static XElement EnsureViewsElement(XElement root)
    {
        XElement? views = FindViewsElement(root);
        if (views is not null) return views;
        views = new XElement("Views");
        root.Add(views);
        return views;
    }

    private static SavedMapView[] ReadSavedViews(XElement? viewsElement)
    {
        if (viewsElement is null) return [];
        var result = new List<SavedMapView>();
        foreach (XElement row in viewsElement.Elements("View"))
        {
            XElement[] names = row.Elements("Name").ToArray();
            XElement[] extents = row.Elements("Extent").ToArray();
            XElement[] bindings = row.Elements("Bind").ToArray();
            if (names.Length != 1 || extents.Length != 1 || bindings.Length != 1)
                throw new InvalidDataException("A saved View must contain exactly one Name, Extent, and Bind element.");
            SavedMapViewExtent extent = ParseExtent(extents[0]);
            string name = names[0].Value;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("A saved View has an empty Name element.");
            string shortcut = bindings[0].Value;
            if (string.IsNullOrWhiteSpace(shortcut))
                throw new InvalidDataException("A saved View has an empty Bind element.");
            result.Add(new SavedMapView(name, extent, shortcut, HashViewEntry(row)));
        }
        return result.ToArray();
    }

    private static SavedMapViewExtent ParseExtent(XElement extentElement)
    {
        double Read(string key)
        {
            string? value = (string?)extentElement.Attribute(key);
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number))
                throw new InvalidDataException($"A saved View has an invalid {key} extent coordinate.");
            return number;
        }
        var extent = new SavedMapViewExtent(Read("Left"), Read("Top"), Read("Right"), Read("Bottom"));
        if (extent.Left >= extent.Right || extent.Bottom >= extent.Top)
            throw new InvalidDataException("A saved View extent must have positive width and height.");
        return extent;
    }

    private static string HashViewsElement(XElement? viewsElement) =>
        HashText(viewsElement is null ? EmptyViewsHashMaterial : viewsElement.ToString(SaveOptions.DisableFormatting));

    private static string HashViewEntry(XElement entry) =>
        HashText(entry.ToString(SaveOptions.DisableFormatting));

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void ApplyViewChange(MapViewsCommitRequest request, XElement views, SavedMapView? before)
    {
        switch (request.Action)
        {
            case "create":
            {
                string name = NormalizeViewName(request.Name);
                SavedMapViewExtent extent = RequireValidExtent(request.Extent);
                string shortcut = request.Shortcut is null ? "None" : NormalizeShortcut(request.Shortcut);
                views.Add(SerializeView(name, extent, shortcut));
                break;
            }
            case "rename":
                RequiredTargetElement(views, before!, "Name").Value = NormalizeViewName(request.Name);
                break;
            case "resetExtent":
                WriteExtent(RequiredTargetElement(views, before!, "Extent"), RequireValidExtent(request.Extent));
                break;
            case "delete":
                FindTargetRow(views, before!).Remove();
                break;
            case "shortcut":
                RequiredTargetElement(views, before!, "Bind").Value = NormalizeShortcut(request.Shortcut);
                break;
            default:
                throw new ArgumentException("The saved-view action is not supported.");
        }
    }

    private static XElement SerializeView(string name, SavedMapViewExtent extent, string shortcut) => new("View",
        new XElement("Extent",
            new XAttribute("Left", extent.Left.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("Top", extent.Top.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("Right", extent.Right.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("Bottom", extent.Bottom.ToString("R", CultureInfo.InvariantCulture))),
        new XElement("Bind", shortcut),
        new XElement("Name", name));

    private static void WriteExtent(XElement element, SavedMapViewExtent extent)
    {
        element.SetAttributeValue("Left", extent.Left.ToString("R", CultureInfo.InvariantCulture));
        element.SetAttributeValue("Top", extent.Top.ToString("R", CultureInfo.InvariantCulture));
        element.SetAttributeValue("Right", extent.Right.ToString("R", CultureInfo.InvariantCulture));
        element.SetAttributeValue("Bottom", extent.Bottom.ToString("R", CultureInfo.InvariantCulture));
    }

    private static XElement FindTargetRow(XElement views, SavedMapView target)
    {
        XElement[] matches = views.Elements("View").Where(row =>
            string.Equals(row.Element("Name")?.Value, target.Name, StringComparison.Ordinal) &&
            HashViewEntry(row).Equals(target.EntrySHA256, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static XElement RequiredTargetElement(XElement views, SavedMapView target, string childName)
    {
        XElement row = FindTargetRow(views, target);
        return row.Elements(childName).SingleOrDefault() ?? throw new SourceFileChangedException();
    }

    private static void ValidateExtent(SavedMapViewExtent? extent)
    {
        _ = RequireValidExtent(extent);
    }

    private static SavedMapViewExtent RequireValidExtent(SavedMapViewExtent? extent)
    {
        if (extent is null || !double.IsFinite(extent.Left) || !double.IsFinite(extent.Top) ||
            !double.IsFinite(extent.Right) || !double.IsFinite(extent.Bottom) ||
            extent.Left >= extent.Right || extent.Bottom >= extent.Top)
            throw new ArgumentException("The map extent must have finite coordinates, positive width, and positive height.");
        return extent;
    }

    private static string NormalizeViewName(string? value)
    {
        if (value is null) throw new ArgumentException("Enter a saved view name.");
        string name = ValidateName(value, "saved view");
        if (name.Any(char.IsControl)) throw new ArgumentException("A saved view name cannot contain control characters.");
        return name;
    }

    private static string NormalizeShortcut(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Enter None or a valid VirtualKey shortcut.");
        string[] parts = value.Trim().Split('+');
        if (parts.Length == 1 && parts[0].Equals("None", StringComparison.OrdinalIgnoreCase)) return "None";
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A shortcut must be None, a VirtualKey, or one modifier plus a VirtualKey.");
        string modifier = "";
        string keyInput;
        if (parts.Length == 2)
        {
            modifier = parts[0].ToLowerInvariant() switch
            {
                "ctrl" or "control" => "Ctrl+",
                "menu" or "alt" => "Menu+",
                "shift" => "Shift+",
                "win" or "windows" => "Win+",
                _ => throw new ArgumentException("A shortcut modifier must be Ctrl, Menu, Shift, or Win.")
            };
            keyInput = parts[1];
        }
        else keyInput = parts[0];

        if (!VirtualKeyNames.TryGetValue(keyInput, out string? key))
            throw new ArgumentException("The shortcut key is not a supported Windows VirtualKey name.");
        if (key == "None") throw new ArgumentException("None cannot be combined with a shortcut modifier.");
        return modifier + key;
    }

    private static bool ShortcutConflicts(string existing, string requested)
    {
        if (existing.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return string.Equals(NormalizeShortcut(existing), requested, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // Existing files may contain a shortcut this cross-platform layer
            // cannot bind. Preserve it and avoid silently rewriting it.
            return false;
        }
    }

    private static Dictionary<string, string> BuildVirtualKeyNames()
    {
        string[] named =
        [
            "LeftButton", "RightButton", "Cancel", "MiddleButton", "XButton1", "XButton2", "Back", "Tab",
            "Clear", "Enter", "Shift", "Control", "Menu", "Pause", "CapitalLock", "Kana", "Hangul",
            "Junja", "Final", "Hanja", "Kanji", "Escape", "Convert", "NonConvert", "Accept", "ModeChange",
            "Space", "PageUp", "PageDown", "End", "Home", "Left", "Up", "Right", "Down", "Select", "Print",
            "Execute", "Snapshot", "Insert", "Delete", "Help", "LeftWindows", "RightWindows", "Application",
            "Sleep", "Multiply", "Add", "Separator", "Subtract", "Decimal", "Divide", "NumLock", "Scroll",
            "LeftShift", "RightShift", "LeftControl", "RightControl", "LeftMenu", "RightMenu", "BrowserBack",
            "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch", "BrowserFavorites", "BrowserHome",
            "VolumeMute", "VolumeDown", "VolumeUp", "MediaNextTrack", "MediaPreviousTrack", "MediaStop",
            "MediaPlayPause", "LaunchMail", "SelectMedia", "LaunchApplication1", "LaunchApplication2", "Oem1",
            "OemPlus", "OemComma", "OemMinus", "OemPeriod", "Oem2", "Oem3", "Oem4", "Oem5", "Oem6",
            "Oem7", "Oem8", "Oem102", "ImeProcessed", "System", "Attn", "CrSel", "ExSel", "EraseEof", "Play",
            "Zoom", "NoName", "Pa1", "OemClear", "NavigationView", "NavigationMenu", "NavigationUp",
            "NavigationDown", "NavigationLeft", "NavigationRight", "NavigationAccept", "NavigationCancel",
            "GamepadA", "GamepadB", "GamepadX", "GamepadY", "GamepadRightShoulder", "GamepadLeftShoulder",
            "GamepadLeftTrigger", "GamepadRightTrigger", "GamepadDPadUp", "GamepadDPadDown", "GamepadDPadLeft",
            "GamepadDPadRight", "GamepadMenu", "GamepadView", "GamepadLeftThumbstickButton",
            "GamepadRightThumbstickButton", "GamepadLeftThumbstickUp", "GamepadLeftThumbstickDown",
            "GamepadLeftThumbstickRight", "GamepadLeftThumbstickLeft", "GamepadRightThumbstickUp",
            "GamepadRightThumbstickDown", "GamepadRightThumbstickRight", "GamepadRightThumbstickLeft"
        ];
        var result = named.ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
        result["None"] = "None";
        for (char key = 'A'; key <= 'Z'; key++) result[key.ToString()] = key.ToString();
        for (int key = 0; key <= 9; key++)
        {
            result["Number" + key.ToString(CultureInfo.InvariantCulture)] = "Number" + key.ToString(CultureInfo.InvariantCulture);
            result["NumberPad" + key.ToString(CultureInfo.InvariantCulture)] = "NumberPad" + key.ToString(CultureInfo.InvariantCulture);
        }
        for (int key = 1; key <= 24; key++) result["F" + key.ToString(CultureInfo.InvariantCulture)] = "F" + key.ToString(CultureInfo.InvariantCulture);
        return result;
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
