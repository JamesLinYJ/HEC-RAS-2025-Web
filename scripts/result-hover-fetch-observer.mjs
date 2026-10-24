/** Main-world, read-only fetch observer for isolated Result hover UI checks. */
export const resultHoverFetchObserverSource = String.raw`(() => {
  if (window.__hecResultHoverObserver?.version === 1) return;
  const maximumRecords = 500;
  const maximumPendingOperations = 32;
  const maximumPendingPollsPerOperation = 12;
  const trace = { version: 1, items: [], dropped: 0 };
  const hoverOperationIds = new Set();
  const pendingPolls = new Map();
  const append = (item) => {
    if (trace.items.length < maximumRecords) trace.items.push(item);
    else trace.dropped++;
  };
  const originalFetch = window.fetch.bind(window);
  Object.defineProperty(window, '__hecResultHoverObserver', {
    configurable: false,
    enumerable: false,
    value: trace,
  });
  window.fetch = async (...args) => {
    const input = args[0];
    const init = args[1] ?? {};
    const request = input instanceof Request ? input : null;
    let url;
    try { url = new URL(request?.url ?? String(input), location.href); }
    catch { return await originalFetch(...args); }
    const method = String(init.method ?? request?.method ?? 'GET').toUpperCase();
    if (!url.pathname.includes('/api/operations')) return await originalFetch(...args);

    let requestBodyPromise = null;
    let submission = null;
    if (url.pathname.endsWith('/api/operations') && method === 'POST') {
      try {
        if (typeof init.body === 'string') submission = parseSubmission(init.body);
        else if (request) requestBodyPromise = request.clone().text().then(parseSubmission).catch(() => null);
      } catch { submission = null; }
    }

    const response = await originalFetch(...args);
    const observeAccepted = (acceptedSubmission) => {
      if (!acceptedSubmission ||
          !['map.hover.identify', 'mapHoverIdentify', 'map.associations.load'].includes(acceptedSubmission.method)) return;
      // Do not await inspection of the cloned response. The app receives the
      // exact original Response as soon as the underlying fetch resolves.
      void response.clone().json().then((payload) => {
        if (!response.ok) return;
        const operationId = payload.id ?? null;
        if (typeof operationId === 'string') {
          hoverOperationIds.add(operationId);
          const buffered = pendingPolls.get(operationId);
          if (buffered) {
            for (const poll of buffered) append(poll);
            pendingPolls.delete(operationId);
          }
        }
        append({
          kind: 'accepted', method, url: url.pathname, status: response.status,
          operationMethod: acceptedSubmission.method, requestID: acceptedSubmission.requestID,
          map: acceptedSubmission.map,
          point: acceptedSubmission.point, profileSelections: acceptedSubmission.profileSelections,
          layers: acceptedSubmission.layers, operationId,
          association: acceptedSubmission.association,
        });
      }).catch(() => {});
    };
    if (submission) observeAccepted(submission);
    else if (requestBodyPromise) void requestBodyPromise.then(observeAccepted);
    if (method === 'GET') {
      const match = /\/api\/operations\/([^/]+)$/.exec(url.pathname);
      const operationId = match ? decodeURIComponent(match[1]) : null;
      if (operationId) {
        void response.clone().json().then(async (payload) => {
          const entries = Array.isArray(payload.result?.entries) ? payload.result.entries : null;
          let associationResult = null;
          if (payload.state === 'completed' && payload.hasMore === false &&
              typeof payload.result?.projectCanonicalPath === 'string' && Array.isArray(payload.result.layers)) {
            associationResult = { mapID: payload.result.mapID, mapIndex: payload.result.mapIndex,
              mapName: payload.result.mapName, mapType: payload.result.mapType,
              visualizationRevision: payload.result.visualizationRevision, contextToken: payload.result.contextToken,
              viewport: payload.result.viewport, layers: await Promise.all(payload.result.layers.map(async layer => {
                let rgbaSHA256 = null;
                if (typeof layer.rgba === 'string' && layer.rgba.length) {
                  const bytes = Uint8Array.from(atob(layer.rgba), character => character.charCodeAt(0));
                  const hash = await crypto.subtle.digest('SHA-256', bytes);
                  rgbaSHA256 = Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
                }
                return { locator: layer.locator, kind: layer.kind, assetPath: layer.assetPath, revision: layer.revision,
                  status: layer.status, width: layer.width, height: layer.height, sourceExtent: layer.sourceExtent,
                  dependencies: layer.dependencies, geometryNodes: layer.geometryNodes, planNodes: layer.planNodes,
                  resultOwners: layer.resultOwners, isChecked: layer.isChecked, effectiveVisible: layer.effectiveVisible, rgbaSHA256 };
              })) };
          }
          const poll = {
            kind: 'poll', method, url: url.pathname, status: response.status,
            operationId, after: url.searchParams.get('after'),
            state: payload.state ?? null, cursor: payload.cursor ?? null,
            hasMore: payload.hasMore ?? null,
            error: payload.error ? { code: payload.error.code ?? null, message: payload.error.message ?? null } : null,
            result: payload.state === 'completed' && payload.hasMore === false && entries
              ? {
                requestID: payload.result.requestID ?? null,
                point: payload.result.point ?? null,
                mapID: payload.result.mapID ?? null,
                mapIndex: payload.result.mapIndex ?? null,
                mapName: payload.result.mapName ?? null,
                mapType: payload.result.mapType ?? null,
                entries: entries.map((entry) => ({
                  node: entry.node, kind: entry.kind, label: entry.label,
                  value: entry.value ?? null, valueType: entry.valueType ?? null,
                  sourcePath: entry.sourcePath ?? null, sourceRevision: entry.sourceRevision ?? null,
                  mapType: entry.mapType ?? null, profileIndex: entry.profileIndex ?? null,
                })),
              } : associationResult,
          };
          if (payload.state === 'failed') append({ ...poll, kind: 'operation-failed', error: payload.error });
          if (hoverOperationIds.has(operationId)) append(poll);
          else {
            if (!pendingPolls.has(operationId)) {
              if (pendingPolls.size >= maximumPendingOperations) pendingPolls.delete(pendingPolls.keys().next().value);
              pendingPolls.set(operationId, []);
            }
            const polls = pendingPolls.get(operationId);
            if (polls.length < maximumPendingPollsPerOperation) polls.push(poll);
          }
        }).catch(() => {});
      }
    }
    return response;

    function parseSubmission(rawBody) {
      const body = JSON.parse(rawBody);
      const parameters = body.parameters ?? {};
      return {
        method: body.method ?? null,
        requestID: parameters.requestID ?? null,
        point: parameters.point ?? null,
        map: parameters.map ? {
          mapID: parameters.map.mapID ?? null,
          mapIndex: parameters.map.mapIndex ?? null,
          mapName: parameters.map.mapName ?? null,
          mapType: parameters.map.mapType ?? null,
        } : null,
        profileSelections: Array.isArray(parameters.profileSelections)
          ? parameters.profileSelections.map((selection) => ({ node: selection.node, profileIndex: selection.profileIndex })) : [],
        layers: Array.isArray(parameters.layers)
          ? parameters.layers.map((layer) => ({
            node: layer.node, kind: layer.kind, sourcePath: layer.sourcePath ?? null,
            sourceRevision: layer.sourceRevision ?? null, mapType: layer.mapType ?? null,
            profileIndex: layer.profileIndex ?? null,
            resultSelections: Array.isArray(layer.resultSelections) ? layer.resultSelections : [],
          })) : [],
        association: body.method === 'map.associations.load' ? {
          geometryNodes: parameters.geometryNodes, planNodes: parameters.planNodes, resultOwners: parameters.resultOwners,
          associationNodes: parameters.associationNodes, viewport: parameters.viewport,
          width: parameters.width, height: parameters.height,
        } : null,
      };
    }
  };
})()`;
