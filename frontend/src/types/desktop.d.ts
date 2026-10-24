export {};

declare global {
  interface Window {
    hecDesktop?: {
      isElectron: true;
      smokeMode: boolean;
      uiSmokeMode: boolean;
      openExternalUrl?: (url: string) => Promise<boolean>;
      reportSmokeResult: (result: unknown) => void;
    };
  }
}
