import type { Language } from '../../../shared/workbench';
import type { DssLocationData } from './types';

interface LocationInformationViewProps {
  location: DssLocationData;
  language: Language;
}

function coordinate(value: number | null): string {
  return value === null || !Number.isFinite(value) ? '—' : String(value);
}

export function LocationInformationView({ location, language }: LocationInformationViewProps) {
  const zh = language === 'zh';
  const entries: Array<[string, string]> = [
    [zh ? 'X 坐标' : 'X coordinate', coordinate(location.x)],
    [zh ? 'Y 坐标' : 'Y coordinate', coordinate(location.y)],
    [zh ? 'Z 坐标' : 'Z coordinate', coordinate(location.z)],
    [zh ? '坐标系标识' : 'Coordinate system', location.coordinateSystem || (zh ? '未提供' : 'Not provided')],
    [zh ? '坐标系代码' : 'Coordinate ID', String(location.coordinateID)],
    [zh ? '水平单位代码' : 'Horizontal units code', String(location.horizontalUnits)],
    [zh ? '水平基准代码' : 'Horizontal datum code', String(location.horizontalDatum)],
    [zh ? '垂直单位代码' : 'Vertical units code', String(location.verticalUnits)],
    [zh ? '垂直基准代码' : 'Vertical datum code', String(location.verticalDatum)],
    [zh ? '时区' : 'Time zone', location.timeZoneName || (zh ? '未提供' : 'Not provided')],
  ];

  return <details className="project-dss-location-information" open>
    <summary>{zh ? '位置记录元数据' : 'Location record metadata'}</summary>
    <dl>{entries.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
    {location.supplemental && <p className="project-dss-supplemental"><span>{zh ? '补充信息' : 'Supplemental information'}</span>{location.supplemental}</p>}
    <small>{zh ? '显示服务返回的原始坐标与代码；此面板不解释坐标系或生成地图位置。' : 'Shows host-returned coordinates and codes; it does not interpret the CRS or create a map location.'}</small>
  </details>;
}
