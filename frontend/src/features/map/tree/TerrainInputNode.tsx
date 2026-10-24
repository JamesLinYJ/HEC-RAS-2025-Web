import type { NodePresentation, NodePresentationProps } from './types';
import { DetailGroup, InfoLine } from './NodeDetails';
import { localText, propertyValue, sourceFilename } from './model';
import { bindTreeNode } from './bindTreeNode';

function presentTerrainInputNode({ node, language }: NodePresentationProps): NodePresentation {
  const sourceKinds: Record<string, readonly [string, string]> = {
    ChannelModTerrainInputParentVC: ['通道地形修改输入', 'Channel terrain modification input'],
    LeveeModTerrainInputVC: ['堤防地形修改输入', 'Levee terrain modification input'],
    PolygonRasterModTerrainInputVisualContext: ['多边形栅格修改输入', 'Polygon raster modification input'],
    EllipseRasterModTerrainInputVisualContext: ['椭圆栅格修改输入', 'Ellipse raster modification input'],
  };
  const sourceKind = sourceKinds[node.sourceClass];
  return {
    icon: '⌁', kind: sourceKind ? sourceKind[language === 'zh' ? 0 : 1] : localText(language, '地形输入', 'Terrain input'),
    detail: <DetailGroup>
      <InfoLine label={localText(language, '编号', 'Index')} value={propertyValue(node, 'Index')} />
      <InfoLine label={localText(language, '文件', 'File')} value={sourceFilename(node)} />
    </DetailGroup>,
  };
}

export const TerrainInputNode = bindTreeNode(presentTerrainInputNode, 'TerrainInputNode');
