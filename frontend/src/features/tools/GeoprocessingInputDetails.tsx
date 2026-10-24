import type { Language, WorkbenchContext } from '../../shared/workbench';
import { displaySourceKind, type GeoprocessingToolDefinition } from './GeoprocessingToolBase';

export function GeoprocessingInputDetails({ language, context, tool, inputMode, onInputModeChange }: {
  language: Language; context: WorkbenchContext; tool: GeoprocessingToolDefinition;
  inputMode: 'All' | 'Selected'; onInputModeChange: (mode: 'All' | 'Selected') => void;
}) {
  const zh = language === 'zh';
  const source = context.selectedAsset;
  const inputLabel = zh ? ({ Polyline: '折线', Polygon: '多边形', Point: '点', Raster: '栅格', Mixed: '矢量要素' }[tool.inputKind]) : tool.inputKind;
  return <div className="tools-input-details">
    <div className="tools-subheading-row"><h4>{zh ? '输入数据' : 'Input data'}</h4><span className="tools-badge">{inputLabel}</span></div>
    {tool.inputKind !== 'Raster' && <label className="tools-field tools-mode-field"><span>{zh ? '要素范围' : 'Feature selection'}</span><select value={inputMode} onChange={event => onInputModeChange(event.target.value as 'All' | 'Selected')}>
      <option value="All">{zh ? '全部要素' : 'All features'}</option><option value="Selected" disabled>{zh ? '所选要素（需要地图选择上下文）' : 'Selected features (requires map selection context)'}</option>
    </select></label>}
    {source ? <div className="tools-context-card"><span className="tools-file-mark">{source.kind.slice(0, 4).toUpperCase()}</span><div><strong>{source.name}</strong><small>{displaySourceKind(source.kind, language)} · {source.path}</small><small>{source.revision ? `${zh ? '修订' : 'Revision'} ${source.revision}` : (zh ? '尚无源修订' : 'No source revision')}</small></div></div> : <p className="tools-muted">{zh ? '未选中地图源。选择文件或地图图层后可查看其身份；浏览器文件不会自动变成 C# 地图源。' : 'No map source is selected. Select a file or map layer to inspect its identity; a browser file does not automatically become a C# map source.'}</p>}
    <p className="tools-field-help">{zh ? '输入由当前地图目录与已捕获的源修订解析。当前前端页面未收到完整 MapTree / 几何捕获。' : 'Inputs resolve from the active map catalog and captured source revision. This page has not received a complete MapTree or geometry capture.'}</p>
  </div>;
}
