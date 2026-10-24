import type { MapExtent } from '../mapTypes';

export type MapViewAction = 'list' | 'create' | 'rename' | 'resetExtent' | 'delete' | 'shortcut';

/** Coordinate names match the original .vis XML attributes. */
export interface ViewExtent {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

/** One saved view read from the current project's .vis file. */
export interface SavedMapView {
  name: string;
  extent: ViewExtent;
  shortcut: string;
  entrySHA256: string;
}

export interface ViewIntent {
  action: Exclude<MapViewAction, 'list'>;
  targetName?: string;
  name?: string;
  extent?: ViewExtent;
  shortcut?: string;
}

export interface ViewOwner {
  projectPath: string;
  projectRevision: string;
  projectSessionID: string;
  visualizationRevision: string | null;
  mapID: string;
  mapIndex: number;
  mapName: string;
  mapType: string;
  coreLayerContext?: unknown;
}

export function viewExtentFromMapExtent(extent: MapExtent): ViewExtent {
  return { left: extent.minX, top: extent.maxY, right: extent.maxX, bottom: extent.minY };
}

export function isValidViewExtent(value: unknown): value is ViewExtent {
  if (!value || typeof value !== 'object') return false;
  const extent = value as Partial<ViewExtent>;
  return Number.isFinite(extent.left) && Number.isFinite(extent.top) &&
    Number.isFinite(extent.right) && Number.isFinite(extent.bottom) &&
    extent.left! < extent.right! && extent.bottom! < extent.top!;
}

export function mapExtentFromViewExtent(extent: ViewExtent): MapExtent {
  return { minX: extent.left, maxX: extent.right, minY: extent.bottom, maxY: extent.top };
}

const validVirtualKeys = new Set([
  ...'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split(''),
  ...Array.from({ length: 10 }, (_, index) => `Number${index}`),
  ...Array.from({ length: 10 }, (_, index) => `NumberPad${index}`),
  ...Array.from({ length: 24 }, (_, index) => `F${index + 1}`),
  'LeftButton', 'RightButton', 'Cancel', 'MiddleButton', 'XButton1', 'XButton2', 'Back', 'Clear', 'Tab', 'Enter',
  'Shift', 'Control', 'Menu', 'Pause', 'CapitalLock', 'Kana', 'Hangul', 'Junja', 'Final', 'Hanja', 'Kanji',
  'Escape', 'Convert', 'NonConvert', 'Accept', 'ModeChange', 'Space', 'PageUp', 'PageDown', 'End', 'Home',
  'Left', 'Up', 'Right', 'Down', 'Select', 'Print', 'Execute', 'Snapshot', 'Insert', 'Delete', 'Help',
  'LeftWindows', 'RightWindows', 'Application', 'Sleep', 'Multiply', 'Add', 'Separator', 'Subtract', 'Decimal',
  'Divide', 'NumLock', 'Scroll', 'LeftShift', 'RightShift', 'LeftControl', 'RightControl', 'LeftMenu', 'RightMenu',
  'BrowserBack', 'BrowserForward', 'BrowserRefresh', 'BrowserStop', 'BrowserSearch', 'BrowserFavorites', 'BrowserHome',
  'VolumeMute', 'VolumeDown', 'VolumeUp', 'MediaNextTrack', 'MediaPreviousTrack', 'MediaStop', 'MediaPlayPause',
  'LaunchMail', 'SelectMedia', 'LaunchApplication1', 'LaunchApplication2', 'Oem1', 'OemPlus', 'OemComma', 'OemMinus',
  'OemPeriod', 'Oem2', 'Oem3', 'Oem4', 'Oem5', 'Oem6', 'Oem7', 'Oem8', 'Oem102', 'ImeProcessed', 'System', 'Attn',
  'CrSel', 'ExSel', 'EraseEof', 'Play', 'Zoom', 'NoName', 'Pa1', 'OemClear', 'NavigationView', 'NavigationMenu',
  'NavigationUp', 'NavigationDown', 'NavigationLeft', 'NavigationRight', 'NavigationAccept', 'NavigationCancel',
  'GamepadA', 'GamepadB', 'GamepadX', 'GamepadY', 'GamepadRightShoulder', 'GamepadLeftShoulder', 'GamepadLeftTrigger',
  'GamepadRightTrigger', 'GamepadDPadUp', 'GamepadDPadDown', 'GamepadDPadLeft', 'GamepadDPadRight', 'GamepadMenu',
  'GamepadView', 'GamepadLeftThumbstickButton', 'GamepadRightThumbstickButton', 'GamepadLeftThumbstickUp',
  'GamepadLeftThumbstickDown', 'GamepadLeftThumbstickRight', 'GamepadLeftThumbstickLeft', 'GamepadRightThumbstickUp',
  'GamepadRightThumbstickDown', 'GamepadRightThumbstickRight', 'GamepadRightThumbstickLeft',
]);

export function normalizeShortcut(value: string): string | null {
  const trimmed = value.trim();
  if (/^none$/i.test(trimmed)) return 'None';
  const parts = trimmed.split('+');
  if (parts.length === 1) {
    const key = canonicalVirtualKey(parts[0]);
    return key ? key : null;
  }
  if (parts.length !== 2) return null;
  const modifierNames: Record<string, string> = { ctrl: 'Ctrl', control: 'Ctrl', menu: 'Menu', alt: 'Menu', shift: 'Shift', win: 'Win', windows: 'Win', meta: 'Win', cmd: 'Win', command: 'Win' };
  const modifier = modifierNames[parts[0].trim().toLowerCase()];
  const key = canonicalVirtualKey(parts[1]);
  return modifier && key ? `${modifier}+${key}` : null;
}

function canonicalVirtualKey(value: string): string | null {
  const raw = value.trim();
  const key = /^[0-9]$/.test(raw) ? `Number${raw}` : /^numpad([0-9])$/i.test(raw) ? `NumberPad${raw.slice(-1)}` : raw;
  if (!key) return null;
  const match = [...validVirtualKeys].find((candidate) => candidate.toLowerCase() === key.toLowerCase());
  return match ?? null;
}

export function shortcutFromKeyboardEvent(event: KeyboardEvent): string | null {
  const modifier = event.metaKey ? 'Win' : event.ctrlKey ? 'Ctrl' : event.altKey ? 'Menu' : event.shiftKey ? 'Shift' : '';
  const modifierCount = Number(event.metaKey) + Number(event.ctrlKey) + Number(event.altKey) + Number(event.shiftKey);
  if (modifierCount > 1) return null;
  const aliases: Record<string, string> = {
    ' ': 'Space', Backspace: 'Back', Esc: 'Escape', ArrowLeft: 'Left', ArrowUp: 'Up',
    ArrowRight: 'Right', ArrowDown: 'Down', PageUp: 'PageUp', PageDown: 'PageDown',
  };
  const key = aliases[event.key] ?? event.key.toUpperCase();
  const canonical = canonicalVirtualKey(key);
  if (!canonical || ['Control', 'Alt', 'Shift', 'Meta'].includes(event.key)) return null;
  return modifier ? `${modifier}+${canonical}` : canonical;
}

const reservedShortcuts = new Set([
  'Shift+V', // Save Current View action from the source application.
  'Tab', 'Enter', 'Escape', 'Back', 'Delete', 'Space',
  'Shift', 'Control', 'Menu', 'LeftShift', 'RightShift', 'LeftControl', 'RightControl', 'LeftMenu', 'RightMenu',
  'LeftWindows', 'RightWindows',
  'Ctrl+Z', 'Win+Z', 'Ctrl+Y', 'Win+Y', // Map undo and redo.
  'Ctrl+R', 'Win+R', 'Ctrl+T', 'Win+T', 'Ctrl+W', 'Win+W',
  'Ctrl+L', 'Win+L', 'Ctrl+N', 'Win+N', 'Ctrl+P', 'Win+P',
  'Ctrl+S', 'Win+S', 'Ctrl+O', 'Win+O', 'Ctrl+F', 'Win+F',
  'Ctrl+H', 'Win+H', 'Ctrl+J', 'Win+J', 'Ctrl+U', 'Win+U',
  'Ctrl+0', 'Ctrl+1', 'Ctrl+2', 'Ctrl+3', 'Ctrl+4', 'Ctrl+5', 'Ctrl+6', 'Ctrl+7', 'Ctrl+8', 'Ctrl+9',
  'Win+0', 'Win+1', 'Win+2', 'Win+3', 'Win+4', 'Win+5', 'Win+6', 'Win+7', 'Win+8', 'Win+9',
  'Ctrl+Tab', 'Win+Tab', 'Menu+Left', 'Menu+Right', 'Menu+Up', 'Menu+Down', 'Ctrl+PageUp', 'Ctrl+PageDown',
  'F1', 'F3', 'F5', 'F6', 'F11', 'F12', 'Ctrl+F4', 'Ctrl+OemPlus', 'Ctrl+OemMinus',
  'Menu+F4', 'Menu+Tab', 'Win+Tab', 'Win+Space',
]);

export function shortcutConflict(value: string, views: SavedMapView[], targetName?: string): string | null {
  const normalized = normalizeShortcut(value);
  if (!normalized) return '快捷键格式无效。请使用一个 VirtualKey，可加 Ctrl、Menu、Shift 或 Win 修饰键。';
  if (normalized !== 'None' && (reservedShortcuts.has(normalized) || /^(Ctrl|Win)\+Number[0-9]$/i.test(normalized))) {
    return '该组合与地图操作、保存视图或浏览器/系统快捷键冲突。';
  }
  if (normalized !== 'None' && views.some((view) => view.name !== targetName && normalizeShortcut(view.shortcut) === normalized)) {
    return '该快捷键已分配给其他已保存视图。';
  }
  return null;
}
