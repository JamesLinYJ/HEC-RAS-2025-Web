using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ras.Hydraulics.HydraulicDesign;
using Ras.Layers;

namespace Ras.Backend;

/// <summary>
/// Stateless request handlers for the retained Stable Channel debug window.
/// Computation uses the source TractiveForceMethod itself; only persisted
/// designs can be saved, and saves replace one captured .scd.h5 file atomically.
/// </summary>
public static class StableChannelDebugService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.Ordinal);
    private static readonly string[] SolveTargets = ["Depth", "Width", "Slope", "ParticleSize"];

    public static StableChannelDebugOpenResponse Open(StableChannelDebugOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestID)) throw new ArgumentException("A request identity is required.");

        if (string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            string sourceID = NormalizeMemorySourceID(request.InMemorySourceID);
            StableChannelDebugState state = request.InMemoryState is null
                ? DefaultState()
                : EnsureParts(request.InMemoryState);
            return new StableChannelDebugOpenResponse(request.RequestID,
                new StableChannelDebugSource(sourceID, null, null, null, null, InMemory: true), state);
        }

        string requestedPath = Path.GetFullPath(request.ProjectPath);
        Project project = CoreLayerImportService.OpenProject(requestedPath, null,
            request.CoreLayerContext);
        try
        {
            string projectPath = Path.GetFullPath(project.Filename);
            string projectRevision = Revision(projectPath);
            StableChannelDesign? design = project.HydraulicDesign.StableChannelDesigns.FirstOrDefault();
            if (design is null)
            {
                string sourceID = NormalizeMemorySourceID(request.InMemorySourceID);
                StableChannelDebugState state = request.InMemoryState is null
                    ? DefaultState()
                    : EnsureParts(request.InMemoryState);
                return new StableChannelDebugOpenResponse(request.RequestID,
                    new StableChannelDebugSource(sourceID, projectPath, projectRevision, null, null, InMemory: true), state);
            }

            string designPath = Path.GetFullPath(design.Filename);
            return new StableChannelDebugOpenResponse(request.RequestID,
                new StableChannelDebugSource(PersistedSourceID(projectPath, designPath), projectPath,
                    projectRevision, designPath, Revision(designPath), InMemory: false),
                ReadState(design.TractiveForce));
        }
        finally { MapResultChartService.DisposeProject(project); }
    }

    public static StableChannelDebugComputeResponse Compute(StableChannelDebugComputeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestID)) throw new ArgumentException("A request identity is required.");
        if (request.Source is null || string.IsNullOrWhiteSpace(request.Source.SourceID))
            throw new ArgumentException("The Stable Channel source identity is missing.");
        if (request.State is null) throw new ArgumentException("The Stable Channel draft is missing.");

        string? sourceError = ValidateComputeSource(request.Source, request.CoreLayerContext);
        if (sourceError is not null)
            return Rejected(request, request.SolveFor ?? [], sourceError);

        string[] selected = request.SolveFor ?? [];
        string? selectionError = ValidateSolveTargets(selected);
        if (selectionError is not null)
            return Rejected(request, selected, selectionError);

        StableChannelDebugState state = EnsureParts(request.State);
        string? inputError = ValidateInputs(state, selected);
        if (inputError is not null)
            return Rejected(request, selected, inputError);

        TractiveForceMethod method = ToMethod(state);
        var targets = new HashSet<string>(selected, StringComparer.Ordinal);
        try
        {
            if (targets.SetEquals(["Depth", "Width"]))
            {
                method.SolveForDepthAndWidth(out double depth, out double width);
                method.Depth = depth;
                method.Width = width;
            }
            else if (targets.SetEquals(["Depth", "Slope"]))
            {
                method.SolveForDepthAndSlope(out double depth, out double slope);
                method.Depth = depth;
                method.Slope = slope;
            }
            else if (targets.SetEquals(["Depth", "ParticleSize"]))
            {
                method.SolveForDepthAndD(out double depth, out double[] values);
                method.Depth = depth;
                method.LeftBank.D = values[0];
                method.Bed.D = values[1];
                method.RightBank.D = values[2];
            }
            else if (targets.SetEquals(["Width", "Slope"]))
            {
                method.SolveForWidthAndSlope(out double width, out double slope);
                method.Width = width;
                method.Slope = slope;
            }
            else if (targets.SetEquals(["Width", "ParticleSize"]))
            {
                method.SolveForWidthAndD(out double width, out double[] values);
                method.Width = width;
                method.LeftBank.D = values[0];
                method.Bed.D = values[1];
                method.RightBank.D = values[2];
            }
            else if (targets.SetEquals(["Slope", "ParticleSize"]))
            {
                method.SolveForSlopeAndD(out double slope, out double[] values);
                method.Slope = slope;
                method.LeftBank.D = values[0];
                method.Bed.D = values[1];
                method.RightBank.D = values[2];
            }
            else
            {
                return Rejected(request, selected, "unsupportedSolveFor");
            }
        }
        catch (Exception)
        {
            return Rejected(request, selected, "solverFailed");
        }

        StableChannelDebugState solved = ReadState(method);
        if (!SolvedValuesAreFinite(solved, selected))
            return Rejected(request, selected, "nonFiniteResult");

        // The request is tied to both the project and design file revisions.
        // Recheck after the source solve so a file replaced during calculation
        // cannot produce a result that appears current in the native window.
        sourceError = ValidateComputeSource(request.Source, request.CoreLayerContext);
        if (sourceError is not null)
            return Rejected(request, selected, sourceError);

        return new StableChannelDebugComputeResponse(request.RequestID, request.Source.SourceID,
            solved, selected, Computed: true, ErrorCode: null);
    }

    public static StableChannelDebugSaveResponse Save(StableChannelDebugSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestID)) throw new ArgumentException("A request identity is required.");
        if (request.Source is null || request.Source.InMemory ||
            string.IsNullOrWhiteSpace(request.Source.ProjectPath) ||
            string.IsNullOrWhiteSpace(request.Source.ProjectRevision) ||
            string.IsNullOrWhiteSpace(request.Source.DesignPath) ||
            string.IsNullOrWhiteSpace(request.Source.DesignRevision))
            throw new InvalidOperationException("Only an existing Stable Channel design file can be saved.");
        if (request.State is null) throw new ArgumentException("The Stable Channel draft is missing.");

        string projectPath = Path.GetFullPath(request.Source.ProjectPath);
        string designPath = Path.GetFullPath(request.Source.DesignPath);
        SemaphoreSlim saveLock = SaveLocks.GetOrAdd(designPath, _ => new SemaphoreSlim(1, 1));
        saveLock.Wait();
        try
        {
            EnsureRevision(projectPath, request.Source.ProjectRevision, "projectChanged");
            EnsureRevision(designPath, request.Source.DesignRevision, "designChanged");

            Project project = CoreLayerImportService.OpenProject(projectPath, null,
                request.CoreLayerContext);
            try
            {
                StableChannelDesign? design = project.HydraulicDesign.StableChannelDesigns.FirstOrDefault();
                if (design is null || !SamePath(design.Filename, designPath))
                    throw new InvalidOperationException("The captured Stable Channel design is no longer the project's first design.");
                if (!string.Equals(PersistedSourceID(projectPath, designPath), request.Source.SourceID, StringComparison.Ordinal))
                    throw new InvalidOperationException("The Stable Channel source identity changed.");

                ApplyState(design.TractiveForce, EnsureParts(request.State));
                string directory = Path.GetDirectoryName(designPath)
                    ?? throw new InvalidOperationException("The Stable Channel design has no parent directory.");
                string stagingPath = Path.Combine(directory,
                    "." + Path.GetFileName(designPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.Copy(designPath, stagingPath, overwrite: false);
                    using (var writer = new H5Assist.H5Writer(stagingPath))
                        design.SaveAs(writer);

                    EnsureRevision(projectPath, request.Source.ProjectRevision, "projectChanged");
                    EnsureRevision(designPath, request.Source.DesignRevision, "designChanged");
                    File.Move(stagingPath, designPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(stagingPath)) File.Delete(stagingPath);
                }

                return new StableChannelDebugSaveResponse(request.RequestID, request.Source.SourceID,
                    designPath, Revision(designPath), ReadState(design.TractiveForce));
            }
            finally { MapResultChartService.DisposeProject(project); }
        }
        finally
        {
            saveLock.Release();
        }
    }

    private static StableChannelDebugComputeResponse Rejected(
        StableChannelDebugComputeRequest request, string[] selected, string errorCode) =>
        new(request.RequestID, request.Source.SourceID, request.State, selected, Computed: false, errorCode);

    private static string? ValidateSolveTargets(string[] selected)
    {
        if (selected.Length != 2 || selected.Distinct(StringComparer.Ordinal).Count() != 2)
            return "selectExactlyTwoTargets";
        return selected.All(target => SolveTargets.Contains(target, StringComparer.Ordinal))
            ? null
            : "unsupportedSolveFor";
    }

    private static string? ValidateInputs(StableChannelDebugState state, string[] selected)
    {
        if (!Enum.TryParse(state.SolutionMethod, ignoreCase: false, out TractiveForceSolutionMethod solutionMethod) ||
            !Enum.IsDefined(solutionMethod))
            return "invalidSolutionMethod";

        StableChannelDebugPartState?[] parts = [state.LeftBank, state.Bed, state.RightBank];
        if (parts.Any(part => part is null)) return "missingPart";
        foreach (StableChannelDebugPartState part in parts.Select(part => part!))
        {
            if (!Enum.TryParse(part.Equation, ignoreCase: false, out BankEquation equation) || !Enum.IsDefined(equation))
                return "invalidEquation";
            if (!IsFinite(part.AngleOfRepose) || !IsFinite(part.Roughness) || !IsFinite(part.SpecificGravity))
                return "missingInput";
            if (!selected.Contains("ParticleSize", StringComparer.Ordinal) && !IsFinite(part.D))
                return "missingInput";
            if (solutionMethod == TractiveForceSolutionMethod.UserDefinedMobility && !IsFinite(part.ShieldsParameter))
                return "missingInput";
        }

        if (!IsFinite(state.Discharge) || !IsFinite(state.LeftBankSideSlope) ||
            !IsFinite(state.RightBankSideSlope) || !IsFinite(state.Temperature))
            return "missingInput";
        if (state.Temperature is < 40 or > 120)
            return "temperatureOutsideSourceRange";

        if (!selected.Contains("Depth", StringComparer.Ordinal) && !IsFinite(state.Depth)) return "missingInput";
        if (!selected.Contains("Width", StringComparer.Ordinal) && !IsFinite(state.Width)) return "missingInput";
        if (!selected.Contains("Slope", StringComparer.Ordinal) && !IsFinite(state.Slope)) return "missingInput";
        return null;
    }

    private static bool SolvedValuesAreFinite(StableChannelDebugState state, string[] selected)
    {
        if (selected.Contains("Depth", StringComparer.Ordinal) && !IsFinite(state.Depth)) return false;
        if (selected.Contains("Width", StringComparer.Ordinal) && !IsFinite(state.Width)) return false;
        if (selected.Contains("Slope", StringComparer.Ordinal) && !IsFinite(state.Slope)) return false;
        if (selected.Contains("ParticleSize", StringComparer.Ordinal) &&
            (!IsFinite(state.LeftBank?.D) || !IsFinite(state.Bed?.D) || !IsFinite(state.RightBank?.D))) return false;
        return true;
    }

    private static bool IsFinite(double? value) => value.HasValue && double.IsFinite(value.Value);

    private static StableChannelDebugState DefaultState() => ReadState(new TractiveForceMethod
    {
        LeftBank = new TractiveForceMethodPart(),
        Bed = new TractiveForceMethodPart(),
        RightBank = new TractiveForceMethodPart()
    });

    private static StableChannelDebugState EnsureParts(StableChannelDebugState state) => state with
    {
        LeftBank = state.LeftBank ?? new StableChannelDebugPartState(0, 0, 0, 0, 0, nameof(BankEquation.Strickler)),
        Bed = state.Bed ?? new StableChannelDebugPartState(0, 0, 0, 0, 0, nameof(BankEquation.Strickler)),
        RightBank = state.RightBank ?? new StableChannelDebugPartState(0, 0, 0, 0, 0, nameof(BankEquation.Strickler))
    };

    private static StableChannelDebugState ReadState(TractiveForceMethod method) => new(
        ToJSONNumber(method.Depth),
        ToJSONNumber(method.Discharge),
        ToJSONNumber(method.Slope),
        ToJSONNumber(method.LeftBankSideSlope),
        ToJSONNumber(method.RightBankSideSlope),
        ToJSONNumber(method.Temperature),
        ToJSONNumber(method.Width),
        method.SolutionMethod.ToString(),
        ReadPart(method.LeftBank),
        ReadPart(method.Bed),
        ReadPart(method.RightBank));

    private static StableChannelDebugPartState ReadPart(TractiveForceMethodPart? part)
    {
        part ??= new TractiveForceMethodPart();
        return new StableChannelDebugPartState(ToJSONNumber(part.D), ToJSONNumber(part.AngleOfRepose),
            ToJSONNumber(part.Roughness), ToJSONNumber(part.SpecificGravity),
            ToJSONNumber(part.ShieldsParameter), part.Equation.ToString());
    }

    private static double? ToJSONNumber(double value) => double.IsFinite(value) ? value : null;
    private static double FromJSONNumber(double? value) => value ?? double.NaN;

    private static TractiveForceMethod ToMethod(StableChannelDebugState state)
    {
        state = EnsureParts(state);
        if (!Enum.TryParse(state.SolutionMethod, ignoreCase: false, out TractiveForceSolutionMethod solutionMethod) ||
            !Enum.IsDefined(solutionMethod))
            throw new ArgumentException("Unsupported Stable Channel solution method.");
        return new TractiveForceMethod
        {
            LeftBank = ToPart(state.LeftBank!),
            Bed = ToPart(state.Bed!),
            RightBank = ToPart(state.RightBank!),
            Depth = FromJSONNumber(state.Depth),
            Discharge = FromJSONNumber(state.Discharge),
            Slope = FromJSONNumber(state.Slope),
            LeftBankSideSlope = FromJSONNumber(state.LeftBankSideSlope),
            RightBankSideSlope = FromJSONNumber(state.RightBankSideSlope),
            Temperature = FromJSONNumber(state.Temperature),
            Width = FromJSONNumber(state.Width),
            SolutionMethod = solutionMethod
        };
    }

    private static TractiveForceMethodPart ToPart(StableChannelDebugPartState part)
    {
        if (!Enum.TryParse(part.Equation, ignoreCase: false, out BankEquation equation) ||
            !Enum.IsDefined(equation))
            throw new ArgumentException("Unsupported Stable Channel roughness equation.");
        return new TractiveForceMethodPart
        {
            D = FromJSONNumber(part.D),
            AngleOfRepose = FromJSONNumber(part.AngleOfRepose),
            Roughness = FromJSONNumber(part.Roughness),
            SpecificGravity = FromJSONNumber(part.SpecificGravity),
            ShieldsParameter = FromJSONNumber(part.ShieldsParameter),
            Equation = equation
        };
    }

    private static void ApplyState(TractiveForceMethod target, StableChannelDebugState state)
    {
        TractiveForceMethod source = ToMethod(state);
        target.LeftBank = source.LeftBank;
        target.Bed = source.Bed;
        target.RightBank = source.RightBank;
        target.Depth = source.Depth;
        target.Discharge = source.Discharge;
        target.Slope = source.Slope;
        target.LeftBankSideSlope = source.LeftBankSideSlope;
        target.RightBankSideSlope = source.RightBankSideSlope;
        target.Temperature = source.Temperature;
        target.Width = source.Width;
        target.SolutionMethod = source.SolutionMethod;
    }

    private static string NormalizeMemorySourceID(string? sourceID) =>
        Guid.TryParse(sourceID, out Guid id) ? id.ToString("D") : Guid.NewGuid().ToString("D");

    private static string? ValidateComputeSource(StableChannelDebugSource source,
        CoreLayerRequestContext? coreLayerContext)
    {
        if (source.InMemory)
        {
            if (!Guid.TryParse(source.SourceID, out _) || source.DesignPath is not null || source.DesignRevision is not null)
                return "sourceChanged";
            if (string.IsNullOrWhiteSpace(source.ProjectPath))
                return source.ProjectRevision is null ? null : "sourceChanged";
            if (string.IsNullOrWhiteSpace(source.ProjectRevision)) return "sourceChanged";
            try
            {
                string projectPath = Path.GetFullPath(source.ProjectPath);
                EnsureRevision(projectPath, source.ProjectRevision, "sourceChanged");
                Project project = CoreLayerImportService.OpenProject(projectPath, null, coreLayerContext);
                try
                {
                    if (project.HydraulicDesign.StableChannelDesigns.Count != 0)
                        return "sourceChanged";
                    EnsureRevision(projectPath, source.ProjectRevision, "sourceChanged");
                    return null;
                }
                finally { MapResultChartService.DisposeProject(project); }
            }
            catch (Exception)
            {
                return "sourceChanged";
            }
        }

        if (string.IsNullOrWhiteSpace(source.ProjectPath) || string.IsNullOrWhiteSpace(source.ProjectRevision) ||
            string.IsNullOrWhiteSpace(source.DesignPath) || string.IsNullOrWhiteSpace(source.DesignRevision))
            return "sourceChanged";
        try
        {
            string projectPath = Path.GetFullPath(source.ProjectPath);
            string designPath = Path.GetFullPath(source.DesignPath);
            EnsureRevision(projectPath, source.ProjectRevision, "sourceChanged");
            EnsureRevision(designPath, source.DesignRevision, "sourceChanged");
            if (!string.Equals(PersistedSourceID(projectPath, designPath), source.SourceID, StringComparison.Ordinal))
                return "sourceChanged";
            Project project = CoreLayerImportService.OpenProject(projectPath, null, coreLayerContext);
            try
            {
            StableChannelDesign? design = project.HydraulicDesign.StableChannelDesigns.FirstOrDefault();
            if (design is null || !SamePath(design.Filename, designPath)) return "sourceChanged";
            EnsureRevision(projectPath, source.ProjectRevision, "sourceChanged");
            EnsureRevision(designPath, source.DesignRevision, "sourceChanged");
            return null;
            }
            finally { MapResultChartService.DisposeProject(project); }
        }
        catch (Exception)
        {
            return "sourceChanged";
        }
    }

    private static string PersistedSourceID(string projectPath, string designPath) =>
        HashText(Path.GetFullPath(projectPath) + "\n" + Path.GetFullPath(designPath));

    private static void EnsureRevision(string path, string expected, string code)
    {
        if (!File.Exists(path) || !string.Equals(Revision(path), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(code);
    }

    private static string Revision(string path)
    {
        using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SamePath(string? actual, string expected) =>
        actual is not null && string.Equals(Path.GetFullPath(actual), expected, StringComparison.Ordinal);
}
