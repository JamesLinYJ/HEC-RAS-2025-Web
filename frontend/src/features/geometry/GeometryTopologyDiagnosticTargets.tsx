import type { Language } from '../../shared/workbench';
import type { GeometryTopologyCompletionMessage, GeometryTopologySnapshot } from './geometryTypes';
import { resolveTopologyDiagnosticTarget, topologyDiagnosticKind, type TopologyDiagnosticLocator } from './geometryTopologySelection';

/** React action surface for the source UI's diagnostic ContextIndexPair feature actions. */
export function GeometryTopologyDiagnosticTargets({ language, snapshot, message, onLocate }: {
  language: Language;
  snapshot?: GeometryTopologySnapshot;
  message: GeometryTopologyCompletionMessage;
  onLocate?: TopologyDiagnosticLocator;
}) {
  if (!message.targets?.length) return null;
  return <div className="geometry-inline-help" style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
    {message.targets.map((target, index) => {
      const resolved = resolveTopologyDiagnosticTarget(snapshot, message.id, target);
      const available = !!onLocate && !!resolved;
      const kind = topologyDiagnosticKind(target.featureKind);
      const kindLabel = language === 'zh' && kind
        ? { node: '节点', arc: '弧线', region: '区域' }[kind]
        : target.featureKind;
      return <button type="button" className="geometry-secondary" key={`${target.featureKind}-${target.fid}-${index}`}
        data-testid="geometry-topology-diagnostic-locate" data-feature-kind={target.featureKind} data-source-fid={target.fid}
        disabled={!available}
        title={available ? (language === 'zh' ? '选择源要素并缩放到地图位置' : 'Select the source feature and zoom to its map location')
          : (language === 'zh' ? '当前快照没有此目标的有效源行或地图几何，无法定位。' : 'The current snapshot has no valid source row or map geometry for this target.')}
        onClick={() => { if (available) onLocate!(message.id, target); }}>
        {language === 'zh' ? '选择并定位' : 'Select and locate'} · {kindLabel} {target.fid} ({target.tableRowID})
      </button>;
    })}
  </div>;
}
