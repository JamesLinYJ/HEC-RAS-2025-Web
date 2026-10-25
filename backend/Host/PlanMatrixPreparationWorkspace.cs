using Ras.Layers;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private sealed record PlanMatrixDependency(string Path, string Revision);

    private static PlanMatrixDependency[] CapturePlanMatrixDependencies(Plan plan) =>
        new CoreLayerBase?[] { plan.Geometry, plan.PlanAssociation?.BoundaryCondition,
                plan.Geometry?.AssociatedTerrain, plan.Geometry?.AssociatedNValueLayer }
            .Where(layer => layer is not null && !layer.IsInMemory)
            .Select(layer => SourceFilePaths.Canonical(layer!.Filename))
            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new PlanMatrixDependency(path, Revision(path))).ToArray();

    private static void VerifyPlanMatrixDependencies(IEnumerable<PlanMatrixDependency> sources)
    {
        foreach (PlanMatrixDependency source in sources) VerifyFileRevision(source.Path, source.Revision);
    }

    /// <summary>
    /// TryPrepareCompute necessarily saves Geometry preparation summaries/property tables.
    /// Keep the captured project, Plan draft and external-connection resolution, but replace
    /// its mutable preparation dependencies with private HDFs. The owning ModelContext must
    /// be disposed before this workspace so all native handles close before cleanup.
    /// </summary>
    private sealed class PlanMatrixPreparationWorkspace : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(),
            "hecras-plan-matrix-" + Guid.NewGuid().ToString("N"));

        public void Stage(Project project, Plan plan)
        {
            var association = project.GetPlanAssociation(plan);
            Geometry sourceGeometry = association.Geometry
                ?? throw new ArgumentException("The selected Plan has no associated geometry.");
            BoundaryCondition? sourceBoundary = association.BoundaryCondition;
            var sourceGeometryAssociation = project.GetGeometryAssociation(sourceGeometry);
            string geometryDirectory = Path.Combine(directory, "Geometries");
            string boundaryDirectory = Path.Combine(directory, "Boundary Conditions");
            Directory.CreateDirectory(geometryDirectory);
            Directory.CreateDirectory(boundaryDirectory);
            string geometryPath = Path.Combine(geometryDirectory, Path.GetFileName(sourceGeometry.Filename));
            string? boundaryPath = sourceBoundary is null ? null
                : Path.Combine(boundaryDirectory, Path.GetFileName(sourceBoundary.Filename));

            // SaveAs preserves the effective in-memory model and unloaded HDF payloads;
            // copying only the on-disk file would lose replayed draft changes.
            sourceGeometry.SaveAs(geometryPath);
            sourceBoundary?.SaveAs(boundaryPath!);
            var geometry = new Geometry(geometryPath, project);
            project.AddGeometry(geometry);
            geometry.OverrideName(sourceGeometry.Name);
            var geometryAssociation = project.GetGeometryAssociation(geometry);
            geometryAssociation.Terrain = sourceGeometryAssociation.Terrain;
            geometryAssociation.NValueLayer = sourceGeometryAssociation.NValueLayer;
            if (geometry.HasLoadErrors)
                throw new IOException("The private ready-to-run geometry could not be loaded.");
            association.Geometry = geometry;
            if (sourceBoundary is not null)
            {
                var boundary = new BoundaryCondition(boundaryPath!, project);
                project.AddBoundaryCondition(boundary);
                boundary.OverrideName(sourceBoundary.Name);
                if (boundary.HasLoadErrors)
                    throw new IOException("The private ready-to-run boundary conditions could not be loaded.");
                association.BoundaryCondition = boundary;
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
