import { useState } from 'react';
import type { Language } from '../../../shared/workbench';
import type { DssTimeSeriesData } from './types';
import { TimeSeriesGraph } from './TimeSeriesGraph';
import { DssDataDialog } from './DssDataDialog';

interface TimeSeriesGraphWindowProps {
  data: DssTimeSeriesData;
  pathname: string;
  sourceRevision: string;
  language: Language;
}

export function TimeSeriesGraphWindow({ data, pathname, sourceRevision, language }: TimeSeriesGraphWindowProps) {
  const zh = language === 'zh';
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" className="project-dss-window-trigger" data-testid="project-dss-timeseries-open-graph-window" onClick={() => setOpen(true)}>
      {zh ? '放大时序图' : 'Open larger time-series graph'}
    </button>
    <DssDataDialog open={open} onClose={() => setOpen(false)} title={zh ? '时序图' : 'Time-series graph'} label={zh ? '时序图窗口' : 'Time-series graph window'} closeLabel={zh ? '关闭窗口' : 'Close window'}>
      <div className="project-dss-window-source">
        <code>{pathname}</code>
        <small>{zh ? '源 SHA-256' : 'Source SHA-256'} · <code>{sourceRevision}</code> · {data.values.length} {zh ? '个源样本' : 'source samples'}</small>
      </div>
      <TimeSeriesGraph data={data} pathname={pathname} language={language} />
    </DssDataDialog>
  </>;
}
