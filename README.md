# HEC-RAS 2025

React and TypeScript provide the shared browser and Electron interface. ASP.NET Core hosts the built web UI and exposes the C# backend over same-origin HTTP. The default web and desktop workflow uses this server architecture; WebAssembly remains optional and is not required to build or start the app.

界面默认使用简体中文，菜单、模型表单、地图、计算和结果页面共用中文资源。工程名称、文件路径和用户数据保持原值。

地图的“导入工程图层”可保存 GeoJSON、完整 Shapefile 和栅格文件。“导入 GDB 目录”保留整个文件地理数据库的目录结构，使用 C# 的完整文件清单和 SHA 校验来源，再保存对应矢量与可渲染子栅格到 `.vis`。栅格读取保留单波段浮点与整数类别，以及 RGB/RGBA 影像的通道和空值语义。“结果速度箭头”读取所选计算结果与原生输出时刻的真实速度，默认关闭；箭头按屏幕间距抽样显示，不改变求解数据。

界面仍在逐文件迁移。组件已挂载、编译通过、真实数据往返和浏览器/Electron 交互验收分别核对，不能把界面文件数量当作完整功能覆盖率。构建目录、依赖缓存、临时工程和验收记录不进入仓库。

## Build and run on macOS

Install Node.js 24 or newer and the .NET 10 SDK selected by `global.json`, then install the locked JavaScript dependencies:

```sh
npm ci
npm run build:web
npm run build:server
```

The server publish uses the checked-in packages in `backend/Packages` as an additional NuGet source and writes to the ignored `artifacts/server` directory. Set `HECRAS_DOTNET` to use a particular `dotnet` executable; that SDK may also need its matching `DOTNET_ROOT` and `DOTNET_CLI_HOME` environment. `HECRAS_NUGET_SOURCE` adds another local path or HTTP(S) feed without URL credentials. Use `HECRAS_NO_RESTORE=1` only when the projects already have a successful restore and an offline publish is needed.

Serve the built UI and API together in a browser:

```sh
npm run serve:web
```

For Vite hot reload, keep the server on port 5080 (the Vite proxy default) in another terminal, then start Vite:

```sh
HECRAS_SERVER_PORT=5080 HECRAS_DEV_ORIGINS=http://127.0.0.1:5173 npm run serve:web
npm run dev
```

Start the desktop app:

```sh
npm run desktop
```

Electron launches its own C# server process on a loopback port and closes only that process when the app exits. `HECRAS_SERVER_PORT` selects a fixed local port; unset or `0` requests a dynamic one. To connect to a server already running, set `HECRAS_SERVER_URL` to its HTTP(S) origin. In that mode the launcher only checks readiness and connects; it does not start or stop the external server. The URL must be an origin without a path or embedded credentials. For Vite development, set `HECRAS_SERVER_URL` to the origin printed by `npm run serve:web`; this also lets Electron and Vite share a server without the dynamic-port mismatch. `HECRAS_SERVER_DLL` can select a published server DLL, and `HECRAS_WEB_ROOT` can select the built UI directory for an owned local server.

`npm run smoke:electron` builds the web app, publishes the server, and runs Electron in a hidden window. It checks real `/api/health` and `/api/runtime` responses, same-origin browser requests, feature navigation, and Chinese/English switching. Set `HECRAS_VERIFY_PROJECT` to a disposable fixture's absolute `.ras` path to also open its Plan, run the real CPU compute, and read the written HDF depth chart through the desktop UI. Without that variable the smoke does not run a hydraulic operation. `npm run test:ui` is a separate headless Chrome UI and file-import check.

For Electron cancellation, set `HECRAS_VERIFY_CANCEL_PROJECT` to a separate fixture created with ServerChecks `--create-cancel-fixture`. The hidden desktop check waits for a real positive solver step, cancels, requires the matching canceled result and a fresh runnable inspection, then continues the normal `HECRAS_VERIFY_PROJECT` solve if provided. `HECRAS_VERIFY_CANCEL_PLAN_NAME` defaults to `Plan CPU`. Cancellation and completed HDF chart checks are reported separately.

For the real project-file HTTP solve and HDF readback, see [ServerChecks](backend/ServerChecks/README.md). The browser check can use the same server with `HECRAS_VERIFY_SERVER_URL=http://127.0.0.1:5080`; set `HECRAS_VERIFY_PROJECT` to a disposable fixture's absolute `.ras` path to exercise editing and saving its Plan, closing/reopening it, running compute, and viewing the written HDF depth chart through the UI. `HECRAS_VERIFY_PLAN_NAME` defaults to `Plan CPU`. These checks modify the test Plan description and create actual result files in the selected fixture.

Set `HECRAS_VERIFY_CANCEL_PROJECT` to a separate long-running fixture created with ServerChecks `--create-cancel-fixture` to also test real CPU cancellation twice. That browser check injects one temporary poll failure and one temporary DELETE failure, verifies recovery against the same operation, and requires a fresh source inspection before running again.

`npm run test:gateway` checks the production HTTP gateway with controlled transport responses: temporary poll and cancellation failures, paged terminal events, completion during cancellation backoff, unknown operation status, and resuming an accepted task without submitting its write request again. This protocol regression is separate from the real C# and UI solve checks.

`npm run test:plan-analysis-drafts`, `npm run test:plan-diff`, `npm run test:r2r-matrix` and `npm run test:mesh-export` check the corresponding source captures, draft ownership and response contracts. The real matrix export and input-isolation check is documented in [ServerChecks](backend/ServerChecks/README.md#ready-to-run-matrix-source-isolation).

`npm run test:stable-channel`, `npm run test:project-projection-export` and `npm run test:project-archive` cover the hydraulic-design form, projection export and prepared-ZIP workflows. Their real C# solve, HDF save/readback, ESRI projection and ZIP download checks are documented in [ServerChecks](backend/ServerChecks/README.md#stable-channel-design). Archive downloads stream an already verified ZIP; temporary archives remain available until explicitly cleaned up.

`npm run test:channel-v6-import` and `npm run test:channel-v6-parent` check Version 6 channel import tracking and parent-draft adoption. Importing prepares a terrain draft; the terrain changes on Save. [ServerChecks](backend/ServerChecks/README.md#version-6-channel-geometry-import) covers real HDF conversion, parent draft undo/redo, save and independent readback.

Background maps use the C# server's existing OpenStreetMap, satellite and shaded-relief providers. Open a saved map in a projected project and enable the sources in its background settings. Styles, density, labels and opacity apply to the current view; they do not save project files. The style picker also inspects and explicitly clears the server's tile cache. `npm run test:background-map-rendering` checks the render protocol, coordinates and request ownership; live provider and browser rendering checks are separate.

The map's **在线数据** entry queries NLD, NSI, NFHL and OpenStreetMap through the retained C# providers. It previews real geometry and attributes in project coordinates and shows any preview limits. Export writes the complete result to an existing directory on the server; **加入地图** separately commits the exported sources to the captured map and reads them back. To add the export to the map, use a directory inside the project or register the source as an external connection. A disconnected write retains its original task for recovery. `npm run test:osm-query-builder`, `npm run test:feature-query-workflow` and `npm run test:feature-query-map-import` cover the builder and client ownership/recovery contracts; live-provider checks are separate.

`HECRAS_VERIFY_WORKSPACE_ZIP` enables the real browser workspace check against `HECRAS_VERIFY_SERVER_URL`. Use an absolute path to a disposable ZIP containing one ServerChecks fixture project and no saved maps. The check uploads and opens it, creates two maps, closes/reopens the project, and downloads the updated workspace. It verifies that archive entries match the actual saved `.ras` and `.vis` bytes. This check uses the `unzip` command to read and validate the downloaded archive.

`HECRAS_VERIFY_EDITOR_PROJECT` selects a disposable project created with ServerChecks `--create-editor-fixture`. The browser check appends and removes a real irregular rain-gauge row, validates a new row's numeric input, and checks both changes after HDF save/reopen. It also confirms draft transforms leave the source bytes and revision unchanged until Save. `HECRAS_VERIFY_EDITOR_BOUNDARY` may override the fixture's default `Boundary Conditions/Solver Boundary.h5` path. These checks modify their fixture files.

`HECRAS_VERIFY_MAP_FIXTURE` selects the JSON manifest emitted by ServerChecks `--create-map-fixture`. With the real server, `npm run test:ui` also checks Geometry/Plan duplication, actual map-source uploads, saved vector and raster rendering, close/reopen persistence, 4 × 4 CPU computation, and native Result time selection and playback. `npm run smoke:electron` accepts a separate fresh manifest to run the same map check in its hidden sandboxed window. `HECRAS_VERIFY_OUTPUT` selects the evidence directory; use a disposable location outside the product source. Fixture creation and the separate source-format HTTP check are described in [ServerChecks](backend/ServerChecks/README.md).

After a complete map run, `HECRAS_VERIFY_MAP_VIEW_ONLY=1` reopens that saved fixture to check Result visibility, the native timeline within the desktop viewport, and independently scrolling expanded layer trees. It verifies `.vis` byte stability and does not repeat model copying, map imports or computation. These layout/readback checks are reported separately from the complete calculation flow.

For GDB and velocity acceptance, use `--create-velocity-map-fixture` instead of `--create-map-fixture` to add real constant wind forcing, then set `HECRAS_VERIFY_GDB_SOURCE` to a small actual `.gdb` directory. The shared browser/Electron check uploads that directory, reads Chinese fields and polygon holes, and verifies nonzero velocity direction and Result time/revision through the checkbox. `HECRAS_VERIFY_GDB_RASTER_SOURCE` selects the GDAL `gdal_test_data.gdb` reference dataset for a separate map with ten supported floating-point raster subdatasets and close/reopen pixel checks. `HECRAS_VERIFY_MAP_RASTER_ONLY=1` runs that raster round trip on an existing fixture without repeating model duplication or CPU computation.

The initial runtime workflow is being verified on macOS. Linux and Windows runtime packaging and execution have not yet been accepted.

## Optional WebAssembly path

The earlier WebAssembly sources and checks remain available separately:

```sh
npm run build:wasm
npm run test:wasm
npm run smoke:electron:wasm
```

These commands require the matching .NET WebAssembly workload. They are not part of the default web or desktop build.

## Code organization

- `frontend/src/interface` and `frontend/src/features`: shared React application and feature pages.
- `frontend/src/platform`: browser and server gateway adapters.
- `desktop`: Electron startup, restricted preload, and server process lifecycle.
- `backend/Server`: ASP.NET Core HTTP server and static UI hosting.
- `backend/Host` and `backend/Source`: C# operation contracts, implementation, and solver code.
- `backend/Packages`: fixed private NuGet packages used by the source projects.
- `resources`: editor schemas and localized resources.

This application is still under development. UI migration, individual backend operations, real model solving, and file-format round trips require their own verification; a successful app launch does not establish full HEC-RAS project compatibility.

The map time controls can apply a result type to active MultiResult groups and synchronize stored results by timestamp. Each group uses its own matching profile index. A missing match stays unavailable; live compute callback synchronization is not yet connected in the frontend. `npm run test:result-layer-settings` and `npm run test:result-layer-react` check client contracts and simulated transport separately from the real HDF acceptance commands in ServerChecks.

Geometry map quick fields edit the selected arc or region through the existing topology draft. Enum choices come from the Host; empty arc size and point count preserve the source's NaN and -1 conventions. Undo/redo uses topology history, and the parent Save persists the HDF. `npm run test:topology-quick-edit` checks input, selection and draft ownership; real HTTP/HDF and map interaction are separate checks.
