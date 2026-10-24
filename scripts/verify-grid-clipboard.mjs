import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'vite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = await mkdtemp(path.join(tmpdir(), 'hecras-grid-clipboard-'));
let checks = 0;
try {
  await build({ configFile: false, root, logLevel: 'silent', build: {
    lib: { entry: path.join(root, 'frontend/src/controls/dataGridClipboard.ts'), formats: ['es'], fileName: () => 'clipboard.mjs' },
    outDir: directory, emptyOutDir: true, minify: false, sourcemap: false,
  } });
  const { parseClipboardMatrix: parse, serializeClipboardMatrix: encode, clipboardMatrixFits: fits } =
    await import(pathToFileURL(path.join(directory, 'clipboard.mjs')).href);
  const equal = (actual, expected) => { assert.deepEqual(actual, expected); checks++; };
  const matrices = [
    [['']], [['0', '0001', '-0', '1e309', 'not-a-number']],
    [['中文分类', '带\t制表符'], ['多行\r\n文本', '包含"引号"']],
    [['a', ''], ['', '']], [['\r', '\n', ' \t ']],
  ];
  for (const matrix of matrices) equal(parse(encode(matrix)), matrix);
  equal(parse('a\tb\r\nc\td\r\n'), [['a', 'b'], ['c', 'd']]);
  equal(parse('"a\tb"\t"quote ""value"""'), [['a\tb', 'quote "value"']]);
  equal(parse('a\t\r\n""'), [['a', ''], ['']]);
  for (const malformed of ['"unfinished', '"closed"junk', 'unquoted"quote']) equal(parse(malformed), null);
  equal(fits([['a', 'b'], ['c']], 1, 1, 3, 3), true);
  equal(fits([['a'], ['b']], 1, 0, 2, 1), false);
  equal(fits([['a', 'b']], 0, 1, 1, 2), false);
  equal(fits([['a']], -1, 0, 2, 2), false);
  equal(fits([['a']], 0, 0.5, 2, 2), false);
  equal(fits([], 0, 0, 2, 2), false);
  equal(fits([[]], 0, 0, 2, 2), false);
  console.log(JSON.stringify({ state: 'passed', checks, scope: 'TSV-lossless-text-and-whole-matrix-bounds; DOM-events-not-covered' }));
} finally { await rm(directory, { recursive: true, force: true }); }
