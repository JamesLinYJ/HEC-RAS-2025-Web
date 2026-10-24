import { useMemo, useState } from 'react';
import type { GeometryTopologyMapCommand, GeometryTopologyMapTool } from './geometryTypes';
import './topologyMapArcTools.css';

export interface TopologyMapArcToolsProps {
  language: 'zh' | 'en';
  mapCaptureReady: boolean;
  canSelect: boolean;
  canEdit: boolean;
  busy: boolean;
  draftReady: boolean;
  draftDirty: boolean;
  mapCellSize: number | null;
  activeTool: GeometryTopologyMapTool;
  onActiveToolChange: (tool: GeometryTopologyMapTool) => void;
  selectedArcFIDs: readonly number[];
  splitMode: 'Anywhere' | 'AtVertex';
  onSplitModeChange: (mode: 'Anywhere' | 'AtVertex') => void;
  onRunArcCommand: (command: GeometryTopologyMapCommand) => void;
}

type ArcCommand = Extract<GeometryTopologyMapCommand, { action: 'reverseArc' | 'filterArc' | 'smoothArc' }>;

const EPSILON_NUMBER = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/;
const INTEGER_NUMBER = /^[+-]?\d+$/;

function parseNonNegativeNumber(raw: string): number | null {
  const trimmed = raw.trim();
  if (!trimmed || !EPSILON_NUMBER.test(trimmed)) return null;
  const value = Number(trimmed);
  return Number.isFinite(value) && value >= 0 ? value : null;
}

function parseBoundedInteger(raw: string, minimum: number, maximum: number): number | null {
  const trimmed = raw.trim();
  if (!trimmed || !INTEGER_NUMBER.test(trimmed)) return null;
  const value = Number(trimmed);
  return Number.isSafeInteger(value) && value >= minimum && value <= maximum ? value : null;
}

function uniqueArcFIDs(values: readonly number[]): number[] {
  return [...new Set(values.filter((value) => Number.isInteger(value) && value >= 0))];
}

export function TopologyMapArcTools({
  language,
  mapCaptureReady,
  canSelect,
  canEdit,
  busy,
  draftReady,
  draftDirty,
  mapCellSize,
  activeTool,
  onActiveToolChange,
  selectedArcFIDs,
  splitMode,
  onSplitModeChange,
  onRunArcCommand,
}: TopologyMapArcToolsProps) {
  const [epsilonText, setEpsilonText] = useState('1');
  const [smoothWindowText, setSmoothWindowText] = useState('3');
  const [smoothIterationsText, setSmoothIterationsText] = useState('2');
  const [hasEditedParameters, setHasEditedParameters] = useState(false);

  const text = language === 'zh';
  const selectedFIDs = useMemo(() => uniqueArcFIDs(selectedArcFIDs), [selectedArcFIDs]);
  const epsilon = parseNonNegativeNumber(epsilonText);
  const smoothWindowSize = parseBoundedInteger(smoothWindowText, 1, 1001);
  const smoothIterations = parseBoundedInteger(smoothIterationsText, 1, 1000);
  const validMapCellSize = Number.isFinite(mapCellSize) && (mapCellSize ?? 0) > 0;
  const canConfigureSelection = mapCaptureReady && canSelect && !busy;
  const canConfigureEditTool = mapCaptureReady && canEdit && draftReady && !busy && validMapCellSize;
  const canRunArcCommand = canEdit && draftReady && mapCaptureReady && !busy && validMapCellSize && selectedFIDs.length > 0;

  const tools: Array<{ id: GeometryTopologyMapTool; label: string; edit: boolean }> = [
    { id: 'select', label: text ? '选择' : 'Select', edit: false },
    { id: 'addArc', label: text ? '绘制弧段' : 'Draw arc', edit: true },
    { id: 'moveArcPoint', label: text ? '移动内点' : 'Move point', edit: true },
    { id: 'insertArcPoint', label: text ? '插入点' : 'Insert point', edit: true },
    { id: 'deleteArcPoint', label: text ? '删除点' : 'Delete point', edit: true },
    { id: 'splitArc', label: text ? '分割弧段' : 'Split arc', edit: true },
  ];

  const fire = (command: ArcCommand): void => {
    if (!canRunArcCommand) return;
    onRunArcCommand(command);
  };

  const cellSize = validMapCellSize ? mapCellSize : null;
  const epsilonError = epsilon === null;
  const smoothWindowError = smoothWindowSize === null;
  const smoothIterationsError = smoothIterations === null;
  const showEpsilonError = hasEditedParameters && epsilonError;
  const showSmoothWindowError = hasEditedParameters && smoothWindowError;
  const showSmoothIterationsError = hasEditedParameters && smoothIterationsError;

  return (
    <section className="topology-map-arc-tools" aria-label={text ? '弧段地图工具' : 'Arc map tools'}>
      <div className="topology-map-arc-tools__row topology-map-arc-tools__map-tools">
        <div className="topology-map-arc-tools__tool-list" role="group" aria-label={text ? '地图工具' : 'Map tools'}>
          {tools.map((tool) => {
            const enabled = tool.edit ? canConfigureEditTool : canConfigureSelection;
            return (
              <button
                key={tool.id}
                type="button"
                className="topology-map-arc-tools__tool"
                aria-pressed={activeTool === tool.id}
                disabled={!enabled}
                onClick={() => onActiveToolChange(tool.id)}
              >
                {tool.label}
              </button>
            );
          })}
        </div>
        {activeTool === 'splitArc' && (
          <fieldset className="topology-map-arc-tools__split-mode" disabled={!canConfigureEditTool}>
            <legend>{text ? '分割位置' : 'Split location'}</legend>
            <label>
              <input
                type="radio"
                name="topology-arc-split-mode"
                value="Anywhere"
                checked={splitMode === 'Anywhere'}
                onChange={() => onSplitModeChange('Anywhere')}
              />
              {text ? '任意位置' : 'Anywhere'}
            </label>
            <label>
              <input
                type="radio"
                name="topology-arc-split-mode"
                value="AtVertex"
                checked={splitMode === 'AtVertex'}
                onChange={() => onSplitModeChange('AtVertex')}
              />
              {text ? '在顶点' : 'At a vertex'}
            </label>
          </fieldset>
        )}
      </div>

      <div className="topology-map-arc-tools__row topology-map-arc-tools__commands">
        <span className="topology-map-arc-tools__selection" aria-live="polite">
          {text ? `已选弧段：${selectedFIDs.length}` : `Selected arcs: ${selectedFIDs.length}`}
        </span>
        <button
          type="button"
          disabled={!canRunArcCommand}
          onClick={() => cellSize !== null && fire({
            action: 'reverseArc', featureKind: 'arc', featureIDs: selectedFIDs, mapCellSize: cellSize,
          })}
        >
          {text ? '反转方向' : 'Reverse direction'}
        </button>
        <label className="topology-map-arc-tools__parameter">
          <span>{text ? '简化容差（地图单位）' : 'Simplification tolerance (map units)'}</span>
          <input
            type="text"
            inputMode="decimal"
            value={epsilonText}
            aria-invalid={showEpsilonError}
            aria-describedby={showEpsilonError ? 'topology-arc-epsilon-error' : undefined}
            onChange={(event) => { setHasEditedParameters(true); setEpsilonText(event.currentTarget.value); }}
          />
          {showEpsilonError && <span className="topology-map-arc-tools__error" id="topology-arc-epsilon-error">
            {text ? '请输入大于或等于 0 的有限数值。' : 'Enter a finite number greater than or equal to 0.'}
          </span>}
        </label>
        <button
          type="button"
          disabled={!canRunArcCommand || epsilon === null}
          onClick={() => cellSize !== null && epsilon !== null && fire({
            action: 'filterArc', featureKind: 'arc', featureIDs: selectedFIDs, mapCellSize: cellSize, epsilon,
          })}
        >
          {text ? '按容差简化' : 'Simplify by tolerance'}
        </button>

        <label className="topology-map-arc-tools__parameter">
          <span>{text ? '平滑窗口（1–1001）' : 'Smoothing window (1–1001)'}</span>
          <input
            type="text"
            inputMode="numeric"
            value={smoothWindowText}
            aria-invalid={showSmoothWindowError}
            aria-describedby={showSmoothWindowError ? 'topology-arc-smooth-window-error' : undefined}
            onChange={(event) => { setHasEditedParameters(true); setSmoothWindowText(event.currentTarget.value); }}
          />
          {showSmoothWindowError && <span className="topology-map-arc-tools__error" id="topology-arc-smooth-window-error">
            {text ? '请输入 1 到 1001 之间的整数。' : 'Enter an integer from 1 to 1001.'}
          </span>}
        </label>
        <label className="topology-map-arc-tools__parameter">
          <span>{text ? '平滑迭代次数（1–1000）' : 'Smoothing iterations (1–1000)'}</span>
          <input
            type="text"
            inputMode="numeric"
            value={smoothIterationsText}
            aria-invalid={showSmoothIterationsError}
            aria-describedby={showSmoothIterationsError ? 'topology-arc-smooth-iterations-error' : undefined}
            onChange={(event) => { setHasEditedParameters(true); setSmoothIterationsText(event.currentTarget.value); }}
          />
          {showSmoothIterationsError && <span className="topology-map-arc-tools__error" id="topology-arc-smooth-iterations-error">
            {text ? '请输入 1 到 1000 之间的整数。' : 'Enter an integer from 1 to 1000.'}
          </span>}
        </label>
        <button
          type="button"
          disabled={!canRunArcCommand || smoothWindowSize === null || smoothIterations === null}
          onClick={() => cellSize !== null && smoothWindowSize !== null && smoothIterations !== null && fire({
            action: 'smoothArc', featureKind: 'arc', featureIDs: selectedFIDs, mapCellSize: cellSize,
            smoothWindowSize, smoothIterations,
          })}
        >
          {text ? '平滑弧段' : 'Smooth arcs'}
        </button>
      </div>

      <div className="topology-map-arc-tools__status" role="status">
        {draftDirty
          ? (text ? '有未保存的几何草稿；这些操作会继续修改当前草稿。' : 'Geometry changes are in an unsaved draft; these operations continue editing that draft.')
          : (text ? '操作写入当前几何草稿，之后可统一保存。' : 'Operations update the current geometry draft, which can be saved afterward.')}
        {!mapCaptureReady && <span>{text ? ' · 请先加载当前地图捕获。' : ' · Load the current map capture first.'}</span>}
        {mapCaptureReady && !draftReady && <span>{text ? ' · 当前几何草稿尚未就绪。' : ' · The current geometry draft is not ready.'}</span>}
        {mapCaptureReady && !validMapCellSize && <span>{text ? ' · 当前地图比例无效，暂不能执行弧段操作。' : ' · The current map scale is invalid; arc operations are unavailable.'}</span>}
        {busy && <span>{text ? ' · 地图操作进行中。' : ' · A map operation is in progress.'}</span>}
      </div>
    </section>
  );
}

export default TopologyMapArcTools;
