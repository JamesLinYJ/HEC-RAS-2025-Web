# Shared browser and desktop UI

The React renderer is shared by the browser build and Electron. It starts the C# WebAssembly runtime in a dedicated worker and calls `Ras.Wasm.EngineExports`; Electron does not launch a managed sidecar.

The current screen verifies only the real in-memory CPU solver path. The request `{}` selects the solver's built-in wet-grid sample. It does not represent project-file, HDF5, DSS, GDAL, or full application UI support.

## Web hosting requirements

WebAssembly threads and cooperative cancellation need cross-origin isolation. Both the Vite development server and the bundled static server set these response headers:

- `Cross-Origin-Opener-Policy: same-origin`
- `Cross-Origin-Embedder-Policy: require-corp`
- `Cross-Origin-Resource-Policy: same-origin`

Serve the generated `dist/` directory with those headers on HTML, JavaScript, worker, and WASM responses. The `.wasm` content type must be `application/wasm`. `npm run serve:web` is the project's local production-style static server.

## Commands

- `npm run dev` — Vite browser development server.
- `npm run build:web` — type-check and build the shared renderer into `dist/`.
- `npm run serve:web` — serve `dist/` with the isolation and MIME headers.
- `npm run desktop` — build and open the Electron application.
- `npm run smoke:electron` — build and run the hidden Electron WASM runtime/solver acceptance entry point. It exits non-zero when the runtime or real solver fails.
