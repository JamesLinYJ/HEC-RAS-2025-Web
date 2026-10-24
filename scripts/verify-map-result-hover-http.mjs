import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import path from 'node:path';
const [baseUrl, manifestPath, reportPath] = process.argv.slice(2);
if (!baseUrl || !manifestPath || !reportPath) {
  throw new Error('Usage: node scripts/verify-map-result-hover-http.mjs <loopback-server-url> <result-hover-manifest.json> <report.json>');
}
const origin = new URL(baseUrl);
assert.ok(['http:', 'https:'].includes(origin.protocol) &&
  ['127.0.0.1', 'localhost', '[::1]'].includes(origin.hostname), 'Use a loopback C# server that can read the local fixture files.');
const manifest=JSON.parse(await readFile(path.resolve(manifestPath),'utf8'));
assert.equal(manifest.schema, 'hecras-result-hover-e2e/v1');
const destination=path.resolve(reportPath);
const report={state:'running',operations:[],checks:[]};
const sha=data=>createHash('sha256').update(data).digest('hex').toUpperCase();
async function op(method,parameters,errorCode){
  const accepted=await fetch(new URL('/api/operations', origin),{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({method,parameters})});
  assert.equal(accepted.status,202);const{id}=await accepted.json();let cursor=0;
  for(let deadline=Date.now()+120000;Date.now()<deadline;){
    const response=await fetch(new URL(`/api/operations/${id}?after=${cursor}`, origin));
    assert.equal(response.status,200);const result=await response.json();cursor=result.cursor;
    if(!result.hasMore&&['completed','failed','cancelled'].includes(result.state)){
      report.operations.push({method,id,state:result.state,error:result.error});
      if(errorCode){assert.equal(result.state,'failed');assert.equal(result.error?.code,errorCode);}else assert.equal(result.state,'completed',JSON.stringify(result.error));
      return result.result;
    }
    await new Promise(resolve=>setTimeout(resolve,50));
  }
  throw new Error(`Unconfirmed operation ${id}`);
}
try{
  const cases=[manifest.cases.find(c=>c.node==='ordinaryResult'),manifest.cases.find(c=>c.id==='multi-A1-to-B0')];
  assert.ok(cases.every(Boolean));
  const watched=[manifest.projectPath,manifest.visualizationPath,...manifest.sources.map(s=>s.resultPath)];
  const before=await Promise.all(watched.map(p=>readFile(p)));
  const opened=await op('project.open',{projectPath:manifest.projectPath});
  const map={projectPath:opened.projectPath,projectXml:opened.projectXml,projectXmlSHA:sha(Buffer.from(opened.projectXml)),
    ...manifest.map,mapID:randomUUID(),projectSessionID:randomUUID(),
    expectedVisualizationRevision:sha(await readFile(manifest.visualizationPath))};
  const selectionOverrides=cases.flatMap(c=>c.selectedSources.map(s=>({node:manifest.nodes[c.node].locator,selectionIndex:s.selectionIndex,mode:'Explicit',resultPath:s.resultPath,resultRevision:s.revision})));
  const context=await op('map.resultSources',{map,selectionOverrides});
  const rendered = await op('map.resultRender', { map, selectionOverrides, contextToken: context.contextToken,
    node: manifest.nodes.multiResult.locator, profileIndex: 1, viewport: null });
  assert.equal(rendered.layers.length, 2, 'The overlapping A1/B0 time must render both actual HDF sources.');
  const renderedPixels = rendered.layers.reduce((sum, layer) => sum + layer.width * layer.height, 0);
  const wireBytes = Buffer.byteLength(JSON.stringify(rendered), 'utf8');
  assert.ok(wireBytes > 8 * 1024 * 1024, 'This regression must exercise a real multi-frame response above the former protocol limit.');
  assert.ok(renderedPixels <= 8 * 1024 * 1024);
  for (const expected of cases[1].entries) {
    const frame = rendered.layers.find(layer => layer.resultPath === expected.resultPath && layer.profileIndex === expected.nativeProfileIndex);
    assert.ok(frame); assert.equal(frame.revision.toUpperCase(), expected.revision.toUpperCase());
    assert.equal(Buffer.from(frame.rgba, 'base64').length, frame.width * frame.height * 4);
  }
  report.multiFrameRendering = { wireBytes, renderedPixels, layers: rendered.layers.map(layer => ({
    path: layer.resultPath, revision: layer.revision, profileIndex: layer.profileIndex,
    width: layer.width, height: layer.height, rgbaSHA256: sha(Buffer.from(layer.rgba, 'base64')) })) };
  report.checks.push('A real two-frame Result response above 8 MiB survives the bounded protocol and preserves both source profiles and RGBA byte lengths.');
  const layers=cases.map(c=>({node:manifest.nodes[c.node].locator,kind:'result',mapType:c.mapType,profileIndex:c.anchorProfileIndex,
    resultSelections:c.selectedSources.map(s=>({selectionIndex:s.selectionIndex,resultPath:s.resultPath,resultRevision:s.revision}))}));
  const point={x:cases[0].point.x,y:cases[0].point.y};
  const request={map,contextToken:context.contextToken,selectionOverrides,requestID:randomUUID(),point,probePoint:{x:point.x+.001,y:point.y},worldUnitsPerScreenPoint:.001,
    profileSelections:cases.map(c=>({node:manifest.nodes[c.node].locator,profileIndex:c.anchorProfileIndex})),layers};
  const result=await op('map.hover.identify',request);
  const expected=cases.flatMap(c=>c.entries.map(e=>({...e,nodeType:manifest.nodes[c.node].kind})));
  assert.equal(result.entries.length,expected.length,'Both nodes must keep independent profile selections.');
  for(const e of expected){const actual=result.entries.find(a=>a.node.nodeType===e.nodeType&&a.sourcePath===e.resultPath&&a.profileIndex===e.nativeProfileIndex);
    assert.ok(actual,JSON.stringify(e));assert.equal(actual.sourceRevision.toUpperCase(),e.revision.toUpperCase());
    assert.ok(Math.abs(Number(actual.value)-e.expectedValue)<=e.tolerance,JSON.stringify({actual,expected:e}));}
  report.entries=result.entries;
  report.checks.push('One real HTTP hover samples ordinary Result A4 and MultiResult A1/B0 independently from the same shared A HDF.');
  for(const mutate of [r=>{r.layers[0].resultSelections[0].resultRevision='0'.repeat(64);},r=>{r.contextToken='stale-context';},r=>{r.map.expectedVisualizationRevision='0'.repeat(64);},r=>{r.profileSelections[0].profileIndex=0;}]){
    const stale=structuredClone(request);stale.requestID=randomUUID();mutate(stale);await op('map.hover.identify',stale,'sourceChanged');
  }
  report.checks.push('Four stale source, context, map revision and profile-selection captures reject without a result.');
  const outside=structuredClone(request);outside.requestID=randomUUID();outside.point={x:-100,y:-100};outside.probePoint={x:-99.999,y:-100};
  report.outside=await op('map.hover.identify',outside);
  assert.equal(report.outside.entries.length,0,'NoData outside the computed mesh must not become a synthetic zero hover.');
  report.checks.push('Outside-mesh NoData stays empty instead of displaying a chart-friendly zero.');
  for(let i=0;i<watched.length;i++)assert.deepEqual(await readFile(watched[i]),before[i],watched[i]);
  report.checks.push('Readback, hover and rejected stale captures preserve project, visualization and both HDF bytes.');
  const flatten = roots => roots.flatMap(node => [node, ...flatten(node.children ?? [])]);
  const sameNode = (actual, expected) => actual?.rootName === expected.rootName &&
    actual.nodeType === expected.nodeType && actual.nodeName === expected.nodeName &&
    actual.assetID === expected.assetID &&
    JSON.stringify(actual.nodePath ?? []) === JSON.stringify(expected.nodePath ?? []);
  // Result-source slot selections and map-tree Plan selections are different
  // contracts. This visibility transaction uses the persisted tree as-is.
  const visibilityMap = { ...map };
  for (const nodeKind of ['ordinaryResult', 'multiResult']) {
    const locator = manifest.nodes[nodeKind].locator;
    for (const checked of [false, true]) {
      const previousRevision = visibilityMap.expectedVisualizationRevision;
      const saved = await op('map.tree.perform', {
        map: visibilityMap, action: 'visibility', node: locator, checked,
      });
      assert.equal(saved.previousVisualizationRevision.toUpperCase(), previousRevision.toUpperCase());
      visibilityMap.expectedVisualizationRevision = saved.visualizationRevision;
      assert.equal(saved.visualizationRevision.toUpperCase(), sha(await readFile(manifest.visualizationPath)));
      for (const tree of [saved, await op('map.tree.load', visibilityMap)]) {
        const matches = flatten(tree.roots).filter(node => sameNode(node.locator, locator));
        assert.equal(matches.length, 1, `${nodeKind} must retain its exact saved membership when hidden.`);
        assert.equal(matches[0].checked, checked ? 'checked' : 'unchecked');
      }
      const visibleSources = await op('map.resultSources', { map: visibilityMap });
      assert.equal(visibleSources.activeNodes.some(node => sameNode(node.node, locator)), checked,
        `${nodeKind} remains a map member but only checked nodes may own active Result sources.`);
    }
  }
  for (let index = 0; index < watched.length; index++) {
    if (watched[index] !== manifest.visualizationPath) {
      assert.deepEqual(await readFile(watched[index]), before[index], watched[index]);
    }
  }
  report.checks.push('Result and MultiResult hide, save, reload and show without losing membership or changing project and HDF bytes; only checked nodes own active sources, and every save receipt matches the .vis hash.');
  report.state='passed';
}catch(error){report.state='failed';report.error=error.stack;process.exitCode=1;}
await writeFile(destination,JSON.stringify(report,null,2));
console.log(JSON.stringify({state:report.state,checks:report.checks.length,entries:report.entries?.map(e=>({node:e.node.nodeType,path:e.sourcePath,profile:e.profileIndex,value:e.value})),outside:report.outside?.entries,error:report.error}));
