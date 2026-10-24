import assert from 'node:assert/strict';
import { lstat, mkdir, readdir, readFile, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';

const maximumGdbBytes = 128 * 1024 * 1024;
const maximumGdbFiles = 25_000;
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex').toUpperCase();

const floatRasterSpecs = [
  ['float16_lz77', 'Float32', 32], ['float24_lz77', 'Float32', 32], ['float32_lz77', 'Float32', 32], ['float64_lz77', 'Float64', 64],
  ['float16_lerc', 'Float32', 32], ['float24_lerc', 'Float32', 32], ['float32_lerc', 'Float32', 32],
  ['float16_lzw', 'Float32', 32], ['float24_lzw', 'Float32', 32], ['float32_lzw', 'Float32', 32],
].map(([name, sampleType, bits]) => ({ name, sampleType, bits, width: 20, height: 20, bands: 1 }));

const integerRasterSpecs = [
  ['byte_lz77', 'Byte', 8], ['int10_lz77', 'UInt16', 16], ['int12_lz77', 'UInt16', 16],
  ['int16_lz77', 'Int16', 16], ['int24_lz77', 'UInt32', 32], ['int32_lz77', 'Int32', 32],
  ['uint16_lz77', 'UInt16', 16], ['uint32_lz77', 'UInt32', 32],
  ['byte_lzw', 'Byte', 8], ['int10_lzw', 'UInt16', 16], ['int12_lzw', 'UInt16', 16],
  ['int16_lzw', 'Int16', 16], ['int24_lzw', 'UInt32', 32], ['int32_lzw', 'Int32', 32],
  ['uint16_lzw', 'UInt16', 16], ['uint32_lzw', 'UInt32', 32],
  ['byte_nodata_123_lz77', 'Byte', 8], ['byte_nodata_107_lz77', 'Byte', 8],
  ['byte_nodata_123_lzw', 'Byte', 8], ['byte_nodata_107_lzw', 'Byte', 8],
  ['byte_nodata_123_lerc', 'Byte', 8], ['byte_nodata_107_lerc', 'Byte', 8],
  ['byte_nodata_123_jpeg', 'Byte', 8], ['byte_nodata_107_jpeg', 'Byte', 8],
  ['byte_nodata_123_jpeg2000', 'Byte', 8], ['byte_nodata_107_jpeg2000', 'Byte', 8],
  ['byte_nodata_123_none', 'Byte', 8], ['byte_nodata_107_none', 'Byte', 8],
].map(([name, sampleType, bits]) => ({ name, sampleType, bits, width: 20, height: 20, bands: 1 }));

const rgbRasterSpecs = [
  ['small_world_lz77', 'Byte'],
  ['small_world_jpeg', 'Byte'],
  ['small_world_jpeg2000', 'Byte'],
].map(([name, sampleType]) => ({ name, sampleType, bits: 8, width: 400, height: 200, bands: 3 }));

const rgbaRasterSpecs = [
  // Fixed alpha-band histogram from the original GDAL-readable fixture; RGB may be lossy, alpha is asserted exactly.
  ['stefan_full_rgba_with_fake_georef_lz77', { transparent: 11780, partial: 6456, opaque: 6064 }],
  ['stefan_full_rgba_with_fake_georef_jpeg', { transparent: 11260, partial: 8217, opaque: 4823 }],
  ['stefan_full_rgba_with_fake_georef_jpeg2000', { transparent: 10625, partial: 8764, opaque: 4911 }],
].map(([name, expectedAlpha]) => ({
  name,
  sampleType: 'Byte',
  bits: 8,
  width: 162,
  height: 150,
  bands: 4,
  expectedAlpha,
}));

const expectedRasterSpecs = [...floatRasterSpecs, ...integerRasterSpecs, ...rgbRasterSpecs, ...rgbaRasterSpecs];
const expectedRasterNames = expectedRasterSpecs.map(({ name }) => name).sort();
const expectedRasterDistribution = {
  total: 44,
  singleBand: { Float32: 9, Float64: 1, Byte: 14, Int16: 2, UInt16: 6, Int32: 2, UInt32: 4 },
  rgb: { Byte: 3, bandsPerImage: 3 },
  rgba: { Byte: 3, bandsPerImage: 4 },
};

const fixedRasterDistribution = {
  total: expectedRasterSpecs.length,
  singleBand: Object.fromEntries([...new Set([...floatRasterSpecs, ...integerRasterSpecs].map(({ sampleType }) => sampleType))]
    .map((sampleType) => [sampleType, [...floatRasterSpecs, ...integerRasterSpecs].filter((spec) => spec.sampleType === sampleType).length])),
  rgb: { Byte: rgbRasterSpecs.length, bandsPerImage: 3 },
  rgba: { Byte: rgbaRasterSpecs.length, bandsPerImage: 4 },
};
assert.deepEqual(fixedRasterDistribution, expectedRasterDistribution, 'The fixed raster acceptance roster no longer matches the checked-in GDAL fixture distribution.');

function rasterTitle(title) {
  const match = /^Raster\s+(\S+)\s+\[(\d+)x(\d+)x(\d+)\],\s+(\d+)\s+bits$/i.exec(title);
  if (!match) return null;
  return { name: match[1], width: Number(match[2]), height: Number(match[3]), bands: Number(match[4]), bits: Number(match[5]) };
}

async function realGdbDirectory(configuredPath) {
  assert.ok(path.isAbsolute(configuredPath), 'HECRAS_VERIFY_GDB_RASTER_SOURCE must be an absolute .gdb directory path.');
  const configuredInfo = await lstat(configuredPath);
  assert.ok(configuredInfo.isDirectory() && !configuredInfo.isSymbolicLink(), 'The GDB raster source must be a real directory, not a symlink.');
  const directory = await realpath(configuredPath);
  assert.match(path.basename(directory), /\.gdb$/i, 'The configured raster source must end in .gdb.');

  let fileCount = 0;
  let byteCount = 0;
  let hasTable = false;
  const visit = async (current) => {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const entryPath = path.join(current, entry.name);
      const info = await lstat(entryPath);
      assert.ok(!info.isSymbolicLink(), `The GDB raster source contains a symbolic link: ${entryPath}`);
      if (info.isDirectory()) {
        await visit(entryPath);
      } else if (info.isFile()) {
        fileCount++;
        byteCount += info.size;
        hasTable ||= /\.gdbtable$/i.test(entry.name);
        assert.ok(fileCount <= maximumGdbFiles, 'The GDB raster source exceeds the fixture file limit.');
        assert.ok(byteCount <= maximumGdbBytes, 'The GDB raster source exceeds the fixture size limit.');
      }
    }
  };
  await visit(directory);
  assert.ok(fileCount > 0 && hasTable, 'The selected directory does not contain a FileGDB table.');
  return { directory, fileCount, byteCount };
}

async function readOptionalFile(filePath) {
  try {
    return await readFile(filePath);
  } catch (error) {
    if (error.code === 'ENOENT') return null;
    throw error;
  }
}

/** Upload and reopen a real GDAL FileGDB raster source through the current map UI. */
export async function checkGdbRasterUI(helpers, fixture, report) {
  const configuredPath = process.env.HECRAS_VERIFY_GDB_RASTER_SOURCE?.trim();
  assert.ok(configuredPath, 'HECRAS_VERIFY_GDB_RASTER_SOURCE is required for the GDB raster UI acceptance.');
  const gdb = await realGdbDirectory(configuredPath);
  const { evaluate, waitFor, command, setFiles, navigate, open, close } = helpers;
  const output = helpers.gdbRasterOutput ?? helpers.output;
  assert.ok(output, 'The GDB raster UI acceptance output directory is missing.');
  assert.equal(typeof open, 'function', 'The GDB raster acceptance needs the real project open helper.');
  assert.equal(typeof close, 'function', 'The GDB raster acceptance needs the real project close helper.');
  await mkdir(output, { recursive: true });

  const projectVisPath = fixture.projectPath.replace(/\.ras$/i, '.vis');
  const visBefore = await readOptionalFile(projectVisPath);
  const gdbName = path.basename(gdb.directory);
  const evidence = {
    sourceDirectory: gdb.directory,
    sourceFileCount: gdb.fileCount,
    sourceBytes: gdb.byteCount,
    projectPath: fixture.projectPath,
    expectedRasterDistribution,
    expectedRasterNames,
    expectedRgbaAlphaCoverage: Object.fromEntries(rgbaRasterSpecs.map(({ name, expectedAlpha }) => [name, expectedAlpha])),
    checks: [],
  };
  report.gdbRaster = evidence;

  const rasterImagesExpression = "Array.from(document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]')).map(node=>({name:node.querySelector('title')?.textContent?.trim()??'',sourcePath:node.dataset.sourcePath??'',href:node.getAttribute('href')??'',width:Number(node.getAttribute('width')),height:Number(node.getAttribute('height')),screenWidth:node.getBBox().width,screenHeight:node.getBBox().height,imageRendering:getComputedStyle(node).imageRendering}))";
  const waitValue = async (condition, expression, label, timeoutMs) => {
    await waitFor(condition, label, timeoutMs);
    return evaluate(expression);
  };
  const selectMapTab = async (name) => {
    const tabExpression = `Array.from(document.querySelectorAll('.map-document-tab-list [role=tab]')).find(tab=>tab.textContent.trim()===${JSON.stringify(name)})`;
    await waitFor(`Boolean(${tabExpression})`, `saved map tab ${name}`, 60_000);
    await evaluate(`${tabExpression}.click()`);
    await waitFor(`document.querySelector('.map-document-tab-list [role=tab][aria-selected=true]')?.textContent.trim()===${JSON.stringify(name)}`,
      `selected saved map tab ${name}`, 30_000);
  };
  const verifyRenderedRasters = async (label) => {
    const images = await waitValue(`document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]').length===${expectedRasterDistribution.total}`,
      rasterImagesExpression, `${label}: ${expectedRasterDistribution.total} persisted GDB raster frames`, 240_000);
    assert.equal(images.length, expectedRasterDistribution.total, `${label}: the selected GDB map did not render the fixed ${expectedRasterDistribution.total}-raster roster.`);
    assert.ok(images.every((image) => image.sourcePath.toLowerCase().includes(gdbName.toLowerCase())), `${label}: raster images are not tied to the configured persisted GDB directory.`);
    const parsedImages = images.map((image) => ({ ...image, raster: rasterTitle(image.name) }));
    assert.ok(parsedImages.every((image) => image.raster), `${label}: a rendered GDB raster does not have a valid catalog title.`);
    assert.equal(new Set(parsedImages.map((image) => image.raster.name)).size, expectedRasterDistribution.total,
      `${label}: the GDB map rendered duplicate raster subdatasets.`);
    assert.deepEqual(parsedImages.map((image) => image.raster.name).sort(), expectedRasterNames,
      `${label}: the rendered GDB raster names differ from the fixed expected roster.`);
    const expectedByName = new Map(expectedRasterSpecs.map((spec) => [spec.name, spec]));
    for (const image of parsedImages) {
      const expected = expectedByName.get(image.raster.name);
      assert.ok(expected, `${label}: unexpected raster subdataset ${image.raster.name}.`);
      if (expected.bands === 1 && !expected.sampleType.startsWith('Float')) {
        assert.equal(image.imageRendering, 'pixelated', `${label}: ${image.raster.name} classification cells must not blend into interpolated colors on the map.`);
      }
      for (const field of ['width', 'height', 'bands', 'bits']) {
        assert.equal(image.raster[field], expected[field], `${label}: ${image.raster.name} ${field} differs from the fixed GDAL catalog.`);
      }
    }
    const decoded = await evaluate(`(async()=>{const output=[];for(const node of Array.from(document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]'))){const href=node.getAttribute('href')??'';const image=new Image();image.src=href;await image.decode();const canvas=document.createElement('canvas');canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;const context=canvas.getContext('2d');if(!context)throw new Error('Could not decode a GDB raster frame into a 2D canvas.');context.drawImage(image,0,0);const pixels=context.getImageData(0,0,image.naturalWidth,image.naturalHeight).data;let opaque=0;let nonTransparent=0;let transparent=0;let partialAlpha=0;const colors=new Set();for(let index=0;index<pixels.length;index+=4){const alpha=pixels[index+3];if(alpha===255)opaque++;if(alpha>0){nonTransparent++;colors.add((pixels[index]<<16)|(pixels[index+1]<<8)|pixels[index+2]);if(alpha<255)partialAlpha++;}else transparent++;}const bounds=node.getBBox();output.push({name:node.querySelector('title')?.textContent?.trim()??'',png:href.startsWith('data:image/png;base64,'),pixelWidth:image.naturalWidth,pixelHeight:image.naturalHeight,opaque,nonTransparent,transparent,partialAlpha,distinctColors:colors.size,screenWidth:bounds.width,screenHeight:bounds.height,sourcePath:node.dataset.sourcePath??''});}return output;})()`);
    assert.equal(decoded.length, expectedRasterDistribution.total, `${label}: not all ${expectedRasterDistribution.total} GDB raster PNG frames decoded.`);
    assert.ok(decoded.every((image) => image.png && image.pixelWidth > 0 && image.pixelHeight > 0), `${label}: one or more persisted GDB frames are not decoded PNG images.`);
    const decodedByName = new Map(decoded.map((image) => [rasterTitle(image.name)?.name, image]));
    for (const spec of expectedRasterSpecs) {
      const image = decodedByName.get(spec.name);
      assert.ok(image, `${label}: ${spec.name} did not decode as a GDB PNG image.`);
      assert.equal(image.pixelWidth, spec.width, `${label}: ${spec.name} PNG width differs from the fixed GDB raster.`);
      assert.equal(image.pixelHeight, spec.height, `${label}: ${spec.name} PNG height differs from the fixed GDB raster.`);
      assert.ok(image.screenWidth > 0 && image.screenHeight > 0, `${label}: ${spec.name} has no visible map extent.`);
      assert.equal(image.opaque + image.partialAlpha + image.transparent, image.pixelWidth * image.pixelHeight,
        `${label}: ${spec.name} PNG alpha accounting is incomplete.`);
      if (spec.bands === 1) {
        // NoData cells and discrete values without a color-table entry may be transparent.
        // Require a visible sample, but deliberately do not require complete opacity.
        assert.ok(image.nonTransparent > 0, `${label}: ${spec.name} has no visible single-band pixels.`);
      } else if (spec.bands === 3) {
        assert.equal(image.opaque, image.pixelWidth * image.pixelHeight, `${label}: RGB image ${spec.name} must render fully opaque.`);
        assert.equal(image.partialAlpha, 0, `${label}: RGB image ${spec.name} contains partially transparent pixels.`);
        assert.ok(image.distinctColors >= 16, `${label}: RGB image ${spec.name} has too little decoded pixel diversity (${image.distinctColors} colors).`);
      } else {
        assert.equal(spec.bands, 4, `${label}: an image raster has an unexpected band count.`);
        assert.deepEqual({ transparent: image.transparent, partial: image.partialAlpha, opaque: image.opaque }, spec.expectedAlpha,
          `${label}: RGBA image ${spec.name} alpha-channel histogram differs from its fixed original GDAL band-4 values.`);
      }
    }
    return { images, decoded };
  };
  const expandMapTree = async (label) => {
    for (let pass = 0; pass < 32; pass++) {
      const collapsed = await evaluate("(() => {const nodes=Array.from(document.querySelectorAll('.map-tree-expand[aria-expanded=false]'));nodes.forEach(node=>node.click());return nodes.length;})()");
      if (!collapsed) return;
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    await waitFor("document.querySelectorAll('.map-tree-expand[aria-expanded=false]').length===0", label, 30_000);
  };
  const zoomAndMeasureRaster = async (name, { minimumWidth, minimumHeight, minimumIntersectionWidth, minimumIntersectionHeight }) => {
    const spec = expectedRasterSpecs.find((candidate) => candidate.name === name);
    assert.ok(spec, `The raster zoom check references an unexpected fixture subdataset: ${name}.`);
    const title = `Raster ${spec.name} [${spec.width}x${spec.height}x${spec.bands}], ${spec.bits} bits`;
    const treeButton = `Array.from(document.querySelectorAll('.map-tree-name')).find(button=>button.title===${JSON.stringify(title)})`;
    await waitFor(`Boolean(${treeButton})`, `real map-tree row for ${name}`, 60_000);
    await evaluate(`(()=>{const button=${treeButton};if(!button)return false;button.click();return true;})()`);
    await waitFor(`${treeButton}?.closest('[role=treeitem]')?.getAttribute('aria-selected')==='true'`, `selected map-tree row for ${name}`, 30_000);
    const doubleClickResult = await evaluate(`(()=>{const button=${treeButton};return button?.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true,view:window}))??false;})()`);
    assert.equal(doubleClickResult, true, `${name}: the actual map-tree double-click event was not dispatched.`);

    const measurement = `(()=>{const title=${JSON.stringify(title)};const image=Array.from(document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]')).find(node=>node.querySelector('title')?.textContent?.trim()===title);const canvas=document.querySelector('svg.map-canvas');if(!image||!canvas)return null;const imageRect=image.getBoundingClientRect();const canvasRect=canvas.getBoundingClientRect();const overlapWidth=Math.max(0,Math.min(imageRect.right,canvasRect.right)-Math.max(imageRect.left,canvasRect.left));const overlapHeight=Math.max(0,Math.min(imageRect.bottom,canvasRect.bottom)-Math.max(imageRect.top,canvasRect.top));const style=getComputedStyle(image);return {title,canvasWidth:canvasRect.width,canvasHeight:canvasRect.height,imageWidth:imageRect.width,imageHeight:imageRect.height,intersectionWidth:overlapWidth,intersectionHeight:overlapHeight,opacity:Number(style.opacity),visibility:style.visibility,href:image.getAttribute('href')??''};})()`;
    const visibleEnough = `(()=>{const m=${measurement};return Boolean(m&&m.imageWidth>=${minimumWidth}&&m.imageHeight>=${minimumHeight}&&m.intersectionWidth>=${minimumIntersectionWidth}&&m.intersectionHeight>=${minimumIntersectionHeight}&&m.imageWidth<=m.canvasWidth+1&&m.imageHeight<=m.canvasHeight+1&&m.intersectionWidth>=m.imageWidth-1&&m.intersectionHeight>=m.imageHeight-1&&m.opacity>0&&m.visibility!=='hidden'&&m.href.startsWith('data:image/png;base64,'));})()`;
    await waitFor(visibleEnough, `${name} zoom-to-layer visible in map viewport`, 60_000);
    const result = await evaluate(measurement);
    assert.ok(result, `${name}: no rendered raster element remained after zoom-to-layer.`);
    assert.ok(result.imageWidth >= minimumWidth && result.imageHeight >= minimumHeight,
      `${name}: zoom-to-layer left a small screen bounding box (${result.imageWidth}×${result.imageHeight}px).`);
    assert.ok(result.intersectionWidth >= minimumIntersectionWidth && result.intersectionHeight >= minimumIntersectionHeight,
      `${name}: the raster has too little visible intersection with the map viewport (${result.intersectionWidth}×${result.intersectionHeight}px).`);
    assert.ok(result.imageWidth <= result.canvasWidth + 1 && result.imageHeight <= result.canvasHeight + 1 &&
      result.intersectionWidth >= result.imageWidth - 1 && result.intersectionHeight >= result.imageHeight - 1,
      `${name}: zoom-to-layer did not fit the whole raster within the canvas.`);
    assert.ok(result.opacity > 0 && result.visibility !== 'hidden', `${name}: the raster is not visibly painted in the map viewport.`);
    assert.ok(result.href.startsWith('data:image/png;base64,'), `${name}: the map element is not showing the decoded PNG raster frame.`);
    const { href, ...measurementEvidence } = result;
    return {
      ...measurementEvidence,
      pngSHA256: sha(Buffer.from(href.slice('data:image/png;base64,'.length), 'base64')),
    };
  };
  const saveMapScreenshot = async (fileName) => {
    // Geometry confirmation precedes this bounded paint interval: a stale
    // compositor surface must not stand in for the selected layer's screenshot.
    await new Promise((resolve) => setTimeout(resolve, 150));
    const screenshot = await command('Page.captureScreenshot', { format: 'png' });
    const screenshotPath = path.join(output, fileName);
    await writeFile(screenshotPath, Buffer.from(screenshot.data, 'base64'));
    return screenshotPath;
  };
  const rasterTitleFor = (name) => {
    const spec = expectedRasterSpecs.find((candidate) => candidate.name === name);
    assert.ok(spec, `The raster visibility check references an unexpected fixture subdataset: ${name}.`);
    return `Raster ${spec.name} [${spec.width}x${spec.height}x${spec.bands}], ${spec.bits} bits`;
  };
  const readRasterVisibility = async (label) => {
    const rows = await evaluate("Array.from(document.querySelectorAll('.map-tree-name[title^=\"Raster \"]')).map(button=>({title:button.title,checked:button.closest('.map-tree-row')?.querySelector('input[type=checkbox]')?.checked??null,disabled:button.closest('.map-tree-row')?.querySelector('input[type=checkbox]')?.disabled??null}))");
    const parsed = rows.map((row) => ({ ...row, raster: rasterTitle(row.title) }));
    assert.equal(parsed.length, expectedRasterDistribution.total, `${label}: the expanded map tree must expose all ${expectedRasterDistribution.total} raster rows.`);
    assert.ok(parsed.every((row) => row.raster && typeof row.checked === 'boolean'), `${label}: a raster row is missing its real visibility checkbox.`);
    assert.deepEqual(parsed.map((row) => row.raster.name).sort(), expectedRasterNames, `${label}: raster visibility rows differ from the fixed 44-item roster.`);
    return new Map(parsed.map((row) => [row.raster.name, row]));
  };
  const assertRasterVisibility = async (visibleNames, label) => {
    const visibility = await readRasterVisibility(label);
    const expected = [...visibleNames].sort();
    const actual = [...visibility].filter(([, row]) => row.checked).map(([name]) => name).sort();
    assert.deepEqual(actual, expected, `${label}: checked raster rows do not match the expected visible set.`);
    return visibility;
  };
  const waitForRasterCanvas = async (visibleNames, label) => {
    const expectedTitles = [...visibleNames].map(rasterTitleFor).sort();
    const titlesExpression = "Array.from(document.querySelectorAll('svg.map-canvas image[data-source-kind=raster]')).map(node=>node.querySelector('title')?.textContent?.trim()??'').sort()";
    const matches = `(()=>{const actual=${titlesExpression};return JSON.stringify(actual)===JSON.stringify(${JSON.stringify(expectedTitles)});})()`;
    await waitFor(matches, `${label}: canvas raster images match saved checkbox state`, 60_000);
    const actualTitles = await evaluate(titlesExpression);
    assert.deepEqual(actualTitles, expectedTitles, `${label}: rendered canvas raster images differ from the expected checkbox state.`);
    return actualTitles;
  };
  const waitForVisRevisionChange = async (beforeHash, label) => {
    const deadline = Date.now() + 60_000;
    while (Date.now() < deadline) {
      const bytes = await readFile(projectVisPath);
      if (sha(bytes) !== beforeHash) return bytes;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(`${label}: the real map visibility action did not change the saved .vis file within 60 seconds.`);
  };
  const toggleRasterVisibility = async (name, checked, expectedVisibleNames, label) => {
    const title = rasterTitleFor(name);
    const beforeVisibleNames = new Set(expectedVisibleNames);
    if (checked) beforeVisibleNames.delete(name);
    else beforeVisibleNames.add(name);
    const beforeRows = await assertRasterVisibility(beforeVisibleNames, `${label}: precondition`);
    assert.equal(beforeRows.get(name)?.checked, !checked, `${label}: ${name} already has the requested visibility state.`);
    const beforeBytes = await readFile(projectVisPath);
    const beforeHash = sha(beforeBytes);
    const button = `Array.from(document.querySelectorAll('.map-tree-name')).find(candidate=>candidate.title===${JSON.stringify(title)})`;
    const checkbox = `${button}?.closest('.map-tree-row')?.querySelector('input[type=checkbox]')`;
    const clicked = await evaluate(`(()=>{const input=${checkbox};if(!input||input.disabled||input.checked===${checked})return false;input.click();return true;})()`);
    assert.equal(clicked, true, `${label}: could not click the actual map-tree checkbox for ${name}.`);

    const completed = `(()=>{const input=${checkbox};const status=document.querySelector('.map-status-message')?.textContent??'';const saved=status.includes('图层可见性已保存到地图文档。')||status.includes('Layer visibility was saved to the map document.');return Boolean(input&&input.checked===${checked}&&!input.disabled&&saved);})()`;
    const changedBytes = await waitForVisRevisionChange(beforeHash, label);
    await waitFor(completed, `${label}: backend save, checkbox readback and UI completion`, 60_000);
    const afterBytes = await readFile(projectVisPath);
    const afterHash = sha(afterBytes);
    assert.notEqual(afterHash, beforeHash, `${label}: the visibility action did not commit a new .vis revision.`);
    assert.equal(afterHash, sha(changedBytes), `${label}: the .vis file changed again after the visibility save completed.`);
    const visibility = await assertRasterVisibility(expectedVisibleNames, `${label}: postcondition`);
    assert.equal(visibility.get(name)?.checked, checked, `${label}: the target row did not retain its requested visibility state.`);
    const imageTitles = await waitForRasterCanvas(expectedVisibleNames, label);
    evidence.visibilityActions ??= [];
    evidence.visibilityActions.push({ name, checked, visBeforeSHA256: beforeHash, visAfterSHA256: afterHash, renderedRasterCount: imageTitles.length });
    return afterBytes;
  };
  const setRasterVisibility = async (requestedNames, label) => {
    const desired = new Set(requestedNames);
    assert.ok([...desired].every((name) => expectedRasterNames.includes(name)), `${label}: requested visibility contains an unknown raster.`);
    let currentRows = await readRasterVisibility(`${label}: current state`);
    let current = new Set([...currentRows].filter(([, row]) => row.checked).map(([name]) => name));
    for (const name of [...current].sort()) {
      if (desired.has(name)) continue;
      const next = new Set(current);
      next.delete(name);
      await toggleRasterVisibility(name, false, next, `${label}: hide ${name}`);
      current = next;
    }
    for (const name of [...desired].sort()) {
      if (current.has(name)) continue;
      const next = new Set(current);
      next.add(name);
      await toggleRasterVisibility(name, true, next, `${label}: show ${name}`);
      current = next;
    }
    await assertRasterVisibility(desired, `${label}: final checked rows`);
    return await readFile(projectVisPath);
  };
  const assertSoloRasterCanvas = async (name, label) => {
    const title = rasterTitleFor(name);
    const stateExpression = `(()=>{const canvas=document.querySelector('svg.map-canvas');if(!canvas)return null;const images=Array.from(canvas.querySelectorAll('image[data-source-kind=raster]'));const overlays=Array.from(canvas.querySelectorAll('image:not([data-source-kind=raster]),rect:not(.map-canvas-surface),path,line,circle,ellipse,polygon,polyline,text,use,foreignObject')).map(node=>({tag:node.tagName.toLowerCase(),className:typeof node.className==='string'?node.className:node.getAttribute('class')??''}));return {titles:images.map(node=>node.querySelector('title')?.textContent?.trim()??''),overlayElements:overlays};})()`;
    const exactSoloState = `(()=>{const s=${stateExpression};return Boolean(s&&s.titles.length===1&&s.titles[0]===${JSON.stringify(title)}&&s.overlayElements.length===0);})()`;
    await waitFor(exactSoloState, `${label}: exactly one target raster and no SVG paint overlays`, 60_000);
    const state = await evaluate(stateExpression);
    assert.deepEqual(state.titles, [title], `${label}: the canvas does not contain exactly the requested raster image.`);
    assert.deepEqual(state.overlayElements, [], `${label}: a non-raster SVG element could cover the isolated raster.`);
    await assertRasterVisibility([name], `${label}: map-tree checkbox readback`);
    return state;
  };
  const captureSoloRaster = async (name, zoomOptions, screenshotName) => {
    const visBytesBeforeZoom = await readFile(projectVisPath);
    const visHashBeforeZoom = sha(visBytesBeforeZoom);
    const beforeCanvas = await assertSoloRasterCanvas(name, `${name} before zoom`);
    const zoom = await zoomAndMeasureRaster(name, zoomOptions);
    const afterCanvas = await assertSoloRasterCanvas(name, `${name} after zoom`);
    assert.deepEqual(afterCanvas, beforeCanvas, `${name}: zoom changed the isolated raster or introduced a visible SVG overlay.`);
    const visBytesAfterZoom = await readFile(projectVisPath);
    assert.deepEqual(visBytesAfterZoom, visBytesBeforeZoom, `${name}: zoom-to-layer changed .vis visibility or saved map state.`);
    const screenshotPath = await saveMapScreenshot(screenshotName);
    return {
      name,
      title: rasterTitleFor(name),
      visibleRasterCount: afterCanvas.titles.length,
      visibleRasterTitles: afterCanvas.titles,
      overlayElements: afterCanvas.overlayElements,
      visRevisionBeforeZoom: visHashBeforeZoom,
      visRevisionAfterZoom: sha(visBytesAfterZoom),
      zoom,
      screenshotPath,
    };
  };
  const verifyFloatRasterHover = async () => {
    const title = rasterTitleFor('float32_lz77');
    const pointerMove = `(()=>{const canvas=document.querySelector('svg.map-canvas');const image=Array.from(canvas?.querySelectorAll('image[data-source-kind=raster]')??[]).find(node=>node.querySelector('title')?.textContent?.trim()===${JSON.stringify(title)});if(!canvas||!image)return null;const rect=image.getBoundingClientRect();const clientX=rect.left+rect.width*(10.5/20);const clientY=rect.top+rect.height*(10.5/20);const dispatched=canvas.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,clientX,clientY,pointerId:1,pointerType:'mouse'}));return {clientX,clientY,dispatched};})()`;
    const tooltipText = "document.querySelector('[data-testid=map-hover-tooltip]')?.textContent?.trim()??''";
    const noTooltip = `(()=>{const tooltip=document.querySelector('[data-testid=map-hover-tooltip]');return !tooltip||!tooltip.textContent?.trim();})()`;
    const dispatchHoverAndRead = async (label) => {
      const event = await evaluate(pointerMove);
      assert.ok(event?.dispatched, `${label}: could not dispatch a real pointermove on the SVG map canvas.`);
      await waitFor(`(()=>{const text=${tooltipText};return text.includes('float32_lz77')&&text.includes('115');})()`,
        `${label}: source-backed float32 raster tooltip at row 10, column 10`, 15_000);
      const text = await evaluate(tooltipText);
      assert.ok(text.includes('float32_lz77') && text.includes('115'), `${label}: the tooltip did not report float32_lz77 value 115.`);
      return { event, text };
    };

    const firstSample = await dispatchHoverAndRead('float32 raster hover');
    await evaluate(`(()=>{const canvas=document.querySelector('svg.map-canvas');if(!canvas)return false;return canvas.dispatchEvent(new PointerEvent('pointerout',{bubbles:true,relatedTarget:document.body,clientX:${firstSample.event.clientX},clientY:${firstSample.event.clientY},pointerId:1,pointerType:'mouse'}));})()`);
    await waitFor(noTooltip, 'float32 raster tooltip clears on pointerleave', 5_000);
    const secondSample = await dispatchHoverAndRead('float32 raster hover before hiding its layer');
    return { pixelColumn: 10, pixelRow: 10, pixelCenterFraction: { x: 10.5 / 20, y: 10.5 / 20 }, firstTooltip: firstSample.text,
      pointerLeaveCleared: true, secondTooltipBeforeHide: secondSample.text, screenPoint: { x: secondSample.event.clientX, y: secondSample.event.clientY } };
  };
  const assertNoRasterHoverTooltip = async (label) => {
    await waitFor("(()=>{const tooltip=document.querySelector('[data-testid=map-hover-tooltip]');return !tooltip||!tooltip.textContent?.trim();})()",
      `${label}: no stale raster hover tooltip`, 5_000);
  };

  try {
    await navigate('map');
    await waitFor("Boolean(document.querySelector('.map-create-document')&&!document.querySelector('.map-create-document').disabled)", 'map catalog ready for GDB raster acceptance', 60_000);
    const tabCountBefore = await evaluate("document.querySelectorAll('.map-document-tab-list [role=tab]').length");
    await evaluate("document.querySelector('.map-create-document').click()");
    await waitFor(`document.querySelectorAll('.map-document-tab-list [role=tab]').length===${tabCountBefore + 1}`,
      'new independent map for GDB raster sources', 60_000);
    await waitFor("document.querySelector('.map-document-tab-list [role=tab][aria-selected=true]')?.textContent.trim()", 'new map selected');
    const mapName = await evaluate("document.querySelector('.map-document-tab-list [role=tab][aria-selected=true]').textContent.trim()");
    assert.ok(mapName, 'The newly created map tab has no saved name.');
    evidence.mapName = mapName;
    await selectMapTab(mapName);

    await setFiles('[data-testid=map-import-gdb-input]', [gdb.directory]);
    await waitFor("(() => {const button=document.querySelector('[data-testid=map-import-gdb]');const status=document.querySelector('.map-status-message')?.textContent??'';return Boolean(button&&!button.disabled&&status.includes('已保存 1 个工程图层来源'));})()",
      'real GDB raster directory upload and .vis commit', 180_000);
    const visAfterImport = await readFile(projectVisPath);
    assert.notDeepEqual(visAfterImport, visBefore, 'Creating the map and importing the GDB did not commit the project .vis file.');

    await expandMapTree('expanded imported GDB map tree');
    const rendered = await verifyRenderedRasters('GDB raster import');
    evidence.rasterCount = rendered.images.length;
    evidence.decodedFrames = rendered.decoded.map(({ name, png, pixelWidth, pixelHeight, opaque, nonTransparent, transparent, partialAlpha, distinctColors, screenWidth, screenHeight, sourcePath }) => ({
      name, png, pixelWidth, pixelHeight, opaque, nonTransparent, transparent, partialAlpha, distinctColors, screenWidth, screenHeight, sourcePath,
    }));
    console.log('GDB raster UI: all 44 source frames decoded.');
    await assertRasterVisibility(expectedRasterNames, 'GDB raster import: all raster layers initially checked');
    const floatZoomOptions = { minimumWidth: 100, minimumHeight: 100, minimumIntersectionWidth: 100, minimumIntersectionHeight: 100 };
    const rgbZoomOptions = { minimumWidth: 100, minimumHeight: 50, minimumIntersectionWidth: 100, minimumIntersectionHeight: 50 };
    const rgbaZoomOptions = { minimumWidth: 100, minimumHeight: 100, minimumIntersectionWidth: 100, minimumIntersectionHeight: 100 };
    const soloRasterCaptures = [];
    let soloVisibilityBytes = await setRasterVisibility(['float32_lz77'], 'isolate float32_lz77');
    soloRasterCaptures.push(await captureSoloRaster('float32_lz77', floatZoomOptions, 'electron-map-gdb-raster-float32-solo.png'));
    evidence.float32Hover = await verifyFloatRasterHover();
    console.log('GDB raster UI: isolated float frame and actual hover value verified.');
    soloVisibilityBytes = await setRasterVisibility(['small_world_lz77'], 'isolate small_world_lz77');
    await assertNoRasterHoverTooltip('float32 layer hidden while switching to RGB');
    evidence.checks.push('The Select pointer sampled float32_lz77 pixel (10,10) through the actual C# hover operation and displayed its independently checked value 115. Leaving the canvas and hiding the sampled layer both cleared the tooltip.');
    soloRasterCaptures.push(await captureSoloRaster('small_world_lz77', rgbZoomOptions, 'electron-map-gdb-raster-rgb-solo.png'));
    soloVisibilityBytes = await setRasterVisibility(['stefan_full_rgba_with_fake_georef_lz77'], 'isolate stefan_full_rgba_with_fake_georef_lz77');
    soloRasterCaptures.push(await captureSoloRaster('stefan_full_rgba_with_fake_georef_lz77', rgbaZoomOptions, 'electron-map-gdb-raster-rgba-solo.png'));
    console.log('GDB raster UI: isolated RGB and RGBA frames captured.');
    evidence.soloRasterCaptures = soloRasterCaptures;
    evidence.visRevisionBeforeVisibilityTests = sha(visAfterImport);
    evidence.visRevisionAfterSoloVisibility = sha(soloVisibilityBytes);
    const visAfterRestore = await setRasterVisibility(expectedRasterNames, 'restore all 44 GDB rasters');
    await assertRasterVisibility(expectedRasterNames, 'all 44 GDB raster checkboxes restored');
    const restoredAllRasters = await verifyRenderedRasters('GDB raster after restoring all checkboxes');
    await waitForRasterCanvas(expectedRasterNames, 'GDB raster after restoring all checkboxes');
    evidence.visRevisionAfterRestore = sha(visAfterRestore);
    evidence.restoredBeforeCloseRasterNames = restoredAllRasters.images.map((image) => rasterTitle(image.name)?.name).sort();
    console.log('GDB raster UI: all 44 visible layers restored.');
    evidence.visRevisionBefore = visBefore ? sha(visBefore) : null;
    evidence.visRevisionAfter = sha(visAfterImport);
    evidence.checks.push(`A real .gdb directory was uploaded through the webkitdirectory chooser into a newly created saved map. The fixed ${expectedRasterDistribution.total}-raster roster (10 Float32/Float64, 28 integer, 3 three-band Byte RGB, and 3 four-band Byte RGBA subdatasets) rendered as real PNGs; single-band NoData transparency remained allowed, RGB frames were fully opaque with at least 16 distinct colors, and each RGBA frame retained source-backed transparent, partial-alpha, and opaque pixels.`);
    evidence.checks.push(`The map-tree checkboxes were used to isolate float32_lz77, small_world_lz77, and stefan_full_rgba_with_fake_georef_lz77 in turn. Before each screenshot the real canvas contained exactly one matching PNG and no other drawable SVG overlays; each zoom remained within the viewport and left the .vis bytes unchanged. The corresponding screenshots and per-toggle .vis hashes are recorded in soloRasterCaptures and visibilityActions.`);

    await close();
    await open();
    await navigate('map');
    await waitFor("Boolean(document.querySelector('.map-create-document')&&!document.querySelector('.map-create-document').disabled)", 'reopened project map catalog', 60_000);
    await selectMapTab(mapName);
    await expandMapTree('expanded reopened GDB map tree');
    await assertRasterVisibility(expectedRasterNames, 'reopened GDB raster checkboxes');
    const visAfterReopen = await readFile(projectVisPath);
    assert.deepEqual(visAfterReopen, visAfterRestore, 'Closing and reopening the project changed the fully restored .vis visibility state.');
    const restored = await verifyRenderedRasters('GDB raster reopen');
    await waitForRasterCanvas(expectedRasterNames, 'reopened GDB raster canvas');
    assert.deepEqual(restored.images.map((image) => image.name).sort(), rendered.images.map((image) => image.name).sort(),
      'Closing and reopening the project changed the persisted GDB raster layers.');
    assert.deepEqual(await readFile(projectVisPath), visAfterRestore, 'Reading the restored GDB raster map rewrote .vis.');
    evidence.restoredRasterNames = restored.images.map((image) => image.name).sort();
    evidence.visRevisionAfterReopen = sha(visAfterReopen);
    evidence.checks.push(`After the three isolated screenshots, all ${expectedRasterDistribution.total} real map-tree checkboxes were restored and the canvas returned to all ${expectedRasterDistribution.total} PNG frames. Closing and reopening the project restored the same full visibility set and byte-identical .vis file without changing the saved map.`);
    report.checks.push(...evidence.checks.map((check) => `GDB raster: ${check}`));
  } catch (error) {
    evidence.error = error instanceof Error ? error.message : String(error);
    throw error;
  } finally {
    await writeFile(path.join(output, 'electron-map-gdb-raster-report.json'), `${JSON.stringify(evidence, null, 2)}\n`);
  }
}
