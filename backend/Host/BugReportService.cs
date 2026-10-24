using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Ras.Layers;
using Utility.Extensions;
using Utility.Web;

namespace Ras.Backend;

/// <summary>
/// Portable adapter for the retained JiraBugReportForm protocol. Issue
/// creation is the only operation that contacts the external report service;
/// geometry capture is a local, read-only source identity check.
/// </summary>
public static class BugReportService
{
    public const int MaximumLogBytes = 25_600;
    public const int MaximumScreenshots = 4;
    public const int MaximumScreenshotBytes = 16 * 1024 * 1024;
    private const int MaximumEmailCharacters = 1_000;
    private const int MaximumSummaryCharacters = 100;
    private const int MaximumDescriptionCharacters = 3_000;
    private const int MaximumProjectXmlBytes = 8 * 1024 * 1024;
    private const int MaximumModuleInventoryBytes = 12 * 1024;
    private const int MaximumCapturedModuleCandidates = 256;
    private const int MaximumModuleNameCharacters = 160;
    private const int MaximumModuleVersionCharacters = 80;
    private const string LinkID = "ras-2025-bugreports";
    private const string KnownIssuesLinkID = "ras-2025-docs-knownissues";
    private const string ReportAPIKey = "ras010_65fd3e26933b";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(90)
    };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static async Task<BugReportComponentsResult> ComponentsAsync(BugReportComponentsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string server = await ResolveServerAsync().ConfigureAwait(false);
        return await LoadComponentsAsync(server).ConfigureAwait(false);
    }

    private static async Task<BugReportComponentsResult> LoadComponentsAsync(string server)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(server + "/components")))
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The bug report service could not load its report categories.");

        List<RemoteComponent>? remote;
        try
        {
            remote = await response.Content.ReadFromJsonAsync<List<RemoteComponent>>(BackendJson.Options)
                .ConfigureAwait(false);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The bug report service returned invalid category data.", error);
        }
        if (remote is null)
            throw new InvalidDataException("The bug report service returned no category list.");

        BugReportComponent[] components = remote
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new BugReportComponent(item.Id, item.Name,
                GeometryAttachComponentNames.Any(item.Name.RoughlyEquals)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (components.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != components.Length)
            throw new InvalidDataException("The bug report service returned duplicate category identities.");

        return new BugReportComponentsResult(Project.SoftwareVersionStripAllTags,
            HECFWLink.BuildURL(KnownIssuesLinkID), components);
    }

    private static readonly string[] GeometryAttachComponentNames = ["Geometry", "Mesh Generation"];

    public static BugReportGeometryCapture CaptureGeometry(BugReportGeometryCaptureRequest request)
    {
        ValidateGeometryRequest(request);
        string projectPath = SourceFilePaths.Canonical(request.ProjectPath);
        string projectRevision = ReadRevision(projectPath);
        RequireRevision(projectRevision, request.ExpectedProjectRevision);

        // Reject a stale known HDF before opening it through Project, so a
        // replacement or truncated file cannot be mistaken for an identity
        // error during the source model load.
        string initialGeometryRevision = ReadRevision(request.GeometryPath);
        if (request.ExpectedGeometryRevision is { Length: > 0 } expectedGeometry)
            RequireRevision(initialGeometryRevision, expectedGeometry);

        Project project = new(request.ProjectPath);
        try
        {
            project.ApplyEditorMetadata(ParseProjectXml(request.ProjectXml));
            string expectedGeometryPath = SourceFilePaths.Canonical(request.GeometryPath);
            Geometry[] matches = project.Geometries.Where(geometry =>
                    string.Equals(geometry.Name, request.GeometryName, StringComparison.Ordinal) &&
                    SamePath(geometry.Filename, expectedGeometryPath) &&
                    SamePath(geometry.Filename, request.GeometryAssetID))
                .ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected Geometry is not one unique source asset in this Project.");

            Geometry geometry = matches[0];
            string geometryRevision = ReadRevision(geometry.Filename);
            RequireRevision(geometryRevision, initialGeometryRevision);
            if (request.ExpectedGeometryRevision is { Length: > 0 } expected)
                RequireRevision(geometryRevision, expected);
            RequireRevision(ReadRevision(projectPath), projectRevision);

            return new BugReportGeometryCapture(projectPath, projectRevision,
                request.ProjectXmlFingerprint, SourceFilePaths.Canonical(geometry.Filename),
                geometry.Name, SourceFilePaths.Canonical(geometry.Filename), geometryRevision);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    public static async Task<BugReportSubmitReceipt> SubmitAsync(BugReportSubmitRequest request)
    {
        ValidateSubmission(request);
        string server = await ResolveServerAsync().ConfigureAwait(false);
        BugReportComponent[] currentComponents = (await LoadComponentsAsync(server).ConfigureAwait(false)).Components;
        var selected = new HashSet<string>(request.ComponentIDs, StringComparer.Ordinal);
        BugReportComponent[] selectedComponents = currentComponents
            .Where(component => selected.Contains(component.Id)).ToArray();
        if (selectedComponents.Length != selected.Count)
            throw new ArgumentException("One or more selected report categories are no longer available.");

        bool selectedGeometryCategory = selectedComponents.Any(component => component.AttachesGeometry);
        string? preparedGeometryBackup = null;
        try
        {
            if (request.Geometry is not null)
            {
                if (!selectedGeometryCategory)
                    throw new ArgumentException("Select a Geometry-related report category before attaching a Geometry.");
                preparedGeometryBackup = CreateGeometryBackup(request.Geometry);
            }

            var payload = new IssuePayload(request.Uid, request.RasVersion, request.Os, request.Cores,
                request.Timezone, request.Email, request.Summary, request.Description,
                ComposeErrorLog(request.ErrorLog, request.CrashContext),
                request.ComponentIDs);
            using var issueResponse = await SendAsync(new HttpRequestMessage(HttpMethod.Post,
                AppendKey(server + "/report"))
            {
                Content = JsonContent.Create(payload, options: BackendJson.Options)
            }).ConfigureAwait(false);
            if (!issueResponse.IsSuccessStatusCode)
                throw new InvalidOperationException("The bug report service rejected the report.");

            IssueResponse? issue;
            try
            {
                issue = await issueResponse.Content.ReadFromJsonAsync<IssueResponse>(BackendJson.Options)
                    .ConfigureAwait(false);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("The bug report service returned an invalid issue receipt.", error);
            }
            if (issue is null || string.IsNullOrWhiteSpace(issue.Id) || string.IsNullOrWhiteSpace(issue.Key))
                throw new InvalidDataException("The bug report service returned an incomplete issue receipt.");

            var warnings = new List<string>();
            foreach (BugReportImage image in request.Images)
            {
                try
                {
                    using var upload = new HttpRequestMessage(HttpMethod.Post,
                        AppendKey(server + "/screenshot/" + Uri.EscapeDataString(issue.Id)))
                    {
                        Content = MultipartFile(image.Data, image.Name, image.ContentType)
                    };
                    using HttpResponseMessage uploadResponse = await SendAsync(upload).ConfigureAwait(false);
                    if (!uploadResponse.IsSuccessStatusCode)
                        warnings.Add("One or more screenshots failed to send with the report.");
                }
                catch (Exception)
                {
                    warnings.Add("One or more screenshots failed to send with the report.");
                }
            }

            if (preparedGeometryBackup is not null)
            {
                try
                {
                    var geometryStream = new FileStream(preparedGeometryBackup, FileMode.Open, FileAccess.Read,
                        FileShare.Read, bufferSize: 128 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var upload = new HttpRequestMessage(HttpMethod.Post,
                        AppendKey(server + "/geometry/" + Uri.EscapeDataString(issue.Id)))
                    {
                        Content = MultipartFile(geometryStream, "geometry.h5", "application/x-hdf5")
                    };
                    using HttpResponseMessage uploadResponse = await SendAsync(upload).ConfigureAwait(false);
                    if (!uploadResponse.IsSuccessStatusCode)
                        warnings.Add("The Geometry could not be attached to the report.");
                }
                catch (Exception)
                {
                    warnings.Add("The Geometry could not be attached to the report.");
                }
            }

            return new BugReportSubmitReceipt(issue.Key, warnings.Distinct(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            if (preparedGeometryBackup is not null)
            {
                try { File.Delete(preparedGeometryBackup); }
                catch { /* A temporary backup cleanup must not mask the report result. */ }
            }
        }
    }

    public static void ValidateSubmission(BugReportSubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Uid) || request.Uid.Length > 128 ||
            string.IsNullOrWhiteSpace(request.RasVersion) || string.IsNullOrWhiteSpace(request.Os) ||
            string.IsNullOrWhiteSpace(request.Cores) || string.IsNullOrWhiteSpace(request.Timezone))
            throw new ArgumentException("Application report information is incomplete.");
        if (string.IsNullOrEmpty(request.Summary) || request.Summary.Length > MaximumSummaryCharacters)
            throw new ArgumentException("The report title must contain 1 to 100 characters.");
        if (string.IsNullOrEmpty(request.Description) || request.Description.Length > MaximumDescriptionCharacters)
            throw new ArgumentException("The report description must contain 1 to 3000 characters.");
        if (request.Email is null || request.Email.Length > MaximumEmailCharacters)
            throw new ArgumentException("The report email address cannot exceed 1000 characters.");
        if (request.ErrorLog is null || Encoding.UTF8.GetByteCount(request.ErrorLog) > MaximumLogBytes)
            throw new ArgumentException("The attached application log exceeds the 25,600-byte limit.");
        if (request.CrashContext is { Length: > 4_096 })
            throw new ArgumentException("The crash context is too large to attach.");
        if (request.ComponentIDs is null || request.ComponentIDs.Any(string.IsNullOrWhiteSpace) ||
            request.ComponentIDs.Distinct(StringComparer.Ordinal).Count() != request.ComponentIDs.Length)
            throw new ArgumentException("The selected report categories are invalid.");
        if (request.Images is null || request.Images.Length > MaximumScreenshots)
            throw new ArgumentException("A maximum of 4 screenshots can be attached.");
        foreach (BugReportImage image in request.Images)
        {
            if (image is null || string.IsNullOrWhiteSpace(image.Name) ||
                image.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                string.IsNullOrWhiteSpace(image.ContentType) || image.Data is null ||
                image.Data.Length == 0 || image.Data.Length > MaximumScreenshotBytes)
                throw new ArgumentException("Each screenshot must be a readable image no larger than 16 MB.");
            if (!AllowedImage(image.Name, image.ContentType, image.Data))
                throw new ArgumentException("Screenshots must use PNG or JPEG format.");
        }
        if (request.Geometry is not null) ValidateGeometryRequest(request.Geometry);
    }

    private static string CreateGeometryBackup(BugReportGeometryCaptureRequest request)
    {
        BugReportGeometryCapture captured = CaptureGeometry(request);
        Project project = new(request.ProjectPath);
        string backupPath;
        do
        {
            backupPath = Path.Combine(Path.GetTempPath(), "hecras-bug-report-" + Guid.NewGuid().ToString("N") + ".h5");
        } while (File.Exists(backupPath) || Directory.Exists(backupPath));
        try
        {
            project.ApplyEditorMetadata(ParseProjectXml(request.ProjectXml));
            Geometry geometry = project.Geometries.Single(candidate =>
                SamePath(candidate.Filename, captured.GeometryPath) &&
                string.Equals(candidate.Name, captured.GeometryName, StringComparison.Ordinal));
            RequireRevision(ReadRevision(request.ProjectPath), captured.ProjectRevision);
            RequireRevision(ReadRevision(geometry.Filename), captured.GeometryRevision);
            geometry.SaveGeometryBackup(backupPath);
            RequireRevision(ReadRevision(request.ProjectPath), captured.ProjectRevision);
            RequireRevision(ReadRevision(geometry.Filename), captured.GeometryRevision);
            using var reopened = new Geometry(backupPath);
            if (reopened.HasLoadErrors)
                throw new InvalidDataException("The selected Geometry could not be prepared as a report attachment.");
            return backupPath;
        }
        catch
        {
            try { if (File.Exists(backupPath)) File.Delete(backupPath); }
            catch { /* Preserve the original failure; this path is unique and app-owned. */ }
            throw;
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static void ValidateGeometryRequest(BugReportGeometryCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath) ||
            !string.Equals(Path.GetExtension(request.ProjectPath), Project.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A saved Project is required to attach a Geometry.");
        if (!File.Exists(request.ProjectPath)) throw new FileNotFoundException("The saved Project is unavailable.");
        ValidateRevision(request.ExpectedProjectRevision);
        if (string.IsNullOrWhiteSpace(request.ProjectXml) ||
            Encoding.UTF8.GetByteCount(request.ProjectXml) > MaximumProjectXmlBytes)
            throw new ArgumentException("The current Project XML context is unavailable.");
        if (!HashText(request.ProjectXml).Equals(request.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The current Project XML context has changed.");
        _ = ParseProjectXml(request.ProjectXml);
        if (string.IsNullOrWhiteSpace(request.GeometryName) ||
            string.IsNullOrWhiteSpace(request.GeometryPath) || !Path.IsPathFullyQualified(request.GeometryPath) ||
            string.IsNullOrWhiteSpace(request.GeometryAssetID) || !Path.IsPathFullyQualified(request.GeometryAssetID) ||
            !string.Equals(Path.GetExtension(request.GeometryPath), ".h5", StringComparison.OrdinalIgnoreCase) ||
            !SamePath(request.GeometryPath, request.GeometryAssetID) || !File.Exists(request.GeometryPath))
            throw new ArgumentException("The selected Geometry identity is incomplete or unavailable.");
        if (request.ExpectedGeometryRevision is { Length: > 0 } revision) ValidateRevision(revision);
    }

    private static XElement ParseProjectXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumProjectXmlBytes
        });
        XElement element = XElement.Load(reader, LoadOptions.None);
        if (!element.Name.ToString().RoughlyEquals("Project"))
            throw new ArgumentException("The current Project XML context is invalid.");
        return element;
    }

    private static async Task<string> ResolveServerAsync()
    {
        var (resolved, text) = await HECFWLink.TryGetLinkIDAsRawString(LinkID).ConfigureAwait(false);
        if (!resolved || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("The bug report service address could not be resolved securely.");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static Uri AppendKey(string endpoint)
    {
        var builder = new UriBuilder(endpoint) { Query = "key=" + Uri.EscapeDataString(ReportAPIKey) };
        return builder.Uri;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            try
            {
                HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                if (statusCode >= (int)HttpStatusCode.MultipleChoices && statusCode < (int)HttpStatusCode.BadRequest)
                {
                    response.Dispose();
                    throw new InvalidOperationException("The bug report service redirected a request.");
                }
                return response;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException) { throw; }
            catch (Exception error)
            {
                throw new InvalidOperationException("The bug report service could not be reached.", error);
            }
        }
    }

    private static MultipartFormDataContent MultipartFile(byte[] data, string name, string contentType)
    {
        var multipart = new MultipartFormDataContent();
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(content, "file", name);
        return multipart;
    }

    private static MultipartFormDataContent MultipartFile(Stream stream, string name, string contentType)
    {
        var multipart = new MultipartFormDataContent();
        var content = new StreamContent(stream, 128 * 1024);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(content, "file", name);
        return multipart;
    }

    private static bool AllowedImage(string name, string contentType, byte[] data)
    {
        if (name.Contains('/') || name.Contains('\\') || name.Contains('\r') || name.Contains('\n')) return false;
        string extension = Path.GetExtension(name);
        bool png = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                   contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) &&
                   data.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        bool jpeg = (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) &&
                    contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                    data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;
        return png || jpeg;
    }

    internal static string ComposeErrorLog(string errorLog, string? crashContext)
    {
        byte[] moduleBytes = Encoding.UTF8.GetBytes(SafeCaptureModuleInventoryXml() + "\n");
        byte[] contextBytes = string.IsNullOrWhiteSpace(crashContext)
            ? []
            : Encoding.UTF8.GetBytes("Crash context: " + crashContext.Trim() + "\n");
        contextBytes = Utf8Tail(contextBytes, Math.Max(0, MaximumLogBytes - moduleBytes.Length));
        byte[] logBytes = Encoding.UTF8.GetBytes(errorLog);
        int maximumTailBytes = Math.Max(0, MaximumLogBytes - moduleBytes.Length - contextBytes.Length);
        byte[] logTail = Utf8Tail(logBytes, maximumTailBytes);
        byte[] result = new byte[moduleBytes.Length + contextBytes.Length + logTail.Length];
        Buffer.BlockCopy(moduleBytes, 0, result, 0, moduleBytes.Length);
        Buffer.BlockCopy(contextBytes, 0, result, moduleBytes.Length, contextBytes.Length);
        Buffer.BlockCopy(logTail, 0, result, moduleBytes.Length + contextBytes.Length, logTail.Length);
        return Encoding.UTF8.GetString(result);
    }

    private static string CaptureModuleInventoryXml()
    {
        var candidates = new List<RuntimeModuleCandidate>();
        var candidateKeys = new HashSet<string>(StringComparer.Ordinal);
        int managedEnumerationFailures = 0;
        bool nativeEnumerationUnavailable = false;

        void AddCandidate(string path, string fallbackName, string fallbackVersion, string kind, bool fileBacked)
        {
            string identity = kind + "\0" + (fileBacked ? path : fallbackName + "\0" + fallbackVersion);
            if (candidateKeys.Add(identity))
                candidates.Add(new RuntimeModuleCandidate(path, fallbackName, fallbackVersion, kind, fileBacked));
        }

        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    AssemblyName assemblyName = assembly.GetName();
                    string name = assemblyName.Name ?? "unknown-managed-module";
                    string version = assemblyName.Version?.ToString() ?? string.Empty;
                    string location;
                    try { location = assembly.Location; }
                    catch (NotSupportedException) { location = string.Empty; }
                    AddCandidate(location, name, version, "managed", !string.IsNullOrWhiteSpace(location));
                }
                catch (Exception error) when (IsAssemblyMetadataFailure(error))
                {
                    managedEnumerationFailures++;
                }
            }
        }
        catch (Exception error) when (IsAssemblyMetadataFailure(error))
        {
            managedEnumerationFailures++;
        }

        try
        {
            using Process process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                string path = module.FileName ?? string.Empty;
                string name = module.ModuleName ?? string.Empty;
                AddCandidate(path, name, string.Empty, "native", !string.IsNullOrWhiteSpace(path));
            }
        }
        catch (Exception error) when (IsNativeModuleEnumerationFailure(error))
        {
            nativeEnumerationUnavailable = true;
        }

        RuntimeModuleCandidate[] orderedCandidates = candidates
            .OrderBy(candidate => ModuleBaseName(candidate.Path, candidate.FallbackName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Kind, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        int omittedCandidateCount = Math.Max(0, orderedCandidates.Length - MaximumCapturedModuleCandidates);
        var records = new List<RuntimeModuleRecord>(Math.Min(orderedCandidates.Length, MaximumCapturedModuleCandidates));
        var fileDetailsCache = new Dictionary<string, ModuleFileDetails>(PathComparer);
        foreach (RuntimeModuleCandidate candidate in orderedCandidates.Take(MaximumCapturedModuleCandidates))
        {
            string name = SafeModuleText(ModuleBaseName(candidate.Path, candidate.FallbackName), MaximumModuleNameCharacters);
            string version = SafeModuleText(candidate.FallbackVersion, MaximumModuleVersionCharacters);
            string sha256 = string.Empty;
            string hashStatus = string.Empty;
            if (!candidate.FileBacked)
            {
                hashStatus = "not-file-backed";
            }
            else
            {
                ModuleFileDetails details;
                if (fileDetailsCache.TryGetValue(candidate.Path, out ModuleFileDetails? cached) && cached is not null)
                {
                    details = cached;
                }
                else
                {
                    details = ReadModuleFileDetails(candidate.Path, version);
                    fileDetailsCache[candidate.Path] = details;
                }
                version = SafeModuleText(details.Version, MaximumModuleVersionCharacters);
                sha256 = details.Sha256;
                hashStatus = details.HashStatus;
            }

            records.Add(new RuntimeModuleRecord(name, version, sha256, candidate.Kind, hashStatus));
        }

        RuntimeModuleRecord[] uniqueRecords = records
            .DistinctBy(record => (record.Name, record.Version, record.Sha256, record.Kind, record.HashStatus))
            .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Kind, StringComparer.Ordinal)
            .ThenBy(record => record.Version, StringComparer.Ordinal)
            .ToArray();
        int hashFailureCount = uniqueRecords.Count(record => record.HashStatus == "unavailable");
        int nonFileBackedCount = uniqueRecords.Count(record => record.HashStatus == "not-file-backed");
        var root = new XElement("Modules",
            new XAttribute("Scope", "backend-process"),
            new XAttribute("Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
            new XAttribute("OmittedCount", omittedCandidateCount));
        foreach (RuntimeModuleRecord record in uniqueRecords)
        {
            var element = new XElement("Module",
                new XAttribute("Name", record.Name),
                new XAttribute("Version", record.Version),
                new XAttribute("Kind", record.Kind));
            if (record.Sha256.Length == 64)
                element.Add(new XAttribute("SHA256", record.Sha256));
            else
                element.Add(new XAttribute("HashStatus", record.HashStatus));
            root.Add(element);
        }
        if (managedEnumerationFailures > 0)
            root.Add(new XElement("ScanFailure", new XAttribute("Source", "managed"),
                new XAttribute("Code", "assembly-metadata-unavailable"),
                new XAttribute("Count", managedEnumerationFailures)));
        if (nativeEnumerationUnavailable)
            root.Add(new XElement("ScanFailure", new XAttribute("Source", "native"),
                new XAttribute("Code", "module-enumeration-unavailable")));
        if (hashFailureCount > 0)
            root.Add(new XElement("ScanFailure", new XAttribute("Source", "hash"),
                new XAttribute("Code", "module-content-unavailable"),
                new XAttribute("Count", hashFailureCount)));
        if (nonFileBackedCount > 0)
            root.Add(new XElement("ScanSummary", new XAttribute("Source", "managed"),
                new XAttribute("Code", "dynamic-or-memory-only-assembly"),
                new XAttribute("Count", nonFileBackedCount)));

        XElement[] moduleElements = root.Elements("Module").ToArray();
        int nextRemoval = moduleElements.Length - 1;
        string xml = root.ToString(SaveOptions.DisableFormatting);
        while (Encoding.UTF8.GetByteCount(xml) > MaximumModuleInventoryBytes && nextRemoval >= 0)
        {
            moduleElements[nextRemoval--].Remove();
            omittedCandidateCount++;
            root.SetAttributeValue("OmittedCount", omittedCandidateCount);
            xml = root.ToString(SaveOptions.DisableFormatting);
        }
        return Encoding.UTF8.GetByteCount(xml) <= MaximumModuleInventoryBytes
            ? xml
            : new XElement("Modules",
                new XAttribute("Scope", "backend-process"),
                new XAttribute("Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
                new XAttribute("OmittedCount", omittedCandidateCount),
                new XElement("ScanFailure", new XAttribute("Source", "inventory"),
                    new XAttribute("Code", "inventory-size-limit")))
                .ToString(SaveOptions.DisableFormatting);
    }

    private static string SafeCaptureModuleInventoryXml()
    {
        try { return CaptureModuleInventoryXml(); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return new XElement("Modules",
                new XAttribute("Scope", "backend-process"),
                new XAttribute("Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
                new XAttribute("OmittedCount", 0),
                new XElement("ScanFailure", new XAttribute("Source", "inventory"),
                    new XAttribute("Code", "collection-unavailable")))
                .ToString(SaveOptions.DisableFormatting);
        }
    }

    private static ModuleFileDetails ReadModuleFileDetails(string path, string fallbackVersion)
    {
        string version = fallbackVersion;
        try
        {
            string? fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (!string.IsNullOrWhiteSpace(fileVersion)) version = fileVersion;
        }
        catch (Exception error) when (IsModuleFileFailure(error)) { }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return new ModuleFileDetails(version, Convert.ToHexString(SHA256.HashData(stream)), string.Empty);
        }
        catch (Exception error) when (IsModuleFileFailure(error))
        {
            return new ModuleFileDetails(version, string.Empty, "unavailable");
        }
    }

    private static string ModuleBaseName(string path, string fallbackName)
    {
        string source = string.IsNullOrWhiteSpace(path) ? fallbackName : path;
        int separator = Math.Max(source.LastIndexOf('/'), source.LastIndexOf('\\'));
        return SafeModuleText(separator >= 0 ? source[(separator + 1)..] : source, MaximumModuleNameCharacters);
    }

    private static string SafeModuleText(string value, int maximumCharacters)
    {
        string safe = new(value.Select(character => char.IsControl(character) ? '_' : character).ToArray());
        safe = safe.Trim();
        return safe.Length <= maximumCharacters ? safe : safe[..maximumCharacters];
    }

    private static byte[] Utf8Tail(byte[] value, int maximumBytes)
    {
        if (maximumBytes <= 0 || value.Length == 0) return [];
        int start = Math.Max(0, value.Length - maximumBytes);
        while (start < value.Length && (value[start] & 0xc0) == 0x80) start++;
        return value.AsSpan(start).ToArray();
    }

    private static bool IsModuleFileFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException or System.ComponentModel.Win32Exception;

    private static bool IsAssemblyMetadataFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or NotSupportedException or BadImageFormatException
            or System.Security.SecurityException;

    private static bool IsNativeModuleEnumerationFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception;

    private sealed record RuntimeModuleCandidate(string Path, string FallbackName, string FallbackVersion,
        string Kind, bool FileBacked);
    private sealed record RuntimeModuleRecord(string Name, string Version, string Sha256, string Kind,
        string HashStatus);
    private sealed record ModuleFileDetails(string Version, string Sha256, string HashStatus);

    private static string ReadRevision(string path)
    {
        string canonical = SourceFilePaths.Canonical(path);
        using var stream = new FileStream(canonical, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SamePath(string left, string right) =>
        PathComparer.Equals(SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right));

    private static void ValidateRevision(string? revision)
    {
        if (string.IsNullOrEmpty(revision) || revision.Length != 64 ||
            revision.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A valid SHA-256 source revision is required.");
    }

    private static void RequireRevision(string actual, string expected)
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

    private sealed record RemoteComponent(string Id, string Name);
    private sealed record IssuePayload(string Uid, string RasVersion, string Os, string Cores,
        string Timezone, string Email, string Summary, string Description, string ErrorLog,
        string[] Components);
    private sealed record IssueResponse(string Id, string Key);
}
