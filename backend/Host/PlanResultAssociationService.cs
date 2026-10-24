using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Layers.Results;

namespace Ras.Backend;

public sealed record PlanResultAssociationRequest(string ProjectPath, string ProjectXml,
    string ResultPath, string Revision);

/// <summary>
/// The association names displayed by the Result Plan associations viewer.
/// These values are read directly from Result.Plan, never looked up by name in
/// the current project's Plans, Geometry, or Boundary Conditions collections.
/// </summary>
public sealed record PlanResultAssociationSnapshot(string ProjectPath, string ProjectRevision,
    string ResultPath, string Revision, bool HasPlan, string? PlanName,
    string? GeometryName, string? BoundaryConditionName);

/// <summary>
/// Reads the associations on the Plan embedded in one exact saved Result.
/// The service is read-only and validates both the supplied Result HDF revision
/// and the project file revision around the source read.
/// </summary>
public static class PlanResultAssociationService
{
    public static PlanResultAssociationSnapshot Inspect(PlanResultAssociationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Open a saved project before reading Result Plan associations.");
        if (string.IsNullOrWhiteSpace(request.ProjectXml))
            throw new ArgumentException("The current project XML context is unavailable.");
        if (string.IsNullOrWhiteSpace(request.ResultPath) || !Path.IsPathFullyQualified(request.ResultPath))
            throw new ArgumentException("Select a saved Result file.");
        if (string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("Refresh the Result before reading its Plan associations.");

        string projectPath = Path.GetFullPath(request.ProjectPath);
        string canonicalProjectPath = SourceFilePaths.Canonical(projectPath);
        string resultPath = SourceFilePaths.Canonical(request.ResultPath);
        if (!File.Exists(canonicalProjectPath))
            throw new FileNotFoundException("The saved project file could not be found.", canonicalProjectPath);
        if (!File.Exists(resultPath))
            throw new FileNotFoundException("The selected Result file could not be found.", resultPath);

        string projectRevision = Hash(canonicalProjectPath);
        string resultRevision = Hash(resultPath);
        VerifyRevision(resultRevision, request.Revision);

        using XmlReader reader = XmlReader.Create(new StringReader(request.ProjectXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        XElement metadata = XElement.Load(reader, LoadOptions.None);

        // Preserve the caller's spelling as the Project base so relative Result
        // references resolve against the same saved project directory.
        Project project = new(projectPath);
        try
        {
            project.ApplyEditorMetadata(metadata);
            Result[] matches = project.Results.Where(candidate => !candidate.IsInMemory
                && !string.IsNullOrWhiteSpace(candidate.Filename)
                && SourceFilePaths.SameSourceFile(candidate.Filename, resultPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected file is not exactly one saved Result in the current project.");

            Result result = matches[0];
            Plan? embeddedPlan = result.Plan;
            var snapshot = new PlanResultAssociationSnapshot(
                projectPath,
                projectRevision,
                resultPath,
                resultRevision,
                embeddedPlan is not null,
                embeddedPlan?.Name,
                embeddedPlan?.Geometry?.Name,
                embeddedPlan?.BoundaryCondition?.Name);

            VerifyRevision(Hash(canonicalProjectPath), projectRevision);
            VerifyRevision(Hash(resultPath), resultRevision);
            return snapshot;
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void VerifyRevision(string actual, string expected)
    {
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
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
