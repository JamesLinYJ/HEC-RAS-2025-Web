import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { build } from 'vite';

if (!globalThis.crypto) globalThis.crypto = webcrypto;
const root = process.cwd();
const temporary = await mkdtemp(path.join(tmpdir(), 'result-groups-'));
let checks = 0;
const check = callback => { callback(); checks++; };
const wait = async tx => {
  for (let i = 0; tx.state.busy && i < 200; i++) await new Promise(resolve => setTimeout(resolve, 5));
  assert.equal(tx.state.busy, false, 'transaction must settle');
};
try {
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/map/results-layer-settings/sharedKeyframe.ts'))};\n` +
    `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/map/results-layer-settings/activeResultProfile.ts'))};\n` +
    `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/map/results-layer-settings/resultLayerSettingsTransaction.ts'))};\n` +
    `export { loadMapDocumentCatalog } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/ProjectTreeListener.ts'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: { lib: { entry, formats: ['es'],
    fileName: () => 'test.mjs' }, outDir: path.join(temporary, 'out'), emptyOutDir: true, minify: false } });
  const { sharedKeyframeFromRender, verifySharedKeyframe, activeResultProfile, ResultLayerWrite, pendingResultLayerWrites,
    loadMapDocumentCatalog } = await import(pathToFileURL(path.join(temporary, 'out/test.mjs')).href);
  const rev = value => value.repeat(64);
  const projectRevision = rev('a'), oldRevision = rev('b'), newRevision = rev('c'), resultRevision = rev('d');
  const context = { projectSessionID: 'session', projectPath: '/fixture/project.ras', projectXml: '<Project />',
    projectRevision, selectedAsset: null };
  const xmlSHA = createHash('sha256').update(context.projectXml).digest('hex').toUpperCase();
  const locator = n => ({ rootName: 'Results', nodeType: 'MultiResult', nodeName: `Plan ${n}`, nodePath: [1, n], assetID: `/fixture/Plan${n}.hdf` });
  const source = n => ({ resultPath: `/fixture/result${n}.hdf`, revision: resultRevision, resultName: `Result ${n}`,
    planName: `Plan ${n}`, endTime: null, isComputing: false, computeState: null, computeFailureReason: null,
    diagnosticStatus: null, diagnosticReadError: null });
  const group = (n, type = 'Depth') => ({ node: locator(n), nodeKind: 'MultiResult', name: `Plan ${n}`, selectedMapType: type,
    profileIndex: 0, selections: [{ selectionIndex: 0, selectionMode: 'Explicit', selectedResult: source(n), choices: [source(n)] }] });
  const steps = [{ id: 0, label: '01JAN2020 00:00:00', time: '2020-01-01T00:00:00' },
    { id: 1, label: '01JAN2020 01:00:00', time: '2020-01-01T01:00:00' }];
  const snapshot = { steps, selectedProfileIndex: 1, selectedProfileTime: steps[1].time };
  const anchor = sharedKeyframeFromRender(group(0), snapshot, 1);
  check(() => assert.deepEqual(anchor.profiles, steps.map(step => ({ index: step.id, name: step.label, time: step.time }))));
  check(() => assert.equal(anchor.resultPath, source(0).resultPath));
  check(() => assert.throws(() => sharedKeyframeFromRender(group(0), { ...snapshot, selectedProfileTime: steps[0].time }, 1)));
  check(() => assert.throws(() => sharedKeyframeFromRender(group(0), { ...snapshot, steps: [steps[1]] }, 1)));
  check(() => assert.throws(() => sharedKeyframeFromRender({ ...group(0), selections: [] }, snapshot, 1)));
  const echo = { node: anchor.node, selectionIndex: 0, resultPath: anchor.resultPath, revision: anchor.revision,
    runID: null, profileIndex: 1, profileName: anchor.profileName, time: anchor.time };
  check(() => assert.doesNotThrow(() => verifySharedKeyframe(anchor, echo)));
  for (const altered of [{ profileIndex: 0 }, { time: steps[0].time }, { revision: rev('e') },
    { node: locator(1) }, { resultPath: source(1).resultPath }, { runID: 'writer' }]) {
    check(() => assert.throws(() => verifySharedKeyframe(anchor, { ...echo, ...altered })));
  }
  const selectedGroup = group(1), targetFrame = { id: 'target', sourceID: 'target-source', sourceMapID: 'map',
    resultPath: source(1).resultPath, revision: resultRevision, mapType: 'Velocity', profileIndex: 2,
    profileName: steps[1].label, time: steps[1].time, selectionIndices: [0] };
  const targetStatus = { resultPath: source(1).resultPath, revision: resultRevision, selectionIndices: [0],
    profileIndex: 2, available: true };
  const targetSnapshot = { contextToken: rev('f'), sourceID: 'target-source', selectedProfileIndex: 1,
    selectedProfileTime: steps[1].time, sharedKeyframeAnchor: echo, sources: [targetStatus], layers: [targetFrame] };
  const frameController = { status: 'ready', activeNode: selectedGroup, activeNodeKey: 'target', keyframeNodeKey: 'anchor',
    selectedProfileIndex: 1, selectedProfileTime: steps[1].time, renderSnapshot: targetSnapshot,
    sources: { contextToken: rev('f'), mapID: 'map' } };
  const projection = activeResultProfile(frameController);
  check(() => assert.equal(projection.selectedProfileIndex, 2));
  check(() => assert.equal(projection.renderSnapshot.selectedProfileIndex, 2));
  check(() => assert.equal(targetSnapshot.selectedProfileIndex, 1));
  for (const patch of [{ sources: [{ ...targetStatus, profileIndex: 1 }] },
    { sources: [{ ...targetStatus, revision: rev('e') }] }, { layers: [{ ...targetFrame, selectionIndices: [1] }] },
    { layers: [{ ...targetFrame, resultPath: source(0).resultPath }] }, { sharedKeyframeAnchor: null }]) {
    check(() => assert.equal(activeResultProfile({ ...frameController, renderSnapshot: { ...targetSnapshot, ...patch } }).ready, false));
  }

  async function fixture(behavior = {}) {
    let currentRevision = oldRevision, mode = behavior.mode ?? 'normal', badReadback = !!behavior.badReadback;
    let commitAccepted = behavior.commitAccepted !== false, endFailures = behavior.endFailures ?? 0;
    const calls = [], commits = [], ends = [], installs = [];
    const op = '1'.repeat(32);
    let document;
    const roots = () => [{ id: 'root', name: 'Results', sourceClass: 'TreeNodeVM', children: [] }];
    const tree = () => ({ roots: roots(), previousVisualizationRevision: currentRevision, visualizationRevision: currentRevision,
      expandedNodeIDs: [], selectedNodeID: null, hiddenLayerIDs: [] });
    const sourceCatalog = () => ({ projectPath: context.projectPath, projectRevision, projectXmlSHA: xmlSHA,
      visualizationRevision: currentRevision, mapID: document.mapID, mapIndex: 0, mapName: 'Map', mapType: 'Project',
      contextToken: rev('f'), activeNodes: [group(0, currentRevision === newRevision && !badReadback ? 'Velocity' : 'Depth'),
        group(1, currentRevision === newRevision && !badReadback ? 'Velocity' : 'Water Surface')],
      treeRoots: roots(), treeSelectionOverrides: [] });
    const gateway = { supports: () => true, request: async (method, params, options) => {
      calls.push({ method, params });
      if (method === 'map.document.list') return { projectRevision, visualizationRevision: currentRevision,
        maps: [{ mapIndex: 0, mapName: 'Map', mapType: 'Project' }], activeMapIndex: 0 };
      if (method === 'map.tree.load') {
        assert.equal(params.expectedVisualizationRevision.toUpperCase(), currentRevision.toUpperCase());
        return tree();
      }
      if (method === 'map.resultSources') return sourceCatalog();
      if (method === 'map.tree.perform') {
        options?.onTransportState?.({ state: 'connected', operationId: op, recoveredAfterAttempt: 0 });
        assert.equal(params.action, 'setResultMapType');
        assert.equal(params.name, 'Velocity');
        assert.equal(params.map.expectedVisualizationRevision, oldRevision);
        if (mode === 'terminal') throw Object.assign(new Error('terminal'), { terminalConfirmed: true, operationId: op });
        currentRevision = newRevision;
        if (mode === 'unknown') throw Object.assign(new Error('network'), { terminalConfirmed: false, operationId: op });
        return { ...tree(), previousVisualizationRevision: mode === 'bad-receipt' ? rev('e') : oldRevision };
      }
      throw new Error(`Unexpected ${method}`);
    }, resumeOperation: async id => {
      calls.push({ method: 'resume', id });
      assert.equal(id, op);
      return { ...tree(), previousVisualizationRevision: oldRevision };
    } };
    const listed = await loadMapDocumentCatalog(gateway, context);
    document = listed.documents[0];
    const map = { projectPath: context.projectPath, projectXml: context.projectXml, projectXmlSHA: xmlSHA,
      projectSessionID: context.projectSessionID, mapID: document.mapID, mapIndex: 0, mapName: 'Map', mapType: 'Project',
      expectedVisualizationRevision: oldRevision };
    const catalog = sourceCatalog();
    const target = { mapID: document.mapID, mapIndex: 0, name: 'Map', mapType: 'Project', visualizationRevision: oldRevision };
    const capture = { scope: `scope-${crypto.randomUUID()}`, backend: gateway, context, document, map, catalog,
      type: 'Velocity', target, leaseID: 'lease', commit: (...args) => { commits.push(args); return commitAccepted; },
      end: id => { ends.push(id); if (endFailures-- > 0) throw new Error('end failed'); } };
    const tx = new ResultLayerWrite(capture);
    pendingResultLayerWrites.set(capture.scope, tx);
    tx.attach((readback, captured) => { installs.push(readback); return captured === capture; });
    return { tx, calls, commits, ends, installs, op, setGoodReadback: () => { badReadback = false; },
      acceptCommit: () => { commitAccepted = true; } };
  }
  {
    const f = await fixture(); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'complete'));
    check(() => assert.equal(f.calls.filter(call => call.method === 'map.tree.perform').length, 1));
    check(() => assert.equal(f.commits.length, 1));
    check(() => assert.equal(f.ends.length, 1));
    check(() => assert.equal(f.installs.length, 1));
    check(() => assert.equal(pendingResultLayerWrites.has(f.tx.capture.scope), false));
  }
  {
    const f = await fixture({ mode: 'unknown' }); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'unknown'));
    check(() => assert.equal(f.tx.state.operationID, f.op));
    check(() => assert.equal(f.ends.length, 0));
    check(() => assert.equal(f.tx.canResume, true));
    await f.tx.resume();
    check(() => assert.equal(f.tx.state.phase, 'complete'));
    check(() => assert.equal(f.calls.filter(call => call.method === 'map.tree.perform').length, 1));
    check(() => assert.equal(f.calls.filter(call => call.method === 'resume').length, 1));
  }
  {
    const f = await fixture({ badReadback: true }); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'readback'));
    check(() => assert.equal(f.commits.length, 1));
    check(() => assert.equal(f.ends.length, 0));
    f.setGoodReadback(); await f.tx.resume();
    check(() => assert.equal(f.tx.state.phase, 'complete'));
    check(() => assert.equal(f.commits.length, 1));
    check(() => assert.equal(f.calls.filter(call => call.method === 'map.tree.perform').length, 1));
  }
  {
    const f = await fixture({ commitAccepted: false }); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'adoption'));
    check(() => assert.equal(f.ends.length, 0));
    f.acceptCommit(); await f.tx.resume();
    check(() => assert.equal(f.tx.state.phase, 'complete'));
    check(() => assert.equal(f.calls.filter(call => call.method === 'map.tree.perform').length, 1));
  }
  {
    const f = await fixture({ mode: 'bad-receipt' }); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'unverified'));
    check(() => assert.equal(f.tx.canResume, false));
    check(() => assert.equal(f.ends.length, 0));
    await f.tx.resume();
    check(() => assert.equal(f.calls.filter(call => call.method === 'map.tree.perform').length, 1));
    pendingResultLayerWrites.delete(f.tx.capture.scope);
  }
  {
    const f = await fixture({ mode: 'terminal' }); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'complete'));
    check(() => assert.equal(f.commits.length, 0));
    check(() => assert.equal(f.ends.length, 1));
  }
  {
    const f = await fixture({ endFailures: 1 }); f.tx.start(); await wait(f.tx);
    check(() => assert.equal(f.tx.state.phase, 'readback'));
    await f.tx.resume();
    check(() => assert.equal(f.tx.state.phase, 'complete'));
    check(() => assert.equal(f.calls.filter(call => call.method === 'map.tree.perform').length, 1));
  }
  console.log(`Result group anchor and transaction protocol: ${checks} checks passed. Synthetic backend; real UI/Host acceptance is separate.`);
} finally {
  await rm(temporary, { recursive: true, force: true });
}
