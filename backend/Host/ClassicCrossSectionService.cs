using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Geospatial.PairedData;
using H5Assist;
using Ras.Hydraulics;
using Ras.Layers.Classic;

namespace Ras.Backend;

public sealed record ClassicCrossSectionCatalogRequest(string ScopeID, string SourcePath,
    string? ExpectedRevision = null, int Offset = 0, int Limit = 256);
public sealed record ClassicCrossSectionRow(int Ordinal, string RowToken, string NodeName,
    string RiverName, string ReachName, string RiverStation, int ProfilePointCount, bool UsesOverride);
public sealed record ClassicCrossSectionCatalog(string ScopeID, string SourcePath, string SourceRevision,
    string SourceName, string ContextToken, int TotalCount, int Offset, int? NextOffset,
    ClassicCrossSectionRow[] Rows);
public sealed record ClassicCrossSectionProfileRequest(string ScopeID, string SourcePath,
    string ExpectedRevision, string ContextToken, int RowOrdinal, string RowToken);
public sealed record ClassicCrossSectionPoint(int Ordinal, double? Station, double? Elevation);
public sealed record ClassicCrossSectionProfile(string ScopeID, string SourcePath, string SourceRevision,
    string ContextToken, ClassicCrossSectionRow Row, bool HasProfile,
    ClassicCrossSectionPoint[] Points, ClassicCrossSectionPoint[] Banks);

/// <summary>
/// Read-only projection of ClassicGeometry.CrossSectionLayer. An ordinal is
/// valid only for its exact file revision: River/Reach/RS and NodeName are
/// display metadata, not unique feature identities. Modern Geometry is never
/// opened as a ClassicGeometry substitute.
/// </summary>
public static class ClassicCrossSectionService
{
    public static ClassicCrossSectionCatalog Catalog(ClassicCrossSectionCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Offset < 0 || request.Limit is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose a valid cross-section page.");
        SourceIdentity source = OpenIdentity(request.ScopeID, request.SourcePath, request.ExpectedRevision);
        using ClassicGeometry geometry = OpenGeometry(source.Path);
        CrossSectionLayer layer = geometry.CrossSectionLayer;
        int count = layer.Count;
        if (request.Offset > count)
            throw new ArgumentOutOfRangeException(nameof(request), "The cross-section page is outside the source table.");
        int end = checked(request.Offset + Math.Min(request.Limit, count - request.Offset));
        var rows = new List<ClassicCrossSectionRow>(end - request.Offset);
        for (int index = request.Offset; index < end; index++)
            rows.Add(Describe(layer[index], index, source.Token));
        EnsureUnchanged(source);
        return new(source.ScopeID, source.Path, source.Revision, geometry.Name, source.Token,
            count, request.Offset, end < count ? end : null, rows.ToArray());
    }

    public static ClassicCrossSectionProfile Profile(ClassicCrossSectionProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedRevision is null)
            throw new ArgumentException("The cross-section source revision is required.");
        SourceIdentity source = OpenIdentity(request.ScopeID, request.SourcePath, request.ExpectedRevision);
        if (request.ContextToken != source.Token || request.RowOrdinal < 0 ||
            request.RowToken != RowToken(source.Token, request.RowOrdinal))
            throw new SourceFileChangedException();
        using ClassicGeometry geometry = OpenGeometry(source.Path);
        CrossSectionLayer layer = geometry.CrossSectionLayer;
        if (request.RowOrdinal >= layer.Count) throw new SourceFileChangedException();
        CrossSection section = layer[request.RowOrdinal];
        StationElevationProfile? profile = section.OverrideStationElevation ?? section.StationElevation;
        ClassicCrossSectionPoint[] points = profile is null ? [] : Enumerable.Range(0, profile.Count)
            .Select(index => Point(index, profile.Stations[index], profile.Elevations[index])).ToArray();
        ClassicCrossSectionPoint[] banks = [];
        // CrossSectionEditorControl uses the computation profile's Sample,
        // not the polyline Z values or a newly computed terrain profile.
        if (profile is not null && section.BankProperties.HasValidStations)
        {
            double left = section.BankProperties.LeftStation, right = section.BankProperties.RightStation;
            banks = [Point(0, left, profile.Sample(left)), Point(1, right, profile.Sample(right))];
        }
        EnsureUnchanged(source);
        return new(source.ScopeID, source.Path, source.Revision, source.Token,
            Describe(section, request.RowOrdinal, source.Token), profile is not null, points, banks);
    }

    private static ClassicCrossSectionRow Describe(CrossSection section, int ordinal, string token) =>
        new(ordinal, RowToken(token, ordinal), section.NodeName ?? "", section.RiverName ?? "",
            section.ReachName ?? "", section.RiverStation ?? "",
            (section.OverrideStationElevation ?? section.StationElevation)?.Count ?? 0,
            section.OverrideStationElevation is not null);

    private static ClassicCrossSectionPoint Point(int index, double station, double elevation) =>
        new(index, double.IsFinite(station) ? station : null, double.IsFinite(elevation) ? elevation : null);

    private static ClassicGeometry OpenGeometry(string path)
    {
        using (var reader = new H5Reader(path))
        {
            reader.AssertLoadedFileID();
            if (!reader.ExistsAsGroup(CrossSectionLayer.HDF5GroupName))
                throw new NotSupportedException("The selected file is not a ClassicGeometry cross-section source.");
        }
        var geometry = new ClassicGeometry(path);
        if (!geometry.HasLoadErrors) return geometry;
        geometry.Dispose();
        throw new InvalidDataException("The ClassicGeometry source could not be loaded.");
    }

    private static SourceIdentity OpenIdentity(string scopeID, string path, string? revision)
    {
        if (!Guid.TryParse(scopeID, out Guid scope) || scope == Guid.Empty)
            throw new ArgumentException("The cross-section source scope is invalid.");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute ClassicGeometry file path.");
        string canonical = SourceFilePaths.Canonical(path);
        if (!File.Exists(canonical)) throw new FileNotFoundException("The cross-section source file is unavailable.", canonical);
        string actual = Revision(canonical);
        if (revision is not null && (revision.Length != 64 || !revision.All(Uri.IsHexDigit) ||
            !actual.Equals(revision, StringComparison.OrdinalIgnoreCase)))
            throw new SourceFileChangedException();
        string normalizedScope = scope.ToString("D").ToUpperInvariant();
        return new(normalizedScope, canonical, actual, Hash(normalizedScope + "|" + actual + "|" + canonical));
    }

    private static void EnsureUnchanged(SourceIdentity source)
    {
        if (SourceFilePaths.Canonical(source.Path) != source.Path || !File.Exists(source.Path) || Revision(source.Path) != source.Revision)
            throw new SourceFileChangedException();
    }
    private static string RowToken(string token, int ordinal) => Hash(token + "|" + ordinal.ToString(CultureInfo.InvariantCulture));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Revision(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private sealed record SourceIdentity(string ScopeID, string Path, string Revision, string Token);
}
