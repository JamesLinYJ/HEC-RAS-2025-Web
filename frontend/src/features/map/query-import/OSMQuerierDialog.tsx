import { FeatureQueryDialog } from './FeatureQueryDialog';
import type { FeatureQueryDialogProps } from './featureQueryTypes';
import { OSMQuerierControl } from './OSMQuerierControl';

/** Original dialog entry point, backed by the shared query/import lifecycle. */
export function OSMQuerierDialog(props: Omit<FeatureQueryDialogProps, 'kind' | 'renderControl'>) {
  return <FeatureQueryDialog {...props} kind="OSM" renderControl={controlProps => <OSMQuerierControl {...controlProps} />} />;
}
