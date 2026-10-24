const { contextBridge, ipcRenderer } = require('electron');

const smokeMode = process.argv.includes('--hec-wasm-smoke');
const uiSmokeMode = process.argv.includes('--hec-ui-smoke');

contextBridge.exposeInMainWorld('hecDesktop', Object.freeze({
  isElectron: true,
  smokeMode,
  uiSmokeMode,
  reportSmokeResult: (result) => {
    if (smokeMode) ipcRenderer.send('hec:wasm-smoke-result', result);
  },
}));
