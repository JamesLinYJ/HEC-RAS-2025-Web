import type { TerrainPolygonEditSettings } from './terrainTopologyTypes';

const shapes: Array<{ id: TerrainPolygonEditSettings['shape'] | 'Square' | 'Circle' | 'Ellipse' | 'HighGround' | 'Channel'; title: string; supported: boolean }> = [
  { id: 'Triangle', title: '三角形', supported: true },
  { id: 'Square', title: '正方形', supported: false },
  { id: 'Rectangle', title: '矩形', supported: true },
  { id: 'Circle', title: '圆形', supported: false },
  { id: 'Ellipse', title: '椭圆', supported: false },
  { id: 'HighGround', title: '高地', supported: false },
  { id: 'Channel', title: '渠道', supported: false },
  { id: 'Custom', title: '自定义多边形', supported: true },
];

/** Source TerrainModPicker shape choices, with unsupported Host shapes left visible and disabled. */
export function TerrainModPicker({ value, onChange, disabled }: {
  value: TerrainPolygonEditSettings['shape'];
  onChange: (shape: TerrainPolygonEditSettings['shape']) => void;
  disabled?: boolean;
}) {
  return <label className="editor-field terrain-mod-picker">
    <span>修改形状</span>
    <select data-testid="terrain-mod-shape" value={value} disabled={disabled} onChange={(event) => {
      const selected = shapes.find((shape) => shape.id === event.target.value);
      if (selected?.supported) onChange(selected.id as TerrainPolygonEditSettings['shape']);
    }}>
      {shapes.map((shape) => <option key={shape.id} value={shape.id} disabled={!shape.supported}>
        {shape.title}{shape.supported ? '' : '（暂不支持）'}
      </option>)}
    </select>
  </label>;
}
