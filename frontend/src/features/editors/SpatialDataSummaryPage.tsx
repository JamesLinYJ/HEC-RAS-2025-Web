import { useEffect, useRef, useState } from 'react';
import type { EditorPageProps, SpatialGridData } from './model';
import { SpatialBoundaryConditionsPage } from './SpatialBoundaryConditionsPage';
import { SpatialBoundaryConditionRasterImporter } from './SpatialBoundaryConditionRasterImporter';
import { SpatialDataHeader, type SpatialBoundaryLayerID } from './SpatialDataHeader';
import { SpatialDataSummary } from './SpatialDataSummary';

export function SpatialDataSummaryPage(props: EditorPageProps) {
  const [layerId, setLayerId] = useState<SpatialBoundaryLayerID>('precipitation');
  const [showImporter, setShowImporter] = useState(false);
  const [frames, setFrames] = useState<Record<string, SpatialGridData>>({});
  const [frameError, setFrameError] = useState('');
  const [frameLoading, setFrameLoading] = useState(false);
  const frameController = useRef<AbortController | null>(null);
  const frameGeneration = useRef(0);
  const label = (zh: string, en: string) => props.language === 'zh' ? zh : en;
  const savedGrid = props.snapshot.grids?.[layerId];
  const grid = frames[layerId] ?? savedGrid;

  useEffect(() => {
    frameController.current?.abort();
    frameGeneration.current++;
    setFrameLoading(false);
    setFrameError('');
  }, [layerId, props.snapshot.revision]);
  useEffect(() => () => frameController.current?.abort(), []);

  const readFrame = async (timeId: string | undefined, component: string) => {
    if (!props.onGridFrame) return;
    frameController.current?.abort();
    const controller = new AbortController();
    frameController.current = controller;
    const current = ++frameGeneration.current;
    setFrameLoading(true);
    setFrameError('');
    try {
      const result = await props.onGridFrame(layerId, timeId, component, controller.signal);
      if (controller.signal.aborted || current !== frameGeneration.current) return;
      setFrames((existing) => ({ ...existing, [layerId]: result }));
    } catch (error) {
      if (!controller.signal.aborted && current === frameGeneration.current)
        setFrameError(error instanceof Error ? error.message : label('读取格网帧失败。', 'Could not read the grid frame.'));
    } finally {
      if (current === frameGeneration.current) setFrameLoading(false);
    }
  };

  return <section className="editor-schema-page editor-meteorology-page" aria-label={label('空间边界数据', 'Spatial boundary data')}>
    <div className="editor-schema-heading"><div>
      <p className="editor-eyebrow">{label('Boundary Conditions · 原空间数据控件组合', 'Boundary Conditions · source spatial-data controls')}</p>
      <h2>{label('空间边界数据', 'Spatial boundary data')}</h2>
      <p className="editor-field-muted">{label('页面读取模型快照的空间输入与格网；测站/输入结构变更由 C# 草稿变换承载，栅格导入则按模型保存回执刷新基线。', 'This page reads spatial inputs and grids from the model snapshot. Station/input structure uses C# draft transforms; raster import refreshes the baseline from the model save receipt.')}</p>
    </div>
      <div className="editor-workbench-actions">
        <button type="button" className="editor-button" onClick={() => props.onPageChange('PrecipitationControl')}>{label('返回降水页面', 'Back to precipitation')}</button>
        <button type="button" className="editor-button" onClick={() => props.onPageChange('BoundaryConditionEditor')}>{label('返回水力边界', 'Back to hydraulic boundaries')}</button>
      </div>
    </div>
    <SpatialDataHeader {...props} layerId={layerId} onLayerChange={setLayerId} onImport={() => setShowImporter(true)} />
    <SpatialDataSummary language={props.language} grid={grid} loading={frameLoading} onFrame={(timeId, component) => { void readFrame(timeId, component); }} />
    {!props.onGridFrame && <p className="editor-field-muted">{label('当前 C# 服务未公布格网帧读取操作。', 'Grid-frame reading is disabled because the C# service has not advertised the operation.')}</p>}
    {frameError && <p className="editor-field-error" role="alert">{frameError}</p>}
    <SpatialBoundaryConditionsPage {...props} layerId={layerId} />
    {showImporter && <SpatialBoundaryConditionRasterImporter {...props} layerId={layerId} onClose={() => setShowImporter(false)} />}
  </section>;
}
