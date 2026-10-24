using System.Globalization;
using Ras.Layers;
using Utility.Logging;

namespace Ras.Backend;

public sealed record CatalogTreeLogDatum(string Key, string? Value);
public sealed record CatalogTreeLayerError(string Code, string Severity, string EvaluatedMessage,
    CatalogTreeLogDatum[] LogData);

/// <summary>
/// Adds the renamed source's already-materialized LayerErrors to the exact catalog
/// asset rows. Source revisions are captured before Project loads its source files,
/// once per canonical path, and checked again after diagnostics have been read.
/// </summary>
internal static class CatalogDiagnosticsService
{
    internal sealed class Context
    {
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static readonly HashSet<string> CoreLayerNodeTypes = new(StringComparer.Ordinal)
        {
            "ObservablePlan",
            "ObservableGeometry",
            "ObservableTerrain",
            "ObservableBoundaryCondition",
            "ObservableNValueLayer",
            "ObservableClassificationLayer"
        };

        private readonly Dictionary<string, CoreLayerBase> coreLayers = new(PathComparer);
        private readonly Dictionary<string, Result> results = new(PathComparer);
        private readonly Dictionary<string, SourceCapture?> captures = new(PathComparer);
        private readonly Dictionary<string, HashSet<string>> captureAliases = new(PathComparer);
        private readonly HashSet<string> changedSources = new(PathComparer);
        private readonly HashSet<string> usedSources = new(PathComparer);
        private readonly HashSet<string> protectedSources;

        internal Context(string projectPath, IReadOnlySet<string>? protectedSources = null)
        {
            this.protectedSources = protectedSources is null
                ? new HashSet<string>(PathComparer)
                : new HashSet<string>(protectedSources, PathComparer);
            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))
                ?? throw new ArgumentException("The project path must have a containing directory.", nameof(projectPath));
            CaptureDirectory(Project.GetDefaultGeometryDirectory(projectDirectory), "*.h5");
            CaptureDirectory(Project.GetDefaultPlanDirectory(projectDirectory), "*.h5");
            CaptureDirectory(Project.GetDefaultResultDirectory(projectDirectory), "*.h5");
            CaptureDirectory(Project.GetDefaultBoundaryConditionDirectory(projectDirectory), "*.h5");
            CaptureDirectory(Project.GetDefaultTerrainsDirectory(projectDirectory), "*.h5");
            CaptureDirectory(Project.GetDefaultTerrainsDirectory(projectDirectory), "*.s3");
            CaptureDirectory(Project.GetDefaultSurfaceLayersDirectory(projectDirectory), "*.h5");
        }

        internal void BindProject(Project project)
        {
            AddCoreSources(project.Plans);
            AddCoreSources(project.Geometries);
            AddCoreSources(project.Terrains);
            AddCoreSources(project.BoundaryConditions);
            AddCoreSources(project.NValues);
            AddCoreSources(project.ClassificationLayers);
            foreach (Result result in project.Results)
                Add(results, result.Filename, result);
        }

        internal CatalogTreeNode[] Attach(CatalogTreeNode[] roots)
        {
            CatalogTreeNode[] prepared = roots.Select(AttachNode).ToArray();
            VerifyCapturedSources();
            return prepared.Select(MarkChangedSource).ToArray();
        }

        private void AddCoreSources<T>(IEnumerable<T> layers) where T : CoreLayerBase
        {
            foreach (T layer in layers) Add(coreLayers, layer.Filename, layer);
        }

        private void CaptureDirectory(string directory, string searchPattern)
        {
            string[] paths;
            try
            {
                paths = Directory.Exists(directory) ? Directory.GetFiles(directory, searchPattern) : [];
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            catch (ArgumentException) { return; }

            foreach (string path in paths)
            {
                string canonicalPath;
                try { canonicalPath = SourceFilePaths.Canonical(path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                catch (ArgumentException) { continue; }
                if (protectedSources.Contains(canonicalPath)) continue;
                if (!captures.ContainsKey(canonicalPath))
                {
                    (string? stablePath, string? revision) = CatalogTreeService.TryReadStableRevision(path);
                    SourceCapture? capture = stablePath != null && revision != null
                        && PathComparer.Equals(stablePath, canonicalPath)
                        ? new SourceCapture(stablePath, revision)
                        : null;
                    captures.Add(canonicalPath, capture);
                    captureAliases.Add(canonicalPath, new HashSet<string>(PathComparer));
                }
                captureAliases[canonicalPath].Add(path);
            }
        }

        private static void Add<T>(Dictionary<string, T> destination, string? path, T source)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { destination.TryAdd(SourceFilePaths.Canonical(path), source); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        internal (string? CanonicalPath, string? Revision) ReadStableRevision(string path)
        {
            string canonicalPath;
            try { canonicalPath = SourceFilePaths.Canonical(path); }
            catch (IOException) { return (null, null); }
            catch (UnauthorizedAccessException) { return (null, null); }
            catch (ArgumentException) { return (null, null); }
            if (protectedSources.Contains(canonicalPath)) return (null, null);
            SourceCapture? capture = CaptureSource(path, canonicalPath);
            usedSources.Add(canonicalPath);
            return capture is null ? (null, null) : (capture.CanonicalPath, capture.Revision);
        }

        private CatalogTreeNode AttachNode(CatalogTreeNode node)
        {
            CatalogTreeNode[]? children = node.Children?.Select(AttachNode).ToArray();
            CatalogTreeNode updated = node with { Children = children };
            if (node.SourceClass == "ObservableResult")
                return AttachSource(updated, results);
            if (CoreLayerNodeTypes.Contains(node.SourceClass))
                return AttachSource(updated, coreLayers);
            return updated;
        }

        private CatalogTreeNode AttachSource<T>(CatalogTreeNode node, IReadOnlyDictionary<string, T> sources)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(node.AssetID))
                return node with { DiagnosticStatus = "unavailable" };

            string canonicalPath;
            try { canonicalPath = SourceFilePaths.Canonical(node.AssetID); }
            catch (IOException) { return node with { DiagnosticStatus = "unavailable" }; }
            catch (UnauthorizedAccessException) { return node with { DiagnosticStatus = "unavailable" }; }
            catch (ArgumentException) { return node with { DiagnosticStatus = "unavailable" }; }

            if (!sources.TryGetValue(canonicalPath, out T? source))
                return node with
                {
                    DiagnosticStatus = "unavailable",
                    DiagnosticSourceIdentity = canonicalPath
                };

            SourceCapture? capture = GetLoadedSourceCapture(node.AssetID, canonicalPath);
            if (capture is null)
            {
                try
                {
                    CatalogTreeLayerError[] errors = Materialize(source switch
                    {
                        CoreLayerBase layer => layer.LayerErrors,
                        Result result => result.LayerErrors,
                        _ => throw new InvalidOperationException("The catalog diagnostic source type is unsupported.")
                    });
                    return errors.Length > 0
                        ? node with
                        {
                            LayerErrors = errors,
                            DiagnosticStatus = "loaded",
                            DiagnosticSourceIdentity = canonicalPath
                        }
                        : node with
                        {
                            DiagnosticStatus = "unavailable",
                            DiagnosticSourceIdentity = canonicalPath
                        };
                }
                catch (Exception error)
                {
                    return node with
                    {
                        DiagnosticStatus = "readFailed",
                        DiagnosticSourceIdentity = canonicalPath,
                        DiagnosticReadError = error.Message
                    };
                }
            }

            try
            {
                CatalogTreeLayerError[] errors = source switch
                {
                    CoreLayerBase layer => Materialize(layer.LayerErrors),
                    Result result => Materialize(result.LayerErrors),
                    _ => throw new InvalidOperationException("The catalog diagnostic source type is unsupported.")
                };
                return node with
                {
                    LayerErrors = errors,
                    DiagnosticStatus = "loaded",
                    DiagnosticSourceIdentity = canonicalPath,
                    DiagnosticSourceRevision = capture.Revision
                };
            }
            catch (Exception error)
            {
                return node with
                {
                    DiagnosticStatus = "readFailed",
                    DiagnosticSourceIdentity = canonicalPath,
                    DiagnosticSourceRevision = capture.Revision,
                    DiagnosticReadError = error.Message
                };
            }
        }

        private SourceCapture? CaptureSource(string path, string canonicalPath)
        {
            if (protectedSources.Contains(canonicalPath))
            {
                captures.TryAdd(canonicalPath, null);
                captureAliases.TryAdd(canonicalPath, new HashSet<string>(PathComparer));
                captureAliases[canonicalPath].Add(path);
                usedSources.Add(canonicalPath);
                return null;
            }
            if (!captures.TryGetValue(canonicalPath, out SourceCapture? capture))
            {
                (string? stablePath, string? revision) = CatalogTreeService.TryReadStableRevision(path);
                capture = stablePath != null && revision != null
                    && PathComparer.Equals(stablePath, canonicalPath)
                    ? new SourceCapture(stablePath, revision)
                    : null;
                captures.Add(canonicalPath, capture);
                captureAliases.Add(canonicalPath, new HashSet<string>(PathComparer));
            }
            captureAliases[canonicalPath].Add(path);
            usedSources.Add(canonicalPath);
            return capture;
        }

        private SourceCapture? GetLoadedSourceCapture(string path, string canonicalPath)
        {
            if (!captures.TryGetValue(canonicalPath, out SourceCapture? capture))
            {
                captures.Add(canonicalPath, null);
                captureAliases.Add(canonicalPath, new HashSet<string>(PathComparer));
            }
            captureAliases[canonicalPath].Add(path);
            usedSources.Add(canonicalPath);
            return capture;
        }

        private void VerifyCapturedSources()
        {
            foreach (string canonicalPath in usedSources)
            {
                if (!captures.TryGetValue(canonicalPath, out SourceCapture? capture)) continue;
                if (capture is null) continue;
                foreach (string path in captureAliases[canonicalPath])
                {
                    (string? currentPath, string? currentRevision) = CatalogTreeService.TryReadStableRevision(path);
                    if (currentPath is null || currentRevision is null ||
                        !PathComparer.Equals(currentPath, capture.CanonicalPath) ||
                        !string.Equals(currentRevision, capture.Revision, StringComparison.Ordinal))
                    {
                        changedSources.Add(canonicalPath);
                        break;
                    }
                }
            }
        }

        private CatalogTreeNode MarkChangedSource(CatalogTreeNode node)
        {
            CatalogTreeNode[]? children = node.Children?.Select(MarkChangedSource).ToArray();
            bool changed = node.DiagnosticSourceIdentity is { } identity && changedSources.Contains(identity);
            if (!changed) return node with { Children = children };
            return node with
            {
                Children = children,
                LayerErrors = null,
                DiagnosticStatus = "sourceChanged",
                DiagnosticSourceRevision = null,
                DiagnosticReadError = null,
                ResultComputeState = null,
                ResultComputeFailureReason = null,
                ResultComputeSourcePath = null,
                ResultComputeSourceRevision = null
            };
        }

        private static CatalogTreeLayerError[] Materialize(IReadOnlyList<Message>? messages) =>
            (messages ?? []).Select(message => new CatalogTreeLayerError(
                message.MessageType?.Code ?? string.Empty,
                message.MessageType?.DefaultSeverity.ToString() ?? "Unknown",
                message.EvaluatedMessage ?? string.Empty,
                message.LogData.Select(pair => new CatalogTreeLogDatum(pair.Key,
                    FormatLogValue(pair.Value))).ToArray()))
            .ToArray();

        private static string? FormatLogValue(object? value) => value switch
        {
            null => null,
            IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        private sealed record SourceCapture(string CanonicalPath, string Revision);
    }
}
