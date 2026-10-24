using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Observables;

namespace Ras.Backend;

public sealed record PlanResultCatalogRequest(string ProjectPath, string ProjectXml,
    string PlanPath, string Revision);
public sealed record PlanResultChoice(string ResultPath, string Revision, string Name,
    string EmbeddedPlanName, bool HasComputed, int ProfileCount);
public sealed record PlanResultCatalog(string ProjectPath, string PlanPath, string Revision,
    string PlanName, PlanResultChoice[] Results);

/// <summary>
/// PlanNodeDetails binds ObservablePlan.NameMatchedResults to the visual-only
/// ActiveResult.Result. Neither that selection nor its choices are Plan fields.
/// </summary>
public static class PlanResultCatalogService
{
    public static PlanResultCatalog Load(PlanResultCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.ProjectPath) || !Path.IsPathFullyQualified(request.PlanPath))
            throw new ArgumentException("Open a saved project and select a saved plan.");
        if (string.IsNullOrEmpty(request.Revision))
            throw new ArgumentException("Refresh the plan before selecting a result.");
        string projectPath = SourceFilePaths.Canonical(request.ProjectPath);
        string planPath = SourceFilePaths.Canonical(request.PlanPath);
        string projectRevision = Hash(projectPath);
        Verify(planPath, request.Revision);
        using var xmlReader = XmlReader.Create(new StringReader(request.ProjectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        XElement metadata = XElement.Load(xmlReader);
        Project project = new(projectPath);
        try
        {
            project.ApplyEditorMetadata(metadata);
            Plan[] plans = project.Plans.Where(plan =>
                SourceFilePaths.SameSourceFile(plan.Filename, planPath)).ToArray();
            if (plans.Length != 1)
                throw new ArgumentException("The selected plan is not exactly one saved plan in the current project.");
            Plan plan = plans[0];
            if (plan.HasLoadErrors)
                throw new IOException(string.Join("\n", plan.LayerErrors.Select(error => error.EvaluatedMessage)));

            var resultRevisions = project.Results.Where(result => !result.IsInMemory && !string.IsNullOrWhiteSpace(result.Filename))
                .ToDictionary(result => SourceFilePaths.Canonical(result.Filename), result => Hash(result.Filename), StringComparer.Ordinal);

            var observable = new ObservableProject(project, Utility.SynchronizationContext.None);
            ObservablePlan selected = observable.Plans.Single(item => ReferenceEquals(item.Layer, plan));
            List<PlanResultChoice> choices = [];
            foreach (ObservableResult item in selected.NameMatchedResults)
            {
                Result result = item.Result;
                if (result.IsInMemory || string.IsNullOrWhiteSpace(result.Filename)) continue;
                string path = SourceFilePaths.Canonical(result.Filename);
                string revision = resultRevisions[path];
                choices.Add(new(path, revision, item.DisplayName, result.Plan.Name,
                    result.HasComputed, result.ProfileCount));
                Verify(path, revision);
            }
            if (choices.Select(item => item.ResultPath).Distinct(StringComparer.Ordinal).Count() != choices.Count)
                throw new ArgumentException("The current plan has ambiguous saved result file identities.");
            Verify(planPath, request.Revision);
            Verify(projectPath, projectRevision);
            foreach (PlanResultChoice item in choices) Verify(item.ResultPath, item.Revision);
            return new(projectPath, planPath, request.Revision, plan.Name, choices.ToArray());
        }
        finally
        {
            foreach (Result result in project.Results) result.Dispose();
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                .Concat(project.BoundaryConditions).Concat(project.Terrains).Concat(project.NValues)
                .Concat(project.ClassificationLayers)) layer.Dispose();
        }
    }

    private static string Hash(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void Verify(string path, string revision)
    {
        if (!string.Equals(Hash(path), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }
}
