import { useId, useState } from 'react';
import type { XY } from './mapTypes';
import type { NavigationTarget } from './mapNavigation';

/** Point/upper-left–lower-right modes retained from ZoomToPoint.cs. */
export function ZoomToPoint({ initialPoint, language, onNavigate, onCancel }: {
  initialPoint: XY; language: 'zh' | 'en'; onNavigate: (target: NavigationTarget) => void; onCancel: () => void;
}) {
  const zh = language === 'zh';
  const id = useId();
  const [mode, setMode] = useState<'point' | 'extent'>('point');
  const [coordinates, setCoordinates] = useState([String(initialPoint[0]), String(initialPoint[1]), '', '']);
  const [error, setError] = useState('');
  const update = (index: number, value: string) => {
    const tuple = index === 0 ? value.trim().replace(/^[([\s]+|[)\]\s]+$/g, '').split(/[,;\s]+/) : [];
    if ((tuple.length === 2 || tuple.length === 4) && tuple.every(part => part !== '' && Number.isFinite(Number(part)))) {
      setCoordinates(tuple.length === 2 ? [...tuple, '', ''] : tuple);
      setMode(tuple.length === 2 ? 'point' : 'extent');
    } else setCoordinates(current => current.map((old, i) => i === index ? value : old));
    setError('');
  };
  const submit = () => {
    try {
      const values = coordinates.slice(0, mode === 'point' ? 2 : 4).map(value => value.trim() === '' ? NaN : Number(value));
      if (!values.every(Number.isFinite)) throw new RangeError('invalidPoint');
      if (mode === 'point') onNavigate({ kind: 'point', point: [values[0], values[1]] });
      else onNavigate({ kind: 'extent', extent: { minX: Math.min(values[0], values[2]), maxX: Math.max(values[0], values[2]), minY: Math.min(values[1], values[3]), maxY: Math.max(values[1], values[3]) } });
    } catch { setError(zh ? '坐标或范围无法显示，请检查数值与可用精度。' : 'The coordinate or extent cannot be displayed. Check the numbers and available precision.'); }
  };
  return <form onSubmit={event => { event.preventDefault(); submit(); }} className="map-location-form">
    <label htmlFor={`${id}-mode`}>{zh ? '定位方式' : 'Mode'}</label>
    <select id={`${id}-mode`} value={mode} onChange={event => { setMode(event.currentTarget.value as 'point' | 'extent'); setError(''); }}>
      <option value="point">{zh ? '点坐标' : 'Point'}</option><option value="extent">{zh ? '范围' : 'Extent'}</option>
    </select>
    <p>{zh ? '使用当前地图的绘制坐标，可在 X 中粘贴两项坐标或四项范围。' : 'Use the current map coordinates. Paste two point coordinates or four extent coordinates into X.'}</p>
    <div className="map-location-fields">{coordinates.slice(0, mode === 'point' ? 2 : 4).map((value, index) => <label key={index}>
      <span>{mode === 'extent' ? (zh ? ['左上 X', '左上 Y', '右下 X', '右下 Y'][index] : ['Upper-left X', 'Upper-left Y', 'Lower-right X', 'Lower-right Y'][index]) : ['X', 'Y'][index]}</span>
      <input name={`coordinate-${index}`} autoFocus={index === 0} autoComplete="off" inputMode="decimal" value={value} onChange={event => update(index, event.currentTarget.value)} />
    </label>)}</div>
    {error && <p className="map-inline-error" role="alert">{error}</p>}
    <footer><button type="button" onClick={onCancel}>{zh ? '取消' : 'Cancel'}</button><button type="submit">{zh ? '定位' : 'Go to'}</button></footer>
  </form>;
}
