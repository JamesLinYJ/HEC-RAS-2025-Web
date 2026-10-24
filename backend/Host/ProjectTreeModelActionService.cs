using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Utility.Files;

namespace Ras.Backend;

public sealed record ProjectTreeModelActionInspectRequest(
    string ProjectPath,
    string ProjectXml,
    string ProjectXmlSHA,
    string ProjectRevision,
    string ProjectSessionID,
    string Action,
    string Kind,
    string NodeID,
    string RootSourceClass,
    string SourceClass,
    string NodeName,
    string AssetPath,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectTreeModelActionPerformRequest(
    string ProjectPath,
    string ProjectXml,
    string ProjectXmlSHA,
    string ProjectRevision,
    string ProjectSessionID,
    string Action,
    string Kind,
    string NodeID,
    string RootSourceClass,
    string SourceClass,
    string NodeName,
    string AssetPath,
    string ExpectedAssetSHA,
    string? ExpectedNewName = null,
    CoreLayerRequestContext? CoreLayerContext = null);

public sealed record ProjectTreeModelActionInspection(
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXmlSHA,
    string Action,
    string Kind,
    string NodeID,
    string RootSourceClass,
    string SourceClass,
    string NodeName,
    string AssetPath,
    string AssetRevision,
    string[] AvailableActions);

public sealed record ProjectTreeModelActionReceipt(
    bool Success,
    bool AssetPublished,
    bool ProjectMetadataCommitted,
    string ProjectPath,
    string ProjectSessionID,
    string ProjectRevision,
    string ProjectXml,
    string ProjectXmlSHA,
    string Action,
    string Kind,
    string OriginalAssetPath,
    string OriginalAssetSHA,
    string NewAssetPath,
    string NewAssetSHA,
    string NewName,
    string? Warning = null);

/// <summary>
/// Publishes the retained project-tree duplicate action for Geometry and Plan
/// models. CatalogModelActionService remains the source of the duplicate file
/// and association mutations; this service owns their project-scoped commit.
/// </summary>
public static class ProjectTreeModelActionService
{
    private sealed record ProjectSnapshot(string Path, string Xml, string Revision, string XmlSHA);
    private sealed record ModelIdentity(string Kind, string RootSourceClass, string SourceClass,
        string AssociationSection, string AssociationRow, string IdentityAttribute);

    public static ProjectTreeModelActionInspection Inspect(ProjectTreeModelActionInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelIdentity identity = ValidateRequest(request);
        ProjectSnapshot source = ReadProjectSnapshot(request);
        ValidateCoreLayerContext(request.ProjectSessionID, source, request.CoreLayerContext);

        CatalogTreeNode sourceNode = FindSourceNode(request, identity, source.Xml);
        (string? assetPath, string? assetRevision) = CatalogTreeService.TryReadStableRevision(sourceNode.AssetID!);
        if (assetPath is null || assetRevision is null || !SourceFilePaths.SameSourceFile(assetPath, request.AssetPath))
            throw new SourceFileChangedException();

        CatalogModelActionInspection modelAction = CatalogModelActionService.Inspect(
            ToCatalogRequest(request, identity, assetRevision));
        if (!modelAction.AvailableActions.Contains("duplicate", StringComparer.Ordinal) ||
            !SourceFilePaths.SameSourceFile(modelAction.CanonicalAssetPath, assetPath) ||
            !string.Equals(modelAction.Revision, assetRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        EnsureSnapshotUnchanged(source);
        return new(source.Path, request.ProjectSessionID, source.Revision, source.XmlSHA,
            "duplicate", identity.Kind, request.NodeID, identity.RootSourceClass, identity.SourceClass,
            sourceNode.Name, assetPath, assetRevision, ["duplicate"]);
    }

    public static ProjectTreeModelActionReceipt Perform(ProjectTreeModelActionPerformRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelIdentity identity = ValidateRequest(request);
        if (!IsSHA256(request.ExpectedAssetSHA))
            throw new ArgumentException("Refresh the selected model before copying it.", nameof(request));
        if (request.ExpectedNewName is not null && string.IsNullOrWhiteSpace(request.ExpectedNewName))
            throw new ArgumentException("The expected copy name is invalid.", nameof(request));

        string projectPath = SourceFilePaths.Canonical(request.ProjectPath);
        using IDisposable projectWriteLock = ProjectWorkspaceService.AcquireProjectWriteLock(projectPath);
            ProjectSnapshot source = ReadProjectSnapshot(request);
            ValidateCoreLayerContext(request.ProjectSessionID, source, request.CoreLayerContext);
            CatalogTreeNode sourceNode = FindSourceNode(request, identity, source.Xml);
            if (!SourceFilePaths.SameSourceFile(sourceNode.AssetID!, request.AssetPath))
                throw new SourceFileChangedException();

            var catalogRequest = ToCatalogRequest(request, identity, request.ExpectedAssetSHA);
            CatalogModelActionInspection modelAction = CatalogModelActionService.Inspect(catalogRequest);
            if (!modelAction.AvailableActions.Contains("duplicate", StringComparer.Ordinal) ||
                !string.Equals(modelAction.Revision, request.ExpectedAssetSHA, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            CatalogModelActionReceipt staged = CatalogModelActionService.Perform(catalogRequest);
            bool stagingCleanupAttempted = false;
            string? CleanupStagingOnce()
            {
                if (stagingCleanupAttempted) return null;
                stagingCleanupAttempted = true;
                return CleanupStaging(staged.StagingDirectories);
            }

            try
            {
            string newName = staged.NewName ?? throw new InvalidDataException("The source duplicate action did not return a copy name.");
            ValidateStagedReceipt(staged, request, identity, source, newName);
            EnsureAssetUnchanged(request.AssetPath, request.ExpectedAssetSHA);
            CatalogModelActionStagedTarget stagedTarget = staged.StagedTargets.Single();
            string targetPath = SourceFilePaths.Canonical(stagedTarget.TargetPath);
            string stagePath = SourceFilePaths.Canonical(stagedTarget.StagePath);
            string targetSHA = stagedTarget.StageSHA;
            if (request.ExpectedNewName is { } expectedName &&
                !string.Equals(expectedName.Trim(), newName, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            string nextXml = ApplyMetadataMutations(source.Xml, staged.MetadataMutations, identity, newName);
            bool hasMetadataChanges = staged.MetadataMutations.Length > 0;
            byte[] nextProjectBytes = hasMetadataChanges ? SerializeProjectXml(nextXml) : [];
            string nextProjectRevision = hasMetadataChanges
                ? HashBytes(nextProjectBytes)
                : source.Revision;
            string committedXml = hasMetadataChanges ? Encoding.UTF8.GetString(nextProjectBytes) : source.Xml;
            string committedXmlSHA = HashText(committedXml);

            EnsureSnapshotUnchanged(source);
            EnsureAssetUnchanged(request.AssetPath, request.ExpectedAssetSHA);
            if (HasFilesystemEntry(targetPath))
                throw new IOException("A model file appeared at the duplicate destination during the action.");

            bool assetPublished = false;
            bool projectMetadataCommitted = !hasMetadataChanges;
            bool projectMetadataWritten = false;
            string? projectTemp = null;
            try
            {
                File.Move(stagePath, targetPath, overwrite: false);
                assetPublished = true;
                if (!string.Equals(HashFile(targetPath), targetSHA, StringComparison.Ordinal))
                    throw new SourceFileChangedException();

                // Validate the published source model and its association rows before changing .ras.
                CatalogTreeSnapshot postCopyTree = CatalogTreeService.LoadProject(new CatalogTreeRequest(
                    source.Path, committedXml, Mode: "Project", ProjectSessionID: request.ProjectSessionID,
                    CoreLayerContext: request.CoreLayerContext));
                VerifyPublishedNode(postCopyTree, identity, targetPath, newName);

                if (hasMetadataChanges)
                {
                    EnsureSnapshotUnchanged(source);
                    projectTemp = WriteProjectTemp(source.Path, nextProjectBytes);
                    EnsureSnapshotUnchanged(source);
                    File.Move(projectTemp, source.Path, overwrite: true);
                    projectTemp = null;
                    projectMetadataWritten = true;
                    projectMetadataCommitted = true;

                    (string? savedPath, string? savedRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
                    if (savedPath is null || savedRevision is null ||
                        !SourceFilePaths.SameSourceFile(savedPath, source.Path) ||
                        !string.Equals(savedRevision, nextProjectRevision, StringComparison.Ordinal))
                    {
                        string? warning = "副本已创建，但保存后的工程修订无法稳定读回；请刷新工程后核对副本。";
                        return BuildReceipt(false, assetPublished, true, source, committedXml,
                            nextProjectRevision, committedXmlSHA, request, identity, targetPath, targetSHA,
                            newName, AppendWarning(warning, CleanupStagingOnce()));
                    }

                    string savedXml = File.ReadAllText(source.Path);
                    string savedXmlSHA = HashText(savedXml);
                    (string? readbackPath, string? readbackRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
                    if (!string.Equals(savedXmlSHA, committedXmlSHA, StringComparison.Ordinal) ||
                        readbackPath is null || readbackRevision is null ||
                        !SourceFilePaths.SameSourceFile(readbackPath, source.Path) ||
                        !string.Equals(readbackRevision, savedRevision, StringComparison.Ordinal))
                    {
                        string warning = "副本已创建，但工程文件读回内容与保存回执不一致；请刷新工程后核对。";
                        return BuildReceipt(false, assetPublished, true, source, savedXml,
                            readbackRevision ?? savedRevision, savedXmlSHA, request, identity, targetPath, targetSHA,
                            newName, AppendWarning(warning, CleanupStagingOnce()));
                    }
                    committedXml = savedXml;
                    committedXmlSHA = savedXmlSHA;
                    nextProjectRevision = savedRevision;
                }
                else
                {
                    EnsureSnapshotUnchanged(source);
                }

                if (hasMetadataChanges)
                    EnsureCommittedSnapshot(source.Path, committedXml, nextProjectRevision, committedXmlSHA);

                string? cleanupWarning = CleanupStagingOnce();
                return BuildReceipt(true, assetPublished, projectMetadataCommitted, source,
                    committedXml, nextProjectRevision, committedXmlSHA, request, identity,
                    targetPath, targetSHA, newName, cleanupWarning);
            }
            catch (Exception exception) when (assetPublished)
            {
                if (projectMetadataWritten)
                {
                    string verificationWarning = "副本已写入，但提交后验证未完成；请刷新工程并核对副本。";
                    return BuildReceipt(false, true, true, source, committedXml,
                        nextProjectRevision, committedXmlSHA, request, identity, targetPath, targetSHA,
                        newName, AppendWarning(verificationWarning, CleanupStagingOnce()));
                }

                string? rollbackWarning = null;
                try
                {
                    EnsureSnapshotUnchanged(source);
                    if (!HasFilesystemEntry(targetPath))
                    {
                        assetPublished = false;
                    }
                    else if (IsRegularFileEntry(targetPath) &&
                        string.Equals(HashFile(targetPath), targetSHA, StringComparison.Ordinal))
                    {
                        File.Delete(targetPath);
                        assetPublished = false;
                    }
                    else if (File.Exists(targetPath) || Directory.Exists(targetPath))
                    {
                        rollbackWarning = "副本目标在操作期间发生变化，服务没有删除该文件。";
                    }
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    rollbackWarning = rollbackException is SourceFileChangedException
                        ? "工程文件在复制期间发生变化，服务保留了新副本；请刷新工程核对。"
                        : "副本已写入，但工程关联未保存且自动回滚失败。";
                }

                if (!assetPublished)
                    throw new IOException("工程关联未保存，已回滚本次新副本。", exception);

                string warning = rollbackWarning ?? "副本已创建，但工程关联未保存；请刷新工程并核对副本。";
                return BuildReceipt(false, true, projectMetadataCommitted, source, source.Xml, source.Revision,
                    source.XmlSHA, request, identity, targetPath, targetSHA, newName,
                    AppendWarning(warning, CleanupStagingOnce()));
            }
            finally
            {
                if (projectTemp is not null)
                {
                    try { File.Delete(projectTemp); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            }
            finally
            {
                CleanupStagingOnce();
            }
    }

    private static ModelIdentity ValidateRequest(ProjectTreeModelActionInspectRequest request)
    {
        if (!string.Equals(request.Action, "duplicate", StringComparison.Ordinal))
            throw new NotSupportedException("本次工程树只支持复制模型。");
        ModelIdentity identity = request.Kind switch
        {
            "Geometry" => new("Geometry", "GeometriesSectionContext", "ObservableGeometry",
                "GeometryAssociations", "GeometryAssociation", "Geometry"),
            "Plan" => new("Plan", "PlansSectionContext", "ObservablePlan",
                "PlanAssociations", "PlanAssociation", "Plan"),
            _ => throw new NotSupportedException("当前仅支持复制工程中的几何和计划模型。")
        };
        if (!string.Equals(request.RootSourceClass, identity.RootSourceClass, StringComparison.Ordinal) ||
            !string.Equals(request.SourceClass, identity.SourceClass, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        ValidateCommonIdentity(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            request.ProjectRevision, request.ProjectSessionID, request.NodeID, request.NodeName,
            request.AssetPath);
        if (request.CoreLayerContext is { } context &&
            !string.Equals(context.ProjectSessionID, request.ProjectSessionID, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return identity;
    }

    private static ModelIdentity ValidateRequest(ProjectTreeModelActionPerformRequest request)
    {
        ModelIdentity identity = ValidateRequest(new ProjectTreeModelActionInspectRequest(
            request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA, request.ProjectRevision,
            request.ProjectSessionID, request.Action, request.Kind, request.NodeID,
            request.RootSourceClass, request.SourceClass, request.NodeName, request.AssetPath,
            request.CoreLayerContext));
        if (request.CoreLayerContext is { } context &&
            !string.Equals(context.ProjectSessionID, request.ProjectSessionID, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return identity;
    }

    private static void ValidateCommonIdentity(string projectPath, string projectXml, string projectXmlSHA,
        string projectRevision, string projectSessionID, string nodeID, string nodeName, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Path.IsPathFullyQualified(projectPath) ||
            !Path.GetExtension(projectPath).Equals(".ras", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("先打开已保存的 .ras 工程，再复制工程树模型。");
        if (string.IsNullOrWhiteSpace(projectXml) ||
            Encoding.UTF8.GetByteCount(projectXml) > 8L * 1024 * 1024 ||
            !IsSHA256(projectXmlSHA) || !string.Equals(HashText(projectXml), projectXmlSHA,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (!IsSHA256(projectRevision))
            throw new ArgumentException("缺少有效的工程源修订。");
        if (string.IsNullOrWhiteSpace(projectSessionID))
            throw new ArgumentException("缺少工程会话标识。");
        if (string.IsNullOrWhiteSpace(nodeID) || string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("请选择一个工程树模型。");
        if (string.IsNullOrWhiteSpace(assetPath) || !Path.IsPathFullyQualified(assetPath))
            throw new ArgumentException("请选择已保存的工程模型文件。");
    }

    private static ProjectSnapshot ReadProjectSnapshot(ProjectTreeModelActionInspectRequest request)
    {
        string fullPath = SourceFilePaths.Canonical(request.ProjectPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("当前工程文件不存在。", fullPath);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(fullPath);
            if (beforePath is null || beforeRevision is null) continue;
            string xml;
            try { xml = File.ReadAllText(fullPath); }
            catch (IOException) when (attempt < 2) { continue; }
            (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(fullPath);
            if (afterPath is null || afterRevision is null ||
                !SourceFilePaths.SameSourceFile(beforePath, afterPath) ||
                !string.Equals(beforeRevision, afterRevision, StringComparison.Ordinal)) continue;
            if (!SourceFilePaths.SameSourceFile(beforePath, request.ProjectPath) ||
                !string.Equals(beforeRevision, request.ProjectRevision, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(HashText(xml), request.ProjectXmlSHA, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(xml, request.ProjectXml, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            XElement parsed;
            try { parsed = XElement.Parse(xml, LoadOptions.None); }
            catch (XmlException exception) { throw new InvalidDataException("当前工程 XML 无法解析。", exception); }
            if (parsed.Name.LocalName != "Project") throw new InvalidDataException("当前工程 XML 根节点无效。");
            return new(beforePath, xml, beforeRevision, HashText(xml));
        }
        throw new SourceFileChangedException();
    }

    private static ProjectSnapshot ReadProjectSnapshot(ProjectTreeModelActionPerformRequest request) =>
        ReadProjectSnapshot(new ProjectTreeModelActionInspectRequest(
            request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA, request.ProjectRevision,
            request.ProjectSessionID, request.Action, request.Kind, request.NodeID,
            request.RootSourceClass, request.SourceClass, request.NodeName, request.AssetPath,
            request.CoreLayerContext));

    private static void ValidateCoreLayerContext(string sessionID, ProjectSnapshot source,
        CoreLayerRequestContext? context)
    {
        if (context is null) return;
        if (!string.Equals(context.ProjectSessionID, sessionID, StringComparison.Ordinal) ||
            !SourceFilePaths.SameSourceFile(context.ProjectPath, source.Path) ||
            !string.Equals(context.ProjectRevision, source.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static CatalogTreeNode FindSourceNode(ProjectTreeModelActionInspectRequest request,
        ModelIdentity identity, string projectXml)
    {
        CatalogTreeSnapshot snapshot = CatalogTreeService.LoadProject(new CatalogTreeRequest(
            request.ProjectPath, projectXml, Mode: "Project", ProjectSessionID: request.ProjectSessionID,
            CoreLayerContext: request.CoreLayerContext));
        CatalogTreeNode[] roots = snapshot.Roots.Where(root =>
            string.Equals(root.SourceClass, identity.RootSourceClass, StringComparison.Ordinal)).ToArray();
        if (roots.Length != 1) throw new SourceFileChangedException();
        CatalogTreeNode[] matches = (roots[0].Children ?? []).Where(node =>
            string.Equals(node.Id, request.NodeID, StringComparison.Ordinal) &&
            string.Equals(node.SourceClass, identity.SourceClass, StringComparison.Ordinal) &&
            string.Equals(node.Name, request.NodeName, StringComparison.Ordinal) &&
            node.AssetID is { } nodePath && SourceFilePaths.SameSourceFile(nodePath, request.AssetPath)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static CatalogTreeNode FindSourceNode(ProjectTreeModelActionPerformRequest request,
        ModelIdentity identity, string projectXml) =>
        FindSourceNode(new ProjectTreeModelActionInspectRequest(
            request.ProjectPath, projectXml, request.ProjectXmlSHA, request.ProjectRevision,
            request.ProjectSessionID, request.Action, request.Kind, request.NodeID,
            request.RootSourceClass, request.SourceClass, request.NodeName, request.AssetPath,
            request.CoreLayerContext), identity, projectXml);

    private static CatalogModelActionRequest ToCatalogRequest(ProjectTreeModelActionInspectRequest request,
        ModelIdentity identity, string assetRevision) => new(request.ProjectPath, request.ProjectXml,
        request.AssetPath, identity.Kind, assetRevision, "duplicate", CoreLayerContext: request.CoreLayerContext);

    private static CatalogModelActionRequest ToCatalogRequest(ProjectTreeModelActionPerformRequest request,
        ModelIdentity identity, string assetRevision) => new(request.ProjectPath, request.ProjectXml,
        request.AssetPath, identity.Kind, assetRevision, "duplicate", CoreLayerContext: request.CoreLayerContext);

    private static void ValidateStagedReceipt(CatalogModelActionReceipt receipt,
        ProjectTreeModelActionPerformRequest request, ModelIdentity identity, ProjectSnapshot source,
        string newName)
    {
        if (!string.Equals(receipt.Action, "duplicate", StringComparison.Ordinal) ||
            !string.Equals(receipt.Kind, identity.Kind, StringComparison.Ordinal) ||
            !string.Equals(receipt.OriginalSHA, request.ExpectedAssetSHA, StringComparison.OrdinalIgnoreCase) ||
            !SourceFilePaths.SameSourceFile(receipt.CanonicalOriginalAssetPath, request.AssetPath) ||
            !string.Equals(receipt.ProjectXmlFingerprint, source.XmlSHA, StringComparison.OrdinalIgnoreCase) ||
            !receipt.ActualClone || string.IsNullOrWhiteSpace(newName) || receipt.StagedTargets.Length != 1 ||
            receipt.StagingDirectories.Length != 1 || receipt.TrashPaths is { Length: > 0 })
            throw new InvalidDataException("源模型复制回执不完整，未发布副本。");

        CatalogModelActionStagedTarget target = receipt.StagedTargets[0];
        CatalogModelActionStagingDirectory staging = receipt.StagingDirectories[0];
        string originalPath = SourceFilePaths.Canonical(request.AssetPath);
        string targetPath = SourceFilePaths.Canonical(target.TargetPath);
        string stageDirectory = SourceFilePaths.Canonical(staging.Path);
        string stagePath = SourceFilePaths.Canonical(target.StagePath);
        string expectedStageDirectory = SourceFilePaths.Canonical(Path.Combine(
            Path.GetDirectoryName(targetPath)!, ".catalog-action-staging", staging.Token));
        if (!IsSHA256(target.StageSHA) || target.ExpectedTargetSHA is not null ||
            !SourceFilePaths.SameSourceFile(stageDirectory, expectedStageDirectory) ||
            !SourceFilePaths.SameSourceFile(Path.GetDirectoryName(stagePath)!, stageDirectory) ||
            !string.Equals(Path.GetFileName(stagePath), Path.GetFileName(targetPath), StringComparison.Ordinal) ||
            !SourceFilePaths.SameSourceFile(Path.GetDirectoryName(targetPath)!, Path.GetDirectoryName(originalPath)!) ||
            !Path.GetExtension(targetPath).Equals(".h5", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileNameWithoutExtension(targetPath), newName, StringComparison.Ordinal) ||
            Path.GetFileName(stageDirectory) != staging.Token ||
            Path.GetFileName(Path.GetDirectoryName(stageDirectory)!) != ".catalog-action-staging" ||
            !Guid.TryParseExact(staging.Token, "N", out _) ||
            !IsWithinDirectory(targetPath, Path.GetDirectoryName(source.Path)!))
            throw new InvalidDataException("源模型复制回执包含无效的目标路径。");
        if (!File.Exists(stagePath) ||
            !string.Equals(HashFile(stagePath), target.StageSHA, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (receipt.MetadataMutations.Any(mutation =>
                !string.Equals(mutation.Section, identity.AssociationSection, StringComparison.Ordinal) ||
                !string.Equals(mutation.RowName, identity.AssociationRow, StringComparison.Ordinal) ||
                !string.Equals(mutation.IdentityAttribute, identity.IdentityAttribute, StringComparison.Ordinal) ||
                !string.Equals(mutation.IdentityValue, newName, StringComparison.Ordinal) ||
                mutation.RemoveRow || !IsAllowedAssociationAttribute(identity, mutation.Attribute)))
            throw new InvalidDataException("源模型复制回执包含不支持的工程关联变更。");
    }

    private static void VerifyPublishedNode(CatalogTreeSnapshot snapshot, ModelIdentity identity,
        string targetPath, string newName)
    {
        CatalogTreeNode[] roots = snapshot.Roots.Where(root =>
            string.Equals(root.SourceClass, identity.RootSourceClass, StringComparison.Ordinal)).ToArray();
        if (roots.Length != 1) throw new InvalidDataException("复制后的工程分组无法确认。");
        CatalogTreeNode[] matches = (roots[0].Children ?? []).Where(node =>
            string.Equals(node.SourceClass, identity.SourceClass, StringComparison.Ordinal) &&
            string.Equals(node.Name, newName, StringComparison.Ordinal) &&
            node.AssetID is { } assetPath && SourceFilePaths.SameSourceFile(assetPath, targetPath)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("复制后的模型没有出现在当前工程树中。");
    }

    private static string ApplyMetadataMutations(string projectXml,
        CatalogModelActionMetadataMutation[] mutations, ModelIdentity identity, string newName)
    {
        if (mutations.Length == 0) return projectXml;
        XElement root = XElement.Parse(projectXml, LoadOptions.None);
        if (root.Name.LocalName != "Project") throw new InvalidDataException("当前工程 XML 根节点无效。");
        XNamespace ns = root.Name.Namespace;
        foreach (CatalogModelActionMetadataMutation mutation in mutations)
        {
            if (!string.Equals(mutation.Section, identity.AssociationSection, StringComparison.Ordinal) ||
                !string.Equals(mutation.RowName, identity.AssociationRow, StringComparison.Ordinal) ||
                !string.Equals(mutation.IdentityAttribute, identity.IdentityAttribute, StringComparison.Ordinal) ||
                !string.Equals(mutation.IdentityValue, newName, StringComparison.Ordinal) ||
                mutation.RemoveRow || !IsAllowedAssociationAttribute(identity, mutation.Attribute))
                throw new InvalidDataException("源模型复制回执包含不支持的工程关联变更。");

            XElement[] sections = root.Elements().Where(element =>
                string.Equals(element.Name.LocalName, mutation.Section, StringComparison.Ordinal)).ToArray();
            if (sections.Length > 1) throw new InvalidDataException("工程关联分组重复，不能安全保存副本。");
            XElement section;
            if (sections.Length == 0)
            {
                if (mutation.NewValue is null || mutation.RemoveRow)
                    throw new SourceFileChangedException();
                section = new XElement(ns + mutation.Section);
                root.Add(section);
            }
            else section = sections[0];

            XElement[] rows = section.Elements().Where(element =>
                string.Equals(element.Name.LocalName, mutation.RowName, StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute(mutation.IdentityAttribute), mutation.IdentityValue,
                    StringComparison.Ordinal)).ToArray();
            if (rows.Length > 1) throw new InvalidDataException("模型关联行重复，不能安全保存副本。");
            XElement? row = rows.SingleOrDefault();
            if (mutation.RemoveRow)
            {
                if (row is null) throw new SourceFileChangedException();
                row.Remove();
                continue;
            }
            if (row is null)
            {
                if (mutation.OldValue is not null || mutation.Attribute != mutation.IdentityAttribute ||
                    mutation.NewValue is null)
                    throw new SourceFileChangedException();
                row = new XElement(ns + mutation.RowName);
                section.Add(row);
            }
            string? currentValue = (string?)row.Attribute(mutation.Attribute);
            if (!string.Equals(currentValue, mutation.OldValue, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            row.SetAttributeValue(mutation.Attribute, mutation.NewValue);
        }
        return Encoding.UTF8.GetString(SerializeProjectXml(root));
    }

    private static bool IsAllowedAssociationAttribute(ModelIdentity identity, string attribute) => identity.Kind switch
    {
        "Geometry" => attribute is "Geometry" or "Terrain" or "N_Values",
        "Plan" => attribute is "Plan" or "Geometry" or "BoundaryCondition",
        _ => false
    };

    private static byte[] SerializeProjectXml(string projectXml) =>
        SerializeProjectXml(XElement.Parse(projectXml, LoadOptions.None));

    private static byte[] SerializeProjectXml(XElement root)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            Indent = true,
            NewLineHandling = NewLineHandling.Entitize
        };
        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, settings)) root.Save(writer);
        return stream.ToArray();
    }

    private static string WriteProjectTemp(string projectPath, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(projectPath)!;
        string temporaryPath = Path.Combine(directory, ".project-model-action-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temporaryPath;
        }
        catch
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void EnsureSnapshotUnchanged(ProjectSnapshot source)
    {
        (string? path, string? revision) = CatalogTreeService.TryReadStableRevision(source.Path);
        if (path is null || revision is null || !SourceFilePaths.SameSourceFile(path, source.Path) ||
            !string.Equals(revision, source.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        string currentXml = File.ReadAllText(source.Path);
        (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(source.Path);
        if (!string.Equals(HashText(currentXml), source.XmlSHA, StringComparison.Ordinal) ||
            !string.Equals(currentXml, source.Xml, StringComparison.Ordinal) ||
            afterPath is null || afterRevision is null ||
            !SourceFilePaths.SameSourceFile(afterPath, source.Path) ||
            !string.Equals(revision, afterRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void EnsureAssetUnchanged(string path, string expectedRevision)
    {
        (string? stablePath, string? revision) = CatalogTreeService.TryReadStableRevision(path);
        if (stablePath is null || revision is null ||
            !SourceFilePaths.SameSourceFile(stablePath, path) ||
            !string.Equals(revision, expectedRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void EnsureCommittedSnapshot(string path, string expectedXml,
        string expectedRevision, string expectedXmlSHA)
    {
        (string? beforePath, string? beforeRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (beforePath is null || beforeRevision is null ||
            !SourceFilePaths.SameSourceFile(beforePath, path) ||
            !string.Equals(beforeRevision, expectedRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        string currentXml = File.ReadAllText(path);
        (string? afterPath, string? afterRevision) = CatalogTreeService.TryReadStableRevision(path);
        if (afterPath is null || afterRevision is null ||
            !SourceFilePaths.SameSourceFile(afterPath, path) ||
            !string.Equals(beforeRevision, afterRevision, StringComparison.Ordinal) ||
            !string.Equals(HashText(currentXml), expectedXmlSHA, StringComparison.Ordinal) ||
            !string.Equals(currentXml, expectedXml, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static ProjectTreeModelActionReceipt BuildReceipt(bool success, bool assetPublished,
        bool projectMetadataCommitted, ProjectSnapshot source, string projectXml, string projectRevision,
        string projectXmlSHA, ProjectTreeModelActionPerformRequest request, ModelIdentity identity,
        string newAssetPath, string newAssetSHA, string newName, string? warning) =>
        new(success, assetPublished, projectMetadataCommitted, source.Path, request.ProjectSessionID,
            projectRevision, projectXml, projectXmlSHA, "duplicate", identity.Kind,
            SourceFilePaths.Canonical(request.AssetPath), request.ExpectedAssetSHA,
            newAssetPath, newAssetSHA, newName, warning);

    private static string? CleanupStaging(CatalogModelActionStagingDirectory[] stagingDirectories)
    {
        string? warning = null;
        foreach (CatalogModelActionStagingDirectory staging in stagingDirectories)
        {
            try
            {
                if (!CatalogModelActionService.Cleanup(new CatalogModelActionCleanupRequest(staging.Path, staging.Token)))
                    warning = "副本已创建，临时目录清理未得到确认。";
            }
            catch (Exception) { warning = "副本已创建，临时目录清理失败。"; }
        }
        return warning;
    }

    private static string? AppendWarning(string? primary, string? secondary) =>
        string.IsNullOrWhiteSpace(secondary) ? primary :
        string.IsNullOrWhiteSpace(primary) ? secondary : primary + secondary;

    private static bool HasFilesystemEntry(string path) => File.Exists(path) || Directory.Exists(path) ||
        new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null;

    private static bool IsRegularFileEntry(string path) => File.Exists(path) && new FileInfo(path).LinkTarget is null;

    private static bool IsWithinDirectory(string path, string directory)
    {
        string canonicalPath = SourceFilePaths.Canonical(path);
        string canonicalDirectory = Path.TrimEndingDirectorySeparator(SourceFilePaths.Canonical(directory));
        string relative = Path.GetRelativePath(canonicalDirectory, canonicalPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return !Path.IsPathRooted(relative) && relative != "." && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison);
    }

    private static string HashFile(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string text) => HashBytes(Encoding.UTF8.GetBytes(text));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsSHA256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
