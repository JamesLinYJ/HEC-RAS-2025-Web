import type { Language } from '../shared/workbench';
import type { ComputeProgressSample } from '../features/compute/computeTypes';
import { MessageControl } from './MessageControl';

const labels: Record<Language, { title: string; empty: string; emptyMessage: string; latest: string }> = {
  zh: { title: '后端进度消息', empty: '服务端尚未发送进度样本。', emptyMessage: '此进度样本未包含文本消息。', latest: '显示最近' },
  en: { title: 'Backend progress messages', empty: 'The server has not sent progress samples.', emptyMessage: 'This progress sample has no text message.', latest: 'Showing latest' },
};

const maxVisibleSamples = 200;

/** Live log list sourced only from received ComputeProgressSample fields. */
export function LiveMessageList({ language, samples }: { language: Language; samples: ComputeProgressSample[] }) {
  const t = labels[language];
  const visibleSamples = samples.slice(-maxVisibleSamples);
  const omitted = samples.length - visibleSamples.length;
  return <section className="compute-live-messages" aria-label={t.title}>
    <div className="compute-live-messages-heading">
      <h3>{t.title}</h3>
      <span>{samples.length}</span>
    </div>
    {visibleSamples.length === 0
      ? <p className="compute-live-messages-empty">{t.empty}</p>
      : <ol className="compute-live-messages-list">
        {visibleSamples.map((sample) => <MessageControl
          key={`${sample.sequence}-${sample.timestamp}`}
          sample={sample}
          language={language}
          emptyMessage={t.emptyMessage}
        />)}
      </ol>}
    {omitted > 0 && <p className="compute-live-messages-limit">{t.latest} {visibleSamples.length} / {samples.length}</p>}
  </section>;
}

export default LiveMessageList;
