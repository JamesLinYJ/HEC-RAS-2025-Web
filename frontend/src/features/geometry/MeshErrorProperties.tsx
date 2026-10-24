import type { Language } from '../../shared/workbench';
import { gt } from './geometryText';
import type { GeometryMeshDegreeScan } from './geometryTypes';

export interface MeshErrorPropertiesProps {
  language: Language;
  scan?: GeometryMeshDegreeScan;
  rawOutput?: string;
}

export function MeshErrorProperties({ language, scan, rawOutput }: MeshErrorPropertiesProps) {
  const issues = scan?.issues ?? [];
  return (
    <section className="geometry-panel">
      <header className="geometry-panel-heading"><div><h3>{language === 'zh' ? '网格错误属性' : 'Mesh error properties'}</h3><p>{scan ? `${issues.length} · ${language === 'zh' ? '面数阈值' : 'Face threshold'} ${scan.maxFacesPerNode}` : (language === 'zh' ? '尚无节点度数扫描结果。' : 'No node-degree scan result yet.')}</p></div></header>
      {!scan ? <div className="geometry-empty"><strong>{language === 'zh' ? '运行“检查节点度数”后查看源扫描结果。' : 'Run Check node degree to inspect source scan results.'}</strong></div> : issues.length === 0 ? (
        <div className="geometry-panel-body"><span className="geometry-ok">{language === 'zh' ? '该扫描未返回超过阈值的节点。' : 'The scan returned no nodes above the threshold.'}</span></div>
      ) : (
        <div className="geometry-validation-list">
          {issues.map((issue) => <details className="geometry-report-item" key={issue.id}>
            <summary>{issue.flowAreaName} · {language === 'zh' ? '网格节点' : 'Mesh node'} {issue.nodeIndex} · {issue.faceCount} {gt(language, 'faces')}</summary>
            <div className="geometry-panel-body geometry-grid two">
              <div className="geometry-kv"><span>ID</span><strong>{issue.id}</strong></div>
              <div className="geometry-kv"><span>{language === 'zh' ? '网格索引' : 'Mesh index'}</span><strong>{issue.meshIndex}</strong></div>
              <div className="geometry-kv"><span>X</span><strong>{issue.x ?? '—'}</strong></div>
              <div className="geometry-kv"><span>Y</span><strong>{issue.y ?? '—'}</strong></div>
            </div>
            {issue.cells.length > 0 && <details className="geometry-inline-help"><summary>{language === 'zh' ? `查看相邻单元（${issue.cells.length}）` : `Incident cells (${issue.cells.length})`}</summary><ul>{issue.cells.map((cell) => <li key={cell.cellIndex}>{language === 'zh' ? '单元' : 'Cell'} {cell.cellIndex}: {cell.coordinates.map((point) => `(${point.x}, ${point.y})`).join(' · ')}</li>)}</ul></details>}
          </details>)}
        </div>
      )}
      {rawOutput && <details className="geometry-inline-help"><summary>{gt(language, 'scanOutput')}</summary><pre style={{ whiteSpace: 'pre-wrap' }}>{rawOutput}</pre></details>}
      <div className="geometry-inline-help">{language === 'zh' ? '此面板显示当前几何度数扫描的真实结果；地图树的自动检查开关属于另一项上下文设置。' : 'This panel shows the current geometry degree scan. The map-tree automatic-check toggle belongs to a separate context setting.'}</div>
    </section>
  );
}

export default MeshErrorProperties;
