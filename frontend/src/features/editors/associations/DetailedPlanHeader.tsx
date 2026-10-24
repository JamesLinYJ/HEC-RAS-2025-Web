import type { Language, WorkbenchContext } from '../../../shared/workbench';
import type { AssociationOption, AssociationState } from './AssociationTypes';

function CurrentAssociation({ label, value, required, language }: {
  label: string;
  value: AssociationOption | null | undefined;
  required: boolean;
  language: Language;
}) {
  return (
    <div className="model-association-current-item">
      <span>{label}</span>
      {value
        ? <><strong>{value.name}</strong><code title={value.path}>{value.path}</code></>
        : <strong>{required
          ? (language === 'zh' ? '尚未绑定' : 'Not associated yet')
          : (language === 'zh' ? '无关联' : 'Not associated')}</strong>}
    </div>
  );
}

export function DetailedPlanHeader({ language, kind, asset, modelRevision, projectRevision, associations }: {
  language: Language;
  kind: 'plan' | 'geometry';
  asset: NonNullable<WorkbenchContext['selectedAsset']>;
  modelRevision: string;
  projectRevision: string | null;
  associations?: AssociationState;
}) {
  const label = kind === 'plan'
    ? (language === 'zh' ? '计划' : 'Plan')
    : (language === 'zh' ? '几何' : 'Geometry');
  return (
    <header className="model-association-header">
      <div><small>{label}</small><h2>{asset.name}</h2><code title={asset.path}>{asset.path}</code></div>
      <dl>
        <div><dt>{language === 'zh' ? '模型 SHA-256' : 'Model SHA-256'}</dt><dd><code title={modelRevision}>{modelRevision}</code></dd></div>
        <div><dt>{language === 'zh' ? '工程修订' : 'Project revision'}</dt><dd><code title={projectRevision ?? ''}>{projectRevision ?? (language === 'zh' ? '未提供' : 'Not provided')}</code></dd></div>
      </dl>
      {associations && <div className="model-association-current" aria-label={language === 'zh' ? '当前关联' : 'Current associations'}>
        {kind === 'plan' ? <>
          <CurrentAssociation language={language} label={language === 'zh' ? '当前几何' : 'Current geometry'} value={associations.geometry} required />
          <CurrentAssociation language={language} label={language === 'zh' ? '当前边界条件' : 'Current boundary condition'} value={associations.boundaryCondition} required />
        </> : <>
          <CurrentAssociation language={language} label={language === 'zh' ? '当前地形' : 'Current terrain'} value={associations.terrain} required={false} />
          <CurrentAssociation language={language} label={language === 'zh' ? '当前糙率图层' : 'Current Manning’s n layer'} value={associations.nValue} required={false} />
        </>}
      </div>}
    </header>
  );
}
