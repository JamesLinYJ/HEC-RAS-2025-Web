import { Component, Suspense, lazy, useEffect, useState } from 'react';
import type { ComponentType, ErrorInfo, ReactNode } from 'react';
import type { FeatureId, FeaturePageProps } from '../shared/workbench';
import { text } from '../shared/workbench';
import { ComputeProofPage } from '../features/compute/ComputeProofPage';
import type { PendingProjectCommand } from '../features/project';

const modules = import.meta.glob<{ default: ComponentType<FeaturePageProps> }>('../features/*/index.tsx');
const pages = new Map<string, ReturnType<typeof lazy<ComponentType<FeaturePageProps>>>>();
for (const [path, load] of Object.entries(modules)) {
  const id = path.split('/').at(-2);
  if (id) pages.set(id, lazy(load));
}

class PageBoundary extends Component<{ children: ReactNode; label: string }, { error: string | null }> {
  state = { error: null as string | null };
  static getDerivedStateFromError(error: Error) { return { error: error.message }; }
  componentDidCatch(_error: Error, _info: ErrorInfo) { /* The visible boundary preserves the exact error. */ }
  render() {
    if (this.state.error) return <section role="alert" className="ras-page-error"><h2>{this.props.label}</h2><pre>{this.state.error}</pre><button onClick={() => this.setState({ error: null })}>↻</button></section>;
    return this.props.children;
  }
}

/** Web counterpart of RasContent.cs: retain document state while switching workspace pages. */
export function RasContent({ activeFeature, ...props }: FeaturePageProps & {
  activeFeature: FeatureId;
  pendingProjectCommand?: PendingProjectCommand | null;
  onProjectCommandHandled?: (id: string) => void;
}) {
  const [computeOpened, setComputeOpened] = useState(activeFeature === 'compute');
  useEffect(() => { if (activeFeature === 'compute') setComputeOpened(true); }, [activeFeature]);
  const keys = ['project', 'editors', 'geometry', 'map', 'results', 'tools'] as const;
  return <div className="ras-content-host">
    {keys.map((feature) => {
      const Page = pages.get(feature);
      return <div key={feature} className="ras-feature-page" hidden={activeFeature !== feature} data-feature={feature}>
        <PageBoundary label={text(props.language, '界面加载失败', 'Page failed to load')}>
          <Suspense fallback={<p role="status">{text(props.language, '正在加载…', 'Loading…')}</p>}>
            {Page ? <Page {...props} /> : <p className="ras-empty">{text(props.language, '此页面尚未接入工作区。', 'This page is not connected to the workspace yet.')}</p>}
          </Suspense>
        </PageBoundary>
      </div>;
    })}
    <div className="ras-feature-page" hidden={activeFeature !== 'compute'} data-feature="compute">{computeOpened && <ComputeProofPage {...props} />}</div>
  </div>;
}
