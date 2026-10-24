import type { Language } from '../../../shared/workbench';
import type { ResultInspectorSnapshot } from './resultDetailsTypes';

interface ResultTerrainAssociationProps {
  language: Language;
  snapshot: ResultInspectorSnapshot;
}

/** Read-only Result terrain links from ResultInspectorService; no save operation is exposed. */
export function ResultTerrainAssociation({ language, snapshot }: ResultTerrainAssociationProps) {
  const zh = language === 'zh';
  const none = zh ? '未提供' : 'Not provided';
  const autoName = snapshot.associatedTerrainName ?? (zh ? '无自动识别地形' : 'No auto-detected terrain');

  return <section className="results-detail-section results-terrain-association" aria-label={zh ? '结果地形关联' : 'Result terrain association'}>
    <h4>{zh ? '地形关联' : 'Terrain association'}</h4>
    <dl className="results-association-grid">
      <div><dt>{zh ? '精确关联地形' : 'Exact terrain'}</dt><dd>{snapshot.exactTerrainName ?? none}</dd></div>
      <div><dt>{zh ? '自动识别地形' : 'Auto-detected terrain'}</dt><dd>{snapshot.showAutoDetectedTerrain ? autoName : (zh ? '已隐藏' : 'Hidden')}</dd></div>
      <div><dt>{zh ? '覆盖地形' : 'Override terrain'}</dt><dd>{snapshot.overrideTerrainName ?? (zh ? '未设置' : 'Not set')}</dd></div>
      <div><dt>{zh ? '自动识别显示' : 'Show auto-detected terrain'}</dt><dd>{snapshot.showAutoDetectedTerrain ? (zh ? '是' : 'Yes') : (zh ? '否' : 'No')}</dd></div>
    </dl>
    {snapshot.terrainChoices.length > 0 && <div className="results-association-choices">
      <span>{zh ? '工程提供的地形选项' : 'Terrain choices in the project'}</span>
      <ul>{snapshot.terrainChoices.map((terrain, index) => <li key={`${terrain}-${index}`}>{terrain}</li>)}</ul>
    </div>}
    <p className="results-detail-status">{zh ? '此视图只读；当前接口没有结果地形关联保存回调。' : 'This view is read-only; no Result terrain-association save callback is available.'}</p>
  </section>;
}
