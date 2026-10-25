import { useEffect, useState } from 'react';
import type { Language } from '../../shared/workbench';
import { GeometryError } from './GeometryError';
import { meshExportSourceIssue, meshExportSourceKey, type MeshExportSource, type MeshShapefileExportKind } from './meshShapefileExport';
import type { MeshShapefileExportWorkspace } from './useMeshShapefileExport';
import './meshShapefileExport.css';

interface Props {
  language: Language;
  source: MeshExportSource;
  workspace: MeshShapefileExportWorkspace;
  available: boolean;
  busy: boolean;
}

/** FlowAreaLayerBehaviors.cs: Cell Centers and Perimeter Faces use the retained Host writers. */
export function FlowAreaLayerBehaviors({ language, source, workspace, available, busy }: Props) {
  const zh = language === 'zh';
  const sourceKey = meshExportSourceKey(source);
  const [flowAreaID, setFlowAreaID] = useState('');
  const [outputPath, setOutputPath] = useState('');
  useEffect(() => { setFlowAreaID(source.mesh?.flowAreas.find(area => area.hasMesh)?.id ?? ''); setOutputPath(''); }, [sourceKey]);
  const area = source.mesh?.flowAreas.find(item => item.id === flowAreaID);
  const issue = meshExportSourceIssue(source, flowAreaID);
  const disabled = busy || workspace.pending || !workspace.writeProtected || !available || !!issue || !outputPath;
  const start = (exportKind: MeshShapefileExportKind): void => { void workspace.start({ flowAreaID, exportKind, outputPath }); };
  return <section className="geometry-panel mesh-shapefile-export" data-testid="mesh-shapefile-export">
    <header className="geometry-panel-heading"><div><h2>{zh ? '导出网格 Shapefile' : 'Export mesh Shapefile'}</h2>
      <p>{zh ? '导出当前草稿的完整网格；源 Geometry 保持不变。' : 'Export the full mesh in the current draft; the source Geometry remains unchanged.'}</p></div></header>
    <div className="geometry-panel-body mesh-shapefile-export__form">
      <label className="geometry-form-field">{zh ? '流动区域' : 'Flow area'}
        <select data-testid="mesh-export-flow-area" value={flowAreaID} onChange={event => setFlowAreaID(event.target.value)} disabled={busy || workspace.pending}>
          <option value="">{zh ? '选择有网格的流动区域' : 'Choose a meshed flow area'}</option>
          {source.mesh?.flowAreas.map(item => <option key={item.id} value={item.id} disabled={!item.hasMesh || item.cellCount <= 0}>
            {item.name} · {item.id} · {item.cellCount} {zh ? '单元' : 'cells'}</option>)}
        </select>
      </label>
      <label className="geometry-form-field">{zh ? '服务器输出文件绝对路径' : 'Absolute output file path on the server'}
        <input data-testid="mesh-export-output-path" type="text" value={outputPath} spellCheck={false}
          onChange={event => setOutputPath(event.target.value)} disabled={busy || workspace.pending} placeholder="/exports/MeshCellCenters.shp" />
      </label>
      <p className="geometry-inline-help">{zh
        ? '输出目录必须已存在。若同名 .shp、.shx、.dbf、.prj 或 .cpg 已存在，服务端会拒绝覆盖。Local 坐标工程不会生成虚构的 .prj。'
        : 'The directory must already exist. Existing .shp, .shx, .dbf, .prj or .cpg files are never overwritten. Local-coordinate projects do not receive a fabricated .prj.'}</p>
      {area && <p className="geometry-inline-help" data-testid="mesh-export-source-counts">{zh ? '源网格' : 'Source mesh'}: {area.cellCount} {zh ? '单元' : 'cells'} / {area.faceCount} {zh ? '面' : 'faces'} · {area.name}</p>}
      {!available && <p role="status">{zh ? '当前服务暂不提供可取消的网格 Shapefile 导出。' : 'Cancellable mesh Shapefile export is unavailable from the current service.'}</p>}
      {!workspace.writeProtected && <p role="status">{zh ? '当前界面缺少工作区写入保护，不能发起导出。' : 'Workspace write protection is unavailable; export is disabled.'}</p>}
      {issue && <p role="status">{issue}</p>}
      <div className="mesh-shapefile-export__actions">
        <button type="button" className="geometry-secondary" data-testid="mesh-export-cell-centers" disabled={disabled} onClick={() => start('CellCenters')}>{zh ? '导出单元中心' : 'Export cell centers'}</button>
        <button type="button" className="geometry-secondary" data-testid="mesh-export-perimeter-faces" disabled={disabled} onClick={() => start('PerimeterFaces')}>{zh ? '导出外围面' : 'Export perimeter faces'}</button>
      </div>
      <p className="geometry-inline-help">{zh ? '外围面按真实网格边界导出，并保留 Face、CellA、CellB、Length 属性。' : 'Perimeter faces come from the actual mesh boundary, retaining Face, CellA, CellB and Length attributes.'}</p>
      {workspace.state.validationError !== undefined && <GeometryError language={language} error={workspace.state.validationError} />}
    </div>
  </section>;
}
