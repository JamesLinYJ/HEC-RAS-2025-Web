import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { access, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const temporary = await mkdtemp(path.join(tmpdir(), 'hecras-osm-builder-'));
try {
  const entry = path.join(root, 'frontend/src/features/map/query-import/OsmQueryBuilderViewModel.ts');
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry, formats: ['es'], fileName: () => 'builder.mjs' },
    outDir: path.join(temporary, 'js'), minify: false,
  } });
  const { buildOsmTemplateQl, osmBuilderFromPreset, OSM_PRESETS } = await import(pathToFileURL(path.join(temporary, 'js/builder.mjs')).href);

  const clause = (flags, filters = []) => ({ id: 'clause', includeNodes: !!(flags & 1), includeWays: !!(flags & 2), includeRelations: !!(flags & 4), filters });
  const filter = (operator, key = '  name:"quoted"\\path  ', value = '  value "quoted"\\path  ') => ({ id: 'filter', key, operator, value });
  const builder = clauses => ({ label: 'Fixture', timeoutSeconds: 180, clauses });
  const cases = [
    ...['exists', 'notExists', 'equals', 'notEquals', 'matches', 'notMatches'].map(operator => builder([clause(7, [filter(operator)])])),
    builder([clause(2, [filter('oneOf', 'name', ' a.b ; x+y ; name with space ; (alpha) ; #tag ; foo\\bar ; "quoted" ')])]),
    ...Array.from({ length: 7 }, (_, index) => builder([clause(index + 1, [filter('equals', 'highway', 'primary')])])),
    builder([clause(3, [filter('exists', ' waterway '), filter('notEquals', '  name  ', '  a\\"b  ')]), clause(4, [filter('matches', 'type', '^(water|land)$')])]),
    builder([clause(7, [filter('equals', '  ', '')])]),
    builder([clause(2, [filter('equals', '\u0085key\u0085', '\u0085value\u0085')])]),
    builder([clause(2, [filter('equals', '\ufeff', '\ufeff')])]),
    builder([clause(2, [filter('equals', '\u0085', '')])]),
    builder([clause(2, [filter('oneOf', 'name', '\u0085a\u0085;\ufeff')])]),
    builder([clause(2, [filter('oneOf', 'name', 'a\u000bb;a]b;a}b;a-b;a\u0085b;a\u2003b')])]),
    builder([]),
    builder([clause(0, [filter('exists')])]),
    builder([clause(2, [filter('equals', 'name', '  ')])]),
    builder([clause(2, [filter('oneOf', 'name', ' ;  ; ')])]),
  ];
  await writeFile(path.join(temporary, 'cases.json'), JSON.stringify(cases));

  // Compile the actual C# query/preset sources with only the unused Extent dependency stubbed.
  const source = path.join(root, 'backend/Source/Geospatial.IO/Geospatial.IO.Network.OSM');
  const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');
  const names = ['OsmPresets', 'OsmPreset', 'OverpassQl', 'OverpassQuery', 'OverpassClause', 'OsmTagFilter', 'OsmTagOperator', 'OsmElementKinds'];
  const includes = names.map(name => `<Compile Include="${xml(path.join(source, `${name}.cs`))}" />`).join('\n');
  await writeFile(path.join(temporary, 'compare.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup><ItemGroup>${includes}</ItemGroup></Project>`);
  await writeFile(path.join(temporary, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>');
  await writeFile(path.join(temporary, 'Extent.cs'), 'namespace Geospatial.Vectors; public struct Extent { public double MinX, MinY, MaxX, MaxY; public bool IsFiniteAndValid() => true; }');
  await writeFile(path.join(temporary, 'Program.cs'), `
using System.Text.Json;
using Geospatial.IO.Network.OSM;

static object Output(OverpassQuery query) {
    bool ok = query.TryBuildTemplateQl(out string ql, out string error);
    return new { ok, ql, error };
}
static OsmTagOperator Operator(string name) => name switch {
    "exists" => OsmTagOperator.Exists, "notExists" => OsmTagOperator.NotExists,
    "equals" => OsmTagOperator.Equals, "notEquals" => OsmTagOperator.NotEquals,
    "matches" => OsmTagOperator.Matches, "notMatches" => OsmTagOperator.NotMatches,
    "oneOf" => OsmTagOperator.OneOf, _ => throw new ArgumentException(name)
};
var presets = OsmPresets.All.Select(p => {
    var query = p.CreateQuery();
    return new { name = p.Name, description = p.Description, label = query.Label, output = Output(query) };
}).ToArray();
using var json = JsonDocument.Parse(File.ReadAllText(args[0]));
var cases = json.RootElement.EnumerateArray().Select(item => {
    var query = new OverpassQuery { Label = item.GetProperty("label").GetString(), TimeoutSeconds = item.GetProperty("timeoutSeconds").GetInt32() };
    foreach (var entry in item.GetProperty("clauses").EnumerateArray()) {
        var kinds = OsmElementKinds.None;
        if (entry.GetProperty("includeNodes").GetBoolean()) kinds |= OsmElementKinds.Node;
        if (entry.GetProperty("includeWays").GetBoolean()) kinds |= OsmElementKinds.Way;
        if (entry.GetProperty("includeRelations").GetBoolean()) kinds |= OsmElementKinds.Relation;
        var clause = new OverpassClause { Kinds = kinds };
        foreach (var f in entry.GetProperty("filters").EnumerateArray()) {
            string key = f.GetProperty("key").GetString();
            if (!string.IsNullOrWhiteSpace(key)) clause.Filters.Add(new OsmTagFilter(key, Operator(f.GetProperty("operator").GetString()), f.GetProperty("value").GetString()));
        }
        query.Clauses.Add(clause);
    }
    return Output(query);
}).ToArray();
Console.Write(JsonSerializer.Serialize(new { presets, cases }));
`);
  const env = { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1' };
  execFileSync('dotnet', ['build', 'compare.csproj', '-c', 'Release', '--nologo', '-v', 'quiet'], { cwd: temporary, env, stdio: 'pipe' });
  const actual = JSON.parse(execFileSync('dotnet', [path.join(temporary, 'bin/Release/net10.0/compare.dll'), path.join(temporary, 'cases.json')], { cwd: temporary, env, encoding: 'utf8' }));

  assert.equal(OSM_PRESETS.length, 14);
  assert.equal(actual.presets.length, OSM_PRESETS.length);
  for (let index = 0; index < OSM_PRESETS.length; index++) {
    const preset = OSM_PRESETS[index];
    const original = actual.presets[index];
    assert.equal(preset.nameEn, original.name);
    assert.equal(preset.descriptionEn, original.description);
    assert.equal(preset.label, original.label);
    assert.ok(preset.nameZh && preset.descriptionZh);
    assert.equal(buildOsmTemplateQl(osmBuilderFromPreset(preset)).ql, original.output.ql, preset.id);
  }
  cases.forEach((input, index) => {
    const result = buildOsmTemplateQl(input);
    assert.equal(result.ql !== null, actual.cases[index].ok, `case ${index} validity`);
    assert.equal(result.ql, actual.cases[index].ql, `case ${index} QL`);
  });
  assert.deepEqual(cases[0].clauses[0].filters[0], filter('exists'), 'generation must not modify raw tag input');
  console.log('PASS: 14 C# presets, seven tag operators, escaped literals/regex, every kind combination, multiple clauses, blank filters and validation match the actual C# template generator.');
  if (process.argv.includes('--browser')) await verifyBrowser(temporary);
} finally {
  // Chromium helpers can finish writing their temporary profile after the browser exits.
  await rm(temporary, { recursive: true, force: true, maxRetries: 20, retryDelay: 100 });
}

async function verifyBrowser(temporary) {
  const chrome = process.env.HECRAS_CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
  await access(chrome);
  const entry = path.join(temporary, 'browser.ts');
  await writeFile(entry, `import { OSMQuerierControl } from ${JSON.stringify(path.join(root, 'frontend/src/features/map/query-import/OSMQuerierControl.tsx'))};
import { createElement, useState } from ${JSON.stringify(path.join(root, 'node_modules/react/index.js'))};
import { createRoot } from ${JSON.stringify(path.join(root, 'node_modules/react-dom/client.js'))};
export { OSMQuerierControl, createElement, useState, createRoot };`);
  await build({ configFile: false, root, logLevel: 'silent', define: { 'process.env.NODE_ENV': '"production"' }, build: {
    lib: { entry, formats: ['iife'], name: 'OsmBrowserTest', fileName: () => 'browser.js' },
    outDir: path.join(temporary, 'browser'), minify: false,
  } });
  let browser, exitPromise;
  try {
    browser = spawn(chrome, ['--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
      '--disable-component-update', '--disable-sync', '--disable-extensions', '--remote-debugging-pipe',
      `--user-data-dir=${path.join(temporary, 'profile')}`, 'about:blank'], { stdio: ['ignore', 'ignore', 'pipe', 'pipe', 'pipe'] });
    exitPromise = new Promise(resolve => browser.once('exit', resolve));
    let sequence = 0;
    const pending = new Map();
    let diagnostic = '';
    browser.stderr.on('data', bytes => { diagnostic += bytes.toString(); });
    const failPending = error => {
      for (const item of pending.values()) { clearTimeout(item.timeout); item.reject(error); }
      pending.clear();
    };
    browser.on('error', failPending);
    browser.stdio[3].on('error', failPending);
    browser.on('exit', code => failPending(new Error(`Headless Chrome exited (${code}): ${diagnostic}`)));
    let incoming = '';
    browser.stdio[4].on('data', bytes => {
      incoming += bytes.toString();
      let end;
      while ((end = incoming.indexOf('\0')) !== -1) {
        const response = JSON.parse(incoming.slice(0, end));
        incoming = incoming.slice(end + 1);
        const item = pending.get(response.id);
        if (item) { pending.delete(response.id); clearTimeout(item.timeout); response.error ? item.reject(new Error(JSON.stringify(response.error))) : item.resolve(response.result); }
      }
    });
    const command = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
      const id = ++sequence, timeout = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 30_000);
      pending.set(id, { resolve, reject, timeout }); browser.stdio[3].write(`${JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) })}\0`);
    });
    const target = await command('Target.createTarget', { url: 'about:blank' });
    const session = await command('Target.attachToTarget', { targetId: target.targetId, flatten: true });
    const evaluate = async expression => {
      const response = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, session.sessionId);
      if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
      return response.result.value;
    };
    await evaluate(`document.body.innerHTML = '<div id="root"></div>'`);
    await evaluate(await readFile(path.join(temporary, 'browser/browser.js'), 'utf8'));
    await evaluate(`(${browserChecks.toString()})(OsmBrowserTest)`);
    console.log('PASS: headless browser exercised initial preset, raw QL editing, builder update, suggestion debounce, cancellation and stale responses.');
    await command('Browser.close');
  } finally {
    if (browser && browser.exitCode === null) browser.kill('SIGTERM');
    if (exitPromise) await Promise.race([exitPromise, new Promise(resolve => setTimeout(resolve, 3000))]);
  }
}

async function browserChecks(A) {
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const check = (condition, message) => { if (!condition) throw new Error(message); };
  const requests = [];
  const backend = { supports: name => name === 'featureQuery.suggest', request: (name, parameters, options) => {
    check(name === 'featureQuery.suggest', `unexpected backend request ${name}`);
    return new Promise(resolve => requests.push({ parameters, signal: options.signal, resolve }));
  } };
  let latestValue, setDisabled, setBackendRevision;
  const h = A.createElement;
  function Fixture() {
    const [value, setValue] = A.useState({ nldLayerId: '', useBoundaryPolygon: false, polygonSourceId: '', rawQl: '', queryLabel: '' });
    const [disabled, setDisabledState] = A.useState(true);
    const [backendRevision, setBackendRevisionState] = A.useState(0);
    setDisabled = setDisabledState;
    setBackendRevision = setBackendRevisionState;
    latestValue = value;
    return h(A.OSMQuerierControl, { language: 'zh', disabled, value, onChange: patch => setValue(previous => ({ ...previous, ...patch })),
      backend, backendRevision, extent: null, polygonSources: [] });
  }
  const root = A.createRoot(document.getElementById('root'));
  root.render(h(Fixture));
  await wait(50);
  check(!latestValue.rawQl && !latestValue.queryLabel, 'initial defaults must wait for the shared snapshot');
  setDisabled(false);
  await wait(50);
  check(latestValue.queryLabel === 'Buildings' && latestValue.rawQl.includes('way["building"]({{bbox}});'), 'default preset not applied');
  const nativeSet = (element, value) => {
    const prototype = element.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : element.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, 'value').set.call(element, value);
    element.dispatchEvent(new Event(element.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
  };
  const raw = document.querySelector('textarea');
  const customQl = '  [out:json];\nnode({{bbox}}); out geom;\n';
  nativeSet(raw, customQl);
  await wait(25);
  check(latestValue.rawQl === customQl, 'raw QL must be preserved exactly');
  const preset = document.querySelector('select');
  nativeSet(preset, 'Culverts');
  await wait(25);
  check(latestValue.queryLabel === 'Culverts' && latestValue.rawQl.includes('way["tunnel"="culvert"]({{bbox}});'), 'preset did not regenerate query');
  document.querySelector('details.feature-query-osm-builder').open = true;
  const key = document.querySelector('input[aria-label="标签键"]');
  key.focus();
  nativeSet(key, 'water');
  await wait(150);
  check(requests.length === 0, 'suggestion was not debounced');
  await wait(200);
  check(requests.length === 1 && requests[0].parameters.value === 'water' && requests[0].parameters.isKey, 'wrong first suggestion request');
  nativeSet(key, 'waterway');
  check(requests[0].signal.aborted, 'old request was not cancelled on change');
  requests[0].resolve({ suggestions: ['STALE-KEY'] });
  await wait(25);
  check(![...document.querySelectorAll('datalist option')].some(option => option.value === 'STALE-KEY'), 'stale key suggestion displayed');
  await wait(325);
  check(requests.length === 2 && requests[1].parameters.value === 'waterway', 'new suggestion was not requested');
  requests[1].resolve({ suggestions: ['waterway'] });
  await wait(25);
  check(document.querySelector('datalist option')?.value === 'waterway', 'latest key suggestion missing');
  check(latestValue.rawQl.includes('["waterway"="culvert"]'), 'builder edit did not regenerate QL');

  nativeSet(key, 'stream');
  await wait(350);
  check(requests.length === 3 && requests[2].parameters.value === 'stream', 'third suggestion was not requested');
  setBackendRevision(1);
  await wait(25);
  check(requests[2].signal.aborted, 'backend revision did not cancel the old request');
  requests[2].resolve({ suggestions: ['STALE-BACKEND'] });
  await wait(25);
  check(![...document.querySelectorAll('datalist option')].some(option => option.value === 'STALE-BACKEND'), 'old backend suggestion displayed');
  const value = document.querySelector('input[aria-label="标签值"]');
  value.focus();
  nativeSet(value, 'can');
  await wait(350);
  check(requests.length === 4 && !requests[3].parameters.isKey && requests[3].parameters.key === 'stream'
    && requests[3].parameters.value === 'can', 'value suggestion did not use current key');
  setDisabled(true);
  await wait(25);
  check(requests[3].signal.aborted, 'disabling the control did not cancel its request');
  requests[3].resolve({ suggestions: ['STALE-VALUE'] });
  await wait(25);
  check(![...document.querySelectorAll('datalist option')].some(option => option.value === 'STALE-VALUE'), 'disabled field received a stale suggestion');
  setDisabled(false);
  await wait(25);
  nativeSet(preset, 'Buildings');
  await wait(25);
  check(latestValue.rawQl.includes('way["building"]({{bbox}});'), 'switching preset failed');
  const buildingKey = document.querySelector('input[aria-label="标签键"]');
  buildingKey.focus();
  nativeSet(buildingKey, 'water');
  await wait(350);
  check(requests.length === 5, 'replacement field did not request suggestions');
  nativeSet(preset, 'Culverts');
  await wait(25);
  check(requests[4].signal.aborted, 'removing a filter did not cancel its request');
  requests[4].resolve({ suggestions: ['STALE-REMOVED'] });
  await wait(25);
  check(![...document.querySelectorAll('datalist option')].some(option => option.value === 'STALE-REMOVED'), 'removed field received a stale suggestion');

  // An invalid builder must not send even a comment as a seemingly nonempty query.
  const comparison = document.querySelector('select[aria-label="标签比较方式"]');
  const missingValue = document.querySelector('input[aria-label="标签值"]');
  check(comparison && missingValue, 'missing expected preset filter');
  nativeSet(missingValue, '');
  await wait(25);
  check(latestValue.rawQl === '' && document.querySelector('[role="alert"]')?.textContent.includes('需要填写值'), 'invalid builder remained queryable');
  const cancelledKey = document.querySelector('input[aria-label="标签键"]');
  cancelledKey.focus();
  nativeSet(cancelledKey, 'cancelled');
  await wait(350);
  check(requests.length === 6, 'cancellation receipt request missing');
  requests[5].resolve({ state: 'canceled', cleanupComplete: true });
  await wait(25);
  check(!document.body.textContent.includes('标签建议暂不可用'), 'canceled operation was reported as a suggestion failure');
  root.unmount();
}
