import type { TerrainPolygonEditSettings } from './terrainTopologyTypes';

const modificationTypes: Array<{ id: TerrainPolygonEditSettings['modificationType']; title: string }> = [
  { id: 'SetValue', title: '设置为指定高程' },
  { id: 'TakeHigher', title: '取较高值' },
  { id: 'TakeLower', title: '取较低值' },
  { id: 'AddValue', title: '增加高程值' },
];

/** Source TerrainModStats values. Settings stay local until Draw or Stamp is submitted. */
export function TerrainModStats({ value, onChange, disabled }: {
  value: TerrainPolygonEditSettings;
  onChange: (next: TerrainPolygonEditSettings) => void;
  disabled?: boolean;
}) {
  const update = (patch: Partial<TerrainPolygonEditSettings>) => onChange({ ...value, ...patch });
  const numberField = (id: 'width' | 'height' | 'rotation' | 'value', title: string, step: string) => <label className="editor-field" key={id}>
    <span>{title}</span>
    <input
      type="number"
      data-testid={`terrain-mod-${id}`}
      step={step}
      value={Number.isFinite(value[id]) ? value[id] : ''}
      disabled={disabled}
      onChange={(event) => update({ [id]: event.currentTarget.value === '' ? Number.NaN : Number(event.currentTarget.value) })}
    />
  </label>;
  return <section className="terrain-mod-stats" aria-label="地形修改参数">
    <h3>修改参数</h3>
    {numberField('width', '宽度（输入坐标单位）', 'any')}
    {numberField('height', '高度（输入坐标单位）', 'any')}
    {numberField('rotation', '旋转角度', 'any')}
    {numberField('value', '高程值', 'any')}
    <label className="editor-field">
      <span>修改方式</span>
      <select data-testid="terrain-mod-modification-type" value={value.modificationType} disabled={disabled} onChange={(event) => update({ modificationType: event.target.value as TerrainPolygonEditSettings['modificationType'] })}>
        {modificationTypes.map((item) => <option key={item.id} value={item.id}>{item.title}</option>)}
      </select>
    </label>
    <small className="editor-field-muted">这些参数只用于本次绘制或放置；执行后才进入地形草稿。</small>
  </section>;
}
