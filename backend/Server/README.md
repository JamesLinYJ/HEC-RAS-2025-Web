# HEC-RAS local server

This ASP.NET Core service hosts the React application and exposes the C# backend through local HTTP operations. The listener is restricted to loopback addresses. It is intended for one local user and does not provide authentication for remote or multi-user deployment.

## Run

From the repository root, run:

```sh
dotnet run --project backend/Server/Ras.Backend.Server.csproj -- --urls http://127.0.0.1:5080
```

The default address is `http://127.0.0.1:5080`; `HECRAS_SERVER_PORT` changes its port. `--urls` may select another loopback address, including `http://[::1]:5080`, but non-loopback listeners are rejected. Set `HECRAS_WEB_ROOT` to a built frontend directory containing `index.html` when the server cannot find the repository `dist` directory. The Electron launcher can pass its chosen address through `--urls` and its frontend directory through `--content-root` or `HECRAS_WEB_ROOT`.

For Vite development, configure the exact loopback origin in `HECRAS_DEV_ORIGINS`, for example `http://127.0.0.1:5173`. Mutating requests from browser pages are accepted only from the request's own origin or an explicitly configured development origin.

`GET /api/health` reports HTTP readiness without loading the backend runtime. `GET /api/runtime` returns the runtime identifier and the backend's supported operation names.

## Operation protocol

Submit an operation with `POST /api/operations` and JSON `{ "method": "...", "parameters": { ... } }`. A successful request returns HTTP 202 and `{ "id": "..." }`. Poll `GET /api/operations/{id}?after=0` for `{ "id", "state", "cursor", "events", "hasMore" }`. Progress events carry monotonically increasing sequence numbers; use the returned cursor as the next `after` value until `hasMore` is false. The final result or error is included only after all progress events have been delivered. States are `queued`, `running`, `completed`, `failed`, and `canceled`.

`DELETE /api/operations/{id}` requests cooperative cancellation and returns `{ "id", "cancelRequested" }`; cancellation is not reported as complete until the worker exits. `POST /api/operations/{id}/controls` accepts `{ "control": { ... } }` and forwards it through the backend's duplex protocol in sequence order. Unknown operation names are rejected before a worker starts.

Each operation runs in a separate `--backend-worker` child process, keeping process-global backend and HDF state isolated. Server workers are deliberately serialized to one at a time because existing project save locks are process-local and cannot protect concurrent child processes writing the same project. HTTP polling and cancellation remain concurrent. Do not increase worker concurrency until project/file locks are shared across processes.

Progress history is bounded; operations that exceed the configured event count or memory budgets fail explicitly rather than silently dropping events. Terminal operations are retained for up to 15 minutes, subject to the bounded history limit.
