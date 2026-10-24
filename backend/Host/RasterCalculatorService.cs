using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Layers;
using RasterTerrainInput = Ras.Layers.Terrains.RasterTerrainInput;
using Ras.Mapping;
using SourceRasterCalculatorInput = Geospatial.Rasters.RasterCalculatorInput;
using Utility;
using Utility.Logging;

namespace Ras.Backend;

/// <summary>
/// Read-only bridge to the retained RasterCalculator. Every operation resolves
/// input assets by canonical path and SHA, and never writes the project or .vis.
/// The returned frame is transient; native code owns its map-scoped lifetime.
/// </summary>
public static class RasterCalculatorService
{
    private const string TokenDomain = "HECRAS-RASTER-CALCULATOR-v1\0";
    private static readonly string[] ResultMapTypes =
        ["Depth", "WaterSurfaceElevation", "Velocity", "DepthTimesVelocity"];
    private static readonly string[] NoDataBehaviors = ["Allow", "WriteNDV", "WriteZero"];
    private static readonly string[] ReservedConstants = ["ndv", "pi", "e", "tau"];
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static RasterCalculatorInspectResult Inspect(RasterCalculatorInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using CalculatorContext context = Open(request.Map);
        RasterCalculatorSourceChoice[] sources = BuildChoices(context.Project);
        context.EnsureUnchanged(sources);
        string token = ContextToken(context, sources);
        return new(context.ProjectPath, context.ProjectRevision, context.ProjectXmlSHA,
            context.VisualizationRevision, request.Map.MapID, request.Map.MapIndex,
            request.Map.MapName, request.Map.MapType, token, "WriteNDV",
            NoDataBehaviors, ReservedConstants, sources);
    }

    public static RasterCalculatorEvaluateResult Evaluate(RasterCalculatorEvaluateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Recipe);
        ArgumentNullException.ThrowIfNull(request.Viewport);
        if (string.IsNullOrWhiteSpace(request.LayerID) || string.IsNullOrWhiteSpace(request.LayerName))
            throw new ArgumentException("A calculator layer identity and name are required.");
        ValidateViewport(request.Viewport);

        using CalculatorContext context = Open(request.Map);
        RasterCalculatorSourceChoice[] sources = BuildChoices(context.Project);
        context.EnsureUnchanged(sources);
        string contextToken = ContextToken(context, sources);
        if (!string.Equals(request.ExpectedContextToken, contextToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        RasterCalculator calculator = BuildCalculator(context.Project, sources, request.Recipe);
        if (request.ProfileIndex < 0 || request.ProfileIndex >= calculator.ProfileCount)
            throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex), "The selected calculator profile is no longer available.");

        // Keep the calculator's source API as the authority for the static
        // versus temporal surface type. This is the same surface branch used
        // by the original AddToMap and SaveToExisting routes.
        ISurface<float> surface = calculator.AsResampler();
        IMultiBandResampler<float>? dynamicResampler = surface as IMultiBandResampler<float>;
        if ((dynamicResampler is not null) != calculator.HasDynamicInputs)
            throw new InvalidDataException("The retained calculator returned an unexpected surface type.");
        IResample<float> profile = dynamicResampler is not null
            ? dynamicResampler.AsSingleProfile(request.ProfileIndex)
            : surface as IResample<float>
                ?? throw new InvalidDataException("The retained calculator returned no static raster surface.");
        Extent validExtent = calculator.ValidExtent;
        if (!validExtent.IsFiniteAndValid())
            throw new InvalidDataException("The retained calculator returned no finite output extent.");

        var renderer = new RasterRenderLayer(profile);
        if (dynamicResampler is not null)
        {
            // Match the original BandedRenderLayer: its palette is built once
            // from profile zero and reused while the selected profile changes.
            IResample<float> styleProfile = dynamicResampler.AsSingleProfile(0);
            double cellSize = styleProfile.ValidExtent.Width / 100.0;
            if (!double.IsFinite(cellSize) || cellSize <= 0)
                throw new InvalidDataException("The retained calculator output extent cannot be styled.");
            Histogram histogram = Histogram.ComputeFrom(styleProfile, cellSize, 256);
            renderer.RasterSymbology = new RasterSymbology(
                new InterpolatedSurfaceFill(ColorPalettes.WaterSurface, histogram));
        }
        else
        {
            renderer.SetDefaultTerrainHistoSymbology();
        }

        RasterDefinition frame = new(new Extent(request.Viewport.MinX, request.Viewport.MaxX,
            request.Viewport.MinY, request.Viewport.MaxY), request.Viewport.PixelHeight,
            request.Viewport.PixelWidth);
        if (!frame.IsValid() || frame.CellCount <= 0)
            throw new ArgumentException("The calculator viewport cannot be represented as a raster.");
        using PlotRequest plot = renderer.Render(frame);
        if (plot.ColorBufferARGB.Length != frame.CellCount)
            throw new InvalidDataException("The retained raster renderer returned an incomplete calculator frame.");
        byte[] rgba = new byte[checked(frame.CellCount * 4)];
        for (int i = 0; i < plot.ColorBufferARGB.Length; i++)
        {
            uint argb = unchecked((uint)plot.ColorBufferARGB.Span[i]);
            rgba[i * 4] = (byte)(argb >> 16);
            rgba[i * 4 + 1] = (byte)(argb >> 8);
            rgba[i * 4 + 2] = (byte)argb;
            rgba[i * 4 + 3] = (byte)(argb >> 24);
        }

        context.EnsureUnchanged(sources);
        string? selectedTime = dynamicResampler is IDiscreteTimesteps discrete
            && discrete.Times is { } times && request.ProfileIndex < times.Count
            ? times[request.ProfileIndex].ToString("O", CultureInfo.InvariantCulture) : null;
        RasterCalculatorProfile[] profiles = dynamicResampler is IDiscreteTimesteps allTimes
            && allTimes.Times is { } profileTimes && profileTimes.Count == calculator.ProfileCount
            ? profileTimes.Select((time, index) => new RasterCalculatorProfile(index,
                time.ToString("O", CultureInfo.InvariantCulture))).ToArray()
            : Enumerable.Range(0, calculator.ProfileCount).Select(index => new RasterCalculatorProfile(index,
                index.ToString(CultureInfo.InvariantCulture))).ToArray();
        var layer = new RasterCalculatorLayerPayload(request.LayerID, request.Map.MapID,
            request.LayerName, frame.Cols, frame.Rows, rgba, frame.MinX, frame.MinY, frame.MaxX, frame.MaxY);
        return new(context.ProjectPath, context.ProjectRevision, context.ProjectXmlSHA,
            context.VisualizationRevision, request.Map.MapID, request.Map.MapIndex,
            request.Map.MapName, request.Map.MapType, contextToken, request.LayerID,
            request.LayerName, request.Recipe, calculator.HasDynamicInputs,
            calculator.ProfileCount, request.ProfileIndex, selectedTime, profiles,
            validExtent.MinX, validExtent.MinY, validExtent.MaxX, validExtent.MaxY, layer, sources);
    }

    private static CalculatorContext Open(MapTreeRequest map)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        if (string.IsNullOrWhiteSpace(map.ProjectPath) || string.IsNullOrWhiteSpace(map.ProjectXml) ||
            string.IsNullOrWhiteSpace(map.ProjectXmlSHA))
            throw new ArgumentException("A saved project and current project XML snapshot are required.");
        if (map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("Raster Calculator requires an active saved Project, Buildable, Geometry, or Plan map.");

        MapTreeSnapshot tree = MapTreeService.Load(map);
        string projectPath = SourceFilePaths.Canonical(map.ProjectPath);
        string projectRevision = RevisionFile(projectPath);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath)) throw new FileNotFoundException("The saved map visualization file was not found.", visualizationPath);
        string visualizationRevision = RevisionFile(visualizationPath);
        if (!visualizationRevision.Equals(tree.VisualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            !visualizationRevision.Equals(map.ExpectedVisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectPath,
            XElement.Parse(map.ProjectXml, LoadOptions.None), map.CoreLayerContext);
        try
        {
            return new CalculatorContext(project, map, projectPath, projectRevision,
                map.ProjectXmlSHA!, visualizationPath, visualizationRevision);
        }
        catch
        {
            MapResultChartService.DisposeProject(project);
            throw;
        }
    }

    private static RasterCalculatorSourceChoice[] BuildChoices(Project project)
    {
        var choices = new List<RasterCalculatorSourceChoice>();
        foreach (Terrain terrain in project.Terrains)
        {
            string assetPath = SourceFilePaths.Canonical(terrain.Filename);
            RasterCalculatorSourceFile[] files = TerrainFiles(terrain);
            bool available = false;
            string? reason = null;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            try
            {
                IResample<float> raster = terrain;
                Extent extent = raster.ValidExtent;
                if (raster.NoDataValue is null || !extent.IsFiniteAndValid())
                    throw new InvalidDataException("The Terrain source has no usable raster extent.");
                minX = extent.MinX; minY = extent.MinY; maxX = extent.MaxX; maxY = extent.MaxY;
                available = files.All(file => file.Exists && file.Revision is not null);
                if (!available) reason = "One or more Terrain source files are unavailable.";
            }
            catch (Exception error)
            {
                reason = error.Message;
            }
            choices.Add(new(SourceID("terrain", assetPath), "terrain", terrain.Name, assetPath,
                AggregateRevision(files), files, available, reason, [], 1, [], terrain.NoDataValue,
                minX, minY, maxX, maxY));
        }

        foreach (Result result in project.Results.Where(item => !item.IsInMemory && !string.IsNullOrWhiteSpace(item.Filename)))
        {
            string assetPath = SourceFilePaths.Canonical(result.Filename);
            RasterCalculatorSourceFile[] files = ResultFiles(result);
            bool available = false;
            string? reason = null;
            int profileCount = 0;
            string[] times = [];
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            try
            {
                if (result.BaseOutputBlock is null || result.AssociatedTerrain is null || result.Geometry?.FlowAreaLayer is null)
                    throw new InvalidDataException("The Result has no map outputs, associated Terrain, or Geometry.");
                IResample<float> terrain = result.AssociatedTerrain;
                Extent extent = new ResultMapResampler(result, MapTypes.Depth).ValidExtent;
                if (!extent.IsFiniteAndValid()) throw new InvalidDataException("The Result map has no finite extent.");
                minX = extent.MinX; minY = extent.MinY; maxX = extent.MaxX; maxY = extent.MaxY;
                profileCount = result.BaseOutputBlock.ProfileCount;
                if (profileCount <= 0) throw new InvalidDataException("The Result has no map profiles.");
                times = result.BaseOutputBlock is IDiscreteTimesteps discrete && discrete.Times is { } profileTimes
                    ? profileTimes.Select(time => time.ToString("O", CultureInfo.InvariantCulture)).ToArray()
                    : Enumerable.Range(0, profileCount).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray();
                if (times.Length != profileCount) throw new InvalidDataException("The Result profile times do not match its map outputs.");
                available = files.All(file => file.Exists && file.Revision is not null);
                if (!available) throw new InvalidDataException("One or more Result or associated Terrain source files are unavailable.");
                _ = terrain.ValidExtent;
            }
            catch (Exception error)
            {
                reason = error.Message;
            }
            choices.Add(new(SourceID("resultMap", assetPath), "resultMap", result.Name, assetPath,
                AggregateRevision(files), files, available, reason, ResultMapTypes, profileCount, times,
                -9999d, minX, minY, maxX, maxY));
        }
        return choices.OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.AssetPath, PathComparer).ToArray();
    }

    private static RasterCalculatorSourceFile[] TerrainFiles(Terrain terrain)
    {
        var paths = new Dictionary<string, (string Path, string Role)>(PathComparer)
        {
            [SourceFilePaths.Canonical(terrain.Filename)] = (SourceFilePaths.Canonical(terrain.Filename), "terrain-hdf")
        };
        foreach (RasterTerrainInput input in terrain.AllRasterInputs())
        {
            string canonical = SourceFilePaths.Canonical(input.Filename);
            paths.TryAdd(canonical, (canonical, "terrain-raster"));
        }
        return CaptureFiles(paths.Values);
    }

    private static RasterCalculatorSourceFile[] ResultFiles(Result result)
    {
        var paths = new Dictionary<string, (string Path, string Role)>(PathComparer);
        void Add(string? path, string role)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string canonical = SourceFilePaths.Canonical(path);
            paths.TryAdd(canonical, (canonical, role));
        }
        Add(result.Filename, "result-hdf");
        Add(result.Geometry?.Filename, "result-geometry-hdf");
        if (result.AssociatedTerrain is { } terrain)
        {
            Add(terrain.Filename, "associated-terrain-hdf");
            foreach (RasterTerrainInput input in terrain.AllRasterInputs()) Add(input.Filename, "associated-terrain-raster");
        }
        return CaptureFiles(paths.Values);
    }

    private static RasterCalculatorSourceFile[] CaptureFiles(IEnumerable<(string Path, string Role)> inputs) =>
        inputs.OrderBy(item => item.Path, PathComparer).Select(item =>
        {
            bool exists = File.Exists(item.Path);
            return new RasterCalculatorSourceFile(item.Path, item.Role, exists,
                exists ? RevisionFile(item.Path) : null);
        }).ToArray();

    private static string AggregateRevision(IReadOnlyList<RasterCalculatorSourceFile> files)
    {
        string material = string.Join("\n", files.OrderBy(item => item.Path, PathComparer)
            .Select(item => string.Join("\0", item.Path, item.Role, item.Exists ? "1" : "0", item.Revision ?? "")));
        return Hash(Encoding.UTF8.GetBytes("HECRAS-RASTER-CALCULATOR-SOURCES-v1\0" + material));
    }

    private static string SourceID(string kind, string path) => Hash(Encoding.UTF8.GetBytes(
        "HECRAS-RASTER-CALCULATOR-SOURCE-ID-v1\0" + kind + "\0" + path));

    private static string ContextToken(CalculatorContext context, RasterCalculatorSourceChoice[] choices)
    {
        var identity = new
        {
            domain = TokenDomain,
            projectPath = context.ProjectPath,
            projectRevision = context.ProjectRevision,
            projectXmlSHA = context.ProjectXmlSHA,
            visualizationRevision = context.VisualizationRevision,
            coreLayerContext = context.Map.CoreLayerContext?.IdentityToken,
            context.Map.MapID, context.Map.MapIndex, context.Map.MapName, context.Map.MapType,
            sources = choices.Select(choice => new { choice.Id, choice.Kind, choice.AssetPath, choice.Revision,
                files = choice.Files.Select(file => new { file.Path, file.Role, file.Exists, file.Revision }) })
        };
        return Hash(JsonSerializer.SerializeToUtf8Bytes(identity, BackendJson.Options));
    }

    private static RasterCalculator BuildCalculator(Project project, RasterCalculatorSourceChoice[] choices,
        RasterCalculatorRecipe recipe)
    {
        if (recipe.Inputs is not { Length: > 0 }) throw new ArgumentException("Add at least one input layer.");
        if (string.IsNullOrWhiteSpace(recipe.Expression)) throw new ArgumentException("Enter an expression.");
        if (!Enum.TryParse(recipe.NoDataBehavior, ignoreCase: false, out CalculatorNdvBehavior behavior) ||
            !Enum.IsDefined(behavior)) throw new ArgumentException("The selected NoData behavior is not supported.");

        var inputs = new List<SourceRasterCalculatorInput>(recipe.Inputs.Length);
        var constants = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["ndv"] = -9999f
        };
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitKeyFrames = recipe.Inputs.Where(input => input.IsKeyFrame).ToArray();
        if (explicitKeyFrames.Length > 1) throw new ArgumentException("Only one Result input can drive the calculator timesteps.");
        string? keyFrameName = recipe.KeyFrameVariableName;
        if (explicitKeyFrames.Length == 1)
        {
            if (!string.IsNullOrWhiteSpace(keyFrameName) && !string.Equals(keyFrameName,
                    explicitKeyFrames[0].VariableName, StringComparison.Ordinal))
                throw new ArgumentException("The selected key-frame input no longer matches the calculator recipe.");
            keyFrameName = explicitKeyFrames[0].VariableName;
        }

        foreach (RasterCalculatorInput input in recipe.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.VariableName)) throw new ArgumentException("All inputs must have a variable name.");
            if (!variableNames.Add(input.VariableName)) throw new ArgumentException("Duplicate variable names are not allowed.");
            if (ReservedConstants.Contains(input.VariableName, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Variable name '{input.VariableName}' is reserved. Choose a different name.");
            RasterCalculatorSourceChoice choice = choices.SingleOrDefault(item => item.Id == input.SourceID)
                ?? throw new SourceFileChangedException();
            if (!choice.IsAvailable || !choice.Revision.Equals(input.SourceRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            if (!string.Equals(choice.Kind, input.Kind, StringComparison.Ordinal))
                throw new ArgumentException("The selected calculator input type no longer matches its source.");

            if (choice.Kind == "terrain")
            {
                if (input.MapType is not null || input.ForceHorizontal || input.IsKeyFrame)
                    throw new ArgumentException("Terrain inputs cannot use Result map options or drive calculator timesteps.");
                Terrain[] matches = project.Terrains.Where(item => PathComparer.Equals(
                    SourceFilePaths.Canonical(item.Filename), choice.AssetPath)).ToArray();
                if (matches.Length != 1) throw new SourceFileChangedException();
                Terrain terrain = matches[0];
                inputs.Add(SourceRasterCalculatorInput.FromStatic(input.VariableName, terrain));
                AddNoDataConstant(constants, input.VariableName, terrain.NoDataValue);
            }
            else if (choice.Kind == "resultMap")
            {
                if (input.IsKeyFrame && !string.Equals(keyFrameName, input.VariableName, StringComparison.Ordinal))
                    throw new ArgumentException("The key-frame input must be one selected Result map.");
                Result[] matches = project.Results.Where(item => !item.IsInMemory && !string.IsNullOrWhiteSpace(item.Filename)
                    && PathComparer.Equals(SourceFilePaths.Canonical(item.Filename), choice.AssetPath)).ToArray();
                if (matches.Length != 1) throw new SourceFileChangedException();
                if (input.MapType is null || !ResultMapTypes.Contains(input.MapType, StringComparer.Ordinal))
                    throw new ArgumentException("Choose one of the supported Result map types.");
                if (input.IsKeyFrame && !string.Equals(keyFrameName, input.VariableName, StringComparison.Ordinal))
                    throw new ArgumentException("The key-frame input must be one selected Result map.");
                MapTypes mapType = input.MapType switch
                {
                    "Depth" => MapTypes.Depth,
                    "WaterSurfaceElevation" => MapTypes.WaterSurfaceElevation,
                    "Velocity" => MapTypes.Velocity,
                    "DepthTimesVelocity" => MapTypes.DepthTimesVelocity,
                    _ => throw new ArgumentException("The selected Result map type is not supported.")
                };
                ResultMapResampler resampler = input.ForceHorizontal
                    ? new ResultMapResampler(matches[0], mapType, () => GeometrySamplingParameters.ForceHorizontalMode)
                    : new ResultMapResampler(matches[0], mapType);
                inputs.Add(SourceRasterCalculatorInput.FromDynamic(input.VariableName, resampler));
                AddNoDataConstant(constants, input.VariableName, resampler.NoDataValue);
            }
            else
            {
                throw new ArgumentException("The selected calculator input type is not supported.");
            }
        }

        foreach (RasterCalculatorConstant constant in recipe.Constants ?? [])
        {
            string name = constant.Name?.Trim() ?? string.Empty;
            if (name.Length == 0) throw new ArgumentException("All constants must have a name.");
            if (variableNames.Contains(name)) throw new ArgumentException($"Constant '{name}' collides with an input variable name.");
            if (ReservedConstants.Contains(name, StringComparer.OrdinalIgnoreCase) || name.EndsWith("_ndv", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Constant name '{name}' is reserved.");
            if (constants.ContainsKey(name)) throw new ArgumentException($"Duplicate constant name '{name}'.");
            if (!float.TryParse(constant.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new ArgumentException($"Constant '{name}' has an invalid numeric value '{constant.Value}'.");
            constants.Add(name, value);
        }

        if (string.IsNullOrWhiteSpace(keyFrameName) && inputs.Any(input => input.IsDynamic))
            keyFrameName = inputs.First(input => input.IsDynamic).VariableName;
        OperationResult operation = RasterCalculator.TryCreate(recipe.Expression, inputs, constants,
            keyFrameName, out RasterCalculator calculator);
        if (!operation.Result)
            throw new ArgumentException(string.Join(Environment.NewLine,
                operation.Messages.Select(message => message.EvaluatedMessage)));
        calculator.NoDataBehavior = behavior;
        return calculator;
    }

    private static void AddNoDataConstant(IDictionary<string, float> constants, string variableName, float? noData)
    {
        if (noData is float value && value != -9999f)
        {
            string name = variableName + "_ndv";
            if (constants.ContainsKey(name)) throw new ArgumentException($"Duplicate constant name '{name}'.");
            constants.Add(name, value);
        }
    }

    private static void ValidateViewport(RasterMapViewExtent viewport)
    {
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MinX >= viewport.MaxX || viewport.MinY >= viewport.MaxY ||
            viewport.PixelWidth is < 1 or > 1536 || viewport.PixelHeight is < 1 or > 1536 ||
            (long)viewport.PixelWidth * viewport.PixelHeight > 2_097_152)
            throw new ArgumentException("The calculator viewport is invalid or too large.");
    }

    private static string RevisionFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class CalculatorContext : IDisposable
    {
        private bool _disposed;
        public Project Project { get; }
        public MapTreeRequest Map { get; }
        public string ProjectPath { get; }
        public string ProjectRevision { get; }
        public string ProjectXmlSHA { get; }
        public string VisualizationPath { get; }
        public string VisualizationRevision { get; }

        public CalculatorContext(Project project, MapTreeRequest map, string projectPath,
            string projectRevision, string projectXmlSHA, string visualizationPath, string visualizationRevision)
        {
            Project = project; Map = map; ProjectPath = projectPath; ProjectRevision = projectRevision;
            ProjectXmlSHA = projectXmlSHA; VisualizationPath = visualizationPath;
            VisualizationRevision = visualizationRevision;
        }

        public void EnsureUnchanged(RasterCalculatorSourceChoice[] choices)
        {
            if (!RevisionFile(ProjectPath).Equals(ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
                !RevisionFile(VisualizationPath).Equals(VisualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            foreach (RasterCalculatorSourceChoice choice in choices)
            {
                RasterCalculatorSourceFile[] current = choice.Kind switch
                {
                    "terrain" => Project.Terrains.Where(item => PathComparer.Equals(SourceFilePaths.Canonical(item.Filename), choice.AssetPath))
                        .SingleOrDefault() is { } terrain ? TerrainFiles(terrain) : [],
                    "resultMap" => Project.Results.Where(item => !item.IsInMemory && !string.IsNullOrWhiteSpace(item.Filename)
                        && PathComparer.Equals(SourceFilePaths.Canonical(item.Filename), choice.AssetPath))
                        .SingleOrDefault() is { } result ? ResultFiles(result) : [],
                    _ => []
                };
                if (!AggregateRevision(current).Equals(choice.Revision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            MapResultChartService.DisposeProject(Project);
        }
    }
}
