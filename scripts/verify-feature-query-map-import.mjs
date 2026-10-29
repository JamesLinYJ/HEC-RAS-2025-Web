import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

if (!globalThis.crypto) globalThis.crypto = webcrypto;
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'feature-query-map-import-'));
let checks = 0;
const check = callback => { callback(); checks++; };
try {
  const entry = path.join(temporary, 'entry.ts');
  await writeFile(entry, `export * from ${JSON.stringify(path.join(root, 'frontend/src/features/map/query-import/featureQueryMapImport.ts'))};\nexport { ServerBackendError } from ${JSON.stringify(path.join(root, 'frontend/src/platform/ServerBackend.ts'))};\n`);
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry, formats: ['es'], fileName: () => 'import.mjs' }, outDir: path.join(temporary, 'out'),
    emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { importFeatureQuerySourcesToMap: run, ServerBackendError } = await import(pathToFileURL(path.join(temporary, 'out/import.mjs')).href);
  const rev = digit => digit.repeat(64);
  const projectRevision = rev('a');
  const context = { projectPath: '/fixture/project.ras', projectXml: '<Project />', projectRevision,
    projectSessionID: 'session-1', selectedAsset: null };
  const xmlSHA = createHash('sha256').update(context.projectXml).digest('hex').toUpperCase();
  const initialVis = rev('b');
  const target = { id: 'map-1', mapID: 'map-1', mapIndex: 0, name: 'Map', mapType: 'Project',
    visualizationRevision: initialVis, roots: [] };
  const geo = (name, revision = rev('c'), type = 'PointsVector') => {
    const sourcePath = `/fixture/${name}.geojson`;
    return { path: sourcePath, revision, name, mapType: type, featureCount: 1, lineColor: '#000000',
      fillColor: '#ffffff', lineWidth: 1, family: { family: 'geojson', sourcePath, revision: rev('d'),
        files: [{ inputPath: sourcePath, path: sourcePath, role: 'source', required: true, exists: true, revision }] } };
  };
  const shape = () => {
    const sourcePath = '/fixture/out.shp', revision = rev('c');
    return { ...geo('out'), path: sourcePath, revision, family: { family: 'shapefile', sourcePath, revision: rev('d'),
      files: ['source', 'shx', 'dbf', 'prj'].map(role => {
        const filePath = role === 'source' ? sourcePath : `/fixture/out.${role}`;
        return { inputPath: filePath, path: filePath, role, required: role !== 'prj', exists: role !== 'prj',
          revision: role === 'prj' ? null : revision };
      }) } };
  };
  const makeFixture = (sources, behavior = {}) => {
    let vis = initialVis, serial = 0, awaitingReceipt = null, omitNode = !!behavior.omitNode;
    let changedFamily = !!behavior.changedFamily, current = true, acceptReadback = !behavior.rejectReadback;
    let acceptParent = !behavior.rejectParent;
    let endFailures = behavior.endFailures ?? 0;
    let calls = [], commits = [], ends = [], readbacks = [];
    const nodes = [];
    const revisions = [rev('e'), rev('f'), rev('1')];
    const makeNode = source => ({ id: `node-${nodes.length}`, name: source.name, sourceClass: 'PointMapLayerVisualContext',
      assetID: source.path, checked: 'checked', diagnosticSourceIdentity: source.family.sourcePath,
      diagnosticSourceRevision: source.revision, locator: { rootName: 'Map Layers', nodeType: 'MapLayer', nodeName: source.name,
        nodePath: [0, 0, nodes.length], mapType: source.mapType, filename: source.path, filenameRelative: source.path.split('/').pop() } });
    const tree = () => ({ roots: [{ id: 'root', name: 'Map Layers', sourceClass: 'MapTreeRootNode',
      children: omitNode ? [] : nodes }], previousVisualizationRevision: null, visualizationRevision: vis,
      expandedNodeIDs: [], selectedNodeID: null, hiddenLayerIDs: [] });
    const gateway = {
      supports: () => true,
      uploadMapSources: async () => { throw new Error('unexpected upload'); },
      request: async (method, params, options) => {
        calls.push({ method, params });
        if (method === 'workspace.file.inspect') {
          const source = sources.find(item => item.path === params.sourcePath);
          return { sourcePath: params.sourcePath, canonicalPath: source?.family.sourcePath ?? context.projectPath,
            revision: source?.revision ?? projectRevision, length: 50 };
        }
        if (method === 'map.vector.captureShapefile') {
          const source = sources.find(item => item.path === params.sourcePath);
          const family = structuredClone(source.family);
          if (changedFamily) family.files.find(file => file.role === 'dbf').revision = rev('9');
          return family;
        }
        if (method === 'map.document.list') return { projectRevision, visualizationRevision: vis,
          maps: [{ mapIndex: 0, mapName: target.name, mapType: target.mapType }], activeMapIndex: 0 };
        if (method === 'map.tree.load') {
          assert.equal(params.expectedVisualizationRevision.toUpperCase(), vis.toUpperCase());
          return tree();
        }
        if (method === 'map.vector.source.load') {
          const source = sources.find(item => item.path === params.expectedFamily.files.find(file => file.role === 'source').inputPath);
          const map = { projectPath: context.projectPath, projectRevision, projectXmlSHA: xmlSHA, visualizationPath: '/fixture/project.vis',
            visualizationRevision: vis, mapID: target.mapID, mapIndex: target.mapIndex, mapName: target.name,
            mapType: target.mapType, isPersisted: true };
          const family = structuredClone(params.expectedFamily);
          if (behavior.badReadbackFamily) family.revision = rev('2');
          return { source: { map, locator: params.node, family }, data: { count: source.featureCount,
            shapeType: source.mapType === 'PointsVector' ? 'Point' : source.mapType === 'PolylinesVector' ? 'Polyline' : 'Polygon' } };
        }
        if (method === 'map.vector.import' || method === 'map.vector.importShapefile') {
          const source = sources.find(item => item.path === params.sourcePath);
          assert.equal(params.map.expectedVisualizationRevision.toUpperCase(), vis.toUpperCase());
          assert.equal(params.expectedProjectRevision, projectRevision);
          if (method === 'map.vector.import') {
            assert.equal(params.expectedSourceCanonicalPath, source.family.sourcePath);
            assert.equal(params.sourceRevision, source.revision);
          } else assert.deepEqual(params.expectedFamily, source.family);
          if (behavior.terminalError) throw behavior.terminalError;
          if (behavior.terminalFailure) throw Object.assign(new Error('confirmed failure'), { terminalConfirmed: true, operationId: 'op-1' });
          const previousVisualizationRevision = vis;
          vis = revisions[serial++];
          nodes.push(makeNode(source));
          const receipt = { mapIndex: 0, mapName: target.name, mapType: target.mapType, previousVisualizationRevision, visualizationRevision: vis };
          if (behavior.unknownFirst && serial === 1) {
            awaitingReceipt = receipt;
            options?.onTransportState?.({ state: 'reconnecting', operationId: 'op-original', attempt: 1, retryInMilliseconds: 0 });
            throw Object.assign(new Error('connection lost'), { operationId: 'op-original', terminalConfirmed: false });
          }
          if (behavior.unknownNoId) throw Object.assign(new Error('acceptance unknown'), { terminalConfirmed: false });
          if (behavior.badReceipt) return { ...receipt, mapName: 'foreign' };
          return receipt;
        }
        throw new Error(`Unexpected method ${method}`);
      },
      resumeOperation: async id => {
        calls.push({ method: 'resume', id }); assert.equal(id, 'op-original');
        if (behavior.resumeError) throw behavior.resumeError;
        if (behavior.resumeFailure) throw Object.assign(new Error('terminal failure'), { terminalConfirmed: true, operationId: id });
        return awaitingReceipt;
      },
    };
    const options = {
      dirtyOwner: 'map.query:fixture',
      onBeginMapVisualizationMutation: (captured, document, owner) => {
        assert.equal(owner, 'map.query:fixture'); assert.equal(captured.projectPath, context.projectPath);
        assert.equal(document.visualizationRevision.toUpperCase(), vis.toUpperCase());
        return `lease-${ends.length + 1}`;
      },
      onCommitMapVisualization: (_captured, document, receipt, lease) => {
        commits.push({ document, receipt, lease }); return acceptParent;
      },
      onEndMapVisualizationMutation: lease => {
        if (endFailures > 0) { endFailures--; throw new Error('release failed'); }
        ends.push(lease);
      },
      onReadback: (document, snapshot) => { readbacks.push({ document, snapshot }); return acceptReadback; },
    };
    return { gateway, options, calls, commits, ends, readbacks, current: () => current,
      reveal: () => { omitNode = false; }, acceptReadback: () => { acceptReadback = true; },
      acceptParent: () => { acceptParent = true; },
      setCurrent: value => { current = value; } };
  };
  const imports = calls => calls.filter(call => call.method === 'map.vector.import' || call.method === 'map.vector.importShapefile');
  const a = geo('one'), b = geo('two', rev('3'), 'PolylinesVector');

  {
    const fixture = makeFixture([a, b]);
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'complete'));
    check(() => assert.deepEqual(result.imported, [a.path, b.path]));
    check(() => assert.deepEqual(imports(fixture.calls).map(call => call.params.map.expectedVisualizationRevision), [initialVis, rev('e')]));
    check(() => assert.equal(fixture.commits.length, 2));
    check(() => assert.equal(fixture.ends.length, 2));
    check(() => assert.equal(fixture.readbacks[1].document.visualizationRevision, rev('f')));
    check(() => assert.deepEqual(fixture.calls.filter(call => call.method === 'map.vector.source.load').map(call => call.params.expectedFamily), [a.family, b.family]));
    check(() => assert.equal(fixture.calls.some(call => call.method === 'upload'), false));
  }
  for (const mutate of [
    item => { item.family.sourcePath = '/foreign/one.geojson'; },
    item => { item.family.files[0].revision = rev('9'); },
    item => { item.family.files.push({ ...item.family.files[0], role: 'extra' }); },
    item => { item.mapType = 'Raster'; },
  ]) {
    const invalid = structuredClone(a); mutate(invalid);
    const fixture = makeFixture([invalid]);
    const result = await run(fixture.gateway, context, target, [invalid], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'failed'));
    check(() => assert.equal(imports(fixture.calls).length, 0));
  }
  {
    const source = shape(); const fixture = makeFixture([source], { changedFamily: true });
    const result = await run(fixture.gateway, context, target, [source], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'failed'));
    check(() => assert.match(result.failures[0].message, /文件族已变化/));
    check(() => assert.equal(imports(fixture.calls).length, 0));
    check(() => assert.equal(fixture.ends.length, 1));
  }
  {
    const source = shape(); const fixture = makeFixture([a, source], { changedFamily: true });
    const result = await run(fixture.gateway, context, target, [a, source], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'partial'));
    check(() => assert.deepEqual(result.imported, [a.path]));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    check(() => assert.equal(fixture.ends.length, 2));
  }
  {
    const source = shape(); const fixture = makeFixture([source]);
    const result = await run(fixture.gateway, context, target, [source], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'complete'));
    check(() => assert.equal(imports(fixture.calls)[0].method, 'map.vector.importShapefile'));
  }
  {
    const fixture = makeFixture([a, b], { unknownFirst: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.equal(result.operationID, 'op-original'));
    check(() => assert.equal(fixture.ends.length, 0));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    const [resumed, duplicate] = await Promise.all([result.resume(), result.resume()]);
    check(() => assert.equal(resumed.status, 'complete'));
    check(() => assert.deepEqual(duplicate.imported, [a.path, b.path]));
    check(() => assert.equal(fixture.calls.filter(call => call.method === 'resume').length, 1));
    check(() => assert.equal(imports(fixture.calls).length, 2));
    check(() => assert.equal(fixture.ends.length, 2));
    await result.resume();
    check(() => assert.equal(imports(fixture.calls).length, 2));
  }
  {
    const fixture = makeFixture([a, b], { rejectParent: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.match(result.message, /父工作区未接纳/));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    check(() => assert.equal(fixture.ends.length, 0));
    check(() => assert.equal(fixture.readbacks.length, 0));
    fixture.acceptParent();
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'complete'));
    check(() => assert.equal(imports(fixture.calls).length, 2));
  }
  {
    const fixture = makeFixture([a], { unknownFirst: true, resumeFailure: true });
    const result = await run(fixture.gateway, context, target, [a], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'failed'));
    check(() => assert.equal(fixture.ends.length, 1));
    check(() => assert.equal(imports(fixture.calls).length, 1));
  }
  {
    const fixture = makeFixture([a, b], { unknownFirst: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    fixture.setCurrent(false);
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'unknown'));
    check(() => assert.equal(fixture.commits.length, 1));
    check(() => assert.equal(fixture.ends.length, 0));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    fixture.setCurrent(true);
    const recovered = await resumed.resume();
    check(() => assert.equal(recovered.status, 'complete'));
    check(() => assert.equal(imports(fixture.calls).length, 2));
  }
  {
    const fixture = makeFixture([a, b], { omitNode: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.deepEqual(result.imported, []));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    check(() => assert.equal(fixture.ends.length, 0));
    fixture.reveal();
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'complete'));
    check(() => assert.deepEqual(resumed.imported, [a.path, b.path]));
    check(() => assert.equal(imports(fixture.calls).length, 2));
  }
  {
    const fixture = makeFixture([a, b], { badReadbackFamily: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.match(result.message, /文件族/));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    check(() => assert.equal(fixture.ends.length, 0));
  }
  {
    const fixture = makeFixture([a, b], { rejectReadback: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.match(result.message, /界面无法接纳/));
    check(() => assert.deepEqual(result.imported, []));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    check(() => assert.equal(fixture.ends.length, 0));
    fixture.acceptReadback();
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'complete'));
    check(() => assert.equal(imports(fixture.calls).length, 2));
  }
  {
    const fixture = makeFixture([a, b], { endFailures: 1 });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.match(result.message, /释放父租约失败/));
    check(() => assert.equal(imports(fixture.calls).length, 1));
    check(() => assert.deepEqual(result.imported, []));
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'complete'));
    check(() => assert.equal(imports(fixture.calls).length, 2));
  }
  {
    const fixture = makeFixture([a, b], { badReceipt: true });
    const result = await run(fixture.gateway, context, target, [a, b], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.equal(fixture.commits.length, 0));
    const resumed = await result.resume();
    check(() => assert.equal(resumed.status, 'unknown'));
    check(() => assert.deepEqual(resumed.imported, []));
    check(() => assert.equal(fixture.ends.length, 0));
    check(() => assert.equal(imports(fixture.calls).length, 1));
  }
  {
    const fixture = makeFixture([a], { terminalFailure: true });
    const result = await run(fixture.gateway, context, target, [a], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'failed'));
    check(() => assert.equal(fixture.ends.length, 1));
  }
  {
    const source = shape();
    const detail = String.raw`The selected Shapefile is outside the current project's supported source directory: C:\Exports\Retained.shp`;
    const terminalError = operationId => new ServerBackendError('invalidInput', detail, { payload: 'do-not-dump-details' },
      { terminalConfirmed: true, operationId });
    const fixture = makeFixture([source], { terminalError: terminalError('op-1') });
    const result = await run(fixture.gateway, context, target, [source], fixture.current, fixture.options);
    const resumedFixture = makeFixture([source], { unknownFirst: true, resumeError: terminalError('op-original') });
    const pending = await run(resumedFixture.gateway, context, target, [source], resumedFixture.current, resumedFixture.options);
    const resumed = await pending.resume();
    check(() => {
      for (const [report, tested] of [[result, fixture], [resumed, resumedFixture]]) {
        assert.equal(report.status, 'failed');
        assert.equal(report.failures[0].path, source.path);
        assert.ok(report.message.includes(detail));
        assert.equal(report.failures[0].message, report.message);
        assert.ok(!report.message.includes('do-not-dump-details'));
        assert.equal(tested.ends.length, 1);
        assert.equal(tested.commits.length, 0);
      }
    });
  }
  {
    const fixture = makeFixture([a], { unknownNoId: true });
    const result = await run(fixture.gateway, context, target, [a], fixture.current, fixture.options);
    check(() => assert.equal(result.status, 'unknown'));
    check(() => assert.equal(result.resume, undefined));
    check(() => assert.equal(fixture.ends.length, 0));
  }
  console.log(`Feature-query map-import protocol verification passed (${checks} checks). Real Host and browser acceptance are separate.`);
} finally {
  await rm(temporary, { recursive: true, force: true });
}
