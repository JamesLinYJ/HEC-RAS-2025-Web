# Server compute acceptance

Map analysis and topology editing checks use isolated copies of existing fixtures:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-map-analysis-http http://127.0.0.1:5080 /tmp/hecras-result-hover/result-hover-manifest.json /tmp/hecras-map-analysis
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-geometry-topology-map-http http://127.0.0.1:5080 /path/to/saved-project.ras /tmp/hecras-topology-map
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-geometry-arc-map-http http://127.0.0.1:5080 /tmp/hecras-topology-map/geometry-topology-map-http-e2e-manifest.json /tmp/hecras-arc-map
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-geometry-smart-connect-http http://127.0.0.1:5080 /tmp/hecras-arc-map/geometry-arc-map-http-e2e-manifest.json /tmp/hecras-smart-connect
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-geometry-polyline-trace-http http://127.0.0.1:5080 /tmp/hecras-topology-map/geometry-topology-map-http-e2e-manifest.json /tmp/hecras-polyline-trace
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-geometry-gdb-deep-connect-http http://127.0.0.1:5080 /tmp/hecras-topology-map/geometry-topology-map-http-e2e-manifest.json /tmp/hecras-gdb-deep-connect
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-geometry-mesh-generation-http http://127.0.0.1:5080 /tmp/hecras-mesh-generation
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-culvert-result-http http://127.0.0.1:5080 /tmp/hecras-culvert-result
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-map-line-hash-vectors > /tmp/hecras-map-line-hashes.json
node scripts/verify-map-analysis.mjs /tmp/hecras-map-line-hashes.json
```

The analysis runner reads actual Cell depth and Face velocity series from completed Result HDF files, imports a persisted source polyline and evaluates flow and cumulative-volume sample-line output. It checks exact map/Result identities and source-file preservation. Its fixture has no culverts, so culvert coverage is limited to the genuine empty-profile diagnostic and time-series rejection; the separate culvert runner creates that positive fixture and checks it through actual CPU computation and HTTP result reads. Mesh and cumulative-volume reads use private temporary postprocess caches that are removed when reading ends. The sample-line response retains a conservative cache-side-effect declaration; it does not prove that a shared cache was written.

The topology runner copies a saved project and prepares real conceptual topology in the copy when its original geometry has only a hydraulic mesh. It obtains a saved-map Geometry context, selects a stable source node through Host hit testing, moves it in the draft, undoes/redoes the move, saves through the parent model and verifies reopened coordinates and revisions. It also checks that an expired draft is rejected. The arc runner copies this topology fixture and exercises creation, filtering, smoothing, point insertion/movement/deletion, splitting and reversal, plus undo/redo, rejected malformed commands and parent-save/reopen. It checks that source arc attributes survive transforms. The culvert runner creates a barrel with stage boundaries, solves the actual CPU Plan, and compares profile and nonzero flow/velocity time series against reopened Result HDF data, preserving native times and source barrel ordinals. These runners require an already-running server and new output directories; they do not perform browser acceptance. The hash-vector command calls the actual Host coordinate-hash implementation; the Node check compares the browser helper with it and separately checks that NoData gaps retain their original times.

The SmartConnect runner uses the arc fixture's real Terrain and exact saved-map capture. It checks a sampled HighGround path, topology-arc connection anchors, undo/redo, rejected stale captures and parent-save/reopen. Split source portions must reconstruct the original path and retain its attributes; unrelated rows and source map features are preserved. Generated region-boundary outlines and topology completion diagnostics are recorded separately because new or overlapping arcs can change region completion. This edit/save acceptance does not establish mesh readiness. The continuous-trace runner checks three-waypoint SmartAdd and ordered DeepConnect anchors, including a persisted GeoJSON line source and folded-back anchors. Read-only previews replay the retained topology operations in temporary memory and return the final connection path after snapping and point pruning. Confirmation carries that path hash; a mismatched hash must reject the edit without changing the parent draft. Undo/redo and parent-save/reopen are checked separately. The map request retains a workspace project session even when the saved Geometry model has no optional core-layer override context. GDB Line/LineCollection sources use a separate full-directory manifest and the same line enumeration as the retained map renderer. Their renderer FID, original feature ordinal/ID, and part ordinal remain distinct. GDB Point and Polygon layers remain diagnostic-only because their retained renderers do not provide the line-source interface. These trace checks do not establish mesh readiness.

The mesh-generation runner starts from a valid Cartesian conceptual topology, generates a new mesh through the retained source algorithm, calculates property tables, and queues Prepare in the same child ledger. Child operations must leave source files unchanged; only the explicit parent model save publishes the Geometry HDF. It then reopens the Project and Geometry and checks mesh counts, property tables and the source prepare summary. This is a positive small-fixture pipeline check, separate from the intentionally overlapping trace fixture and from browser interaction. Source diagnostics are retained.

For headless Chrome or hidden Electron geometry interaction checks, set `HECRAS_VERIFY_SERVER_URL`, `HECRAS_VERIFY_MAP_FIXTURE` to the topology runner's manifest and `HECRAS_VERIFY_GEOMETRY_TOPOLOGY_ONLY=1`. Run `node scripts/verify-ui.mjs` or `node desktop/launch.mjs --server-smoke`, also setting `HECRAS_SERVER_URL` for Electron. The shared check exercises source-node selection/drag and multi-point arc completion without saving, correlates actual HTTP operations and verifies source bytes remain unchanged. Set `HECRAS_VERIFY_GEOMETRY_TRACE_ONLY=1` instead to also exercise continuous SmartAdd and topology-source DeepConnect with actual Host previews, ordered anchors, and one connection arc per confirmation. Persisted vector-source UI interaction requires its own source fixture. These are UI checks, separate from CPU solver acceptance.

This console project creates one small, real HEC-RAS source fixture for HTTP and browser tests. It uses a flat GeoTIFF terrain (elevation 42), one 2D flow-area cell, constant roughness, a wet initial boundary stage (43), and a short CPU Plan with its retained default equation set. The closed cell starts at 1 m depth; in SI units the 3.6 mm/hour precipitation rate contributes about 0.000004 m over four seconds. The HTTP run reopens the computed Result and requires every stored cell depth to be finite and within 0.0001 m of the 1 m envelope, allowing room for retained solver and HDF precision. It creates and reopens the source project before printing fixture paths as one JSON object.

Create a fixture without contacting the server:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --create-fixture /tmp/hecras-server-fixture
```

Run the full loopback acceptance against a started server:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-http http://127.0.0.1:5080 /tmp/hecras-server-e2e
```

Create a separate CPU cancellation fixture, without contacting the server:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --create-cancel-fixture /tmp/hecras-server-cancel-fixture
```

Run the cancellation acceptance against a started loopback server:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-cancel-http http://127.0.0.1:5080 /tmp/hecras-server-cancel-e2e
```

The cancellation fixture contains a 16 × 16 2D mesh, a 36,000-second Plan, a 0.1-second compute timestep, and hourly output. It remains a real source CPU Plan with the original HEC-RAS worker and telemetry path. The runner uploads it, loads and inspects it, then waits for `Plan Compute` telemetry reporting at least one completed solver step before sending HTTP DELETE. It requires the operation to reach a canceled terminal state, rejects a canceled payload marked successful, and completes a later `backend.info` request to verify the server worker remains usable. The command only contacts an already-started loopback server; it does not launch or publish one.

The HTTP run uploads the fixture as a zip workspace, loads and inspects the uploaded Plan, executes the real CPU solver, verifies source progress and messages, checks the retained Result file revision, then reopens the Result through the server's inspector and profile operations and requires finite wet cell-depth values. It also requires a missing-project operation to return its terminal error promptly and a subsequent backend request to succeed, covering duplex worker failure recovery. Each output directory must be new; the project never overwrites or cleans a caller-owned directory.

Verify saved-map discovery and source revision guards against a started loopback server:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-map-http http://127.0.0.1:5080 /tmp/hecras-map-e2e
```

This check uploads a new real project, confirms listing without a `.vis` file does not create one, creates two Chinese-named maps with consecutive visualization revisions, discovers their exact order and active identity, and loads both source trees. Listing and loading must preserve the `.vis` bytes. A stale project list request and a stale visualization mutation must both return `sourceChanged` without modifying the file, and a later backend request must still complete. This is a server contract check; reopening the maps through React requires the separate browser UI check.

Verify transactional Geometry, Boundary Condition and Plan creation using a completed Result-hover fixture manifest:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-model-creation-http http://127.0.0.1:5080 /tmp/hecras-result-hover/result-hover-manifest.json /tmp/hecras-model-creation
```

The runner copies the referenced project to a new output directory before calling `modelCreation.inspect/commit`. It verifies the created HDF files, exact Geometry/Plan association rows, `project.open` and project-tree readback, unchanged existing HDF bytes, and rejection of an outdated project revision without publishing another model. Boundary Conditions are discovered from their standard directory by the retained C# loader; the format has no Boundary Conditions XML section. A successful creation can therefore keep the project SHA unchanged. The legacy `modelCreation.create` operation remains HDF-only; the React creation dialogs use the project-commit operation.

Verify real external Project connection add, update and remove transactions using the completed Result-hover fixture:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-project-connections-http http://127.0.0.1:5080 /tmp/hecras-result-hover/result-hover-manifest.json /tmp/hecras-project-connections
```

This check copies the completed fixture into a new output directory, then exercises ordinary File (`.txt`) add, update and remove through `project.connection.inspect/commit`. It verifies canonical path and source SHA readback, rejects a changed target source and a stale Project revision without modifying the Project, and confirms each commit through `project.open` and `catalog.tree.load`. Original and copied HDF files are checked byte-for-byte; only files and XML in the new output copy are modified. The command requires an already-running loopback server and does not launch one.

Verify saved Plan and Geometry association edits against the same completed fixture:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-project-associations-http http://127.0.0.1:5080 /tmp/hecras-result-hover/result-hover-manifest.json /tmp/hecras-project-associations
```

This check copies the fixture into a new output directory, rejects a saved Result as a writable Plan, inspects the exact saved Plan and Geometry HDFs, writes and reopens the standard `PlanAssociations` row, clears both optional Geometry dependencies and restores their exact original Terrain/N-Value selections. It verifies `project.open` and `project.tree.load` readback, rejects stale source-reference and Project revisions without touching HDFs, and compares the original and copied HDF SHA-256 values before reporting success. The command uses only an already-running loopback server; it does not start a service or alter the source fixture.

Create a separate fixture for the React meteorological time-series editor:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --create-editor-fixture /tmp/hecras-editor-fixture
```

It contains one actual Point precipitation gauge named `中文雨量站` with three irregular absolute-time source-table values (0.5, 1.25, 0.75). Gauge rows are authoritative; their chart samples are projections and must not be sent as independent edits. This fixture is for editing and HDF save/reopen checks, rather than the constant-precipitation numerical solve envelope above. Select its `.ras` path using `HECRAS_VERIFY_EDITOR_PROJECT` with `npm run test:ui` and a running loopback server.

Verify real saved vector and raster sources:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-map-source-http http://127.0.0.1:5080 /tmp/hecras-map-source-fixture
```

This creates a 4 × 4 hydraulic fixture and real GeoJSON, OGR-written Shapefile and GDAL-written GeoTIFF sources, uploads the workspace, imports each source into a saved map, and reads the source geometry and pixels back through HTTP. It checks feature IDs and attributes, MultiPoint positions, polygon holes and MultiPolygon ring groups, raster size and extent, source and visualization SHA values, read-only `.vis` byte stability, and stale project, visualization and source rejection. This is separate from drawing and interacting with those files in React.

For the browser or hidden Electron map UI check, first create a fresh fixture and retain its JSON manifest outside the product source:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --create-map-fixture /tmp/hecras-map-ui-fixture > /tmp/hecras-map-ui-fixture.json
```

Set `HECRAS_VERIFY_MAP_FIXTURE` to that manifest's absolute path. The UI check creates Geometry and Plan copies, imports actual browser files, checks the vector/raster canvas and close/reopen persistence, computes the original 4 × 4 CPU Plan, then reads native Result times and pixels through the map controls. It modifies the disposable fixture. Use separate fresh directories for browser and Electron runs.

For a real nonzero velocity field, create the same mesh with constant wind forcing:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --create-velocity-map-fixture /tmp/hecras-velocity-ui > /tmp/hecras-velocity-ui.json
```

This fixture retains the four-second CPU Plan and five output times. It is intended for velocity rendering checks, rather than the uniform-depth numerical acceptance envelope used by the rainfall-only fixture.

Generate a small actual OpenFileGDB with a Chinese-field point layer and a multipolygon containing one interior ring, or run its complete upload/source contract acceptance:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --create-gdb-vector-fixture /tmp/hecras-vector-fixture.gdb
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-gdb-source-http http://127.0.0.1:5080 /tmp/hecras-gdb-http
```

The HTTP check verifies the uploaded directory manifest and file bytes, real catalog/import/source reads, Chinese attributes, the multipolygon interior ring, `.vis` revisions and read-only stability, stale source/project rejection, and invalid relative paths. Output paths must be new. Use the first command's directory with `HECRAS_VERIFY_GDB_SOURCE` and a velocity fixture manifest for the separate browser/Electron interaction check.

Verify the tree-scoped raster directory cache with an actual GDAL reference FileGDB:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-gdb-tree-cache /path/to/gdal_test_data.gdb /tmp/hecras-gdb-cache-check
```

This copies the source into the new output directory, catalogs its real raster subdatasets, and checks shared capture for canonical path aliases and fresh capture in a separate tree scope. Controlled read failures verify failure reuse and `sourceChanged` propagation; an actual internal-file mutation after capture must fail final manifest verification, then its bytes are restored. The caller's original directory is only read and verified. These cache contracts are separate from real HTTP tree-snapshot comparisons and browser/Electron interaction checks.

Run real GDAL typed-cell and renderer checks for the supported classification/image formats:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-raster-formats /tmp/hecras-raster-formats-check
```

The output must be new and outside the repository. This creates actual Byte/Int16/UInt16/Int32/UInt32 GeoTIFFs and VRTs, checks boundary sample values and explicit/nonexistent NoData, renders known category colors, RGB and RGBA in row order, and rejects unsupported band layouts and invalid integer NoData. UInt32 checks preserve exact category keys and hover values through UInt32.MaxValue, including a high NoData value that must not hide other categories. RGBA checks cover source alpha, layer opacity, a nonblack premultiplied recolorer, declared RGB/alpha NoData and valid black pixels. These numeric/renderer checks are separate from actual GDB upload, saved MapType, PNG decoding and close/reopen checks shared by the browser and Electron runners.

Generate two real CPU Results whose five-profile timelines overlap at different native indices:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-result-hover-http http://127.0.0.1:5080 /tmp/hecras-result-hover
node scripts/verify-map-result-hover-http.mjs http://127.0.0.1:5080 /tmp/hecras-result-hover/result-hover-manifest.json /tmp/hecras-result-hover/http-report.json
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- --run-result-raster-budget
```

The fixture solves a 4 × 4 Plan twice, with the second draft starting one second later, then reads the stored HDF cell profiles and records their float32 map-depth baseline. Its own `.vis` includes a supported serialized standalone Result row alongside the Plan's MultiResult; the product's normal AddResult grouping rules are unchanged. The separate HTTP check renders two real default-size frames exceeding the former 8 MiB protocol limit, samples standalone A4 and MultiResult A1/B0 together, rejects stale identities, preserves outside-mesh NoData and verifies read-only source bytes remain unchanged. It then hides, saves, reloads and shows each exact Result and MultiResult node, checking retained membership, visibility and every visualization-revision receipt while the project and HDF bytes remain unchanged. The raster-budget check covers 160 viewport/layer combinations across 1–32 sources plus invalid inputs, verifying aggregate pixel limits, world bounds, aspect ratio and unchanged full-resolution frames whenever they fit. To resume fixture preparation from an existing two-result `map-fixture.json`, use `--complete-result-hover-fixture <loopback-url> <existing-map-fixture.json> <new-output-directory>`.

For the shared browser/Electron interaction check, set `HECRAS_VERIFY_SERVER_URL`, `HECRAS_VERIFY_MAP_FIXTURE` to `map-fixture.json`, `HECRAS_VERIFY_RESULT_HOVER_FIXTURE` to `result-hover-manifest.json`, and `HECRAS_VERIFY_RESULT_HOVER_ONLY=1`. Run `node scripts/verify-ui.mjs` for headless Chrome or `node desktop/launch.mjs --server-smoke` for hidden Electron, also setting `HECRAS_SERVER_URL` for Electron. Both operate the real comparison controls and pointer handler, correlate completed HTTP operations, and compare their values with the HDF baseline.

Run `--run-association-raster-budget` to check aggregate association pixel limits across layer counts and viewport sizes.

Verify the source-equivalent Cartesian combined cell-size edit through the actual geometry mesh HTTP operations:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- \
  --run-geometry-mesh-patch-http http://127.0.0.1:5080 \
  /tmp/hecras-geometry-mesh-patch
```

This runner creates a new real Project/Geometry fixture in the supplied output directory, adds a Cartesian topology only to that generated Geometry HDF, and checks the single `setCartesianCombinedCellSize` mesh-draft mutation. It requires both primary and secondary region sizes to change together, round-trips the before/after draft states with the same selected FID, rejects zero/negative/non-finite inputs and stale source revisions, verifies that the source HDF stays unchanged until `model.save`, then reopens the HDF and checks both values plus the source `AutoPopulateMeshValues` behavior for its four outer arcs. The test requires an already-running loopback C# server. It does not start a service or touch a caller-owned fixture.

Verify that a computed Plan's Terrain association is rendered and sampled through the actual C# HTTP operations:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- \
  --run-terrain-association-http http://127.0.0.1:5080 \
  /tmp/hecras-result-hover/map-fixture.json \
  /tmp/hecras-result-hover/result-hover-manifest.json \
  /tmp/hecras-terrain
node scripts/verify-map-terrain-http.mjs http://127.0.0.1:5080 \
  /tmp/hecras-terrain/terrain-association-manifest.json \
  /tmp/hecras-terrain/http-report.json
```

The command copies the complete computed fixture into its new output directory before it changes any map visibility; the input project, `.vis`, Terrain raster, Geometry, Plan and Result HDF files are verified byte-for-byte unchanged. It records the exact Terrain, Geometry and Plan locators plus the selected Result owner and source revisions. `map.associations.load` must return visible RGBA for both the static Plan Terrain and the selected-Result Terrain; a 1,448 × 1,448 one-layer frame exercises an actual response larger than 8 MiB while staying inside the renderer's per-layer pixel bound. `map.hover.identify` samples a known world coordinate and an outside-raster NoData coordinate. The known elevation comes from direct C# sampling of the source Terrain, while the manifest separately carries the original C# direct-HDF `Cell Depth` baseline at the same mesh cell. The Node runner also verifies stale source/viewport rejection, reloads hidden and shown rows, checks `.vis` revisions against every save receipt, and leaves the copied Terrain visible for browser/Electron interaction checks.

The Node runner also calls `map.measureTerrainElevations` with the captured Terrain dependencies, requires the known source elevation and an explicit `null` for the outside-raster NoData point, and rejects a stale HDF SHA. The request's `expectedTerrainRevision` is the source HDF SHA; the response's `terrainRevision` fingerprints the Terrain file dependencies together and is a different identity. The response dependencies must match the captured source files exactly.

The output directory is self-contained: `map-fixture.json` points to its copied project, `result-hover-manifest.json` contains rebased HDF paths, and `terrain-association-manifest.json` supplies `mapRequest`, `contextToken`, exact node locators, owners, revisions, viewport, known sample points, direct-source values, HDF reference values and frame hash. Feed its `map-fixture.json` to the shared UI runner and use the Terrain locator/value in `terrain-association-manifest.json` to check canvas drawing and pointer interaction in both Chrome and Electron. Operation reports and runtime logs belong outside the product folder.

Verify source polygon-Terrain map rendering and editing through the actual HTTP host:

```sh
dotnet run --project backend/ServerChecks/Ras.Backend.ServerChecks.csproj -- \
  --run-terrain-polygon-map-http http://127.0.0.1:5080 \
  /tmp/hecras-terrain-polygon-map
```

The runner creates a fresh project fixture with two existing polygon modifications, then exercises `map.terrainInput.render` and `model.terrainTopology` against the exact Terrain input capture. It checks matching map/topology feature IDs, Triangle and Rectangle stamps, a custom drawn polygon, map-space selection, removal and parent-source undo/redo, stale source and visualization rejection, and unchanged Terrain/input-raster bytes until `model.save`. Finally it reopens the Project and Terrain and compares the persisted polygon IDs through both render and topology readers. The output includes a map fixture and `terrain-polygon-map-manifest.json` with the exact input node and model IDs, source revisions, map request, edit sequence and before/after HDF SHA values; it is suitable for the React canvas handoff. This command requires an already-running loopback C# server and modifies only its newly created fixture.

### External source session context

`--run-session-references-http <loopback-base-url> <result-hover-manifest.json> <new-output-directory>` creates a disposable empty project and attaches copied Plan, Result, Terrain, Geometry and Boundary Condition HDF sources. It verifies Host-issued context composition, saved-map/catalog readback, rejected source/session conflicts and an external Terrain save with a rebased capability. The output directory must be outside the repository and must not exist; the original fixture remains unchanged. This is real HTTP/file-format acceptance, separate from UI and live-provider sign-in verification.

### Classification and Manning editor round trip

`--run-classification-manning-http <loopback-base-url> <new-output-directory>` creates an isolated project with real classification and Manning HDF files and a small classification raster. It exercises fixed-identity raster mapping edits, rejection of inserted/deleted/key-mutated mapping rows, polygon undo/redo, NLCD interpretation defaults and N-Value preview, then saves and reopens both surface layers. The directory must be new and outside the repository; this runner contacts an existing loopback service and does not launch one. These HTTP/HDF checks are separate from browser interaction.
