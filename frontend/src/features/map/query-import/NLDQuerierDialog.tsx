import { FeatureQueryDialog } from './FeatureQueryDialog';
import { NLDQuerierControl } from './NLDQuerierControl';
import type { FeatureQueryDialogProps } from './featureQueryTypes';

export type NLDQuerierDialogProps = Omit<FeatureQueryDialogProps, 'kind' | 'renderControl'>;

export function NLDQuerierDialog(props: NLDQuerierDialogProps) {
  return <FeatureQueryDialog {...props} kind="NLD"
    renderControl={(controlProps) => <NLDQuerierControl {...controlProps} />} />;
}
