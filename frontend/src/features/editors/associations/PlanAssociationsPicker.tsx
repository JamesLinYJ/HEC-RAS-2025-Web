import type { Language } from '../../../shared/workbench';
import type { AssociationOption } from './AssociationTypes';
import { BoundaryConditionPicker } from './BoundaryConditionPicker';
import { GeometryPicker } from './GeometryPicker';

export function PlanAssociationsPicker({ language, options, geometryPath, boundaryConditionPath, disabled, onGeometryChange, onBoundaryConditionChange }: {
  language: Language;
  options: { geometry: AssociationOption[]; boundaryCondition: AssociationOption[] };
  geometryPath: string;
  boundaryConditionPath: string;
  disabled: boolean;
  onGeometryChange: (path: string) => void;
  onBoundaryConditionChange: (path: string) => void;
}) {
  return (
    <section className="model-association-group" aria-label={language === 'zh' ? '计划关联' : 'Plan associations'}>
      <header><h3>{language === 'zh' ? '计划关联' : 'Plan associations'}</h3><p>{language === 'zh' ? '计划必须关联一个工程几何和一个边界条件。' : 'A plan requires a project geometry and a boundary condition.'}</p></header>
      <div className="model-association-grid">
        <GeometryPicker language={language} options={options.geometry} value={geometryPath} onChange={onGeometryChange} disabled={disabled} />
        <BoundaryConditionPicker language={language} options={options.boundaryCondition} value={boundaryConditionPath} onChange={onBoundaryConditionChange} disabled={disabled} />
      </div>
    </section>
  );
}
