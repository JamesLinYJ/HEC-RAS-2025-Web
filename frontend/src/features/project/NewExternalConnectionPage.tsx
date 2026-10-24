import type { Language } from '../../shared/workbench';
import { text } from '../../shared/workbench';
import type { ExternalConnectionKind } from './ExternalConnectionWorkflow';

export function NewExternalConnectionPage({ language, onChoose, onCancel }: {
  language: Language;
  onChoose: (kind: Extract<ExternalConnectionKind, 'file' | 'result'>) => void;
  onCancel: () => void;
}) {
  return (
    <section className="project-external-wizard" aria-labelledby="project-external-wizard-title">
      <div className="project-operation-kicker">{text(language, '外部连接', 'External connection')}</div>
      <h3 id="project-external-wizard-title">{text(language, '选择连接来源', 'Choose a connection source')}</h3>
      <p>{text(language, '文件和结果连接都将作为真实连接提交到当前工程。', 'File and result connections are committed as project connections.')}</p>
      <div className="project-external-choice-grid">
        <button type="button" className="project-external-choice" data-testid="project-external-connection-add-file" onClick={() => onChoose('file')}>
          <span aria-hidden="true">▤</span><strong>{text(language, '外部文件', 'External file')}</strong>
          <small>{text(language, '添加服务器路径上的文件连接。', 'Add a file connection using a server path.')}</small>
        </button>
        <button type="button" className="project-external-choice" data-testid="project-external-connection-add-result" onClick={() => onChoose('result')}>
          <span aria-hidden="true">◈</span><strong>{text(language, '工程结果', 'Project result')}</strong>
          <small>{text(language, '从当前工程目录中的真实结果 HDF 选择。', 'Choose an actual result HDF from the current project catalog.')}</small>
        </button>
      </div>
      <div className="project-footer-actions"><button type="button" className="project-button project-button-secondary" onClick={onCancel}>{text(language, '取消', 'Cancel')}</button></div>
    </section>
  );
}
