export {};

declare global {
  interface Window {
    hecDesktop?: {
      isElectron: true;
      smokeMode: boolean;
      uiSmokeMode: boolean;
      reportSmokeResult: (result: unknown) => void;
    };
  }
}
