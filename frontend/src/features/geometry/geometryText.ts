import type { Language } from '../../shared/workbench';
import { editorAssetKind, editorText, editorValueLabel } from '../editors/editorLocalization';

const labels = {
  zh: {
    geometry: '几何数据', summary: '概要', topology: '拓扑', nodes: '节点', arcs: '弧段', regions: '区域', mesh: '网格', culverts: '涵洞',
    description: '说明', file: '文件', layer: '图层', project: '工程', revision: '源修订', session: '工程会话', source: '数据来源',
    sourceBacked: '来自当前几何文件', localDraft: '本地临时草稿', noAsset: '请先从工程树选择一个几何图层。',
    overview: '几何概要', topologySummary: '拓扑概要', nodesCount: '节点数', arcsCount: '弧段数', regionsCount: '区域数', flowAreaCount: '二维流动区域', barrelCount: '涵洞线',
    table: '要素表', filter: '筛选…', addLocal: '添加本地行', delete: '删除所选', select: '选择', selected: '已选', rows: '行', noRows: '没有可显示的要素。',
    emptyHint: '加载该几何数据后，此处会显示实际源要素。', localOnly: '此行仅保存在当前界面的临时草稿中，尚未写入后端草稿。',
    loading: '正在读取几何数据…', unavailable: '当前后端接口未提供此操作。', load: '重新读取', loadFailed: '读取失败', editDisabled: '当前几何不可编辑。',
    dirty: '有未保存修改', clean: '已与源数据同步', undo: '撤销', redo: '重做', reset: '放弃全部修改', save: '保存几何', saving: '正在保存…', saved: '保存完成',
    saveUnavailable: '当前后端尚不支持保存几何，草稿保留在本页。', saveBlocked: '本地临时行不能作为真实几何草稿保存。', noChanges: '没有待保存的几何更改。',
    validation: '校验', validationCount: '项需要处理', valid: '字段格式有效', required: '此字段不能为空。', positive: '请输入大于零的有限数字。', invalidNumber: '请输入有效数字。',
    backendErrors: '后端返回的几何诊断', completion: '拓扑完成度', complete: '拓扑完整', incomplete: '拓扑尚未完成',
    flowAreas: '二维流动区域', targetSpacing: '目标单元间距', name: '名称', hasMesh: '已有网格', cells: '单元', faces: '边', meshNodes: '网格节点',
    prepare: '准备几何', preparing: '准备已排队，将在父级保存时执行', generate: '生成网格', generating: '正在生成网格…', degreeScan: '检查节点度数', maxFaces: '最大面数',
    generationReport: '生成结果', propertyTables: '生成属性表', meshNoData: '网格数据尚未加载。', selectedRegions: '选中拓扑区域',
    barrels: '涵洞线', barrelProperties: '涵洞管身属性', openingProperties: '进出口属性', terrain: '关联地形', noTerrain: '未关联地形',
    addBarrel: '按坐标添加涵洞线', addBarrelProperties: '添加管身属性集', addOpeningProperties: '添加进出口属性集', startX: '起点 X', startY: '起点 Y', endX: '终点 X', endY: '终点 Y',
    add: '添加', cancel: '取消', choosePolyline: '请输入有效的起止坐标；生成的线会进入几何父级草稿。',
    undoUnavailable: '当前版本不支持此草稿操作。', dirtyCaption: '子页修改会汇总到几何父级事务；不会单独保存子表。', parentTxn: '统一父级保存',
    noData: '没有几何数据', localEditMode: '编辑器离线草稿模式', operationRunning: '正在执行操作…', idle: '就绪',
    node: '节点', arc: '弧段', region: '区域', flowArea: '流动区域', barrel: '涵洞线', barrelPropertiesKind: '管身属性', openingPropertiesKind: '进出口属性',
    value: '值', status: '状态', operation: '操作', validationNote: '表格校验基于当前可见字段；几何拓扑诊断以服务端返回为准。',
    embeddedImages: '嵌入图像', draftTag: '草稿', yes: '是', no: '否', identifier: '编号', openTable: '查看要素表',
    softwareVersion: '软件版本', duration: '总耗时', startTime: '开始时间', arcCountUnit: '弧段', diagnosticDetails: '查看后端诊断详情', rawErrorDetails: '查看原始错误详情',
    fieldError: '字段格式无效', positiveFieldError: '数值必须大于零', unknownFailure: '操作未能完成',
    shape: '形状', diameter: '直径', span: '跨度', rise: '净高', roughness: '糙率', manningRoughness: '曼宁糙率', twoPhaseMannings: '使用两段糙率', bottomMannings: '底部曼宁糙率', manningSwitchDepth: '糙率切换水深',
    upstreamInvert: '上游底高程', downstreamInvert: '下游底高程', barrelPropertyName: '管身参数名称', upstreamOpeningName: '上游进口名称', downstreamOpeningName: '下游出口名称', payloadFixedStartTime: '任一载荷使用固定起始时间',
    inletLossCoefficient: '进口损失系数', outletLossCoefficient: '出口损失系数', openingType: '进出口类型', rotationDegrees: '旋转角度', internalBias: '内部偏差', multipoint: '多点', tinResolution: 'TIN 分辨率', sizeTolerance: '尺寸容差', inletType: '入口类型',
    elevation: '高程', lossCoefficient: '损失系数', outletType: '出口类型', length: '长度', area: '面积', station: '桩号', nodeID: '节点编号', arcID: '弧段编号', regionID: '区域编号', type: '类型',
    serverProgress: '查看服务端进度详情', scanOutput: '查看节点度数检查结果', propertyOutput: '查看属性表计算结果', generationDetails: '查看网格生成诊断',
    sizingStatus: '网格尺寸状态', parameter: '参数', constant: '常数', variable: '变量', custom: '自定义',
  },
  en: {
    geometry: 'Geometry', summary: 'Summary', topology: 'Topology', nodes: 'Nodes', arcs: 'Arcs', regions: 'Regions', mesh: 'Mesh', culverts: 'Culverts',
    description: 'Description', file: 'File', layer: 'Layer', project: 'Project', revision: 'Source revision', session: 'Project session', source: 'Data source',
    sourceBacked: 'Current geometry source', localDraft: 'Local temporary draft', noAsset: 'Select a geometry layer from the project tree first.',
    overview: 'Geometry summary', topologySummary: 'Topology summary', nodesCount: 'Nodes', arcsCount: 'Arcs', regionsCount: 'Regions', flowAreaCount: '2D flow areas', barrelCount: 'Culvert lines',
    table: 'Feature table', filter: 'Filter…', addLocal: 'Add local row', delete: 'Delete selected', select: 'Select', selected: 'selected', rows: 'rows', noRows: 'No features to display.',
    emptyHint: 'Actual source features appear here after the geometry is loaded.', localOnly: 'This row exists only in the current view draft and has not entered the backend draft.',
    loading: 'Loading geometry…', unavailable: 'The current backend gateway does not provide this operation.', load: 'Reload', loadFailed: 'Load failed', editDisabled: 'This geometry cannot currently be edited.',
    dirty: 'Unsaved changes', clean: 'Synchronized with source', undo: 'Undo', redo: 'Redo', reset: 'Discard all changes', save: 'Save geometry', saving: 'Saving…', saved: 'Save complete',
    saveUnavailable: 'Parent model.save is not available yet; drafts remain in this page.', saveBlocked: 'Local temporary rows cannot be saved as a real geometry draft.', noChanges: 'There are no geometry changes to save.',
    validation: 'Validation', validationCount: 'items need attention', valid: 'Field formats are valid', required: 'This field is required.', positive: 'Enter a finite number greater than zero.', invalidNumber: 'Enter a valid number.',
    backendErrors: 'Geometry diagnostics from backend', completion: 'Topology completion', complete: 'Topology complete', incomplete: 'Topology incomplete',
    flowAreas: '2D flow areas', targetSpacing: 'Target cell spacing', name: 'Name', hasMesh: 'Mesh exists', cells: 'Cells', faces: 'Faces', meshNodes: 'Mesh nodes',
    prepare: 'Prepare geometry', preparing: 'Preparation queued; it runs during parent save', generate: 'Generate mesh', generating: 'Generating mesh…', degreeScan: 'Check node degree', maxFaces: 'Maximum faces',
    generationReport: 'Generation report', propertyTables: 'Generate property tables', meshNoData: 'Mesh data has not loaded.', selectedRegions: 'Selected topology regions',
    barrels: 'Culvert lines', barrelProperties: 'Barrel properties', openingProperties: 'Opening properties', terrain: 'Associated terrain', noTerrain: 'No terrain associated',
    addBarrel: 'Add culvert line by coordinates', addBarrelProperties: 'Add barrel property set', addOpeningProperties: 'Add opening property set', startX: 'Start X', startY: 'Start Y', endX: 'End X', endY: 'End Y',
    add: 'Add', cancel: 'Cancel', choosePolyline: 'Enter valid start and end coordinates; the resulting line enters the parent geometry draft.',
    undoUnavailable: 'This draft operation is not available in the current gateway.', dirtyCaption: 'Child edits are collected into the geometry parent transaction; child tables are not saved separately.', parentTxn: 'Parent transaction',
    noData: 'No geometry data', localEditMode: 'Local editor draft mode', operationRunning: 'Operation in progress…', idle: 'Ready',
    node: 'Node', arc: 'Arc', region: 'Region', flowArea: 'Flow area', barrel: 'Culvert line', barrelPropertiesKind: 'Barrel properties', openingPropertiesKind: 'Opening properties',
    value: 'Value', status: 'Status', operation: 'Operation', validationNote: 'Table checks cover visible fields; source geometry diagnostics come from the C# backend.',
    embeddedImages: 'Embedded images', draftTag: 'Draft', yes: 'Yes', no: 'No', identifier: 'ID', openTable: 'Open feature table',
    softwareVersion: 'Software version', duration: 'Duration', startTime: 'Start time', arcCountUnit: 'arcs', diagnosticDetails: 'Show backend diagnostic details', rawErrorDetails: 'Show original error details',
    fieldError: 'has an invalid value', positiveFieldError: 'must be greater than zero', unknownFailure: 'The operation could not be completed',
    shape: 'Shape', diameter: 'Diameter', span: 'Span', rise: 'Rise', roughness: 'Roughness', manningRoughness: 'Mannings', twoPhaseMannings: 'Use Two Phase Mannings', bottomMannings: 'Mannings Bot', manningSwitchDepth: 'Mannings Switch Depth',
    upstreamInvert: 'Upstream invert', downstreamInvert: 'Downstream invert', barrelPropertyName: 'Barrel Property Name', upstreamOpeningName: 'Upstream Opening Name', downstreamOpeningName: 'Downstream Opening Name', payloadFixedStartTime: 'Any Payloads Use Fixed Start Time',
    inletLossCoefficient: 'K In', outletLossCoefficient: 'K Out', openingType: 'Opening Type', rotationDegrees: 'Rotation Degrees', internalBias: 'Bias Internal', multipoint: 'Multipoint', tinResolution: 'Tin Resolution', sizeTolerance: 'Size Tolerance', inletType: 'Inlet type',
    elevation: 'Elevation', lossCoefficient: 'Loss coefficient', outletType: 'Outlet type', length: 'Length', area: 'Area', station: 'Station', nodeID: 'Node ID', arcID: 'Arc ID', regionID: 'Region ID', type: 'Type',
    serverProgress: 'Show server progress details', scanOutput: 'Show node degree scan results', propertyOutput: 'Show property table results', generationDetails: 'Show mesh generation diagnostics',
    sizingStatus: 'Mesh sizing status', parameter: 'Parameter', constant: 'Constant', variable: 'Variable', custom: 'Custom',
  },
} satisfies Record<Language, Record<string, string>>;

export function gt(language: Language, key: keyof typeof labels.en): string {
  return labels[language][key];
}

export function gkind(language: Language, kind: string): string {
  const labelsByKind: Record<string, keyof typeof labels.en> = {
    node: 'node', arc: 'arc', region: 'region', flowArea: 'flowArea', barrel: 'barrel',
    barrelProperties: 'barrelPropertiesKind', openingProperties: 'openingPropertiesKind',
  };
  const normalized = kind.replace(/[^a-z]/gi, '').toLocaleLowerCase();
  const aliases: Record<string, keyof typeof labels.en> = {
    geometry: 'geometry', geometryasset: 'geometry', observablegeometry: 'geometry',
    node: 'node', arc: 'arc', region: 'region', flowarea: 'flowArea', barrel: 'barrel',
    barrelproperties: 'barrelPropertiesKind', openingproperties: 'openingPropertiesKind',
  };
  const alias = aliases[normalized];
  if (alias) return gt(language, alias);
  const key = labelsByKind[kind];
  if (key) return gt(language, key);
  if (language !== 'zh') return kind;
  const editorLabel = editorAssetKind(language, kind);
  return editorLabel !== kind ? editorLabel : '几何对象';
}

const fieldLabels: Record<string, keyof typeof labels.en> = {
  name: 'name', description: 'description', type: 'type', length: 'length', area: 'area', station: 'station',
  nodeid: 'nodeID', arcid: 'arcID', regionid: 'regionID',
  targetcellspacing: 'targetSpacing', cellspacing: 'targetSpacing', hasmesh: 'hasMesh',
  cellcount: 'cells', facecount: 'faces', nodecount: 'meshNodes', elevation: 'elevation', invertelevation: 'elevation',
  shape: 'shape', diameter: 'diameter', span: 'span', rise: 'rise', manningn: 'roughness', roughness: 'roughness',
  upstreaminvert: 'upstreamInvert', downstreaminvert: 'downstreamInvert', inlettype: 'inletType', outletype: 'outletType',
  entrancecoefficient: 'lossCoefficient', exitcoefficient: 'lossCoefficient', entranceloss: 'lossCoefficient', exitloss: 'lossCoefficient', kin: 'inletLossCoefficient', kout: 'outletLossCoefficient',
  mannings: 'manningRoughness', usetwophasemannings: 'twoPhaseMannings', manningsbot: 'bottomMannings', manningsswitchdepth: 'manningSwitchDepth',
  anypayloadsusefixedstarttime: 'payloadFixedStartTime', barrelpropertyname: 'barrelPropertyName', upstreamopeningname: 'upstreamOpeningName', downstreamopeningname: 'downstreamOpeningName', openingtype: 'openingType',
  rotationdegrees: 'rotationDegrees', biasinternal: 'internalBias', multipoint: 'multipoint', tinresolution: 'tinResolution', sizetolerance: 'sizeTolerance',
};

function fieldKey(value: string): string {
  const tail = value.split(/[./\\:]/).at(-1) ?? value;
  return tail.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[^a-z0-9]/gi, '').toLocaleLowerCase();
}

export function gfield(language: Language, id: string, title?: string): string {
  if (language !== 'zh') return title || id;
  const normalizedID = fieldKey(id);
  if (normalizedID === 'x' || normalizedID === 'y') return normalizedID.toUpperCase();
  const candidates = [title, id].filter((value): value is string => Boolean(value));
  for (const candidate of candidates) {
    if (/[\u3400-\u9fff]/.test(candidate)) return candidate;
    const key = fieldLabels[fieldKey(candidate)];
    if (key) return gt(language, key);
    const sourceTitle = candidate.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_/\\]+/g, ' ').trim();
    const resourceLabel = editorText(language, sourceTitle);
    if (resourceLabel !== sourceTitle) return resourceLabel;
  }
  return '字段';
}

export function gerror(language: Language, value: unknown): string {
  const raw = value instanceof Error ? value.message : String(value ?? '');
  if (language !== 'zh' || !raw) return raw;
  const message = raw.replace(/\s+\[[A-Z0-9_.-]+\]$/i, '');
  if (/后端尚未发布操作：[a-z0-9._-]+/i.test(message)) return '当前服务端尚未提供所需的几何操作。';
  if (/^[\u3400-\u9fff]/.test(message)) return raw;
  const resourceLabel = editorText(language, message);
  if (resourceLabel !== message) return resourceLabel;
  if (/earlier project session/i.test(message)) return '此操作属于较早的工程会话，已取消。';
  if (/topology draft is not loaded/i.test(message)) return '当前几何的拓扑草稿尚未加载。';
  if (/topology response revision does not match/i.test(message)) return '服务端返回的拓扑结果与当前源版本不匹配，请重新读取几何数据。';
  if (/mesh draft is not loaded/i.test(message)) return '当前几何的网格草稿尚未加载。';
  if (/mesh response revision does not match|generation response belongs to a different source revision/i.test(message)) return '服务端返回的网格结果与当前源版本不匹配，请重新读取几何数据。';
  if (/culvert draft is not loaded/i.test(message)) return '当前几何的涵洞草稿尚未加载。';
  if (/culvert response revision does not match/i.test(message)) return '服务端返回的涵洞结果与当前源版本不匹配，请重新读取几何数据。';
  if (/saving geometry requires/i.test(message)) return '保存前需要加载几何数据、源版本和所属工程。';
  if (/there are no geometry changes to save/i.test(message)) return gt(language, 'noChanges');
  if (/local-only table changes cannot/i.test(message)) return gt(language, 'saveBlocked');
  if (/topology draft state is missing/i.test(message)) return '缺少拓扑草稿状态，无法保存。';
  if (/mesh draft state is missing/i.test(message)) return '缺少网格草稿状态，无法保存。';
  if (/culvert draft state is missing/i.test(message)) return '缺少涵洞草稿状态，无法保存。';
  if (/Failed to fetch|NetworkError|fetch failed/i.test(message)) return '无法连接后端服务，请检查服务状态和网络连接。';
  if (/not available|unavailable/i.test(message)) return '当前功能暂不可用，请检查服务端支持的操作。';
  if (/must be greater than zero|positive integer/i.test(message)) return '请输入大于零的有效数值。';
  if (/aborted|cancelled/i.test(message)) return '操作已取消。';
  return gt(language, 'unknownFailure');
}

export function gmode(language: Language, value: string | null | undefined): string {
  if (!value) return '—';
  const known: Record<string, keyof typeof labels.en> = { constant: 'constant', variable: 'variable', linear: 'variable', function: 'variable' };
  const key = known[value.toLocaleLowerCase()];
  return key ? gt(language, key) : language === 'zh' ? gt(language, 'custom') : value;
}

export function gchoice(language: Language, value: string): string {
  if (language !== 'zh') return value;
  const known: Record<string, string> = {
    true: gt(language, 'yes'), false: gt(language, 'no'), yes: gt(language, 'yes'), no: gt(language, 'no'),
    circular: '圆形', circle: '圆形', rectangular: '矩形', rectangle: '矩形', arch: '拱形', elliptical: '椭圆形',
    constant: gt(language, 'constant'), variable: gt(language, 'variable'), linear: gt(language, 'variable'),
  };
  return known[value.toLocaleLowerCase()] ?? editorValueLabel(language, value);
}

export function gdiagnostic(language: Language, value: string): string {
  if (language !== 'zh' || /[\u3400-\u9fff]/.test(value)) return value;
  return editorText(language, value);
}
