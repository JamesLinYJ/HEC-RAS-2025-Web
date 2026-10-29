import { FeatureQueryDialog } from './FeatureQueryDialog';
import { NFHLQuerierControl } from './NFHLQuerierControl';
import type { FeatureQueryDialogProps } from './featureQueryTypes';

export type NFHLQuerierDialogProps = Omit<FeatureQueryDialogProps, 'kind' | 'renderControl'>;

export function NFHLQuerierDialog(props: NFHLQuerierDialogProps) {
  return <FeatureQueryDialog {...props} kind="NFHL"
    renderControl={(controlProps) => <NFHLQuerierControl {...controlProps} />} />;
}
