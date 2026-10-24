using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Layers.Results;

namespace Ras.Backend;

public sealed record ResultPlanContextRequest(string ProjectPath, string ProjectXml,
    string ResultPath, string Revision, CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ResultPlanPropertyItem(string Label, string Value, bool IsLocalizedValue = false);
public sealed record ResultPlanPropertyGroup(string Title, ResultPlanPropertyItem[] Items);
public sealed record ResultPlanCandidate(string ResultPath, string Revision, string Name,
    string? PlanName, bool HasPlan);
public sealed record ResultPlanCandidateGroup(string? PlanName, bool HasPlan,
    ResultPlanCandidate[] Results);
public sealed record ResultPlanContextSnapshot(string ProjectPath, string ProjectRevision,
    string ResultPath, string Revision, string AnchorName, string? AnchorPlanName,
    bool AnchorHasPlan, ResultPlanPropertyGroup[] EmbeddedPlanGroups,
    ResultPlanCandidateGroup[] CandidateGroups)
{
    public ResultPlanCandidate Anchor => new(ResultPath, Revision, AnchorName, AnchorPlanName, AnchorHasPlan);
}

/// <summary>
/// Returns the clicked Result's own embedded Plan details and source-backed
/// project Result candidates for the Link Plans picker. Embedded Plan values
/// never resolve through the current project's same-named Plans collection.
/// This service is read-only and verifies every file-backed identity before and
/// after reading it.
/// </summary>
public static class ResultPlanContextService
{
    public static ResultPlanContextSnapshot Inspect(string projectPath, string projectXml,
        string resultPath, string revision, CoreLayerRequestContext? coreLayerContext = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathFullyQualified(projectPath))
            throw new ArgumentException("Open a saved project before linking its results.");
        if (string.IsNullOrWhiteSpace(projectXml))
            throw new ArgumentException("The current project XML context is unavailable.");
        if (string.IsNullOrWhiteSpace(resultPath) || !Path.IsPathFullyQualified(resultPath))
            throw new ArgumentException("Select a saved result file before linking plans.");
        if (string.IsNullOrWhiteSpace(revision))
            throw new ArgumentException("Refresh the result before linking plans.");

        string normalizedProjectPath = Path.GetFullPath(projectPath);
        string canonicalProjectPath = SourceFilePaths.Canonical(normalizedProjectPath);
        string canonicalResultPath = SourceFilePaths.Canonical(resultPath);
        if (!File.Exists(canonicalProjectPath))
            throw new FileNotFoundException("The saved project file could not be found.", canonicalProjectPath);
        if (!File.Exists(canonicalResultPath))
            throw new FileNotFoundException("The selected result file could not be found.", canonicalResultPath);

        string projectRevision = Hash(canonicalProjectPath);
        string anchorRevision = Hash(canonicalResultPath);
        VerifyHash(anchorRevision, revision);

        using XmlReader reader = XmlReader.Create(new StringReader(projectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        XElement metadata = XElement.Load(reader, LoadOptions.None);
        // Keep the caller's normalized, user-visible path as the Project base
        // so relative model/result references resolve from the same directory.
        // The physical canonical path above is for file identity and hashing only.
        Project project = CoreLayerImportService.OpenProjectWithMetadata(normalizedProjectPath,
            metadata, coreLayerContext);
        try
        {
            Result[] matchingAnchors = project.Results.Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Filename)
                && SourceFilePaths.SameSourceFile(candidate.Filename, canonicalResultPath)).ToArray();
            if (matchingAnchors.Length != 1)
                throw new ArgumentException("The selected file is not exactly one saved result in the current project.");

            Result anchor = matchingAnchors[0];
            if (anchor.IsInMemory || string.IsNullOrWhiteSpace(anchor.Filename))
                throw new ArgumentException("Only a saved project Result can anchor linked plans.");

            Plan? embeddedPlan = anchor.Plan;
            string? anchorPlanName = embeddedPlan?.Name;
            ResultPlanPropertyGroup[] embeddedGroups = embeddedPlan == null
                ? []
                : BuildEmbeddedPlanGroups(embeddedPlan);

            Result[] projectResults = project.Results.ToArray();
            var fileBackedResults = new List<(Result Result, string Path, string Revision, string? PlanName, bool HasPlan)>();
            foreach (Result candidate in projectResults)
            {
                if (candidate.IsInMemory || string.IsNullOrWhiteSpace(candidate.Filename)) continue;
                string path = SourceFilePaths.Canonical(candidate.Filename);
                if (!File.Exists(path)) continue;
                string candidateRevision = Hash(path);
                Plan? candidatePlan = candidate.Plan;
                fileBackedResults.Add((candidate, path, candidateRevision,
                    candidatePlan?.Name, candidatePlan != null));
            }

            var ambiguousPaths = fileBackedResults.GroupBy(item => item.Path, PathComparer)
                .Where(group => group.Count() != 1).Select(group => group.Key).ToArray();
            if (ambiguousPaths.Length != 0)
                throw new ArgumentException("The current project contains ambiguous saved Result file identities.");

            var groups = fileBackedResults
                .Where(item => !PathComparer.Equals(item.Path, canonicalResultPath)
                    && !(anchorPlanName != null && item.HasPlan
                        && string.Equals(item.PlanName, anchorPlanName, StringComparison.Ordinal)))
                .GroupBy(item => (item.HasPlan, item.PlanName))
                .OrderBy(group => group.Key.PlanName ?? "(no plan)", StringComparer.CurrentCulture)
                .Select(group => new ResultPlanCandidateGroup(group.Key.PlanName, group.Key.HasPlan,
                    group.OrderBy(item => item.Result.Name, StringComparer.CurrentCulture)
                        .Select(item => new ResultPlanCandidate(item.Path, item.Revision,
                            item.Result.Name, item.PlanName, item.HasPlan)).ToArray()))
                .ToArray();

            var snapshot = new ResultPlanContextSnapshot(normalizedProjectPath, projectRevision,
                canonicalResultPath, anchorRevision, anchor.Name, anchorPlanName,
                embeddedPlan != null, embeddedGroups, groups);

            // Ensure the project, anchor and every candidate still identify the
            // exact bytes used to construct this context.
            VerifyFile(canonicalProjectPath, projectRevision);
            VerifyFile(canonicalResultPath, anchorRevision);
            foreach (var item in fileBackedResults) VerifyFile(item.Path, item.Revision);
            return snapshot;
        }
        finally
        {
            DisposeProject(project);
        }
    }

    internal static ResultPlanPropertyGroup[] BuildEmbeddedPlanGroups(Plan plan)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        var timeWindow = new ResultPlanPropertyGroup("Time Window",
        [
            Item("Mode", plan.TimeWindowMode == PlanTimeWindowMode.Plan ? "Custom" : "Boundary Condition", true),
            Item("Start", plan.StartTime.ToString("yyyy-MM-dd HH:mm:ss", culture)),
            Item("End", plan.EndTime.ToString("yyyy-MM-dd HH:mm:ss", culture)),
            Item("Compute Step", plan.ComputeTimestep.ToString("c", culture)),
            Item("Output Interval", plan.BaseOutputInterval.ToString("c", culture)),
            Item("Profiles", plan.ExpectedProfileCount.ToString(culture))
        ]);

        var computationOptions = new ResultPlanPropertyGroup("Computation Options",
        [
            Item("Solver", plan.SolverType.ToString(), true),
            Item("Equation Set", plan.EquationSet == Ras.Engine.SolverControl.EquationSet.DWE
                ? "Diffusion Wave Equation" : "Shallow Water Equations", true),
            Item("Time Stepping", plan.TimeStepping.ToString(), true),
            Item("Implicit Adaptive Time Stepping", Bool(plan.ImplicitAdaptiveTimestepping), true),
            Item("Maximum Courant", Number(plan.MaxCourant, culture)),
            Item("Use GPU Adaptive Timestep", Bool(plan.UseGPUAdaptive), true),
            Item("LTS Levels", plan.LtsLevels switch
            {
                Ras.Engine.SolverControl.LtsLevelOptions.GlobalStepping => "Disabled",
                Ras.Engine.SolverControl.LtsLevelOptions.LtsLevels1 => "1 time level",
                Ras.Engine.SolverControl.LtsLevelOptions.LtsLevels2 => "2 time levels",
                Ras.Engine.SolverControl.LtsLevelOptions.LtsLevels3 => "3 time levels",
                Ras.Engine.SolverControl.LtsLevelOptions.LtsLevels4 => "4 time levels",
                Ras.Engine.SolverControl.LtsLevelOptions.LtsLevels5 => "5 time levels",
                _ => plan.LtsLevels.ToString()
            }, true),
            Item("Time Step Safety Factor", Number(plan.TimestepSafetyFactor, culture)),
            Item("Use Minimum Advective Time Step", Bool(plan.UseMinAdvectiveTimeStep), true),
            Item("Minimum Advective Time Step", Number(plan.MinAdvectiveTimeStep, culture)),
            Item("Maximum Wet Ratio for Minimum Advective Time Step", Number(plan.MaxPercentWetAdvTimeStep, culture)),
            Item("Use Partial Grid", Bool(plan.UsePartialGrid), true),
            Item("Active Threshold for Partial Grid", Number(plan.ActiveThresholdPartGrid, culture)),
            Item("Partial Grid Buffer Count", plan.BufferCountPartGrid.ToString(culture)),
            Item("Partial Grid Elevation Threshold", Number(plan.ElevationThresholdPartGrid, culture)),
            Item("Maximum Inner Iterations", plan.MaxInnerIterations.ToString(culture)),
            Item("Volume Tolerance", Number(plan.VolumeTolerance, culture)),
            Item("Maximum Volume Tolerance", Number(plan.VolumeMaxTolerance, culture)),
            Item("RMS Volume Tolerance", Number(plan.VolumeRMSTolerance, culture)),
            Item("Maximum Outer Iterations", plan.MaxOuterIterations.ToString(culture)),
            Item("Water Surface Tolerance", Number(plan.WaterSurfaceTolerance, culture)),
            Item("Maximum Water Surface Tolerance", Number(plan.WaterSurfaceMaxTolerance, culture)),
            Item("RMS Water Surface Tolerance", Number(plan.WaterSurfaceRMSTolerance, culture)),
            Item("Use FBF", Bool(plan.UseFBF), true),
            Item("Use Advection", Bool(plan.UseAdvection), true),
            Item("Advection Method", plan.AdvectionMethod switch
            {
                Ras.Engine.SolverControl.AdvectionMethod.ExplicitUpwind => "Explicit Upwind",
                Ras.Engine.SolverControl.AdvectionMethod.SemiImplicitUpwind => "Semi-Implicit Upwind",
                _ => "None"
            }, true),
            Item("Advection Implicit Factor", Number(plan.AdvectionImplicitFactor, culture)),
            Item("Implicit Factor", Number(plan.ImplicitFactor, culture)),
            Item("Linear Friction Coefficient", Number(plan.LinearFrictionCoefficient, culture)),
            Item("Use Diffusion", Bool(plan.UseDiffusion), true),
            Item("Turbulence Model", plan.TurbulenceModel switch
            {
                Ras.Engine.TurbulenceModel.Smagorinsky => "Parabolic-Smagorinsky",
                Ras.Engine.TurbulenceModel.KEpsilon => "Standard k-epsilon",
                _ => "Prandtl k-l"
            }, true),
            Item("Base Eddy Viscosity", Number(plan.BaseEddyViscosity, culture)),
            Item("Longitudinal Mixing Coefficient", Number(plan.LongMixCoeff, culture)),
            Item("Transverse Mixing Coefficient", Number(plan.TransMixCoeff, culture)),
            Item("Smagorinsky Mixing Coefficient", Number(plan.HorizMixCoeff, culture)),
            Item("Latitude for Coriolis", Number(plan.Latitude, culture)),
            Item("Core Count", plan.CoreCount.ToString(culture)),
            Item("Parallel Method", plan.ParallelMethod == Ras.Engine.SolverControl.ParallelMethod.THREAD_FOR
                ? "Dedicated Threads" : "Thread Pool", true),
            Item("Extra Output Fields", Bool(plan.ExtraOutput), true)
        ]);

        return [timeWindow, computationOptions];
    }

    private static ResultPlanPropertyItem Item(string label, string value, bool isLocalizedValue = false) =>
        new(label, value, isLocalizedValue);
    private static string Bool(bool value) => value ? "Yes" : "No";
    private static string Number(double value, CultureInfo culture) => value.ToString("G", culture);
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void VerifyFile(string path, string revision) => VerifyHash(Hash(path), revision);
    private static void VerifyHash(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void DisposeProject(Project project)
    {
        foreach (Result result in project.Results) result.Dispose();
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                     .Concat(project.Geometries).Concat(project.Terrains)
                     .Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
    }
}
