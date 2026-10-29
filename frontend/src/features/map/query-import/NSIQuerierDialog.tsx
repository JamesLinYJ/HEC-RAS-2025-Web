import { FeatureQueryDialog } from './FeatureQueryDialog';
import { NSIQuerierControl } from './NSIQuerierControl';
import type { FeatureQueryDialogProps } from './featureQueryTypes';

export type NSIQuerierDialogProps = Omit<FeatureQueryDialogProps, 'kind' | 'renderControl'>;

export function NSIQuerierDialog(props: NSIQuerierDialogProps) {
  return <FeatureQueryDialog {...props} kind="NSI"
    renderControl={(controlProps) => <NSIQuerierControl {...controlProps} />} />;
}
