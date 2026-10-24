import type { Language } from '../../shared/workbench';
import type { SpatialGridBand } from './model';

export function SpatialBoundaryConditionBandInput(props: {
  language: Language;
  band: SpatialGridBand;
  components: string[];
  selected: boolean;
  disabled?: boolean;
  onChange: (band: SpatialGridBand) => void;
  onPreview: (bandId: string) => void;
}) {
  const { language, band } = props;
  return <tr>
    <td><input type="checkbox" checked={band.included} disabled={props.disabled} aria-label={language === 'zh' ? `包含波段 ${band.band + 1}` : `Include band ${band.band + 1}`} onChange={(event) => props.onChange({ ...band, included: event.currentTarget.checked })} /></td>
    <td><button type="button" className="editor-button" disabled={props.disabled} onClick={() => props.onPreview(band.id)}>{band.band + 1}</button></td>
    <td><input type="text" value={band.time ?? ''} disabled={props.disabled} aria-label={language === 'zh' ? `波段 ${band.band + 1} 时间` : `Band ${band.band + 1} time`} placeholder="YYYY-MM-DDTHH:mm:ss" onChange={(event) => props.onChange({ ...band, time: event.currentTarget.value || null })} /></td>
    <td><input type="text" value={band.issueTime ?? ''} disabled={props.disabled} aria-label={language === 'zh' ? `波段 ${band.band + 1} 起报时间` : `Band ${band.band + 1} issue time`} placeholder="YYYY-MM-DDTHH:mm:ss" onChange={(event) => props.onChange({ ...band, issueTime: event.currentTarget.value || null })} /></td>
    <td>{band.units || '—'}</td>
    <td><select value={band.component} disabled={props.disabled} onChange={(event) => props.onChange({ ...band, component: event.currentTarget.value })}>
      {props.components.map((component) => <option key={component} value={component}>{component}</option>)}
    </select></td>
    <td><button type="button" className="editor-button" disabled={props.disabled} aria-pressed={props.selected} onClick={() => props.onPreview(band.id)}>{language === 'zh' ? (props.selected ? '预览中' : '预览') : (props.selected ? 'Previewing' : 'Preview')}</button></td>
  </tr>;
}
