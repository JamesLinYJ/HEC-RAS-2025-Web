using Ras.Backend;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record GdbTreeCacheAcceptanceReport(
    string State,
    string FixtureSource,
    int SourceFileCount,
    int RasterSubdatasetCount,
    int SharedCatalogCalls,
    int SharedSourceVerifications,
    bool CanonicalAliasesShareCatalog,
    bool SeparateTreesUseFreshCatalogs,
    bool FailedCatalogIsNotRetriedPerChild,
    bool FinalVerificationFailurePropagates,
    bool SourceChangedDuringCatalogPropagates,
    bool SourceChangedAfterCatalogRejected,
    bool SourceBytesRestored);

/// <summary>Real GDAL catalog/manifest checks plus controlled directory-failure contracts.</summary>
internal static class GdbTreeCacheAcceptance
{
    public static GdbTreeCacheAcceptanceReport Run(string sourceDirectory, string outputDirectory)
    {
        if (!Path.IsPathFullyQualified(sourceDirectory) || !Path.IsPathFullyQualified(outputDirectory))
            throw new ArgumentException("Use absolute source and new output directory paths.");
        string sourceRoot = SourceFilePaths.Canonical(sourceDirectory);
        string destination = SourceFilePaths.Canonical(outputDirectory);
        string relativeDestination = Path.GetRelativePath(sourceRoot, destination);
        if (relativeDestination == "." || (!Path.IsPathRooted(relativeDestination) &&
            relativeDestination != ".." && !relativeDestination.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)))
            throw new ArgumentException("The acceptance output must be outside the original GDB directory.");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The GDB cache acceptance output must be a new directory.");
        GdbMapSourceIdentity original = GdbMapSourceService.CaptureSource(sourceRoot);
        Directory.CreateDirectory(destination);
        RasRuntimeBootstrap.Configure();
        string copied = Path.Combine(destination, "catalog-fixture.gdb");
        Directory.CreateDirectory(copied);
        foreach (GdbMapSourceFileRevision file in original.Files)
        {
            string relative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string target = Path.Combine(copied, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(original.SourcePath, relative), target);
        }
        GdbMapSourceService.VerifySource(original);
        int catalogCalls = 0, verifyCalls = 0;
        GdbRasterCatalogResult Capture(string source)
        {
            catalogCalls++;
            return GdbRasterSourceService.CatalogSubdatasets(source);
        }
        void Verify(GdbMapSourceIdentity source)
        {
            verifyCalls++;
            GdbMapSourceService.VerifySource(source);
        }
        var shared = new MapTreeGdbCatalogs(Capture, Verify);
        GdbRasterCatalogResult first = shared.Get(copied);
        if (first.Subdatasets.Length < 10 || first.Subdatasets.Count(item => item.CanRender) < 10)
            throw new InvalidDataException("Use the real GDAL reference GDB with ten renderable raster subdatasets.");
        string alias = Path.Combine(copied, "..", Path.GetFileName(copied));
        for (int child = 0; child < 10; child++)
        {
            GdbRasterCatalogResult next = shared.Get(child % 2 == 0 ? copied : alias);
            if (!ReferenceEquals(first, next))
                throw new InvalidDataException("Sibling raster layers did not share the same captured directory identity.");
        }
        shared.VerifySuccessfulSources();
        if (catalogCalls != 1 || verifyCalls != 1)
            throw new InvalidDataException("A single map tree must catalog and finalize its shared GDB once.");
        int sharedCatalogCalls = catalogCalls, sharedSourceVerifications = verifyCalls;

        var separate = new MapTreeGdbCatalogs(Capture, Verify);
        GdbRasterCatalogResult second = separate.Get(copied);
        separate.VerifySuccessfulSources();
        if (catalogCalls != 2 || verifyCalls != 2 || ReferenceEquals(first, second))
            throw new InvalidDataException("A later tree reused a previous operation's GDB catalog.");

        int failedCalls = 0, failedVerifications = 0;
        var failure = new IOException("Controlled catalog read failure.");
        var failed = new MapTreeGdbCatalogs(_ => { failedCalls++; throw failure; }, _ => failedVerifications++);
        for (int child = 0; child < 10; child++)
        {
            try { _ = failed.Get(child % 2 == 0 ? copied : alias); }
            catch (IOException error) when (ReferenceEquals(error, failure)) { continue; }
            throw new InvalidDataException("The failed directory catalog did not preserve its real read failure.");
        }
        failed.VerifySuccessfulSources();
        if (failedCalls != 1 || failedVerifications != 0)
            throw new InvalidDataException("A failed directory was repeatedly read or treated as successfully captured.");

        var finalFailure = new IOException("Controlled final manifest read failure.");
        var unreadableFinalSource = new MapTreeGdbCatalogs(_ => first, _ => throw finalFailure);
        _ = unreadableFinalSource.Get(copied);
        bool finalFailureObserved = false;
        try { unreadableFinalSource.VerifySuccessfulSources(); }
        catch (IOException error) when (ReferenceEquals(error, finalFailure)) { finalFailureObserved = true; }
        if (!finalFailureObserved)
            throw new InvalidDataException("A tree with an unreadable final source was allowed to pass verification.");

        var changedDuringRead = new MapTreeGdbCatalogs(_ => throw new SourceFileChangedException(), _ =>
            throw new InvalidDataException("A changed catalog cannot be published as a successful source."));
        ExpectSourceChanged(() => changedDuringRead.Get(copied));

        string internalFile = Path.Combine(copied,
            first.Source.Files[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] before = File.ReadAllBytes(internalFile);
        try
        {
            File.WriteAllBytes(internalFile, [.. before, 0x01]);
            // The catalog is deliberately retained while constructing sibling
            // nodes; the final check must reject this changed directory.
            _ = shared.Get(copied);
            ExpectSourceChanged(shared.VerifySuccessfulSources);
        }
        finally { File.WriteAllBytes(internalFile, before); }
        GdbMapSourceService.VerifySource(first.Source);
        GdbMapSourceService.VerifySource(original);
        if (!File.ReadAllBytes(internalFile).SequenceEqual(before))
            throw new InvalidDataException("The temporary GDB mutation was not restored.");
        return new("passed", first.Source.SourcePath, first.Source.Files.Length, first.Subdatasets.Length,
            SharedCatalogCalls: sharedCatalogCalls, SharedSourceVerifications: sharedSourceVerifications,
            CanonicalAliasesShareCatalog: true, SeparateTreesUseFreshCatalogs: true,
            FailedCatalogIsNotRetriedPerChild: true, FinalVerificationFailurePropagates: finalFailureObserved,
            SourceChangedDuringCatalogPropagates: true,
            SourceChangedAfterCatalogRejected: true, SourceBytesRestored: true);
    }

    private static void ExpectSourceChanged(Action action)
    {
        try { action(); }
        catch (SourceFileChangedException) { return; }
        throw new InvalidDataException("A modified GDB source was not rejected with sourceChanged.");
    }
}
