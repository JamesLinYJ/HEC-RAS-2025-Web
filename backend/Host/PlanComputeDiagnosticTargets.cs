using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    internal static PlanDiagnosticTarget? ResolvePlanDiagnosticTarget(Project project, Plan plan,
        string key, string? value, string projectPath, string projectRevision,
        string planPath, string planRevision)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        string? kind = null;
        string? assetPath = null;
        string? featureID = null;
        string? featureName = null;
        switch (key)
        {
            case "PlanName" when string.Equals(value, plan.Name, StringComparison.Ordinal):
                kind = "plan";
                assetPath = plan.Filename;
                break;
            case "GeometryName":
            {
                PlanAssociation association = project.GetPlanAssociation(plan);
                Geometry? geometry = association.Geometry;
                if (geometry is not null && string.Equals(value, geometry.Name, StringComparison.Ordinal))
                {
                    kind = "geometry";
                    assetPath = geometry.Filename;
                }
                break;
            }
            case "MeshName":
            {
                Geometry? geometry = project.GetPlanAssociation(plan).Geometry;
                if (geometry is null) break;
                var matches = geometry.FlowAreaLayer
                    .Select((area, index) => (Area: area, Index: index))
                    .Where(item => string.Equals(value, item.Area.Name, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1) break;
                kind = "flowArea";
                assetPath = geometry.Filename;
                featureID = "flow-area-row:" + matches[0].Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                featureName = matches[0].Area.Name;
                break;
            }
            case "BoundaryConditionName":
            {
                BoundaryCondition? boundary = project.GetPlanAssociation(plan).BoundaryCondition;
                if (boundary is not null && string.Equals(value, boundary.Name, StringComparison.Ordinal))
                {
                    kind = "boundaryCondition";
                    assetPath = boundary.Filename;
                }
                break;
            }
            case "BoundaryConditionLineName":
            {
                BoundaryCondition? boundary = project.GetPlanAssociation(plan).BoundaryCondition;
                if (boundary is null) break;
                var matches = boundary.BoundaryConditionLines
                    .Select((line, index) => (Line: line, Index: index))
                    .Where(item => string.Equals(value, item.Line.Name, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1) break;
                kind = "boundaryConditionLine";
                assetPath = boundary.Filename;
                featureID = "boundary-line-row:" + matches[0].Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                featureName = matches[0].Line.Name;
                break;
            }
        }

        if (kind is null || string.IsNullOrWhiteSpace(assetPath)) return null;
        try
        {
            projectPath = SourceFilePaths.Canonical(projectPath);
            planPath = SourceFilePaths.Canonical(planPath);
            string canonicalAssetPath = SourceFilePaths.Canonical(assetPath);
            if (!File.Exists(projectPath) || !File.Exists(planPath) || !File.Exists(canonicalAssetPath) ||
                !string.Equals(Revision(projectPath), projectRevision, StringComparison.Ordinal) ||
                !string.Equals(Revision(planPath), planRevision, StringComparison.Ordinal)) return null;

            string assetRevision = Revision(canonicalAssetPath);
            if (!string.Equals(Revision(projectPath), projectRevision, StringComparison.Ordinal) ||
                !string.Equals(Revision(planPath), planRevision, StringComparison.Ordinal)) return null;
            return new PlanDiagnosticTarget(kind, projectPath, projectRevision, planPath, planRevision,
                canonicalAssetPath, assetRevision, featureID, featureName);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static PlanComputeJobMessage ProjectPlanComputeJobMessage(int index, Utility.Logging.Message message,
        Project project, Plan plan, string projectPath, string projectRevision,
        string planPath, string planRevision)
    {
        PlanComputeJobLogDatum[] logData = message.LogData.Select(pair => new PlanComputeJobLogDatum(
            pair.Key, FormatLogValue(pair.Value), ResolvePlanDiagnosticTarget(project, plan,
                pair.Key, FormatLogValue(pair.Value), projectPath, projectRevision, planPath, planRevision)))
            .ToArray();
        return new PlanComputeJobMessage(index, message.MessageType.Code,
            message.MessageType.DefaultSeverity.ToString(), message.EvaluatedMessage, logData);
    }

}
