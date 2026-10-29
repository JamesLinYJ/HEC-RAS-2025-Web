import { useRef, useState } from 'react';
import { text } from '../../../shared/workbench';
import { NLDQuerierDialog } from './NLDQuerierDialog';
import { NSIQuerierDialog } from './NSIQuerierDialog';
import { NFHLQuerierDialog } from './NFHLQuerierDialog';
import { OSMQuerierDialog } from './OSMQuerierDialog';
import type { FeatureQueryImporterProps, FeatureQueryKind } from './featureQueryTypes';

/** Map toolbar entry and a single source-specific dialog at a time. */
export function FeatureQueryImporter(props: FeatureQueryImporterProps) {
  const [kind, setKind] = useState<FeatureQueryKind | null>(null);
  const entry = useRef<HTMLButtonElement>(null);
  const close = () => { setKind(null); requestAnimationFrame(() => entry.current?.focus()); };
  const common = { ...props, onClose: close, onKindChange: (next: FeatureQueryKind) => setKind(next) };
  return <>
    <button ref={entry} type="button" className="map-import-button feature-query-entry"
      onClick={() => setKind('NLD')} disabled={!props.document} title={!props.document ? text(props.language, '请先打开已保存的地图。', 'Open a saved map first.') : undefined}>
      {text(props.language, '在线数据', 'Online data')}
    </button>
    {kind === 'NLD' && <NLDQuerierDialog {...common} />}
    {kind === 'NSI' && <NSIQuerierDialog {...common} />}
    {kind === 'NFHL' && <NFHLQuerierDialog {...common} />}
    {kind === 'OSM' && <OSMQuerierDialog {...common} />}
  </>;
}
