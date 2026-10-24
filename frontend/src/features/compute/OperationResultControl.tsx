import type { Language } from '../../shared/workbench';
import type { ComputeRunState } from './ComputeButton';
import { MessageListControl } from './MessageListControl';
import { parsePlanComputeMessages, type PlanComputeExecutionResult, type PlanComputeMessage } from './computeTypes';

const labels = {
  zh: { title: '求解器消息', live: '正在接收计算过程消息', streamed: '已接收的计算过程消息', terminal: '计算终态消息', noMessages: '服务端没有返回消息。' },
  en: { title: 'Solver messages', live: 'Receiving compute messages', streamed: 'Received compute messages', terminal: 'Terminal compute messages', noMessages: 'The server returned no messages.' },
} satisfies Record<Language, Record<string, string>>;

/** Source OperationResultControl responsibility, backed only by actual plan-compute DTOs. */
export function OperationResultControl({ language, state, result, streamedMessages }: {
  language: Language;
  state: ComputeRunState;
  result?: PlanComputeExecutionResult;
  streamedMessages: readonly PlanComputeMessage[];
}) {
  const t = labels[language];
  const running = state === 'running' || state === 'cancelling';
  const terminalMessages = result ? parsePlanComputeMessages(result.messages) : [];
  const useStreamed = (running || terminalMessages.length === 0) && streamedMessages.length > 0;
  const messages = useStreamed ? streamedMessages : terminalMessages;
  const title = running ? t.live : useStreamed ? t.streamed : t.terminal;

  return <section className="compute-operation-result" aria-label={t.title} data-testid="compute-operation-result">
    <div className="section-row"><h3>{t.title}</h3><span>{messages.length}</span></div>
    <p className="compute-operation-result-caption">{title}</p>
    {messages.length > 0
      ? <MessageListControl language={language} messages={messages} displayLimit={10} sortBySeverity useSeverityRowBackground />
      : <p className="compute-operation-messages-empty">{t.noMessages}</p>}
  </section>;
}

export default OperationResultControl;
