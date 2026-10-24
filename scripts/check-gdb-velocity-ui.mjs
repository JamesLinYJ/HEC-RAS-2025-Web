import assert from 'node:assert/strict';
import { lstat, mkdir, readdir, readFile, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';

const maximumGdbBytes = 128 * 1024 * 1024;
const maximumGdbFiles = 25_000;
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex').toUpperCase();

async function realSmallGdbDirectory(configuredPath) {
  assert.ok(path.isAbsolute(configuredPath), 'HECRAS_VERIFY_GDB_SOURCE must be an absolute .gdb directory path.');
  const configuredInfo = await lstat(configuredPath);
  assert.ok(configuredInfo.isDirectory() && !configuredInfo.isSymbolicLink(), 'The configured GDB source must be a real directory, not a symlink.');
  const directory = await realpath(configuredPath);
  assert.match(path.basename(directory), /\.gdb$/i, 'The configured source directory must end in .gdb.');

  let fileCount = 0;
  let byteCount = 0;
  let hasTable = false;
  const visit = async (current) => {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const entryPath = path.join(current, entry.name);
      const info = await lstat(entryPath);
      assert.ok(!info.isSymbolicLink(), `The GDB source contains a symbolic link: ${entryPath}`);
      if (info.isDirectory()) {
        await visit(entryPath);
      } else if (info.isFile()) {
        fileCount++;
        byteCount += info.size;
        hasTable ||= /\.gdbtable$/i.test(entry.name);
        assert.ok(fileCount <= maximumGdbFiles, 'The GDB source exceeds the small-fixture file limit.');
        assert.ok(byteCount <= maximumGdbBytes, 'The GDB source exceeds the small-fixture size limit.');
      }
    }
  };
  await visit(directory);
  assert.ok(fileCount > 0 && hasTable, 'The selected directory does not contain a FileGDB table.');
  return { directory, fileCount, byteCount };
}

function svgRings(data) {
  const rings = [];
  const commands = /([ML])\s*([-+]?(?:\d*\.\d+|\d+\.?\d*)(?:e[-+]?\d+)?)\s+([-+]?(?:\d*\.\d+|\d+\.?\d*)(?:e[-+]?\d+)?)/gi;
  for (const match of String(data).matchAll(commands)) {
    if (match[1].toUpperCase() === 'M') rings.push([]);
    rings.at(-1)?.push([Number(match[2]), Number(match[3])]);
  }
  return rings.filter((ring) => ring.length >= 4 && ring.every(([x, y]) => Number.isFinite(x) && Number.isFinite(y)));
}

function pointInRing(point, ring) {
  let inside = false;
  for (let left = 0, right = ring.length - 1; left < ring.length; right = left++) {
    const [x, y] = ring[left];
    const [previousX, previousY] = ring[right];
    if ((y > point[1]) !== (previousY > point[1]) &&
        point[0] < (previousX - x) * (point[1] - y) / (previousY - y) + x) inside = !inside;
  }
  return inside;
}

function hasHole(paths) {
  return paths.some(({ data, fillRule }) => {
    if (fillRule !== 'evenodd' || (data.match(/z/gi) ?? []).length < 2) return false;
    const rings = svgRings(data);
    return rings.some((inner, index) => rings.some((outer, outerIndex) =>
      outerIndex !== index && Math.abs(signedArea(outer)) > Math.abs(signedArea(inner)) && pointInRing(inner[0], outer)));
  });
}

function signedArea(ring) {
  let area = 0;
  for (let index = 0; index < ring.length; index++) {
    const [x1, y1] = ring[index];
    const [x2, y2] = ring[(index + 1) % ring.length];
    area += x1 * y2 - x2 * y1;
  }
  return area / 2;
}

/** Exercise the actual directory chooser, persisted GDB renderer and Result velocity overlay. */
export async function checkGdbVelocityUI(helpers, fixture, report) {
  const configuredPath = process.env.HECRAS_VERIFY_GDB_SOURCE?.trim();
  assert.ok(configuredPath, 'HECRAS_VERIFY_GDB_SOURCE is required for the GDB and velocity UI acceptance.');
  const gdb = await realSmallGdbDirectory(configuredPath);
  const { evaluate, waitFor, command, setFiles } = helpers;
  const waitValue = async (expression, label, timeoutMs) => {
    await waitFor(expression, label, timeoutMs);
    return evaluate(expression);
  };
  const output = helpers.gdbOutput ?? helpers.output;
  assert.ok(output, 'The GDB UI acceptance output directory is missing.');
  await mkdir(output, { recursive: true });
  const projectVisPath = fixture.projectPath.replace(/\.ras$/i, '.vis');
  const visBefore = await readFile(projectVisPath);
  const datasetName = path.basename(gdb.directory);
  const evidence = {
    sourceDirectory: gdb.directory,
    sourceFileCount: gdb.fileCount,
    sourceBytes: gdb.byteCount,
    projectPath: fixture.projectPath,
    checks: [],
  };
  report.gdbVelocity = evidence;

  try {
    await setFiles('[data-testid=map-import-gdb-input]', [gdb.directory]);
    await waitFor("(() => {const button=document.querySelector('[data-testid=map-import-gdb]');const status=document.querySelector('.map-status-message')?.textContent??'';return Boolean(button&&!button.disabled&&status.includes('已保存 1 个工程图层来源'));})()",
      'real GDB directory upload and .vis commit', 180_000);
    const visAfter = await readFile(projectVisPath);
    assert.notDeepEqual(visAfter, visBefore, 'Importing the GDB did not commit the project .vis file.');

    for (let pass = 0; pass < 8; pass++) {
      const collapsed = await evaluate("(() => {const nodes=Array.from(document.querySelectorAll('.map-tree-expand[aria-expanded=false]'));nodes.forEach(node=>node.click());return nodes.length;})()");
      if (!collapsed) break;
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    const tree = await waitValue(`(() => {const nodes=Array.from(document.querySelectorAll('.map-backend-tree li[role=treeitem]'));const root=nodes.find(node=>node.querySelector(':scope > .map-tree-row .map-tree-name')?.getAttribute('title')===${JSON.stringify(datasetName)});const children=root?.querySelector(':scope > ul[role=group]');const checkbox=root?.querySelector(':scope > .map-tree-row input[type=checkbox]');return root&&children?.children.length&&checkbox?.checked?{children:children.children.length}:false;})()`,
      'saved GDB dataset and vector child nodes', 90_000);
    assert.ok(tree.children > 0, 'The persisted GDB dataset has no source layer rows.');

    const features = await waitValue("(() => {const nodes=Array.from(document.querySelectorAll('svg.map-canvas g.map-feature')).filter(node=>(node.dataset.layerId??'').startsWith('persisted-gdb-vector:'));return nodes.length?nodes.map(node=>({id:node.dataset.featureId,layerID:node.dataset.layerId,paths:Array.from(node.querySelectorAll('.map-feature-path')).map(path=>({data:path.getAttribute('d')??'',fillRule:path.getAttribute('fill-rule')}))})):false;})()",
      'real persisted GDB vector features on the map canvas', 120_000);
    assert.ok(features.length > 0 && features.every((feature) => feature.id && feature.layerID), 'GDB canvas features are missing source feature identities.');
    const holeFeature = features.find((feature) => hasHole(feature.paths));
    assert.ok(holeFeature, 'No persisted GDB polygon renders an interior hole with the even-odd fill rule.');
    evidence.vectorFeatureCount = features.length;
    evidence.holeFeatureID = holeFeature.id;
    evidence.gdbTreeChildCount = tree.children;
    evidence.visRevisionBefore = sha(visBefore);
    evidence.visRevisionAfter = sha(visAfter);
    evidence.checks.push('A real .gdb directory was uploaded through the webkitdirectory chooser, committed into .vis, and rendered as persisted GDB vector features with an interior polygon hole.');

    const chineseAttributes = {};
    for (const feature of features) {
      await evaluate(`(() => {const node=Array.from(document.querySelectorAll('svg.map-canvas g.map-feature')).find(item=>(item.dataset.layerId??'').startsWith('persisted-gdb-vector:')&&item.dataset.featureId===${JSON.stringify(feature.id)}&&item.dataset.layerId===${JSON.stringify(feature.layerID)});node?.dispatchEvent(new MouseEvent('click',{bubbles:true}));})()`);
      await waitFor(`(() => {const selected=document.querySelector('.map-feature.is-selected');return selected?.dataset.featureId===${JSON.stringify(feature.id)}&&selected?.dataset.layerId===${JSON.stringify(feature.layerID)}&&Boolean(document.querySelector('.map-inspector-pane .map-feature-properties'));})()`,
        'selected real GDB feature attributes', 20_000);
      const attributes = await evaluate("Array.from(document.querySelectorAll('.map-inspector-pane .map-feature-properties .map-property-row')).map(row=>({key:row.querySelector('span')?.textContent??'',value:row.querySelector('code')?.textContent??''}))");
      for (const fieldName of ['测站名称', '地类说明']) {
        const matching = attributes.find((attribute) => attribute.key === fieldName);
        if (matching) chineseAttributes[fieldName] ??= { featureID: feature.id, value: matching.value };
      }
      if (chineseAttributes['测站名称'] && chineseAttributes['地类说明']) break;
    }
    assert.ok(chineseAttributes['测站名称'], 'The real SurveyPoints GDB feature does not expose its 测站名称 field in the inspector.');
    assert.ok(chineseAttributes['地类说明'], 'The real ParcelWithHole GDB feature does not expose its 地类说明 field in the inspector.');
    assert.equal(chineseAttributes['地类说明'].featureID, holeFeature.id, 'The GDB polygon with an interior hole is not the ParcelWithHole feature exposing 地类说明.');
    evidence.chineseAttributes = chineseAttributes;
    evidence.checks.push('Selecting real SurveyPoints and ParcelWithHole GDB features shows the 测站名称 and 地类说明 fields in the inspector.');

    const result = report.mapResult;
    assert.ok(result?.resultPath && /^[a-f0-9]{64}$/i.test(result.resultRevision ?? ''), 'The current UI report has no completed Result path and revision for velocity validation.');
    const nativeSteps = await evaluate("Array.from(document.querySelectorAll('datalist option[data-testid^=map-result-step-]')).map(option=>({value:option.value,profileIndex:option.dataset.profileIndex,label:option.label}))");
    assert.ok(nativeSteps.length > 0, 'The current Result has no native profile steps.');
    const lastStep = nativeSteps.at(-1);
    const finalProfile = Number(lastStep.profileIndex);
    assert.ok(Number.isInteger(finalProfile) && finalProfile >= 0, 'The last native Result profile index is invalid.');
    await evaluate(`(() => {const input=document.querySelector('[data-testid^=map-result-profile-]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,${JSON.stringify(lastStep.value)});input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));})()`);
    await waitFor(`(() => {const frame=document.querySelector('svg.map-canvas image[data-source-kind=result]');return frame?.dataset.sourcePath===${JSON.stringify(result.resultPath)}&&frame?.dataset.profileIndex===${JSON.stringify(String(finalProfile))};})()`,
      'final native Result profile on the current map', 90_000);

    const setting = await evaluate("(() => {const input=document.querySelector('[data-testid=map-show-velocity-arrows]');return input?{checked:input.checked}:null;})()");
    assert.ok(setting, 'The Result velocity arrow setting is missing.');
    assert.equal(setting.checked, false, 'Result velocity arrows must be off by default.');
    await evaluate("document.querySelector('[data-testid=map-show-velocity-arrows]').click()");
    const currentArrows = await waitValue(`(() => {const group=document.querySelector('[data-testid=map-velocity-arrows]');const arrow=group?.querySelector('[data-testid=map-velocity-arrow]');return group&&arrow&&group.dataset.revision?.toUpperCase()===${JSON.stringify(result.resultRevision.toUpperCase())}&&group.dataset.profileIndex===${JSON.stringify(String(finalProfile))}?{profile:group.dataset.profileIndex,revision:group.dataset.revision,arrowCount:group.querySelectorAll('[data-testid=map-velocity-arrow]').length,arrow:{worldX:arrow.dataset.worldX,worldY:arrow.dataset.worldY,vx:arrow.dataset.vx,vy:arrow.dataset.vy,magnitude:arrow.dataset.magnitude,profile:arrow.dataset.profileIndex,revision:arrow.dataset.revision,transform:arrow.getAttribute('transform')}}:false;})()`,
      'nonzero velocity arrows for the final Result profile and matching capture revision', 120_000);
    const vector = Object.fromEntries(['worldX', 'worldY', 'vx', 'vy', 'magnitude'].map((key) => [key, Number(currentArrows.arrow[key])]));
    assert.ok(Object.values(vector).every(Number.isFinite), 'The rendered arrow has non-finite world coordinates or velocity components.');
    assert.ok(Math.hypot(vector.vx, vector.vy) > 0 && vector.magnitude > 0, 'The real Result velocity vector is zero.');
    assert.equal(currentArrows.arrow.profile, String(finalProfile));
    assert.equal(currentArrows.arrow.revision.toUpperCase(), result.resultRevision.toUpperCase());
    const angleMatch = /rotate\(([-+\d.e]+)\)/i.exec(currentArrows.arrow.transform ?? '');
    assert.ok(angleMatch, 'The rendered velocity arrow is missing its direction transform.');
    const angle = Number(angleMatch[1]);
    const expectedAngle = Math.atan2(-vector.vy, vector.vx) * 180 / Math.PI;
    const angleError = Math.abs(((angle - expectedAngle + 540) % 360) - 180);
    assert.ok(angleError < 1e-6, 'The rendered arrow direction does not match its captured vx/vy components.');
    evidence.velocity = { resultPath: result.resultPath, resultRevision: currentArrows.revision,
      profileIndex: finalProfile, profileLabel: lastStep.label, arrowCount: currentArrows.arrowCount,
      arrow: vector, angleDegrees: angle, expectedAngleDegrees: expectedAngle };
    evidence.checks.push('The default-off setting loads real nonzero Result velocity for the final native profile, matches capture revision/profile, and draws an arrow in the vx/vy direction.');

    const screenshot = await command('Page.captureScreenshot', { format: 'png' });
    const screenshotPath = path.join(output, 'electron-map-gdb-velocity.png');
    await writeFile(screenshotPath, Buffer.from(screenshot.data, 'base64'));
    evidence.screenshot = screenshotPath;
    await evaluate("document.querySelector('[data-testid=map-show-velocity-arrows]').click()");
    await waitFor("!document.querySelector('[data-testid=map-show-velocity-arrows]')?.checked&&!document.querySelector('[data-testid=map-velocity-arrow]')", 'turning off and clearing Result velocity arrows');
    evidence.arrowsClearedWhenDisabled = true;
    evidence.checks.push('The actual map canvas screenshot records the Result arrows; turning the setting off clears the arrows.');
    report.checks.push(...evidence.checks.map((check) => `GDB and velocity: ${check}`));
  } catch (error) {
    evidence.error = error instanceof Error ? error.message : String(error);
    throw error;
  } finally {
    await writeFile(path.join(output, 'electron-map-gdb-velocity-report.json'), `${JSON.stringify(evidence, null, 2)}\n`);
  }
}
