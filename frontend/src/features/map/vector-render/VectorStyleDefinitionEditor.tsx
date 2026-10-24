import type { Language } from '../../../shared/workbench';
import type { GlobalSymbologyValues } from '../global-symbology/globalSymbologyWorkflow';
import type { MapVectorPlotStyle, VectorStyleProperty, VectorSymbologyDefinition } from './vectorRenderTypes';
import { editableVectorStyleProperties, vectorStyleGeometry } from './vectorRenderWorkflow';
import { VectorLineSymbologyEditor } from './VectorLineSymbologyEditor';
import { VectorPointSymbologyEditor } from './VectorPointSymbologyEditor';
import { VectorPolygonSymbologyEditor } from './VectorPolygonSymbologyEditor';

export function VectorStyleDefinitionEditor({
  style,
  definition,
  language,
  disabled,
  globalValues,
  canResetToGlobal,
  onResetToGlobal,
  onChange,
  onValidityChange,
}: {
  style: MapVectorPlotStyle;
  definition: VectorSymbologyDefinition;
  language: Language;
  disabled: boolean;
  globalValues?: GlobalSymbologyValues;
  canResetToGlobal: boolean;
  onResetToGlobal: (definitionID: string) => void;
  onChange: (definitionID: string, property: VectorStyleProperty, value: string | number) => void;
  onValidityChange: (definitionID: string, property: VectorStyleProperty, invalid: boolean) => void;
}) {
  const zh = language === 'zh';
  const geometry = vectorStyleGeometry(style.definitionID);
  const rendererProperties = editableVectorStyleProperties(geometry);
  const properties = rendererProperties.filter(property => definition.editableProperties.includes(property));
  const sourceLabel = style.isProjectOverride === true
    ? (zh ? '当前工程中的图层覆盖' : 'Project layer override')
    : style.isProjectOverride === false
      ? (zh ? '继承全局默认' : 'Inherited global default')
      : (zh ? '样式来源未确定' : 'Style source unknown');
  return (
    <section className="vector-render-style-card" data-style-definition={style.definitionID}>
      <header className="vector-render-style-heading">
        <div>
          <h4>{geometry === 'point' ? (zh ? '点' : 'Point')
            : geometry === 'line' ? (zh ? '线' : 'Line')
              : geometry === 'polygon' ? (zh ? '多边形' : 'Polygon')
                : (zh ? '未识别的样式' : 'Unrecognized style')}</h4>
          <code>{style.definitionID}</code>
          <span className="vector-render-definition-name">{definition.name}</span>
        </div>
        <span className={`vector-render-source-badge${style.isProjectOverride === true ? ' is-project-override' : ''}`}>
          {sourceLabel}
        </span>
      </header>
      {geometry === 'point' && <VectorPointSymbologyEditor style={style} language={language}
        properties={properties} disabled={disabled} onChange={onChange} onValidityChange={onValidityChange} />}
      {geometry === 'line' && <VectorLineSymbologyEditor style={style} language={language}
        properties={properties} disabled={disabled} onChange={onChange} onValidityChange={onValidityChange} />}
      {geometry === 'polygon' && <VectorPolygonSymbologyEditor style={style} language={language}
        properties={properties} disabled={disabled} onChange={onChange} onValidityChange={onValidityChange} />}
      {rendererProperties.length > 0 && properties.length === 0 && <p className="vector-render-warning">
        {zh ? '服务端未声明此样式定义的可编辑属性；当前保持只读。' : 'The service did not declare editable properties for this style. It remains read-only.'}
      </p>}
      {geometry === 'unknown' && <p className="vector-render-warning">
        {zh ? '此渲染器返回了未识别的样式定义。为避免误改，不提供编辑控件。' : 'The renderer returned an unrecognized style definition. Editing is disabled to avoid changing unsupported data.'}
      </p>}
      {geometry === 'text' && <p className="vector-render-warning">
        {zh ? '文字样式不在当前地图矢量渲染器的可写定义中。' : 'Text symbology is not a writable definition for this map vector renderer.'}
      </p>}
      {style.sourceOverrides.length > 0 && <p className="vector-render-warning">
        {zh ? '此样式含有只读的来源条件结果。这里只保存基础值，工程中的条件规则保持不变。' : 'This style has read-only source condition results. Only base values are saved; project conditions remain unchanged.'}
      </p>}
      {properties.length > 0 && <button type="button" className="vector-render-secondary-button"
        disabled={disabled || !canResetToGlobal || !globalValues}
        title={!globalValues ? (zh ? '尚未读取到一致的全局默认快照。' : 'A matching global-default snapshot is not available.') : undefined}
        data-testid={`vector-style-reset-global-${style.definitionID}`}
        onClick={() => onResetToGlobal(style.definitionID)}>
        {zh ? '恢复为全局默认值' : 'Restore global defaults'}
      </button>}
    </section>
  );
}
