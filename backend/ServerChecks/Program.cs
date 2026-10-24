using System.Text.Json;

namespace Ras.Backend.ServerChecks;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--run-geometry-smart-connect-http", string smartConnectBaseUrl, string smartConnectManifest, string smartConnectOutput])
            {
                TextWriter protocol = Console.Out;
                GeometrySmartConnectAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunGeometrySmartConnectAsync(
                    new Uri(EnsureTrailingSlash(smartConnectBaseUrl)), smartConnectManifest, smartConnectOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-culvert-result-http", string culvertBaseUrl, string culvertOutput])
            {
                TextWriter protocol = Console.Out;
                CulvertResultAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunCulvertResultAsync(
                    new Uri(EnsureTrailingSlash(culvertBaseUrl)), culvertOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-geometry-arc-map-http", string arcBaseUrl, string arcManifest, string arcOutput])
            {
                TextWriter protocol = Console.Out;
                GeometryArcMapAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunGeometryArcMapAsync(
                    new Uri(EnsureTrailingSlash(arcBaseUrl)), arcManifest, arcOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-map-line-hash-vectors"])
            {
                Console.WriteLine(JsonSerializer.Serialize(ServerAcceptance.CoordinateHashVectors(), JsonOptions));
                return 0;
            }
            if (args is ["--run-map-analysis-http", string analysisBaseUrl, string analysisManifest, string analysisOutput])
            {
                TextWriter protocol = Console.Out;
                MapAnalysisAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunMapAnalysisAsync(
                    new Uri(EnsureTrailingSlash(analysisBaseUrl)), analysisManifest, analysisOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-geometry-topology-map-http", string topologyMapBaseUrl, string topologyMapProject, string topologyMapOutput])
            {
                TextWriter protocol = Console.Out;
                GeometryTopologyMapAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunGeometryTopologyMapAsync(
                    new Uri(EnsureTrailingSlash(topologyMapBaseUrl)), topologyMapProject, topologyMapOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-classification-manning-http", string classificationBaseUrl, string classificationOutput])
            {
                TextWriter protocol = Console.Out;
                ClassificationManningAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunClassificationManningAcceptanceAsync(
                    new Uri(EnsureTrailingSlash(classificationBaseUrl)), classificationOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-session-references-http", string referenceBaseUrl, string referenceManifest, string referenceOutput])
            {
                TextWriter protocol = Console.Out;
                SessionReferenceAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await ServerAcceptance.RunSessionReferenceAcceptanceAsync(
                    new Uri(EnsureTrailingSlash(referenceBaseUrl)), referenceManifest, referenceOutput); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-terrain-polygon-map-http", string polygonBaseUrl, string polygonOutputDirectory])
            {
                TextWriter protocol = Console.Out;
                TerrainPolygonMapAcceptanceReport report;
                Console.SetOut(Console.Error);
                try
                {
                    report = await ServerAcceptance.RunTerrainPolygonMapAsync(
                        new Uri(EnsureTrailingSlash(polygonBaseUrl)), polygonOutputDirectory);
                }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-geometry-mesh-patch-http", string patchBaseUrl, string patchOutputDirectory])
            {
                TextWriter protocol = Console.Out;
                GeometryMeshPatchAcceptanceReport report;
                Console.SetOut(Console.Error);
                try
                {
                    report = await ServerAcceptance.RunGeometryMeshPatchAsync(
                        new Uri(EnsureTrailingSlash(patchBaseUrl)), patchOutputDirectory);
                }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-result-raster-budget"])
            {
                Console.WriteLine(JsonSerializer.Serialize(ResultRasterBudgetAcceptance.Run(), JsonOptions));
                return 0;
            }
            if (args is ["--run-raster-formats", string rasterOutputDirectory])
            {
                TextWriter protocol = Console.Out;
                RasterFormatAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = RasterFormatAcceptance.Run(rasterOutputDirectory); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--run-gdb-tree-cache", string cacheSourceDirectory, string cacheOutputDirectory])
            {
                TextWriter protocol = Console.Out;
                GdbTreeCacheAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = GdbTreeCacheAcceptance.Run(cacheSourceDirectory, cacheOutputDirectory); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args is ["--create-fixture", string fixtureDirectory])
            {
                FixtureManifest fixture = CreateFixtureQuietly(fixtureDirectory);
                Console.WriteLine(JsonSerializer.Serialize(fixture, JsonOptions));
                return 0;
            }

            if (args is ["--create-editor-fixture", string editorFixtureDirectory])
            {
                TextWriter protocol = Console.Out;
                EditorFixtureManifest fixture;
                Console.SetOut(Console.Error);
                try { fixture = PlanComputeFixture.CreateEditor(editorFixtureDirectory); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(fixture, JsonOptions));
                return 0;
            }

            if (args is ["--create-cancel-fixture", string cancelFixtureDirectory])
            {
                FixtureManifest fixture = CreateCancellationFixtureQuietly(cancelFixtureDirectory);
                Console.WriteLine(JsonSerializer.Serialize(fixture, JsonOptions));
                return 0;
            }

            if (args is ["--create-map-fixture", string mapRenderFixtureDirectory])
            {
                TextWriter protocol = Console.Out;
                FixtureManifest fixture;
                Console.SetOut(Console.Error);
                try { fixture = PlanComputeFixture.CreateMap(mapRenderFixtureDirectory); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(fixture, JsonOptions));
                return 0;
            }

            if (args is ["--create-velocity-map-fixture", string velocityFixtureDirectory])
            {
                TextWriter protocol = Console.Out;
                FixtureManifest fixture;
                Console.SetOut(Console.Error);
                try { fixture = PlanComputeFixture.CreateVelocityMap(velocityFixtureDirectory); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(fixture, JsonOptions));
                return 0;
            }

            if (args is ["--run-http", string baseUrl, string httpFixtureDirectory])
            {
                FixtureManifest fixture = CreateFixtureQuietly(httpFixtureDirectory);
                AcceptanceReport report = await ServerAcceptance.RunAsync(new Uri(EnsureTrailingSlash(baseUrl)), fixture);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-cancel-http", string cancelBaseUrl, string cancelHttpFixtureDirectory])
            {
                FixtureManifest fixture = CreateCancellationFixtureQuietly(cancelHttpFixtureDirectory);
                CancellationAcceptanceReport report = await ServerAcceptance.RunCancellationAsync(
                    new Uri(EnsureTrailingSlash(cancelBaseUrl)), fixture);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-map-http", string mapBaseUrl, string mapFixtureDirectory])
            {
                FixtureManifest fixture = CreateFixtureQuietly(mapFixtureDirectory);
                MapDocumentAcceptanceReport report = await ServerAcceptance.RunMapDocumentsAsync(
                    new Uri(EnsureTrailingSlash(mapBaseUrl)), fixture);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-map-source-http", string mapSourceBaseUrl, string mapSourceFixtureDirectory])
            {
                TextWriter protocol = Console.Out;
                FixtureManifest fixture;
                Console.SetOut(Console.Error);
                try { fixture = PlanComputeFixture.CreateMap(mapSourceFixtureDirectory); }
                finally { Console.SetOut(protocol); }
                MapSourceAcceptanceReport report = await MapSourceAcceptance.RunAsync(
                    new Uri(EnsureTrailingSlash(mapSourceBaseUrl)), fixture);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-result-hover-http", string resultHoverBaseUrl, string resultHoverOutputDirectory])
            {
                ResultHoverAcceptanceReport report = await ServerAcceptance.RunResultHoverAsync(
                    new Uri(EnsureTrailingSlash(resultHoverBaseUrl)), resultHoverOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--complete-result-hover-fixture", string completeResultHoverBaseUrl,
                    string existingMapFixtureManifest, string completeResultHoverOutputDirectory])
            {
                ResultHoverAcceptanceReport report = await ServerAcceptance.CompleteResultHoverFixtureAsync(
                    new Uri(EnsureTrailingSlash(completeResultHoverBaseUrl)), existingMapFixtureManifest,
                    completeResultHoverOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-association-raster-budget"])
            {
                Console.WriteLine(JsonSerializer.Serialize(AssociationRasterBudgetAcceptance.Run(), JsonOptions));
                return 0;
            }
            if (args is ["--run-terrain-association-http", string terrainBaseUrl,
                    string terrainMapFixtureManifest, string terrainResultHoverManifest,
                    string terrainOutputDirectory])
            {
                TerrainAssociationAcceptanceReport report = await ServerAcceptance.RunTerrainAssociationAsync(
                    new Uri(EnsureTrailingSlash(terrainBaseUrl)), terrainMapFixtureManifest,
                    terrainResultHoverManifest, terrainOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-model-creation-http", string modelCreationBaseUrl,
                    string modelCreationResultHoverManifest, string modelCreationOutputDirectory])
            {
                ModelCreationAcceptanceReport report = await ServerAcceptance.RunModelCreationAsync(
                    new Uri(EnsureTrailingSlash(modelCreationBaseUrl)), modelCreationResultHoverManifest,
                    modelCreationOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-project-connections-http", string connectionBaseUrl,
                    string connectionResultHoverManifest, string connectionOutputDirectory])
            {
                ProjectConnectionAcceptanceReport report = await ServerAcceptance.RunProjectConnectionsAsync(
                    new Uri(EnsureTrailingSlash(connectionBaseUrl)), connectionResultHoverManifest,
                    connectionOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-project-associations-http", string associationBaseUrl,
                    string associationResultHoverManifest, string associationOutputDirectory])
            {
                ProjectAssociationAcceptanceReport report = await ServerAcceptance.RunProjectAssociationsAsync(
                    new Uri(EnsureTrailingSlash(associationBaseUrl)), associationResultHoverManifest,
                    associationOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--run-map-views-http", string viewsBaseUrl,
                    string viewsResultHoverManifest, string viewsOutputDirectory])
            {
                MapViewsAcceptanceReport report = await ServerAcceptance.RunMapViewsAsync(
                    new Uri(EnsureTrailingSlash(viewsBaseUrl)), viewsResultHoverManifest,
                    viewsOutputDirectory);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            if (args is ["--create-gdb-vector-fixture", string gdbVectorPath])
            {
                TextWriter protocol = Console.Out;
                string source;
                Console.SetOut(Console.Error);
                try { source = GdbSourceAcceptance.CreateVectorSource(gdbVectorPath); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(new { sourcePath = source }, JsonOptions));
                return 0;
            }

            if (args is ["--run-gdb-source-http", string gdbBaseUrl, string gdbFixtureDirectory])
            {
                TextWriter protocol = Console.Out;
                GdbSourceAcceptanceReport report;
                Console.SetOut(Console.Error);
                try { report = await GdbSourceAcceptance.RunAsync(new Uri(EnsureTrailingSlash(gdbBaseUrl)), gdbFixtureDirectory); }
                finally { Console.SetOut(protocol); }
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }

            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-geometry-smart-connect-http <loopback-base-url> <arc-map-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-culvert-result-http <loopback-base-url> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-geometry-arc-map-http <loopback-base-url> <topology-map-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-map-line-hash-vectors");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-map-analysis-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-geometry-topology-map-http <loopback-base-url> <saved-project.ras> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-raster-formats <new-output-directory-outside-repository>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-gdb-tree-cache <actual-raster-gdb-directory> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --create-fixture <new-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --create-editor-fixture <new-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-session-references-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-http <loopback-base-url> <new-fixture-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --create-cancel-fixture <new-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --create-map-fixture <new-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --create-velocity-map-fixture <new-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-cancel-http <loopback-base-url> <new-fixture-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-map-http <loopback-base-url> <new-fixture-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-map-source-http <loopback-base-url> <new-fixture-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-result-hover-http <loopback-base-url> <new-output-directory-outside-repository>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --complete-result-hover-fixture <loopback-base-url> <existing-map-fixture.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-terrain-association-http <loopback-base-url> <map-fixture.json> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-model-creation-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-project-connections-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-project-associations-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-map-views-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --create-gdb-vector-fixture <new-absolute-gdb-directory>");
            Console.Error.WriteLine("  Ras.Backend.ServerChecks --run-gdb-source-http <loopback-base-url> <new-fixture-directory>");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static FixtureManifest CreateFixtureQuietly(string directory)
    {
        TextWriter protocol = Console.Out;
        Console.SetOut(Console.Error);
        try { return PlanComputeFixture.Create(directory); }
        finally { Console.SetOut(protocol); }
    }

    private static FixtureManifest CreateCancellationFixtureQuietly(string directory)
    {
        TextWriter protocol = Console.Out;
        Console.SetOut(Console.Error);
        try { return PlanComputeFixture.CreateCancellation(directory); }
        finally { Console.SetOut(protocol); }
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
}
