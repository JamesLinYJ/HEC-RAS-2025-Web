# UI 状态与服务契约

这份设计说明与 [UI_RELATIONSHIPS.json](UI_RELATIONSHIPS.json) 区分当前 React 实现与迁移时需要保留的来源契约。它们描述状态、关系和持久化边界，不是测试日志或完成率统计。当前产品架构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 当前 React 调用关系

`RasMainWindow → RasContent → 功能页 → BackendGateway / ServerBackend → ASP.NET Core 操作队列 → C# 工作进程 → 领域模型 / 算法`

浏览器与 Electron 使用相同的 React 页面及 HTTP 网关。`RasMainWindow.tsx` 创建 `ServerBackend`、管理语言、活动页面、工程上下文与未保存草稿所有者；`RasContent.tsx` 在切换页面时保留已挂载页面，计算页首次打开后也继续保留其会话。默认语言为简体中文，显式切换英文后保存语言偏好。

| 当前领域 | 实现入口 | 状态与提交边界 |
| --- | --- | --- |
| 工程与资产目录 | `features/project/ProjectExplorerControl.tsx`、`ProjectTreeMode.tsx`、`ProjectOpenProjectDialog.tsx` | 打开和保存采用真实 XML 与修订；目录刷新重新绑定选择，不能清空活动模型。快速 ZIP/核心 HDF 准备由 `QuickProjectImportControl` 适配到现有工程打开流程 |
| 模型表单 | `features/editors/EditorWorkbench.tsx`、`modelDraft.ts` | 草稿绑定工程会话、源文件与修订；保存只提交实际变化并采纳匹配源身份的回执 |
| 几何、拓扑与涵洞 | `features/geometry/index.tsx`、`useGeometryWorkbench.ts` | 表格、撤销历史及子编辑属于当前几何草稿；父保存才提交模型 |
| 地图与图层 | `features/map/MapFeaturePage.tsx`、`RasProjectMap.tsx` | 工程地图与本地 GeoJSON 预览具有不同持久化边界；本地导入和绘制不自动成为已保存工程资产 |
| 结果与用户序列 | `features/results/ResultsWorkspace.tsx` | 服务端 HDF 读取与本地 CSV / JSON 明确区分；本地单元格草稿在切页时保留，导出前校验 |
| 空间数据工具 | `features/tools/index.tsx` 及各工具组件 | 能力来自服务端公布的操作集合；参数校验、执行与输出文件需要分别确认 |
| 计划计算 | `features/compute/ComputeProofPage.tsx`、`OperationResultControl.tsx` | 完整源模型先检查，再消费一次检查身份启动计算；取消等待服务端终态。进度与流式 planMessages 属于唯一运行状态，终态消息优先展示；Host 未提供暂停/继续，因此按钮禁用 |
| 桌面与浏览器能力 | `desktop/main.mjs`、`frontend/src/platform/ServerBackend.ts` | Electron 管理自己启动的服务进程；浏览器通过 HTTP 上传工程，不把用户设备文件路径误当服务器路径 |

`WorkbenchContext.projectRevision` 标识 `.ras` 文件，`selectedAsset.revision` 标识模型或结果；保存工程不能覆盖模型修订。`onContextCommit` 只允许活动上下文采纳对应源的保存回执。`onDirtyStateChange` 汇总草稿所有者，工程切换与关闭须经过相应处理。

HTTP 操作的受理 ID、传输状态、领域任务 ID、进度与终态分别管理。终态仍可能带有未读 `hasMore` 分页，网关须排空后才能读取结果或错误。取消请求的 HTTP 回执只证明请求已送达；只有后续操作终态能确认取消。`BackendGateway.resumeOperation → ServerBackend.trackOperation` 只用已受理的 HTTP ID 继续轮询，`SessionBackend` 原样转发该能力，不重发原写入 POST。mesh 导出保留领域 operation ID 与 HTTP tracking ID 的对应关系；前者用于源捕获/领域回执，后者用于传输恢复，不能互换。R2R 的本地任务 ID、检查用 `inspectionID` 和 HTTP ID 也各有独立职责。mesh/R2R 导出在状态失联、受理响应缺少 ID 或领域回执无法验证时保留原捕获及写入保护；没有 HTTP ID 就不能安全恢复查询。`terminalConfirmed` 区分已确认失败/取消与未知状态，取消竞态仍可收到并采用真实完成回执，不能凭取消按钮宣称回滚。

当前地图由 `MapFeaturePage` 持有地图文档和画布相机，`usePersistedVectorLayers`、`usePersistedRasterLayers`、`useMapResultFrames` 分别读取已勾选的真实矢量、栅格与结果。C# 返回的工程坐标与浏览器直接读取的源坐标具有不同标记；未确认坐标系的本地预览不能与工程图层错误叠加。手动 Fit visible layers 使用可见矢量/GDB 几何范围、持久栅格和关联图层的来源范围及实际 Result 源范围，不以渲染图像 viewport 作为数据范围。 相机按工程会话、工程路径和地图 owner 记录用户控制：手动定位、缩放、适配、平移或开始地图编辑后，后台 Result/来源就绪不再触发自动适配覆盖当前视角；切换工程或地图后恢复首次自动适配。持久化矢量与 GDB 图层由 `useMapVectorRendering` 按精确来源和当前视口请求 C# 绘图，再由 `RasProjectMap` 将 RGBA 帧和返回标签覆盖在透明源几何 hit targets 上；其它实际要素与图像仍在同一工程坐标画布。`MapTimeAxis` 控制同一份结果读取状态，不再创建第二个结果会话。Result 首帧读取全幅范围，之后才按相机视口请求；时刻采用原生 profile 索引与时间，播放等待上一帧确认完成。结果历史路径与修订是当前会话的显式选择，不因 `.vis` 保存就假定为持久化选择。`ResultsWorkspace` 的 `ResultProfileMeshPicker` 通过 `result.profileMap` 读取当前 Result HDF 的嵌入 cell/face 几何；选择保持原 Result 内部 ordinal，再交给已有 `result.profile` 读取该元素的时间序列，不以工程 Geometry 替代，也不写模型或地图。

`useMapHoverIdentify → map.hover.identify → MapHoverIdentifyService → MapDynamicProfileService.SampleForMapNode` 使用同一个已确认的 Result 帧所有者。MultiResult 请求传 slot 0 时间轴索引，响应保留各子来源按时间匹配后的原生 profile 索引；`mapResultHoverIdentity` 只接纳与当前绘制帧路径、修订、类型和原生时刻一致的标签。Host 按具体节点重新绑定共享的 Result 来源，避免普通 Result 与 MultiResult 同时可见时串用时刻。更换节点、来源或时间先清除旧标签，迟到响应不能恢复它；鼠标离开和隐藏来源也使捕获失效。

结果图层的显隐使用 `MapLayerTree → map.tree.perform(visibility) → MapTreeService.SetSourceVisibility`，保存回执确认后才更新树。原 `RasTreeBase.SerializeDefault` 无论是否勾选都保存 Type/Name，因此 Result、MultiResult、Terrain、NValue 和 Classification 取消勾选后仍是当前地图成员，必须保留精确 locator 并可重新显示。Web 的“从此地图移除”是独立的 map-scoped `removeSourceNode` 事务：仅允许来源身份已捕获的普通矢量和栅格 MapLayer，从当前 `.vis` 树移除单个节点，不删除磁盘源文件或其他地图中的引用。这与全局/工程资产级 Remove 的范围不同；未知或未采纳的回执必须先按精确地图及来源重读对账，不能盲目重试。显隐或移除提交会使相关旧渲染帧失效；同一工程会话与地图的显式比较来源仍保留，并由服务端对新 `.vis` 修订重新验证 locator、路径与 SHA。

浏览器的“导入工程图层”经 `ServerBackend.uploadMapSources` 把用户选择的 GeoJSON、完整 Shapefile 文件族或自包含 GeoTIFF 放入工程 `Map Sources` 的独立目录；工程 SHA、会话和文件名先校验，文件夹发布后再逐项进入原有地图导入事务。每次 `.vis` 提交都推进修订，失败后先重读地图再允许继续写入。上传成功与地图导入成功分别处理；已复制的来源不会因后续失败而冒充已保存地图。直接“导入 GeoJSON”和绘制仍属于浏览器草稿。刷新图层只重新读取工程地图；切换到新计算结果时也会刷新来源树。

工程树的 Geometry / Plan 复制采用 `project.modelAction.inspect → perform`：捕获工程 XML/SHA、节点身份与原 HDF SHA，使用原 `DuplicateBySaveAsH5` 生成暂存副本，核对后发布并提交工程关联。复制与 `project.save` 共用本机跨 worker 的写锁；回执只有在文件与工程关联均确认后才更新上下文。右键仅更新树内选择，菜单“打开对象”与双击会显式进入相应编辑页；复制当前已保存文件不会把编辑器草稿写进副本。

组件入口存在、schema 字段可渲染及后端公布操作都不等于全部交互已接通。每条来源契约仍需用对应 React 操作与实际数据单独核验。

## 同名控件的挂载与保存边界

| UI 入口 | 子控件 | 真实数据与提交 |
| --- | --- | --- |
| `BoundaryConditionLineEditorPage` | `DetailedBoundaryConditionHeader`、`BoundaryConditionLineAndPayloadPickerPage`、`BCLinesLayerActiveFeatures`、`BCLineLayerLastSelectedFeatureLineEditor`、`BoundaryConditionLineEditor` 及各载荷编辑器 | 线列表按快照 GUID 本地选择；载荷变更经 `model.boundaryTransform` 留在父草稿，显式保存才提交 HDF |
| `TerrainLayersEditor` | 栅格、函数地形、渠道与 Levee 属性编辑器 | 按选中输入的 schema、scope 和实际表渲染，沿用字段与单元格草稿回调 |
| `GeometryEditor → GeometryTopologyArcsPage / GeometryTopologyRegionsPage / GeometryTopologyNodesPage / GeometryMeshPage / GeometryCulvertsPage` | 拓扑页挂载 `TopologyArcActiveFeatures`、`TopologyNodeActiveFeatures`、`TopologyControlPointsActiveFeatures`；nodes/arcs/regions 页共用 `useGeometryTopologyMap → GeometryTopologyMapPanel → GeometryTopologyCanvas`，弧页另挂 `TopologyMapArcTools`、SmartAdd、DeepConnect 状态与已保存矢量来源控件；网格流动区域和涵洞 barrel 页挂载 `FlowAreaLayerActiveFeatures`、`CulvertBarrelLayerActiveFeatures`，表格共用 `ActiveFeaturesControl`。 | 筛选与选中按稳定源行 ID/FID 工作；节点详情坐标只读，地图编辑进入父 Geometry 草稿并由 `model.save` 持久化。SmartAdd 支持多途经点与只读路径预览；DeepConnect 支持当前拓扑弧、Host 检查确认的持久 GeoJSON/Shapefile 组件，以及 GDB Line/LineCollection 组件；GDB Polygon/Point 仅返回诊断，不生成可连接组件。GDB 以目录 manifest revision 捕获，primary SHA 和 sourceFamily 为空；详细来源、草稿与 Host token 边界见专用关系表。
| `ProjectTreeModeReactView → RasCatalogTreeMode → FilterSearchBar` | Project 与 Catalog 各有独立搜索/分类/折叠/选择状态；搜索在当前 Host 树中过滤名称、来源类和 asset ID 并保留匹配祖先。单击通过 `onSelect` 选择资产并保留当前页；双击或显式打开通过 `onOpen` 携带目标功能页，脏上下文确认期间保留该导航意图。 | `project.tree.load` 与 `catalog.tree.load` 仍是不同目录；过滤、选择和页面导航只更新本地工作区状态，不写工程或地图文件。无 assetID 节点仍按工程会话和节点路径身份恢复。
| `SchemaFieldRenderer` | 文本、日期、数字、下拉、段落、颜色及 `PlanTimeWindowModePickerPropertyItem`；`Labeled` 展示字段标签/帮助/条件与 disabled，schema group 使用 `CollapsibleContainer → ControlHeader` | 原始字段键、只读、条件显示及中文验证由 schema 分派；Plan 模式值仍进入父模型草稿。TriState 仅在运行时 schema 的 `mixedBy` 与同 scope 快照诊断值都存在时显示混合态；诊断标记只读，交互只写原字段键的 true/false。 |
| `TimeSeriesPayloadChart` | `ChartView`、`ChartPlot`、`ChartAxisControl`、`ChartLegend`、`ChartScrollbar` | 使用真实序列、时刻和稳定 series ID，空值保持断线，范围经父回调更新 |
| `ResultsWorkspace → ResultProfileMeshPicker` | 当前 Result、活动变量和 flow area 就绪时，`result.profileMap` 从该 Result HDF 读取 Cell/Face 网格形状；鼠标或键盘选择返回原始元素 ordinal。 | 选中 ordinal 交给原有 `result.profile` 时间序列读取链路。网格范围超过 Host 元素/顶点上限时返回不可选的完整性诊断，不展示局部伪完整网格；结果绑定 Result 路径/SHA、flow area、类型与请求代次。只读嵌入网格、只更新 ResultsWorkspace 本地选择，不写 HDF、工程或地图。 |
| `SourceManagerControl` | 核心图层、Terrain 和 Classification 导入器 | 来源检查与写入分别执行，保留工程会话、规范路径、来源修订及保存回执 |
| `ImportExportToolControl` | `PlanExporterControl`、`RasterImporter`、`DepthShapeExporterDialog`、`RgbTiffExporterDialog` | 计划与栅格工具沿各自事务运行；Depth/RGB 先检查和预览，点击后才让 Host 写服务器输出路径 |
| `GeoprocessingToolControl` | `ReprojectionToolControl`、栅格属性摘要、`GeoprocessingVectorSourcePicker`、`GeoprocessingPointSourcePicker`、`GeoprocessingPolylineExecutionDetails` 与 `geoprocessingMapLayerWorkflow` | 已保存地图来源按地图树 locator、工程/地图/`.vis` 修订、完整来源身份捕获；折线支持 GeoJSON 与完整文件族的 Shapefile，多边形仅支持 GeoJSON。原 C# `DefaultOutputHandlers.MapLayerOutput` 对应的地图输出现接入可捕获精确地图且实际生成 Shapefile 的 Point/Polyline/Polygon 向量分支；Raster/TIN 仍禁用。此处记录源码接线，不代表运行验收 |
| `ToolsPageView → tools-tab-raster-calculator → RasterCalculatorEditor` | 计算器页拥有配方草稿、保存地图/来源捕获与预览；`useRasterCalculatorSessions` 在当前工程会话登记临时计算图层，`MapFeaturePage → MapLayerTree` 展示图层控制，动态层时刻由 `CalculatorProfileStateSlider` 控制。 | Host `inspect/evaluate` 按真实来源修订计算 RGBA 帧；Add to Map 与 Save to Existing 只更新工程会话内存图层。替换按 layer ID/revision、精确地图身份和静态/动态类型校验，保留现有 ID、名称、显隐与透明度。帧按真实地图视口与 profile 重算；C# `RasterDefinition` 以正方形像元按左上角对齐并可能扩展返回范围，前端校验该规范化范围并按回执实际范围绘图；隐藏、离开当前地图或身份失配会清除帧并取消过期请求。编辑请求带图层修订；计算器草稿未完成时由用户取消、暂缓或放弃草稿后载入，并消费对应请求。会话仅限当前打开的工程会话，不跨重启保存，也不写 `.ras`、`.vis`、模型 HDF、来源栅格或计算像元。记录源码接线，不代表构建或运行验收。 |

控件拆分不自动提供原 UI 的全部命令。父级尚未提供的结构编辑、混合值重置、地图几何操作、结果捕获或会话引用事务，必须保持明确的能力边界，不能显示成功回执。`workspace.file.inspect` 为用户明确输入的服务端文件路径捕获规范路径和 SHA；核心图层检查与复制继续验证该捕获，不要求用户手填 SHA。

关联地图层由 `useAssociatedMapLayers → map.associations.load` 读取。所有者来自当前所选 Result 的 `treeRoots`，Geometry、Plan、Result 与分类/NValue/Terrain 关联节点保持各自 locator。图像沿返回的 world viewport 绘制，实际像素尺寸受聚合预算限制；鼠标识别携带该帧的来源 SHA、依赖、所有者及上下文。相机、显隐、来源或会话变化先使旧绘图/识别帧捕获失效；地形测量另以 semantic source scope 判定是否过期，关联帧短暂缺失不会单独取消相同来源的请求。

地形 HDF 的文件 SHA 与动态剖面的完整依赖摘要是不同身份。鼠标识别先核验 HDF SHA，再按 HDF、栅格和 sidecar 的依赖摘要匹配剖面；所有采样完成后再次核验依赖。前端回执沿用绘制帧所捕获的 HDF SHA，不把依赖摘要误当文件 SHA。

分类输入页由 `ClassificationLayersEditor` 按输入类型挂载栅格映射、Polygon 属性和 `ClassificationGeometryEditor`。多边形在使用当前工程坐标的专用 SVG 中选择，不依赖主地图点击；创建、编辑顶点、删除、复制/粘贴、撤销/重做经 `model.classificationTransform` 进入父 `ModelDraft`，父 `model.save` 才写 HDF。分类来源检查使用 `model.classificationInspect`，用户明确确认后 `model.classificationImport` 立即保存 HDF 并返回新模型修订，不进入普通草稿保存；失败或取消但无最终回执时需重新载入核对。映射表行 ID、整数 Key 和行数均由来源固定，只允许编辑 Value；原 C# `ObservableClassificationMapRow.Key` 为 getter-only，集合变更处理拒绝增删，因此 UI 不提供改 Key 或增删行，这不是遗漏的写操作。`model.classificationMap` 读取当前草稿的栅格与图例；其透明度滑块只改本地预览。糙率页由 `ManningsNEditor` 切换常数值或分类解释表。分类关联变更会读 `model.manningTransform(refresh)` 投影，`setNLCD` 只更新名称匹配的已有行且保留其余值；Key、行 ID 和行数固定，只有 Value 可编辑。常数、关联和解释表普通编辑留在 ModelDraft，由父 `model.save` 保存；`nvalue.preview` 只对干净的已保存修订提供只读栅格、图例和直方图预览。空间时序以 rows 为权威，同一 table-backed 序列的 samples 仅作图表投影，不在结构变换和保存时重复提交。新增测站保留原行、测站和 payload GUID 身份。

拓扑页分别挂载 `TopologyArcsEditor`、`TopologyRegionsEditor` 和 `TopologyNodesEditor`。弧段标签、点坐标对话框、快速缩放与 Recipe 操作都属于当前可撤销草稿。概念网格区域页通过 `TopologyRecipeBuilder → RecipeBuilder` 使用选中 control-point FID 调用 `topology.recipe` 检查并以 `topology.edit/applyRecipe` 应用；请求与回执核对 revision、draftState、draftIdentity 及 FID，结果归父 Geometry 撤销/重做草稿，只有父 `model.save` 写 HDF。编辑现有 Recipe 步骤时，原始参数均保留；来源参数白名单只控制可见字段和新步骤默认值，序列化按来源顺序写可见参数后追加原有隐藏参数，避免编辑或重排时丢失算法值。Host 未提供描述的未知参数仍保持只读。`RasCatalog` 的树行、树模式及详情面板共享现有选择和展开状态，不建立第二份工程会话。地图时间轴的结果选择、比较槽和滑条共享唯一 `useMapResultFrames`，首尾帧导航仍使用服务端 step ID。

`MapFeaturePage` 只把已确认连续栅格帧的精确 `rasterSource` 传给 `RasterSymbologyEditor`，直方图、颜色带、Plot Options 与应用调用真实符号服务；`.vis` 提交回执必须匹配请求的上一修订和来源 SHA。绘制器属性只通过 Host 提供的 descriptor 暴露，标准样式字段与 renderer-only 选项按各自保存格式持久化。水力工具页的 Wilcock 控件与单位计算器分别调用 `wilcock.load/evaluate` 和 `unitCalculator.load/convert`；数值运算留在 C#，前端只管理输入、工况、请求与真实结果显示。


## 专用界面的挂载与数据边界

`ProjectExplorerControl` 分别加载 `project.tree.load`（Project 模式）与 `catalog.tree.load`（Catalog 模式）；两棵树服务不同目录视图。外部连接节点只在 Catalog 树中枚举，连接提交回读也必须走 Catalog 树，不能用 Project 树验证。

| UI 挂载 | 状态所有者与读取 | 写入或只读边界 |
| --- | --- | --- |
| `ProjectExplorerControl → ProjectExplorerMainPageTiled → FirstLaunchScreen / NewProjectPage / ProjectOpenProjectDialog` | 首页动作由工程工作区持有；最近工程仅把成功打开的服务器路径存入浏览器 `localStorage`（最多 12 项）。新建表单的 `NewProjectDraft` 是弹窗草稿，父目录初始为空。 | `projection.evaluate` 只验证；`project.create` 需要用户输入 C# 服务主机上的绝对父目录，不接受浏览器本机路径。只有完整创建回执且原工程会话仍匹配时才切换上下文；没有持久化的默认起始目录。 |
| `ProjectExplorerControl → ProjectTreeMode → ProjectTreeModeReactView → DefaultRasCatalogNode → RasCatalog` | `ProjectTreeMode` 持有当前 Project/Catalog 树状态，`ProjectExplorerControl` 持有选择资产；`RasMainWindow` 持有活动页、脏上下文守卫和待处理导航选项。单击对应原树 `onSelect`，双击/“打开对象”对应 `onOpen`。 | `ContextChangeOptions.navigation` 支持 `preserve`、显式 `FeatureId` 与 `auto`。单击选择默认用 `preserve`，不离开当前页；显式打开按目标资产类型设定目标页。若有脏 owner，`RasMainWindow` 将 target context 与导航选项一起暂存；保存/丢弃后才提交并导航，取消则保持原上下文/页面。树选择和导航均不写项目数据。此处是原树选择/打开职责的 React 接线，不计为新的 UI inventory 项。
| `MapFeaturePage → MapDocumentTabs → NewMapFlyout` | 工作区工程会话/XML/修订与当前地图 `.vis` 修订是请求所有者；地图类型、菜单与请求代次是弹层本地状态。 | `map.document.create` 以工程 XML SHA 和预期 `.vis` 修订创建保存地图；地图类型、索引、名称及前后修订匹配并完成树读取后才安装新标签。写 `.vis`，不代替工程 `.ras` 保存。 |
| `MapFeaturePage → MapDocumentTabs → MapDocumentActions → mapDocumentActionsWorkflow` | 页面捕获当前工程会话、XML/修订/Core 引用、精确地图身份及操作前完整目录；`MapVisualizationTarget.documentMutation` 预留重命名目标或删除后的完整索引映射，提交由 `RasMainWindow` 的地图写入 lease 保护。 | `map.document.rename/delete` 写 `.vis`，类型化 `MapVisualizationAcknowledgement` 经父层采纳后还须重读完整 catalog/tree 并核对。删除仅移除地图文档；未确定结果保留 dirty owner 与地图写入保护，先读回核对。受影响地图含临时计算器图层时禁用操作，详见下文。 |
| `MapFeaturePage → VectorSymbologyEditor` | 旧条件规则检查/求值预览控件，绑定已验证的 GeoJSON 来源、活动地图节点、路径与来源 SHA；它与下方 `VectorLayerStyleEditor` 的地图绘制/样式保存是两条独立流程。 | `vector.symbology.inspect/evaluate` 只检查属性并返回 C# 条件求值预览，不写文件或把结果应用到地图；本控件没有 `.vis` 样式保存回执。 |
| `GeometryEditor (nodes/arcs/regions) → useGeometryTopologyMap → GeometryTopologyNodesPage / GeometryTopologyArcsPage / GeometryTopologyRegionsPage → TopologyNodesEditor / TopologyArcsEditor / TopologyRegionsEditor → GeometryTopologyMapPanel → GeometryTopologyCanvas; arcs also mount TopologyMapArcTools, TopologySmartAddControls, TopologyDeepConnectControls and TopologyDeepConnectVectorControls` | `useGeometryTopologyMap` 从保存地图目录/树按精确 Geometry locator 捕获地图；`map.geometry.load` 固定地图、`.vis`、Geometry 路径/修订与实际视口。Canvas 以 `topology.load` 回执呈现拓扑，并可叠加当前选择的 Host 检查确认矢量组件和只读路径预览；节点/弧表通过稳定源 FID 同步选集。`topology.arcPoints` 绑定唯一选中弧的来源点序列、arc FID、revision 和 draft identity；`useGeometryVectorSources` 管理当前保存地图中已勾选且有效可见的线/面来源检查及选中组件。 | `topology.spatialSelect` 按世界坐标、实测 `MapCellSize` 和源 FID 返回地图选集。节点拖动与八个普通弧操作 `addArc`、`moveArcPoint`、`insertArcPoint`、`deleteArcPoint`、`splitArc`、`reverseArc`、`filterArc`、`smoothArc` 经 `topology.edit` 的 `mapCommand` 进入父 Geometry 草稿/撤销历史。SmartAdd 要求当前拓扑中的关联 Terrain 捕获、同一 Geometry/map owner、真实相机窗口、ResizeObserver raster 行列和正 `MapCellSize`；界面可编辑原始 13 项参数（含 `PreviewThrottlePixels`，默认 1.5）。它接受 2–512 个有序起点/途经点，使用只读 `topology.mapCommand.preview` 显示 Host 路径；Host 对每一相邻点对调用保留的 C# Terrain 算法并在单个请求作用域复用栅格/采样工作，再连接为一个弧段。预览模拟最终 AddArc/Complete 与节点/弧段捕捉，返回最终连接弧、端点 FID 和规范路径 SHA；该 hash 对应完成捕捉后的最终连接弧路径，不是 Terrain sampler 的原始路径。用户完成时命令携带 `expectedPathSHA256`，Host 重算并比对后才把一个结果写入父拓扑草稿；预览自身不改草稿或撤销历史。DeepConnect 可选当前 Geometry 拓扑弧、当前保存地图中 Host 检查确认的折线/多边形来源组件，或已勾选且有效可见的 GDB Line/LineCollection 图层中 Host 返回的线组件。`topology.vectorSource.inspect` 按精确 map/tree locator 和 `.vis` 修订捕获来源。GeoJSON/Shapefile 返回 canonical 路径、主文件 SHA 和完整文件族；GDB 返回 canonical `.gdb` 路径、`GdbLayer` 描述与 `GdbSource` 完整目录 manifest（目录 revision 及每个非锁文件的相对路径、长度和 revision），不提供单文件 source SHA 或 `sourceFamily`。GDB 组件编号复用 `GdbMapSourceService.EnumerateRendererLineComponents`，与实际 renderer 一致，包含 renderer FID、`pathIndex=0`、`componentIdentity=feature:<FID>:part:0`、来源 feature/part 序号、source ID 与坐标；请求的 `sourceRevision` 使用目录 manifest revision。GDB Line/LineCollection 有 Host 返回组件时可连接；GDB Polygon/Point 和 Host 不支持的几何只给诊断，不能猜测 Polygon ring 是 DeepConnect 线组件。DeepConnect 接受同一来源组件上的 2–512 个顺序 segment/fraction 锚点，Host 重读并复核相应的 GeoJSON/Shapefile 文件族或 GDB 目录 manifest 与图层描述、FID/component identity 和全部锚点。`topology.mapCommand.preview` 只读重放最终 AddArc/Complete 捕捉逻辑，回传最终连接弧路径及规范 hash；最终 `topology.edit` 携带对应 `expectedPathSHA256`，不匹配即拒绝。SmartAdd 和两类 DeepConnect 结果均沿同一个父 draft/undo/redo，只有父级 `model.save` 写 Geometry HDF；inspect/preview/edit 不改矢量源文件或地图 `.vis`。UI/Host 在工程会话、地图/`.vis`、Geometry source revision/draft identity、矢量来源文件族/SHA 或 GDB 目录 manifest revision 与请求代次上做身份校验；Host 仍未独立证明 `contextToken` 由先前 `map.geometry.load` 签发。弧点仍由 `topology.arcPoints` 与来源 `internalPointIndex` 操作，插入/分割按 Host 路径顺序索引；共享内部顶点选最低 source segment index，歧义不猜。原 UI 职责对应 `AddNewArcHandler`、`SmartAppendLineFeaturePointHandler`/`BuildTopologySmartAddRouter`、拓扑 DeepConnect router/`DeepConnectTraceAppendHandler` 与已有弧编辑 handlers。新增控件、来源选择器、hooks、canvas preview 和 Host request adapter 是接线胶水，不另计原 C# UI inventory；这里只记录源码关系，不声明运行验收。
| `MapFeaturePage → map inspector → MapMeshTimeSeriesPanel / MapCulvertAnalysisPanel / MapSampleLineAnalysisPanel` | 三个懒加载面板共享 `MapFeaturePage` 从当前 `useMapResultFrames` 树立的不可变 Result 捕获：工程/会话/XML SHA、地图和 `.vis` 修订、map context token、活动 Result 节点、完整选择覆写、slot-zero Result 及当前 profile/render snapshot。mesh 面板另消费已捕获的 Result 内嵌 Geometry/FlowArea 与当前 hover 命中；采样线面板仅消费当前选中的、已勾选且已保存的 `PolylinesVector` 要素及主来源文件 SHA。 | `map.result.meshTimeSeries.inspect/execute` 以 Host 检查返回的 FlowArea 身份为选项，Cell 读取必须匹配原输出变量名 `Cell Depth`，Face 读取必须匹配 `Face Velocity`；这是索引时序，不是等值线/轮廓生成。执行端收到成功 HTTP 返回后，若本地解析回执失败，状态是已终态失败；只有尚未确认终态的传输才标记结果未确认。mesh 时序读取及采样线的 CumulativeVolume 积分通过 `ResultPostProcessReadScope → PostProcessor.CreateReadCache` 使用独立临时目录；处理作用于该只读缓存，释放时关闭 HDF 并清理目录，原 Result/HDF 不变。采样线 Flow 模式不据此推断会写共享缓存。`map.result.culvertProfile/culvertTimeSeries` 读取 Host 给出的涵洞 barrel 身份及保存的流量/速度数据；本批没有涵洞正向 Flow/Velocity 输出及方向的 fixture 验收；源码接线不构成数值或符号验收。`map.resultSampleLine` 仅对明确选择的单个线要素/单个 MultiLineString part 请求 Flow 或 CumulativeVolume，保留 Host 时刻、单位、NoData 与副作用回执；Host 绑定路径/主 SHA/坐标哈希，但不验证 featureID 是否存在于来源记录表。采样线 DTO 仍返回 `ResultPostProcessCacheMayBeCreatedOrUpdated`，这是按预期默认缓存路径生成的保守副作用声明；回执本身不证明本次写入或改动了共享缓存。上述分析不保存 `.ras`、`.vis` 或源 Result/HDF。原采样线图职责最接近 `DynamicTimeSeriesProfilePlot`/`DynamicProfilePlot` 和 Geometry 弧/涵洞线来源；mesh 跨 Result 时序与存储涵洞结果读取没有直接对应的原 UI 类，是新的 Host 集成胶水，不作为原始 UI 项计数。此处仅记录源码接线，未声称这些面板已通过运行验收。 |
| `MapFeaturePage measureLength → useTerrainElevationMeasurement → TerrainMeasurementReadout` | 捕获键区分 semantic source scope 与 `map.associations.load` 帧证据。`associationContextToken` 只作 frame provenance，不参与来源失效 key；短暂缺帧时保留同 scope 请求/回执。 | 真实工程/会话/地图/目录/可见性与选择、Terrain/core 来源依赖或 Result owner 变化才取消旧测量。请求/回执按 catalog token 校验；source HDF SHA 与复合依赖 SHA 分开比较。Host 将非有限采样值或等于 Terrain `NoDataValue`（如 -9999）的值转成 null；端点有 null 时不计算纵坡。平面长度保持可用，坡度取末两点 `(前点高程 - 最新点高程)/末段长度`。测距与测面积点是临时输入，不触发持久草稿的 dirty notice 或父级待保存状态；普通绘图、图层和背景修改仍保留原保护。只读取样，不保存工程或 HDF。
| `ProjectTreeModeReactView → RasCatalogTreeMode → RasCatalogInfoPane (ObservableTerrain) → TerrainCatalogSummary / TerrainRasterExportPanel` | 面板仅在 Catalog 树选中 `ObservableTerrain` 时显示；它持有像元尺寸和该目录项的导出操作状态。启动前必须捕获一致的工程会话、Terrain 文件 SHA、核心图层身份，并确认没有未保存草稿。 | `workspace.file.inspect` 核对工程与 Terrain，`model.load` 捕获 Terrain 后再次校验两者；`terrain.raster.export` 提交空字段/行变更的只读源请求。Host 使用来源 Terrain 的有效范围、投影和 overview writer，并在工程默认导出目录分配唯一 TIFF 路径；UI 不选输出路径、不下载到浏览器，也不提交 `.ras`。完成回执后回读输出 SHA/长度并复核工程/Terrain 未变；取消可能留下明确标记的部分 TIFF，不显示为成功。该 React 面板是原 Terrain 全栅格导出行为的工作流胶水，不另计原 C# UI 项；仅记录源码接线，未声称运行验收。 |
| `MapFeaturePage → useMapVectorRendering → RasProjectMap vectorFrames; map inspector → VectorLayerStyleEditor` | 只捕获当前地图中已保存且身份、来源 SHA 和 `.vis` 修订均匹配的矢量 / GDB 图层；当前视口、精确树 locator、地图和工程会话随检查及绘图请求传递。`VectorLayerStyleEditor` 持有选中图层的基础样式和 Plot Options 草稿，`MapFeaturePage` 持有临时视图预览。 | `map.vector.symbology.inspect` 读取来源与已保存基础值，`map.vector.plot.render` 通过 C# 绘制器返回带标签的 RGBA 帧；临时预览只改变当前地图画面。`map.vector.symbology.save` 仅把基础样式稀疏写到对应 `.vis` 节点，核验源修订、style SHA 与全局修订，保留原条件及未知 XML，并通过匹配的地图修订回执推进地图目录、清旧捕获后重绘且保留相机。绘图选项只用于临时 renderer，不写 `.vis`。条件规则仅在 Host 能把它们绑定到已验证的 GeoJSON source FID 时参与绘制；不支持的条件、来源/ID 不匹配或未知符号 XML 会显示警告、保留几何预览，并禁用带样式绘制和临时预览。已确认绘图帧作为图像覆盖在源几何 hit targets 下，标签由 renderer 返回。Host 的 `rgba8-premultiplied` 像素在转为 `ImageData` 前会解预乘。此处记录源码接线，不代表构建或运行验收。 |
| `ResultsWorkspace → ResultNodeDetails → PlanNodeDetails` | 详情按当前 Result HDF 路径/修订及工程修订读取。`PlanNodeDetails` 持有局部描述草稿，按工程会话/路径和 Result 路径/修订注册独立 dirty owner。导入的用户数据草稿另按工程会话/路径存入内存 `ResultsDraftStore`；只有选择用户数据源时才向父级登记其 dirty 状态。地图 `MultiResultNodeDetails` 仅在活动树节点为 MultiResult 时出现，比较源属于当前地图结果会话。 | `result.planSettings.save` 仅允许精确匹配 Plan 描述在工程/HDF 修订一致、父提交可用且没有其它 dirty owner 时写入；检查会排除该描述自己的 dirty owner，因此可以保存自身草稿。取消更改恢复服务端描述；用户数据的“还原”恢复本次导入基线，CSV 导出则把导出数据设为新本地基线，均不写 HDF。Plan 保存回执更新选中 Result HDF 修订，不写 `.ras`；地形关联、关联列表、候选和内嵌字段/图片只读。地图比较源增删仅改当前会话，不写 `.vis`。 |
| `SchemaFieldRenderer → InputDateTimePropertyItem / DateTimeInputBox / DateInputBox` | 普通模型日期字段使用受控文本值；识别的日期时间形式保留原分隔符和时区后缀，不能识别的原文仍按原样显示。 | 输入只通过 `onFieldChange` 更新 `EditorWorkbench` 的模型草稿，校验通过后由 `model.save` 写入。专用 `BoundaryConditionTimeWindow` 仍使用 `datetime-local` 转换；结果表的 `TimeSeriesPayloadDateTimeColumn` 只是只读时间标签。 |
| `EditorWorkbench → BoundaryConditionLineEditorPage → BoundaryConditionLineEditor / BoundaryConditionLinePayloadsList / NewPayloadDialog → NewPayloadPage → ExternalConnectionLinker → PayloadExternalConnectionFlow → NewExternalConnectionPage → NewFileExternalConnectionPage / NewResultExternalConnectionPage` | Remote 选择项来自 Host `NewPayloadPage/ViewModel_SelectedExternalConnection`。新增动作沿用原 C# `NewPayloadPage.AddExternalConnectionRequested` 与目标页 `OnNavigatedTo` 的边界线上下文契约；React 在载荷弹窗内内嵌复用现有文件/结果连接页面。对话框通过 portal 挂到编辑器内、父保存表单外，保留工作区显隐与主题；对话框截断 React submit 冒泡，连接表单不会触发父级保存。载荷表单值和目标 `lineID` 留在 `NewPayloadDialog` 本地；活动表单与连接刷新状态注册到同一 `EditorWorkbench` owner。 | `project.connection.inspect → project.connection.commit` 立即提交独立 `.ras` 工程连接事务，受精确工程会话/路径/修订/XML SHA、选中 Boundary Conditions 路径/种类/修订及 core-layer identity 的 project-context lease 保护；lease 只允许本编辑器自身已有 dirty owner，且与其他工程/地图/引用写入互斥。父级只接纳匹配的工程与来源回执并保留编辑器；随后 `model.boundaryRefresh` 在原 Boundary Conditions HDF 修订上重放当前字段、行、样本和 sourceState，不写 HDF。`rebaseBoundaryExternalConnection` 要求线与载荷 ID、scope、顺序未变；只读回执未指定选择时保留本地已验证的选中线/载荷，显式替换选择仍拒绝，并验证目标 `lineID` 恰有一个新连接名选项；成功后只更新 boundary 选项投影/字段元数据，仍保留父 `ModelDraft` 和本地表单输入，向导回调才把连接名放入本地 `SelectedExternalConnection`。该连接已在 `.ras` 保存；点击“加入草稿”才调用 `model.boundaryTransform` 创建待保存载荷，后续父 `model.save` 才写 Boundary Conditions HDF。已知连接回执但刷新失败时保留 continuation，重试只调用 `model.boundaryRefresh`，不会再次创建；提交终态未知或回执无法接纳时锁定重试与保存并要求重新核对工程。打开的本地载荷表单会进入草稿保护提示，连接状态非 idle 时普通页面/视图转移被拦截；父保存与保存并继续均有表单/状态硬保护。显式丢弃可放弃本地表单输入，但不会撤销已提交的工程连接。此处记录源码接线，不声称运行验收；新增流程 glue 不增加原 C# UI inventory。 |
| `GeometryEditor → GeometryCulvertsPage → CulvertsEditor → CulvertEditor` | `geometry.culverts.load` 返回的稳定行 ID、源修订、服务端 `draftState` 和撤销状态归 Geometry 工作区；选行及子页是视图状态。 | `geometry.culverts.edit` 修改的是服务端草稿；父级 `model.save` 才提交 HDF。服务端能力不可用时的本地添加/覆写会保持 local-only，不能冒充可保存几何。 |
| `EditorWorkbench → TerrainLayersEditor → selected terrain-input detail/table editors; loaded ChannelMod input → ChannelModTerrainInputProperties → ChannelModTopologyEditor; loaded PolygonRasterModTerrainInput → TerrainPolygonMapEditor` | ChannelMod 与 PolygonRasterMod 编辑器只在各自 Host 输入已加载后挂载。ChannelMod 使用 Host 源坐标和 validExtent/geometry 独立 SVG；PolygonRasterMod 单独读取 project map catalog/tree 并通过 `map.terrainInput.render` 捕获精确已保存 visual-context、Terrain path、sourceVectorIndex 和 `.vis` revision，独立绘制项目坐标多边形与相机范围。 | `model.terrainTransform` / `model.terrainTopology` 只投影到父 `ModelDraft`。ChannelMod 的 load/select/draw/remove/undo/redo/split/moveNodes/moveVertex/insertVertex/deleteVertex/maskRegion/setMetadata 和 Polygon 的 load/select/selectAt/stamp/draw/remove/undo/redo 都校验 owner、revision、draftIdentity 与来源/FID 身份后更新父草稿，只有 `model.save` 写 Terrain HDF。Polygon 不接主地图选中；无 Host extent 时需要明确范围；不支持形状禁用，未提供顶点拖动/平移。通用 picker/creator/stats/table 不等于 TerrainMod canvas。源码接线静态记录，未声称 build 或 UI 验收。 |
| `ToolsPageView → USGS 地形 tab → USGSTerrainImportControl / USGSTerrainImportDialog` | 页面按需挂载并绑定活动 Terrain、工程会话及源修订。查询范围、产品筛选/选择、拼接和逐来源舍入选项由当前导入工作流持有。 | USGS 查询、下载、导入检查、追加暂存/状态查询分开执行；确认的 `model.save` 才提交 Terrain HDF。Host 临时下载文件的清理是独立操作，须重新确认已保存 Terrain 不引用这些路径。普通工程内 Terrain 使用其现有 path/revision/sourceState；只有实际存在的会话外部引用才需要额外捕获。 |
| `ProjectExplorerControl → NewGeometryDialog / NewBoundaryConditionDialog / NewPlanDialog → useProjectModelCreation` | 三种模型共用弹窗字段与 `modelCreation.inspect` 身份；请求绑定当前工程会话、路径、XML、修订和 XML SHA。 | `modelCreation.commit` 在 Host 暂存并写入模型 HDF、核对工程与目录读回，失败时按文件身份与内容有条件回滚；Plan 写入 PlanAssociation，带可选 Terrain/N-Value 的 Geometry 写入关联元数据。新 Boundary Condition，以及没有可选 Terrain/N-Value 关联的新 Geometry，可能不改变 `.ras` XML/SHA；新模型 HDF 仍由 `assetRevision` 标识。只有匹配的父上下文回执才切换当前选择。 |
| `ProjectExplorerControl toolbar → SurfaceCreationDialog → useSurfaceCreation` | 新建 Terrain、Classification、N-Value 的名称与检查回执归弹窗本地状态；请求绑定服务器工程路径、XML/SHA、session，并要求无未保存草稿。 | `workspace.file.inspect` 核对工程和新 HDF SHA；`surfaceCreation.inspect/create` 检查名称/目标目录并创建目标 HDF，随后 `project.tree.load` 必须读回唯一匹配类别、名称和规范路径的节点才切换选择。该流程不另行写 `.ras` XML；提交后的取消保持结果不确定，需要目录核验后再重试。它是 React 工作流 glue，不增加原 C# UI inventory 项。本条记录源码关系，不声称运行验收。
| `ProjectExplorerControl → ProjectTreeMode → RasCatalog → RasCatalogInfoPane → GeometryBackupAction` | Geometry 目录项提供命名备份，`BackupNodeVM` 目录项提供恢复为新 Geometry；名称、busy 状态与服务回执由操作组件持有，父工程会话持有目录刷新/恢复后选择。 | 创建前 `workspace.file.inspect` 和 `model.load` 核对所选 Geometry canonical path 与 SHA，再调用 `geometry.backup.createNamed` 并校验工程/XML/源/备份身份后刷新目录。恢复前重新检查备份与父 Geometry，`geometry.backup.restore` 生成不覆盖来源的新 Geometry；父级再用 `workspace.file.inspect`、`project.tree.load` 唯一节点读回和 `model.load` 修订核验，全部匹配后才切换选择。需要干净工程草稿与当前会话身份；不隐式提交 `.ras` 或模型草稿。此处是源码接线，未做 build/service/browser/GUI 验收；React 操作是工作流 glue，不新增原 C# inventory 项。 |
| `ProjectExplorerControl → MigrateProjectControl` | 用户输入的旧版 `.prj` 路径、`projectMigration.inspect` 返回的源 SHA/修订与独立目标目录绑定迁移确认。 | `projectMigration.execute` 调用 Host 转换器，旧源保持只读，输出写到另一个目录；`project.open` 读回输出并经父工程会话校验后才切换上下文。迁移不是改写当前 `.ras` 的 `project.save`。 |
| `EditorWorkbench → SpatialDataSummaryPage → SpatialDataSummary / SpatialBoundaryConditionsPage → GriddedDataControl → GriddedDataImporter / GriddedDataImporterDialog / GriddedDataImporterSlider / GriddedDataImporterTimeline; CumulusImportSource uses CumulusLoginWindow; SpatialBoundaryConditionRasterImporter uses the importer dialog` | 页面按 Boundary Conditions 快照修订读取支持图层的检查信息与帧；请求绑定当前模型 owner、修订、图层、时刻/分量。测站与空间输入行/字段属于 `ModelDraft`；帧缓存与可取消读取属于空间页。 | `model.gridInspect` / `model.gridFrame` 只检查来源与读取 Host 帧，帧选择复用 `onGridFrame`。栅格导入需用户二次确认，并仅在工作台返回确认后关闭；`model.gridImport` 使用已保存快照并立即提交模型，拒绝脏或无效草稿，且需新修订与导入读回后才能刷新基线。测站/输入结构用 `model.spatialTransform` 更新父草稿，之后仍须 `model.save`。Cumulus 的环境/产品/UTC 格网查询、覆盖范围检查和导入保存回执已接入现有工作流；它要求草稿干净且有效，只有工作台接受新模型修订后才刷新基线。没有真实地图裁剪范围时使用格网完整范围；ET 格网未接通。此处为源码关系，不代表真实账户或运行验收。 |
| `EditorWorkbench → PlanEditorControl → PlanSettingsPage → SchemaFieldRenderer → PlanTimeWindowModePickerPropertyItem` | Host schema 的 `ViewModel_SelectedPlan_TimeWindowMode` 字段、choices/title 与 `planTimeWindowFieldIDs` 提供真实 enum 值和 mode/start/end 字段身份；React 受控控件把原值写回同一 `fieldKey`。 | 模式选择仅更新 `ModelDraft`，不会自行改写开始/结束时间。`EditorWorkbench` 把模式与时间范围的实际变更作为 `planTimeWindowEdits` 交给 `model.save`；Result Plan 仍只读。PlanSettings 挂载 PropertyItem，新建方案的 `NewPlanControl` 按原 C# 组合独立 `PlanTimeWindowModePicker`；已挂载控件不包含原属性头的动态图标表现。 |
| `GeometryEditor toolbar → ArcImportDialog → DropTarget / ImportingPreviewList → useGeometryWorkbench.inspectVectorImport / applyVectorImport` | Dialog 捕获当前 Geometry topology 的 revision/draftState/draftIdentity，Host 检查完整 Shapefile 文件族和 line/polygon 要素；浏览器文件走现有 `uploadMapSources` 复制到工程 sources，服务端绝对路径也可输入，上传回执与几何导入回执分开。 | `geometry.vectorImport.apply` 将选中要素及 Merge/NoMerge、MergeContiguousArcs、RemoveExternalArcs 选项合入父 topology 草稿；新快照进入现有拓扑 undo/redo/dirty 流，只有父 `model.save` 持久化。Host/UI 不提供图层属性或 metadata priority 选项。该流程是 Geometry vector import，不是 Shapefile map-layer import。此处为静态源码关系，未做本批 build/UI 验收。 |
| `GeometryEditor → GeometryTopologySummaryPage / GeometryTopologyArcsPage / GeometryTopologyRegionsPage / GeometryTopologyNodesPage` | `TopologyControl` 导航到拓扑页；弧段/区域详情挂载 `TagList`，弧线和区域表之后挂载 `TagFilterControl`，概览页挂载 `TopologyStatus`、`TopologyBuildResultViewer` 与 `TopologyProperties`。表格选集仍映射稳定源行 ID/FID。 | 标签新建/赋值/移除通过 `topology.edit` 作用于当前 Geometry 草稿和撤销历史；`topology.filter` 捕获来源修订和草稿、验证返回 FID 后只更新表格选集。全局缩放因子按 Host 支持范围更新弧线/控制点属性并留在草稿，父 `model.save` 才提交 Geometry HDF。Geometry 保存请求发送 `fields: {}`，不回放只读 catalog summary；Host 从原 HDF 开始，仅应用已捕获的 child draft state，因此保留原上下文/metadata。筛选只支持弧线和区域；跨页选集缩放未接通。节点表详情显示源 FID、坐标和关联弧，坐标本身只读；节点页另有独立地图画布，使用保存地图捕获、`topology.spatialSelect` 与 `mapCommand/moveNode` 完成空间选中/拖动，编辑仍进入原 topology 草稿。可编辑快照中的删除选中源节点走现有拓扑草稿并进入撤销历史，不提供本地新建。地图 token 的签发来源未由 Host 拓扑端点验证。
| `TerrainLayersEditor → LeveeModTerrainInputEditor → LeveeModEditor / LeveeModPropertiesEditor` | 只在所选 Terrain 输入确实返回 `LeveeModsTable` 或 `LeveeModPropertiesEditor/PropertySets` 时挂载对应子编辑器；两者读取同一选中输入的表、行和 schema。 | Levee 属性/行变更回到 `ModelDraft` 的字段、行或 `model.terrainTransform` 投影，不单独保存；父 `model.save` 是 Terrain HDF 持久化边界。 |
| `MapFeaturePage → RasMapElementsOverlay → ScaleBar` | 比例尺采用当前地图相机缩放与已确认工程坐标身份；只有 Local SI / USCustomary 分别显示 m/km 或 ft/mi。 | 其他投影、未确认或未知单位显示通用 coordinate units；只随 overlay 展示，不写工程/地图文件。 |
| `MapFeaturePage → MapLayerTree → RasTreeV2 → MapTreeNode → type-specific tree nodes` | 文件夹、Geometry、Plan、Result、MultiResult、Terrain、NValue、Classification、Boundary Condition、Terrain input、FlowArea、Culvert barrel、Point/Polyline/Polygon 与通用节点按节点类型挂载；选择和展开状态属于当前地图树。 | 工程来源显隐经 `map.tree.perform` 保存 `.vis` 并核对前后修订；隐藏不删除树成员，移除是独立动作。本地预览开关、`RasMapMinimap` 拖动和 `ZoomToPointDialog` 坐标导航只改变当前相机，不写 `.vis` 或 `.ras`。 |
| `MapFeaturePage → MapLayerTree → selected persisted MapLayer → 从此地图移除` | 仅普通 GeoJSON 矢量 MapLayer 与普通栅格 MapLayer 在精确来源路径/修订可验证时提供入口；GDB 及其他树节点类型不可移除。 | `map.tree.perform(action='removeSourceNode')` 绑定工程会话、精确地图身份、`.vis` 修订、locator、源路径和源修订；Host 回执核对目标身份恰减一且兄弟身份不变。父级取得地图写租约并采纳回执后才刷新地图目录、更新修订并清除目标选择/hover/frame。动作仅移除当前地图成员关系，不删除磁盘文件或其他地图引用，也不等同于全局资产 Remove。未知/未采纳结果锁住重试，显式重读精确地图和来源进行对账；读回结果不是原提交回执。此处是源码关系，不代表运行验收。 |
| `MapFeaturePage → map inspector → GlobalSymbologyEditorPage` | 收起的全局矢量默认值区域按 Host 定义分组，编辑快照、默认值和 revision 归此编辑器本地草稿；设置文件路径由 Host 选择，`HECRAS_SETTINGS_DIRECTORY` 可指定隔离根。 | `global.symbology.inspect/save` 读写用户级 XML 设置；保存提交完整定义集及 expectedRevision，Host 拒绝过期版本并原子保存稀疏样式。C# 地图 renderer 只对显式 `isProjectOverride=false` 且全局修订吻合的绘图请求应用全局默认值，不猜测缺少来源标记的样式。保存后主地图推进设置 epoch，干净的图层编辑器重新检查并绘图；有脏草稿时延后刷新。本条记录源码接线，不代表运行验收。 |
| `MapFeaturePage → map inspector → MapViewsPanel → ViewViewer / ViewEditor` | 实际地图相机范围、活动地图身份、工程会话和当前 `.vis` 修订共同定位视图目录；保存的 extent 由当前画布恢复。 | `map.views.inspect/commit` 管理 `.vis` 中的创建、改名、删除、范围重置和快捷键；提交进行中由父级锁住地图切换、新建地图、刷新/导入及受保护入口，避免同工程切图让面板丢失已完成回执。工程会话改变仍会使旧所有者失效；其余只接受同一工程/地图及上一修订匹配的回执，随后使旧图层快照失效并重新读取。定位操作对当前画布 `fitExtent`，不会打开新窗口。本条是源码关联记录，不代表本批运行验收。 |
| `SourceManagerControl → AttributeTableEditorControl` | `workspace.file.inspect` 捕获主文件 SHA；Shapefile 另由 `map.vector.captureShapefile` 捕获 `.shp/.shx/.dbf` 与可选 `.prj` 文件族，`attributeTable.load` 读取表。读取前后复核主文件和文件族修订。 | `attributeTable.transform` 仅返回内存预览（增删改列/单元格）；此属性表流程没有写回 Shapefile 的动作。地图文件导入是另一条 `uploadMapSources → map.vector.importShapefile` 流程，不能把预览编辑算作磁盘写入。 |
| `ImportExportToolControl → DepthShapeExporterDialog / RgbTiffExporterDialog` | 两个对话框分别绑定保存地图中的 Result/profile 或 RGB 栅格来源、检查所得来源身份与参数，以及精确匹配的 Host 预览。 | `depthExport.execute` 与 `rasterExport.execute` 只有点击导出后才执行 Host 文件写入；用户提供的是 Host 可解析的绝对输出路径。完成回执描述服务器产物，Web 不宣称已下载到浏览器本机；此处记录源码调用链，不代表运行时验收。 |
| `ProjectExplorerControl → ExternalConnectionManager / ExternalConnectionDetails / DssExternalConnectionPreview` | 工程上下文提供 XML/session/revision 身份；连接节点和选择来自 `catalog.tree.load` 的 Catalog 模式，不能从 `project.tree.load` 的 Project 树读回。Result 类型选项来自真实 `ObservableResult` 节点。DSS 分支挂 `DssCatalogViewer`、`DssFilterControl`、`PartFilterItemControl`、时序图/表、配对数据图/表和 `LocationInformationView` 八个基础视图控件，并提供时序大图、完整时序表和完整配对数据表三个全尺寸窗口；它们复用已读取的记录回执，不发起第二次 DSS 读取。 | `project.connection.inspect` 返回 `canCommit/validationMessage`、规范路径、来源存在性/文件数/摘要、source-references 摘要及 before/after 身份；`project.connection.commit` 提交 `expectedConnectionSHA256`、`expectedSourceSHA256`、必填 `expectedSourceReferencesSHA256`，新增还提交 `expectedNormalizedName`。Host 重新打开工程后用 `CatalogTreeService.LoadCatalog(Mode=Catalog)` 验证新增或删除，`project.tree.load` 不包含外部连接节点。提交回执包含新工程快照、`previousProjectRevision`、before/after、`sourceCapture`、`previousSourceReferencesSHA256` 和 `sourceReferencesSHA256`；客户端校验回执后才调用 `onContextCommit`。Result 按原工程格式保存为指向实际 HDF 的 File connection。`.dss` 目录/记录预览走 `dss.catalog/read`，只读并绑定完整 DSS 路径、SHA/修订及所选记录路径；A–F/类型/文本筛选不会清掉按完整路径保存的当前选择。图表仅显示 Host 返回的时刻、数值、质量数组、paired curves 或位置字段，NoData 断线、不插值；位置代码不被解释成地图 CRS，当前 UI 明示只接 DSS 7。全量时序表支持分页、跳行与完整 CSV 复制/下载，配对表保留全部曲线和源数组索引；NoData 导出为空单元，质量数组按返回顺序独立展示。绝对路径属于 Host 所在机器；断开来源仍可按原连接身份移除。 |
| `SourceManagerControl → SessionHdfImportControl` | 工具页“数据源”区域允许输入服务端可访问的 HDF 路径、检查类型/身份、从当前工程目录选择兼容的已保存地图，并显示当前会话已有的 Plan/Result/Terrain 引用。 | `workspace.file.inspect` 与 `workspace.import.sessionHdf.inspect` 捕获来源；`workspace.references.compose` 验证下一个 CoreLayerContext；只有目标地图的 `map.tree.perform(addSessionHdf)` 返回匹配 `.vis` 回执后，父 `RasMainWindow` 才接纳会话上下文。HDF 保留原路径，不复制、不写 `.ras`；UI 目前只有添加和只读列表，没有移除按钮。源码关系记录，不代表运行验收。 |
| `SourceManagerControl → CoreLayerImportDialog → Add Reference / Copy File` | Web 复用原 `CoreLayerImportDialog` 的 Geometry / Boundary Condition 来源检查与 Add/Copy/Cancel 选择。原 WinUI 的 Add Reference 同时把核心图层加入工程对象和关联地图；当前 Web 则用会话捕获保留外部来源，并由地图树回执驱动接纳。 | Add Reference 由 `workspace.import.coreLayer.perform` 生成会话捕获，`workspace.references.compose` 验证完整上下文，随后重读目标地图并以 `map.tree.perform(addGeometry|addBoundaryCondition)` 更新 `.vis`；父级只在读回唯一目标节点后接纳上下文。Web 的 Add Reference 不复制 HDF，也不写 `.ras` 成员关系；Copy File 是独立的文件复制操作。 |
| `ProjectOpenProjectDialog → QuickProjectImportControl` | 在现有打开工程流程中提供 ZIP 与核心 HDF 的服务器路径导入；路径及目标目录由用户输入，不是浏览器本地文件选择器。 | 先以 `workspace.file.inspect` 捕获 SHA，再分别运行 `workspace.import.archive` 或 `workspace.import.coreHdf`，严格核对来源、目标目录和准备工程回执后才调用既有打开回调。ZIP 必须恰有一个 `.ras`；HDF 仅支持 Result、Geometry 或 Boundary Condition 并复制到新工程目录。它是 `ProjectExplorerMainPageTiled` 快速打开/拖放能力的 Web 路径适配，不是新增的原始 C# 控件；运行证据另行核验。 |
| `EditorWorkbench → SpatialBoundaryConditionsPage → GriddedDataControl → GriddedDataImporter → CumulusImportSource / CumulusLoginWindow` | `CumulusImportSource` 负责环境、产品、UTC 日期范围、所选时刻/版本及格网覆盖范围；认证令牌留在当前组件会话内。原 C# `CumulusLoginWindow` 用 WebView2 自动拦截登记的 redirect URI；Web 登录打开独立浏览器弹窗，Electron 通过受限主进程桥接打开系统浏览器；两者均要求用户粘贴完整 callback URL。 | `cumulus.environments/products/grids.query/grids.footprint` 提供目录、格网和覆盖检查；真实登录经 PKCE、严格校验回调 origin/path/state/code 后调用 `cumulus.token.exchange`，令牌刷新走 `cumulus.token.refresh`。显式 Mock 环境/令牌标为本地模拟，不代表真实账户。`model.cumulusGridImport` 立即提交所选格网；仅在草稿干净、有效、Host 返回新模型修订与对应格网、且当前工作台接纳回执后更新基线。ET 不支持；无真实地图裁剪范围时按完整格网范围导入。这里只记录源码接线，不代表真实账户或运行验收。 |
| `RasMainWindow → sessionReferences / SessionBackend → C# Host` | `RasMainWindow` 在当前已打开工程会话内持有混合的 Geometry/Boundary Condition 与 Plan/Result/Terrain 引用上下文；子工作区切换沿用同一 context，项目会话或路径变化重置。`SessionBackend` 仅为精确匹配当前工程路径/会话的项目请求注入上下文。 | `workspace.references.compose` 生成并核验候选上下文，不写注册表或项目文件；父级通过引用变更租约阻止 dirty owner/工程切换，在地图事务成功后才接受新 context。`model.load` 快照携带该上下文；`model.save` 回执按已修改的同路径 HDF SHA、类型和名称重基对应引用及 token，React 仅在仍匹配的工程/资产 owner 下采纳回执。`project.save` 写前检查会话与外部来源，保存后按新工程 SHA 续期上下文及 token，并同步全局和所选资产 locator。独立 Terrain 在 Geometry/Plan 地图进入 Layers；Geometry 地图中的独立 Plan 也进入 Layers，重新读取仅恢复明确保存的成员。registry 是应用内存状态，无 Remove UI 的功能不能从 Host compose 参数推断成已提供。该基础设施和 wrapper 不是额外原始 UI source item。 |
| `EditorWorkbench → ClassificationEditor / NValueEditor → ClassificationLayersEditor / ClassificationImportEditor / ClassificationRasterEditor / ClassificationGeometryEditor / ManningsNEditor → ConstantManningsNEditor / ClassificationLayerManningNEditor / ManningInterpretationTable`; `SourceManagerControl → ClassificationImporter` | 分类输入、映射及多边形 UI 按当前模型快照、选中输入和 ModelDraft 工作；专用 SVG 多边形选集与复制板是本地视图状态。Manning 模式、关联、固定行和草稿糙率值归同一模型所有者。 | `model.classificationInspect` 先检查服务器路径与来源，`model.classificationImport` 在显式确认后立即保存并须接纳新模型回执；它不是普通 `model.save` 草稿。分类输入结构与多边形几何通过 `model.classificationTransform` 留在草稿，`model.classificationMap` 和干净修订上的 `nvalue.preview` 均只读预览。Raster mapping 的源行 ID / `Key` / 行数固定，仅 `Value` 可编辑；原 C# row key 为 getter-only，行集合增删本就未实现。Polygon 选择发生在分类页工程坐标 SVG，不是主地图点击。`model.manningTransform` 的 `refresh` 和 `setNLCD` 操作 返回草稿投影；默认值只写匹配的现有 NLCD 名称，未知行原值保留，Key/ID/行数只读，糙率 `Value` 可编辑。普通字段、表格与操作结果都需父 `model.save` 才持久化。
| `BoundaryConditionEditor → AirPressurePage → AirPressureHeader / AirPressureControl` | Header 与压力字段/单位读取同一 Boundary Conditions 快照并共享父草稿回调。 | 输入只更新 `ModelDraft`，由 `model.save` 持久化；单位呈现不产生独立写入。 |
| `ToolsPageView → Unit calculator → UnitCalculatorPage / UnitCalculator; SchemaFieldRenderer → UnitAwareNumberField → UnitComboBox / UnitCalculatorButton` | `UnitCalculatorPage` 以 `unitCalculator.load` 获取单位族与源/目标单位，并以 `unitCalculator.convert` 请求 C# 计算；满足单位和有限数值条件的模型字段显示绑定 field ID 的计算器按钮。 | 计算对话框按字段、源/显示单位和初始源值绑定请求；取消不改草稿。应用仅通过 `UnitAwareNumberField` 原有逆变换/源单位校验更新 `ModelDraft`，单位显示选择本身不改字段值，父 `model.save` 才持久化。 |
| `ResultsWorkspace → CrossSectionEditorWindow / CrossSectionEditorControl; DynamicProfilePlot → ProfileSampleLinePlot / ScrubbedDataDisplay` | 独立断面窗口按当前 Geometry 来源读取实际站距、高程和岸线样本；采样线按 Result 来源、用户输入的工程 XY 与 Flow/CumulativeVolume 选择计算。 | 经典断面只读；采样线按各来源原生时刻、单位和 NoData 展示，不是空间网格剖面且不依赖地图点击。Scrubber 显示真实时刻与样本数值；这些结果界面不写模型 HDF。 |
| `SourceManagerControl → VectorImporter → VectorImporterDialog → NewShapefileDialog / ShapefileAttributeTableViewer` | 工作流要求从当前 Host 工程地图目录显式选择已保存地图；创建或导入绑定项目会话/修订、地图身份与 `.vis` 修订。导入捕获规范 Shapefile 路径及 `.shp/.shx/.dbf` 和可选 `.prj` 完整文件族。 | `map.shapefile.create` 只创建文件并返回创建回执，不表示图层已导入；`map.vector.importShapefile` 再次检查来源文件族和目标地图后写 `.vis`。只有刷新 `map.document.list/map.tree.load` 并确认精确 locator 后才记为导入成功。属性表转换仅为内存预览；服务器绝对路径不等于浏览器本地文件路径。 |
| `ImportExportToolControl → PlanExporterDialog / PlanPickerDialog / ReprojectionToolDialog → ProjectionSummary` | 计划导出检查捕获工程会话、Plan 路径/修订、参数及 Host SHA；投影搜索、输入识别与坐标转换结果均按服务响应和稳定表格行身份显示。 | 只有显式导出才运行 `planExport.executeDuplex`，输出路径由 Host 解析且执行前重查来源。`reprojection.transform` 只返回坐标表和 Host 投影回执，不写文件；CRS 不从文本猜测。 |
| `MapFeaturePage → RasMapElementsOverlay → RasMapLegend → OverlayItem / MapOverlayChrome; contextual menu → RasContextualActionsBar; inspector → RasLayerPropertiesPane` | 图例只显示当前地图、精确栅格来源已成功应用 Host snapshot 的分级值和颜色；属性面板显示已选 GeoJSON 要素属性/CRS，或工程树节点类型、名称与诊断。 | `rasterSymbology.apply` 核对 `.vis` 修订和来源后才发布新图例；palette 保存/删除不会替代已应用分级。覆盖层跟随地图显示设置。属性检查不写项目数据；只在确有已加载范围时提供缩放。 |
| `ModelDataTable / SourceManagerControl → AttributeTableEditorControl → AttributeTableDataGrid → DataGrid → GenericEnumFilterControl / DataGridContextMenuFlyout; dataGridSelection` | 共用 `DataGrid` 保存过滤/排序/分页与稳定源 `rowID+columnID` 选集。单元格坐标映射到完整的当前排序/筛选视图，跨页焦点等待目标页 DOM 安装。模型表格仍归父 `ModelDraft`，属性表仍用 `[rowId,columnId]` 本地草稿；列宽只在本地视图保存。 | `GenericEnumFilterControl` 依据实际离散值过滤，超过 80 个不同值时隐藏选项而不截断数据。Ctrl/Command+方向键跳到视图首末行列，包含空单元格；Shift 从锚点扩展矩形，全选包含当前筛选视图的所有页。复制输出 TSV；粘贴及 Delete/Backspace/菜单清空先按稳定 ID 冻结可编辑源行列计划，再经既有 adapter 提交原始字符串（清空为空字符串），跳过只读或 adapter 不允许的单元格。中文菜单提供复制、粘贴、清空、全选，编辑控件保留自身按键和原生右键。异步剪贴板读取核对行列对象、reset/view/selection/version 与 adapter，过期结果不写入；权限失败明确报告。`ModelDataTable` 继续调用 `onCellChange(tableID,rowID,columnID,rawString)`，属性表仍经显式 `attributeTable.transform` 生成预览，不写回 Shapefile。列宽支持拖动、Shift+方向键及双击恢复；回调提交不代表 Host 保存。公式/引用偏移、完整行列修改/导出菜单、dirty-cell/toast 汇总、属性表磁盘写回及 TableOrList/ListDetail 仍不属于此网格。 |
| `ComputeProofPage → ComputeButton → PauseContinueStop; ComputeProofPage → PlanComputeControl / RealTimeMonitor; ComputeProofPage → OperationResultControl → MessageListControl` | ComputeProofPage 拥有唯一计算运行状态、进度样本、流式 planMessages 与终态结果。planMessages 仅接受当前 generation 和 RunId，并按源消息 index 合并；新运行或工程/计划身份变化时清空。终态存在真实 `result.messages` 时优先显示终态消息。 | `plan.compute.inspect/execute` 与原取消/终态回执流程不变。停止沿用协作取消并等待 Host 终态；Host 未提供的暂停/继续保持禁用。进度剩余时间明确为线性估算，实时进度消息列表最多显示最近 200 条。操作消息显示 Host 严重级别、代码、文本、日志参数/目标与重复计数；原始终态 JSON 仍由 `ComputeSummaryControl` 显示。没有 Web JobHub 数据源，未挂载 JobHubSummaryButton；来源 ProgressDialog 不移作 solver 控件，聊天仍 deferred。此处为静态接线，不代表运行验收。 |
| `EditorWorkbench model view → ModelAssociationsPanel → PlanAssociationsPicker / GeometryAssociationsPicker` | 仅在当前工程的 Plan 或 Geometry 模型快照 ready 时挂载，位于模型表单之外。检查身份包含 project session/path/XML/revision、活动模型路径/修订与 sourceState/core-layer token；未保存模型草稿或其他 dirty owner 会阻止应用。 | `project.association.inspect` 读取当前关联、候选模型及其 SHA/依赖；应用前 `project.association.commit` 再检查。Host 只修改标准 `.ras` 关联 XML，不写模型 HDF；发布后重开工程并通过 Project 模式目录核验目标，客户端校验 before/after 和来源捕获回执后才由 `onContextCommit` 更新工程上下文。Catalog 树仍专用于外部连接节点；此流程是独立的工程 XML 事务，不能当作 `model.save`。 |
| `EditorWorkbench → Boundary Conditions summary → AttachInitialConditionsDialog` | 结果选项来自当前工程 `project.tree.load` 中真实 `ObservableResult` 节点；检查绑定当前 BC HDF 路径/修订、工程 XML 指纹/修订、结果路径/SHA、可选时刻和数值区域。EditorWorkbench 按工程/模型 owner 保留操作及 `operationID`，请求和行身份基线写入 `sessionStorage`。 | `boundary.initialConditions.attach` 是即时的配对事务：Host 将结果引用写进 Boundary Conditions HDF 并更新工程 XML 连接，随后返回更新后的工程快照及模型快照；不经过父 `ModelDraft` 或等待 `model.save`。`boundary.initialConditions.status` 只查询事务日志并返回状态/已提交回执。响应丢失或切换会话时保持不确定态并用同一 ID 查询；仅 `notFound/prepared/rolledBack` 可按原请求重试，`conflict` 不自动重试。服务已提交而上下文未接纳时可重试接纳所存回执，不重复写入。 |
| `ProjectExplorerControl → ProjectExplorerMainPageTiled → ProjectExplorerSettingsPage; → ExampleAndSyntheticDatasetsPage → SearchBarWithResultsPopup / SearchResultsControl / ExampleProjectSummaryPage` | `RasMainWindow` 持有 Light/Dark/System 主题，并以版本化 localStorage 键 `hecras.theme.preference.v1` 保存；System 跟随 `matchMedia` 且清理监听器。设置只清理当前应用的最近工程列表。示例搜索在当前 `projectLibrary.catalog` 响应中过滤标题/说明/标签，并支持键盘选择。 | Catalog uses `debugMode=false` and keeps only `kind=example`. Opening uses the existing `projectLibrary.open` Host destination followed by `project.open` and parent context acceptance. `ExampleProjectSummaryPage` 的 Back 按钮只受实际打开操作的 `busy` 状态控制；目的地无效时由独立 `canOpen` 禁用主操作，不会锁住返回。Theme and recents do not write project data. Default Host directory/credentials, filesystem enumeration and generic server search have no Web contract; `ProjectOpenProjectDialog` remains the separate existing upload/path flow. Source wiring only; no build or GUI acceptance is claimed. |
| `EditorWorkbench model page → SchemaPage → SchemaFieldRenderer → DescriptionGroupControl / EmbededImageGallery / EmbededImageControl` | Schema 同组含描述与 `images` 字段时才组合显示；字段值和图片集合分别按 scoped field key 保存在 `ModelDraft`。图库管理本地预览/上传/移除，并按图片签名检查栅格 MIME。 | 新图单张上限 2 MiB、模型总量 3 MiB，拒绝 SVG；超预算旧图仍可预览或移除。新增、删除与描述文本都先进入同一父模型草稿，Host 只接收 schema 声明的图片键，`model.save` 才将嵌入图片写入 HDF；没有独立图片提交。 |
| `ResultsWorkspace time-series analysis → Timeline / IntervalPicker / TimeWindow / TimeWindowGroup → MapResultsChart` | ResultsWorkspace 持有当前结果或用户数据集的原始时刻、选中时步、可见序列与图表范围，子控件消费同一份状态。时间控件自身不创建 Result 会话或发起 Host 请求。 | 间隔跳转要求可解析且非递减的时刻，并落到已有输出时步；编辑时间窗要求完整起止时刻且至少两个时步。范围只改变图表视口，摘要统计可见序列中的有效值与 NoData，不筛除或重采样源数据，也不写 HDF。 |
| `GeometryEditor → GeometryTopologyArcsPage / GeometryTopologyRegionsPage → ConceptualMeshTopologyArcsEditor / ConceptualMeshTopologyRegionsEditor → ConceptualMeshContextualControl / TopologyRegionQuickEditPicker; GeometryMeshPage → FlowAreaEditorContextualControl; CulvertsEditor → DetailedCulvertBarrelLayerHeader` | 拓扑缩放和区域 quick-size 依赖当前表格选中的源 FID；流动区域细节与只读网格元数据来自当前选中行；涵洞层头只显示要素数及选中数。状态属于对应 topology/mesh/culvert 草稿及其修订/draftState。 | 选中弧线/控制点比例与区域尺寸操作通过 `topology.edit` 更新拓扑草稿；FlowArea 名称/目标单元间距由 `geometry.mesh.edit` 更新网格草稿。元数据和涵洞层头只读；能力缺失时的 local-only 覆写不是可保存回执。支持的修改由 Geometry 撤销/重做管理，父 `model.save` 才提交 HDF。 |
| `GeometryEditor (mesh page) → GeometryMeshPage → PatchEditor → CartesianPatchQuickEditor / TrianglePatchQuickEditor / QuadPatchQuickEditor` | 区域选集来自当前 mesh snapshot 并映射回源 region FID；按当前 patch sizing kind 显示对应快速编辑器。Quad 还绑定有效的来源弧线选择修订、topology fingerprint 与源 arc FID。 | `geometry.mesh.edit` 请求携带模型、修订、mesh/topology 草稿状态和选集。Cartesian 合并尺寸在一次 mutation 中作用于首个选中 Cartesian FID；Triangle 作用于首个选中 Triangle FID；Quad 作用于全部选中 Quad FID。回执校验修订与选集；仅改变 draftState 才入 undo 并清 redo，undo/redo 回读已有草稿，父 `model.save` 才写 Geometry HDF。PatchType 强制设置属于 Regions 页 `topology.edit`。本条为静态源码关系，不代表构建或交互验收。 |
| `GeometryEditor (mesh page) → GeometryMeshPage → MeshElementInspector → meshElementInspectorData` | 直接消费父 Geometry 的 model、mesh/topology 快照与工程会话；局部选择、读取代次和 AbortController 绑定完整来源及草稿身份。Cell 点选来自 Host 有限/抽样的 `map.Cells`（JSON `map.cells`）；Face 由已知零基 ordinal 输入。 | `geometry.mesh.findElement → geometry.mesh.elementPropertyChart` 先核验真实元素定位，再读取实际属性表点。Cell 显示库容—高程、Face 显示过水面积—高程，并提供中文数值表；`requiresPropertyTables` 不显示曲线，用户经既有属性表计算区生成后重新选择。Inspector 不新增计算意图、不保存 HDF，Host 可重放草稿已有的计算意图；它是新增 React 胶水。 |
| `GeometryEditor → topology/mesh/culvert tables → *ActiveFeatures` | `GeometryFeatureTable` 共用 `ActiveFeaturesControl` 的行过滤和复选；拓扑弧/节点/控制点、FlowArea、culvert barrel 分别由对应 `*ActiveFeatures` 组件把选集传给已有详情或父编辑器。 | 筛选匹配 row ID/当前 FID 且不写草稿；弧/区域 Host 筛选回执经 revision/draftIdentity 校验后仅映射为当前表格选集。节点表坐标仍只读；节点页独立的 `GeometryTopologyCanvas` 捕获地图视口并使用 `MapCellSize` 进行空间选择和 `mapCommand/moveNode` 拖动。可编辑快照中的源节点删除走现有拓扑草稿，无本地节点创建。已有拓扑、网格和涵洞修改继续由各自父草稿与 `model.save` 持久化。
| `ToolsPageView → GeoprocessingToolControl → ToolOptionsFlyout / ToolParameterForm / ToolParameterField; GeoprocessingVectorSourcePicker / GeoprocessingPointSourcePicker → GeoprocessingPolylineExecutionDetails → geoprocessingMapLayerWorkflow` | 工具定义和参数属于当前工具页。来源从已保存地图树选取并由 `map.vector.source.load` 捕获；请求绑定工程/XML/修订、地图和 `.vis` 修订、精确 locator、源路径与修订、要素/路径身份。折线接受 GeoJSON 或完整 Shapefile 文件族；多边形仅接受 GeoJSON，并按 Host 返回的 `polygonIndex/isHole` 保留外环/洞环。Shapefile 折线要求目标工程 WKT。 | 七个既有折线操作加 BufferPolyline、PolylineToPolygon、BufferPolygon、DensifyPolygons、PolygonToPolyline 共十二项经 `geoprocessing.execute` 执行；接纳前核对返回的 path identities、数量和几何类型。Clipboard 提供文本预览/复制/下载；服务器 Shapefile 输出限定为工程 Exports 直接子路径，并经 `workspace.file.inspect` 和完整 `map.vector.captureShapefile` 回执核验。原 C# `DefaultOutputHandlers.MapLayerOutput` 对应的 Map Layer 只向具备精确已保存 `MapDocumentTab` 捕获且实际支持 Shapefile 输出的 Point/Polyline/Polygon 向量分支开放，Raster/TIN 仍禁用。目标保留来源选择器捕获的具体文档，不按显示名重匹配；捕获包含工程会话/路径/修订/XML SHA、mapID/index/name/type、`.vis` 修订及来源 locator。执行期间父工作区持有该目标的地图写入 lease 至回执接纳；在途地图写入、建图和栅格写入会保护导航、上下文切换与放弃草稿。工作流重检完整 Shapefile 文件族并重捕精确地图 owner，通过 `map.vector.importShapefile` 添加已生成文件，不创建或覆盖输出。父层只接纳匹配目标和前一 `.vis` 修订的 receipt 一次，递增 `mapRevision` 并触发 `MapFeaturePage` 重读；同一工程刷新后按 mapIndex/name/mapType 保留当前地图选择。项目级未决列表用于精确重读和对账：提交结果不确定时，只有确认输出路径未进入目标地图、`.vis` 未变化且文件族仍有效，才允许仅重试地图导入；`receiptNotAdopted` 只能对账，不能重试。提交前取消会停止地图变更；提交后状态不确定时保留输出路径和诊断，不显示成功。此处记录源码接线，不声称运行验收。 |
| `MapFeaturePage → RasterSymbologyEditor → ColorPalettePicker / ColorPaletteList / RasterSymbologySettingsGroup / AdditionalOptionProperties / ColorPaletteControl` | 控件绑定精确栅格来源、来源 SHA 和 `.vis` 修订；断点、颜色、灰度、值源和 Host Plot Options PropertyName/value 对进入同一受控应用请求。 | `rasterSymbology.apply` 写匹配地图节点并推进 `.vis` 修订：源兼容 `RasterSymbology` 字段沿用旧 XML 序列化，renderer-only `PlotRasterTileOutlines` 存于版本化 `RasterPlotOptions`，`ResampleMode` 复用 `InterpolationMode`；服务 reload 将保存值应用回 renderer 后返回真实 RGBA。默认选项省略，自定义颜色带保存/删除仍是独立事务。仅记录静态服务合同，不代表本批 UI 运行验收。 |

### 当前表格选择与单元格命令

原 C# `Visual.WinUI.Controls/DataGrid.cs` 在非编辑状态下把 Ctrl+方向键解释为显示表格边缘跳转，Delete/Backspace 清空选区中可编辑的可见列。当前 `DataGrid → dataGridSelection` 将 Command 同样作为命令键，并按完整排序/筛选视图解析目标，包含空值；Shift 保持选择锚点，全选覆盖该视图所有分页。`pendingFocus` 保存源行列 ID，先切页再等待布局提交后聚焦目标单元格，不能把页内位置当作源行索引。

`DataGridContextMenuFlyout` 提供中文复制、粘贴、清空和全选，经单元格右键、Shift+F10/菜单键或工具栏打开；Escape 恢复活动单元格焦点，输入控件保留自身编辑按键及原生右键。清空与粘贴共用 `planGridCellEdits → onCellPaste`，在任何回调前冻结源对象计划，跳过只读、adapter 拒绝及当前视图外的目标，保留空字符串与原始文本。同步异常按单元格统计，状态只说明向编辑器提交了请求，后续校验和父保存继续归 adapter/编辑器。菜单剪贴板操作依赖实际系统 API；行列对象、重置标识、视图、选区、交互代次或 adapter 变化会使旧读取失效，不能用内部复制缓存替代成功的系统读写。该菜单接入原控件的部分职责，未引入原完整的行列修改、导出或公式处理。

### 当前拓扑表格与诊断定位

`GeometryEditor → useGeometryTopologyMap` 为 Nodes、Arcs、Regions 三页持有同一选集；`useTopologyTableSelection → geometryTopologySelection` 把表格行映射到当前快照中唯一匹配的源 FID 与 row ID，选中一种类型会清除其他类型。选集身份包含工程 owner/session/context、服务版本、Geometry 路径/源修订、原样 `draftState` 和 completion `draftIdentity`；身份变化后旧选集及定位立即不可用。每次表格、空间选择、诊断定位或重置采纳都会推进共享 `selectionVersion`：本地新增行只留在产生它的表格选择中，外部选择即使回到相同 FID 也不能恢复旧本地行。表格或诊断选择还会中止前序空间选择请求，迟到响应不能覆盖新选择。

`TopologyBuildResultViewer` 与 `TopologyFeatureErrorControl` 通过 `GeometryTopologyDiagnosticTargets` 提供“选择并定位”。控件只接受当前 completion 消息原有的 typed targets，并同时核对 `featureKind/fid/tableRowID/mapFeatureIDs` 与当前真实源行和地图要素；`controlPoint` 显式映射 Regions，缺少有效行、几何或有限坐标时禁用。Host `GeometryTopologyService.DiagnosticTargets` 把 `ArcIndex/NodeIndex` 源集合序号解析成 FID；`RegionIndex` 是完成区域序号，须经该区域的 control-point indices 解析成源控制点 FID。React 不从诊断正文或 logData 文本推测索引。选择完成后转到对应页并保留原始诊断文本。

高亮使用 Host 返回的真实 map feature IDs；范围遍历实际 `paths`，区域边界的空 FID 通过实际 `selectionIDs` 关联源行。`GeometryTopologyMapPanel → GeometryTopologyCanvas` 将“缩放至选中要素”或诊断定位请求交给本地 SVG 相机：非点范围加 1.25 倍边距，单点使用拓扑源范围的 1/20 尺度；无有效尺度则不可定位。Regions 地图以 `allowMapEditing=false` 挂载，选集/定位本身不发送 `topology.edit` 或保存 HDF。既有节点/弧编辑继续使用父 Geometry 草稿与 `model.save`。原 `Topology*Editor` 的 FeatureSelection 绑定、`TopologyErrorHelpers → ContextIndexPairBehaviors` 和 `TopologyVisualContextBehaviors` 提供选择/定位职责依据；新增 helper、hook 和诊断按钮是 React 接线，不等同于完整 WinUI 动画或上下文菜单迁移。

### 当前地图节点网格检查

`MapFeaturePage → MapLayerTree` 的精确 FlowArea 选择，经 `useMapMeshErrors → mapMeshErrorsWorkflow` 向 `RasLayerPropertiesPane → MapMeshErrorProperties` 提供地图节点的检查设置、真实 degree-error 列表和定位。候选节点必须来自匹配当前工程/地图/`.vis` 的 `map.resultSources.treeRoots`，保留 FlowArea、Geometry 及适用的 Result/MultiResult 祖先 locator；`workspace.file.inspect` 捕获真实 HDF 路径和 SHA。请求使用 `sourceTreeCapture=true`，可读取未勾选的源树节点；Result 的嵌入 Geometry 使用该 Result 文件与显式 slot 0 owner，MultiResult 另须匹配精确 Plan 的来源覆盖。来源正在被计算写入时不以旧几何替代读取。

此地图入口只调用 `map.tree.meshErrors` 的 `load/set`，不使用 `draftSettings`，也不读取 Geometry 编辑器的未保存草稿。`load` 显示已保存设置对应的真实扫描，设置默认关闭、阈值默认 8；保存按原 `MaxFacesPerNodeEntry` 规则 AwayFromZero 取整并限制到 3–64，算法只返回连接面数严格超过阈值的节点。Host 最多返回 10,000 条并明确遗漏数量。前端验证工程会话、workflow/context token、地图身份、前后 `.vis` 修订、精确 locator、源路径/SHA、设置、计数及有限坐标；稀疏 `.vis` 节点实体化只允许原先为空的 locator 路径补齐，不能接受已知路径漂移。

设置草稿由稳定挂载在 MapFeaturePage 的 hook 持有，`map.mesh-errors:<scope>` dirty owner 与 guard 保护地图、对象、结果时间轴和工程切换，并暂停普通 catalog 刷新。`set` 先取得普通 root map-write lease，只改当前 `.vis` 节点设置；真实回执被根工作区采纳后，仍须完整读取地图目录、每张树、最新 `map.resultSources`、同一源 SHA 及新的 `meshErrors load`。读回匹配保存修订及设置后，先安装完整树、remembered map selection 和原 FlowArea 的新 locator，再解除 guard、恢复 catalog effect 并释放 lease。`savedReadbackUnavailable` 表示设置已经写入而诊断后读失败；不能作为未保存重新提交。未知结果、未采纳回执或读回失败保留事务/lease/dirty owner，只允许重新读取核对；没有有效原回执时不能凭新设置补造回执，只有已确认终态且旧修订/旧设置均未变化的读回可解除未写入事务。取消中止请求或读取，但不能宣称写入回滚；普通读取的代次和完整 owner 校验拒绝过期结果。

列表定位遵循原 `MeshErrorProperties.cs` 的 `CellsExtent.RatioAroundCenter(2.0)`：采用真实相邻单元范围放大两倍后适配画布，范围缺失、退化或溢出时以真实 NodePoint 居中并保持当前比例。定位标记属于当前 capture，手动相机保护避免后台自动适配覆盖。`MapMeshErrorProperties` 及其 hook/workflow 是地图胶水；既有同名 `features/geometry/MeshErrorProperties` 仍由 `GeometryMeshPage → MeshDiagnostics` 展示 `geometry.mesh.degreeErrors` 的当前 Geometry 草稿扫描。地图保存设置与几何草稿扫描分别保留各自职责，均不把 degree-error 面板视为属性曲线入口，也不重复计算原 UI inventory。

### 当前地图文档重命名与删除

`MapDocumentActions` 在地图标签旁提供名称输入和显式删除确认，名称会去空白并检查长度、控制字符及同工程内不区分大小写的重名。`MapFeaturePage` 只允许当前活动的已保存地图进入操作；工程草稿、地图草稿、其他地图写入、未读完或失败的目录以及缺少父写入保护都会禁用提交。`mapDocumentActionsWorkflow` 构造包含工程路径、XML 及 SHA、Core 引用、旧 `.vis` 修订、地图 index/name/type 的请求，分别调用 Host `MapTreeService.RenameMap/DeleteMap`。它们只更改 `.vis` 的地图文档目录及适用的活动地图名称，不保存 `.ras`、模型 HDF 或来源文件。

提交前，`MapVisualizationTarget.documentMutation` 连同原地图身份进入 `captureMapVisualizationLease`：重命名记录 `newName`；删除根据连续的操作前目录计算每一张剩余地图的 `previousMapIndex/mapIndex/mapName/mapType`，包括索引未变化的前序地图。Host 删除回执必须逐项匹配这份完整映射，且返回被删文档身份和变化后的 `.vis` SHA；重命名回执须匹配新名称、原 index/type 和前后修订。`MapVisualizationAcknowledgement` 保留 `documentAction` 与真实回执，由 `RasMainWindow.onCommitMapVisualization` 对原工程、目标和 lease 验证后采纳一次并递增 `mapRevision`。

采纳后仍通过 `loadMapDocumentCatalog` 读取地图清单、每张地图的树，再复读清单确认一致性。完整读回必须匹配回执修订及预期全部地图身份，才安装新标签和活动选择，清除旧要素/节点/网格选择、悬停、符号预览、测量及待处理的计算器定位。删除不是对旧标签数组做局部 splice：新目录按 Host 的全部剩余索引重新绑定；活动地图按读回的活动索引或仍存在的相邻位置选择。文档操作处于 `mapDocumentMutationBusy` 或 `mapDocumentNeedsReconcile` 时，普通 catalog effect 暂停自动刷新，由事务独占目录读回；effect cleanup 中止在途读取并递增 `mapCatalogGenerationRef`，旧响应不能安装。事务先通过 `installMapDocumentReadback` 写入新目录、活动标签及 `rememberedMapSelectionRef` 的新 index/name/type，再解除 busy/未决状态，普通刷新才恢复并按新身份保持选择；父回执提前递增 `mapRevision` 不会再让旧名称匹配失败后回退到 Host `activeMapIndex` 覆盖事务选择。

`MapDocumentRequestNotSubmittedError` 专门标识能力检查、参数校验或请求构造在发送前失败；网络错误、无效回执和提交后处理失败不能据此宣称请求未送达。未确定状态、有效回执未被父层采纳或目录读回不一致时，notice 保留原目录、目标、修订及可用回执，禁止重复提交。单次 root lease 在 `finally` 结束，但 `needsReconcile → mapMetadataCommitBusy → map.write:<projectContextKey>` dirty owner 继续保持地图写入与工程上下文保护。显式“重新读取并核对”确认完整操作后目录且 `.vis` 已变化时可解除未决状态，并说明这是读回证据而非补造原请求回执；仅在已确认请求未提交/终态失败、完整旧目录与旧 `.vis` 都未变化时才开放重试。其余读回或读取失败继续保留保护。

临时栅格计算器图层尚无地图文档重命名/索引变更的自动重绑定。重命名检查目标地图；删除检查目标及所有后续会移位的地图。只要这些地图绑定当前会话计算器图层，界面明确阻止操作并要求先移除受影响的临时图层；隐藏图层也参与此检查。

### 当前 Geometry 网格元素检查

`GeometryEditor` 将当前 model、工程上下文和父 mesh/topology 快照传给 `GeometryMeshPage` 的 `MeshElementInspector`。`meshElementInspectorData` 解析 `geometry.mesh.load` 携带的 `GeometryMeshMapData.Cells`；Host 按总单元数抽样，并限制为 12,000 个单元、1,000,000 个顶点，回报实际显示、遗漏及无效候选数。前端核验预算、计数、有限坐标、重复元素和 Flow Area 源行后绘制这些实际多边形；点选保留 `flowAreaID/meshIndex/cellIndex`。这些是修订范围内的源行及网格局部零基 ordinal，不是 HDF FID。预览没有 Face 命中数据，因此 Face 只按已知源 ordinal 输入；不会把 Cell 的多边形或索引解释成 Face。预览中缺少的元素仍可用有效索引向 Host 定位，Node 不在此检查器的选择范围内。

读取顺序是 `geometry.mesh.findElement` 后接 `geometry.mesh.elementPropertyChart`。请求捕获 Geometry 路径/修订、工程会话/Core 上下文、Flow Area ID/名称/源行、元素类型/ordinal，以及实际 mesh/topology `draftState` 文本的 SHA-256；空 topology 草稿也按空文本计算摘要。定位回执校验源路径/修订、Flow Area、类型/索引、双草稿 SHA、有限中心/视口和实际多边形或面折线；曲线回执另校验 operation ID、projectSessionID、assetID、Flow Area 行、单位及数值点。工程、来源、草稿、服务实例/能力版本或选集变化、取消和卸载会递增代次并中止请求，旧响应不能恢复旧定位或曲线。

Host 以捕获的源模型及子草稿读取真实 CellVolume/CellElevation 或 FaceArea/FaceElevation 属性表，前端用返回的物理单位绘制库容—高程或过水面积—高程，并提供可展开的中文数值表。`ready` 必须有真实有限点；`requiresPropertyTables/cancelled` 必须没有点，不显示合成、示例或沿用旧曲线。缺表时提示用户经现有 Geometry 属性表计算区生成后重新选择。检查器不追加 `computePropertyTables` 意图，也不写源 HDF；若草稿已有该计算意图，Host 可在隔离的临时 `ModelContext` 内重放真实计算再读表。草稿计算和父 `model.save` 的持久化边界保持原有归属。

原 C# `Ras.Visual.WinUI.Controls/MeshErrorProperties.cs` 的关系是 `FlowAreaLayerVisualContext.MeshErrors → MeshNodeDegreeError → MeshErrorList_ItemClick → CellsExtent/NodePoint → ZoomToPoint.ZoomToExtent`，用于 degree error 列表与定位，没有此属性曲线入口。`MeshElementInspector` 和 `meshElementInspectorData` 是使用现有真实 Host 服务的新 React 胶水，不是原 `MeshErrorProperties` 已有图表的同名迁移，也不增加原 UI inventory 项。

### 当前 Geometry 网格 Shapefile 导出

`GeometryEditor → useMeshShapefileExport → MeshShapefileExportController` 在网格子页之外保留导出任务；`GeometryMeshPage → FlowAreaLayerBehaviors` 选择当前真实 Flow Area、导出类型和服务器绝对 `.shp` 路径，`MeshShapefileExportTasks` 展示原捕获、进度、终态及文件回执。原 `FlowAreaLayerBehaviors.cs` 的两个导出 action 提供职责依据；当前同名 React 控件只接单元中心与外围面导出，不代表原类其他动作或 Windows 文件管理器行为已迁移。它明确选择一个 Flow Area，而非沿用原 action 的首个要素假设。

`meshShapefileExport` 捕获工程会话、路径/XML/修订/Core、Geometry model 与源修订、Flow Area ID/名称、完整 mesh/topology 草稿原文及 SHA-256；未进入 Host 子草稿的本地表格修改会阻止导出。同步登记 `geometry.mesh-export:<operationID>` 后才计算摘要并发送 `geometry.mesh.exportShapefile`，摘要完成前再次核对来源和后端。写入期间保留原任务上下文，切网格子页不丢失任务，来源改变也不会将旧回执应用到新对象。

Host 在临时模型中重放现有拓扑和网格变更，导出过程不重放属性表计算。`CellCenters` 使用完整真实 `mesh.CellCenters`，不从有限地图预览取样；`PerimeterFaces` 遍历原 `mesh.Faces`，仅导出一侧 cell 为负的真实外围折线，并保留零基 `Face`、`CellA`、`CellB` 与实际 `Length`。输出目录须已存在，同名 `.shp/.shx/.dbf/.prj/.cpg` 任一文件均禁止覆盖；暂存写入、完整文件族验证、源修订复核后才发布。投影来自实际工程 WKT，Local 坐标不编造 `.prj`。该操作只写用户指定的服务器 Shapefile 文件族，不保存 Geometry 草稿、工程或地图。

完成回执须匹配领域 operation ID、源路径/修订、Flow Area、导出类型、双草稿 SHA、目标路径、要素数及文件族；每件产物保留 Host 返回的路径、字节数和 SHA-256。领域取消回执必须为零要素、空产物和空输出路径。传输或解析状态未知时，controller 保留 dirty owner 并阻止新导出及放弃未决任务；有原 HTTP ID 时仅恢复该任务轮询。取消表示请求停止，收到已完成回执仍按完成处理。页面展示的是服务器产物回执，不表示浏览器已下载文件。

### 当前 Plan 分析草稿桥

`EditorWorkbench → capturePlanEditorDraft → onPlanAnalysisDraftChange → RasMainWindow` 发布保留中的 Plan 编辑器来源，`planAnalysisDrafts.resolveAnalysisDrafts` 为 PlanDiff 与 R2R 提供同一份可核对捕获。registry 记录 editor ID、精确 dirty owner、工程会话/路径/XML/修订/Core、后端实例/能力版本、Plan 路径/源修订、草稿代次与可用状态；它不随根工作区的最新资产选择改写原编辑器身份。`captureKey` 只作内存一致性比较，含源 XML 和原始值，不写日志或持久化草稿存储。

捕获沿用父 `model.save` 合同：只传相对快照实际变化的字段，排除只读字段与独立时间窗字段；保留原始字符串、精确 `sourceState`，按 mode/start/end 的父保存次序生成实际变化的 `planTimeWindowEdits`。图像、表格、samples 或尚未提交的时序文本变化无法由此合同完整表达，会阻止分析；验证规则/快照未就绪、无效草稿、处理中的操作、来源或修订不匹配、重复 Plan/dirty owner、未捕获的其他 dirty owner 同样阻止。不能移除某个 dirty 标志后改读已保存 Plan；R2R 仅能排除自身表单/导出 owner，不能绕过已发布的 Plan owner。

父层按 editor ID 原子替换并冻结 publication，每次变化推进 registry 修订；普通卸载移除该 publication，已提交保存仍在途或结果未知时则保留 `uncertain` 记录。`hasProtectedSourceWrite` 将地图写入、`geometry.mesh-export:*`、`tools.r2r-export:*` 及 Plan 的 `writePending/uncertain` 纳入根页面导航、工程切换与放弃保护；真实保存回执经独立 `onContextCommit` 路径采纳。

`EditorWorkbench → planAnalysisDraftLifecycle` 固定原 gateway、后端能力版本、源上下文、Plan asset、dirty owner、已受理 HTTP ID 和已收到的保存回执。“继续核对原保存”只通过原 gateway 的 `resumeOperation` 续查同一 HTTP 任务；已有回执则重新核对并申请采纳，不重发 `model.save`。没有原 HTTP ID 时保留草稿和保护，不能自动续查或重新提交。仅在尚未收到完成回执、且服务端明确确认失败或取消时解除 `uncertain`，保留当前草稿；未知状态或回执未获采纳继续保留原捕获和来源保护。

续查前同步发布 `uncertain + busy`，同时登记原 HTTP ID 与本次 `attemptID`。`RasMainWindow` 仅允许 editor ID、dirty owner、HTTP ID、attempt ID 和精确源上下文全部匹配的真实回执通过一次；其他写入保护仍生效。该路径不先异步清除 guard，根页面采纳后才更新编辑器基线并解除原保存的不确定状态。显式放弃由 `discardRevision` 通知恢复基线或接受新来源、重置矩阵设置；`matchesPlanDiscardTransition` 只消费与该 revision、旧 source owner、目标 owner 全部相同的旧 effect，防止它再次建立已放弃的 pending 来源切换。registry、捕获/生命周期 helper 与 root 接线均是 Web 胶水；草稿持久化仍归既有父保存，分析接口只在私有 Plan 实例重放捕获。

### 当前 Plan 属性比较

`ToolsPageView → PlanDiffView → PlanDiffOption / planDiffWorkflow` 通过 `plan.analysis.diffOptions` 读取真实来源目录，再以选中的两个完整 option 与当前可捕获的 Plan 草稿调用 `plan.analysis.diff`。原 `PlanDiffView.cs` 的双来源选择、KEY/左右原值、差异标记和仅差异过滤继续分别保留；Host 按原规则反射简单公开 Plan 属性并排序、格式化，React 保留原始属性键、字符串值及错误/null 文本，只对差异行作本地过滤和高亮。

原 `PlanDiffOption.ForResult` 使用 `result.Plan.Plan`，因此 Result 选项比较其关联 Plan，所带草稿也按同一规范 Plan 路径/修订匹配。当前 Host 的 `resultRevision` 为 null，不能据此声称校验了 Result HDF 的内容修订；不可用 Result 也可保留 `resultPath=null` 的独立 option ID。前端校验可空字段实际存在，并要求回执完整匹配左右 ID、Plan 路径/修订、Result 字段、标签及类型，不把缺失的 Result 修订补成 Plan SHA。

目录与比较各有请求代次。工程会话/路径/XML/修订/Core、活动资产、服务实例/能力版本、左右来源或共享草稿捕获变化，会使旧比较立即不可展示并取消其请求；切回相同来源也不能复活旧响应。`resolveAnalysisDrafts` 未能完整捕获当前相关草稿时显示阻止原因，不退回已保存文件冒充当前编辑。比较只在临时 Plan 上应用草稿并读取属性，不写工程、Plan、Result 或其他模型文件。

### 当前 R2R 参数矩阵

`ToolsPageView → R2RMatrixView → R2RMatrixVM → r2rMatrixWorkflow` 使用 `plan.r2rMatrix.options/preview/emit`，分别对应原 `R2RMatrixVM` 的参数轴、非法组合规则、不适用参数折叠、组合表及显式 emitter 职责。选项来自所选源 Plan 和当前可捕获草稿，保留原 `PropertyName`、枚举原值及候选顺序；中文标签独立于服务值。轴不能重复，候选可选中/移除，自由值由原 C# 解析并按解析结果去重；来源、草稿、轴/值顺序、规则、折叠或输出设置变化使旧预览失效。Host 返回实际组合文件名、每项适用性/原因及可写数量，组合上限为 2048；前端不自行模拟水力规则。

`tools.r2r-form:*` 只保护当前矩阵设置。真正导出先冻结来源、草稿、预览、表单和原网关，同步登记 `tools.r2r-export:pending-<localID>`；获得 HTTP ID 后先登记对应任务 owner 再释放临时 owner，保护不中断。`inspectionID` 由 Host 绑定工程路径/修订/XML 摘要、Plan 路径/修订、原始字段/sourceState/有序时间窗编辑、规范化轴值及顺序、规则顺序、折叠、输出目录和已捕获依赖路径/修订。它不代替编辑器会话/owner 校验，也不是文件产物回执。`emit` 重建并核对同一检查身份后才执行，不能拿旧预览提交新参数。

`PlanAnalysisService → PlanMatrixPreparationWorkspace → PlanReadyToRunEmitter` 保留原 `TryPrepareCompute` 的准备行为，同时隔离其写入：以有效内存模型 `SaveAs` 出私有 Geometry 与存在的 BC HDF，重新绑定临时 Plan 关联，保留实际 Terrain/NValue 关联和外部连接解析。属性表/准备摘要只在私有 Geometry 中保存；临时模型释放并关闭原生句柄后清理工作目录。缺失 BC 不被构造为虚假默认源，仍由原计算前检查返回诊断。预览及提交前后核对已捕获的已保存 Geometry、BC、Terrain、NValue 核心 HDF 修订；这些路径/修订也进入检查身份。终态 `sourcesStillCurrent` 合并工程、Plan 与该依赖集合，不代表对任意外部栅格或 sidecar 的无限递归清单验证。该入口生成服务器 `.r2r.h5`，不启动求解器、不保存源 Plan/工程或覆盖这些输入 HDF。

导出沿原 emitter 逐文件返回 Written/Skipped/Failed/Excluded/Cancelled/NotRun、路径、字节数和诊断。覆盖开关只决定同名 R2R HDF 的处理；`writeManifest` 会生成或更新同目录同计划 CSV，独立于 HDF 覆盖开关。前端核对来源、检查身份、组合顺序/文件名、目录、逐文件状态与汇总计数，不将进度事件当终态，也不替没有完整文件回执的失败/取消补造文件清单。部分文件可在失败或取消前已经写出，取消不回滚它们。未知传输/无效回执保留原捕获、原始响应及写保护；有 HTTP ID 时通过捕获的网关恢复同一任务，无 ID 时禁止重发。任务回执始终属于提交时来源，表单、目录选项或后续选择不能改写它。

### 当前稳定河道设计

`ToolsPageView → StableChannelWindow → TractiveForceControl → TractiveForceSolveFor / TractiveForceRequiredInput` 保留四个原 C# UI 文件的独立职责：窗口持有设计来源，Control 组合表单，SolveFor 管理方法与未知量，RequiredInput 管理流量、水温、边坡及左岸/河床/右岸材料参数。`useStableChannel / stableChannelWorkflow` 是 React 草稿与 HTTP 接线。Host 打开当前工程首个 `StableChannelDesign`，不存在时使用明确标记的原始内存默认设计；内存设计不能保存或自动创建文件。原 Control 的硬编码样例图未作为计算图展示，页面挂载不代表完整 WinUI 窗口表现已迁移。

草稿保留数字输入的原始字符串和独立基线；从 Depth、Width、Slope、ParticleSize 中选择恰好两个目标后，显式计算或有效输入变化触发的延迟计算调用原 `TractiveForceMethod`，只回填求解目标。ft、ft³/s、°F、mm 和角度单位沿用来源，内部角度/粒径换算仍由原 `convertValues` 执行，React 不重复转换。读取代次与工程会话/路径/XML/修订/Core、后端实例/能力版本、设计路径/修订/source ID 一起约束响应；打开或保存前经 `project.open` 核对当前 XML 正是已保存来源。

`stableChannel.save` 只对捕获的现有 `.scd.h5` 进行暂存及替换，前后核对工程与设计修订，不保存 `.ras` 或 `.vis`。回执必须匹配 request ID、source ID、设计路径和提交的数字状态，才能推进设计修订及草稿基线。未决写入保留原网关、HTTP ID 和写保护；传输终态未知与服务已返回但回执未通过核对分别展示，均不重复 POST 保存。保存失败保留输入，真实完成回执不能被后续工程或后端身份采纳。

### 当前工程投影导出

`ProjectExplorerControl` 的工程标题通过 `ProjectCatalogHeaderBehaviors`，所选工程节点通过 `ProjectBehaviors`，注册同一个 `GenericProjectBehaviors.ExportProjection`。实际 catalog 的工程节点为 `ModelSectionContext`，必须与当前工程匹配；投影来自精确目录来源的 `Project/ProjectionWKT`，不使用所选模型的修订。这里覆盖三个来源文件中的投影导出与注册职责，`GenericProjectBehaviors` 和 `ProjectBehaviors` 的其他命令并未因此全部迁移；controller、parser 和回执面板是 Web 胶水。

请求捕获工程会话/路径/XML/修订/Core、后端实例/能力版本，并在发送前登记 `project.projection-export:*` 保护。内存工程、无投影、目录来源过期、未保存输入及并行写入会禁用操作。Host `project.projection.export` 在私有工程模型应用提交的 XML 元数据，调用原 `Projection.ExportEsri`，核对工程文件修订，以不覆盖方式在工程 `Exports` 中发布唯一 `Projection.prj` 或编号文件。前端校验工程与目录/文件名回执，再以 `workspace.file.inspect` 确认相同输出路径、规范路径、非零长度和 SHA-256。

传输未知时只续查原网关的同一 HTTP 任务；已有路径但读回失败时仅重读该文件，不重新导出。工程或后端变化拒绝迟到回执。本操作只生成服务器 `.prj`，不保存工程或改变模型/地图修订；当前入口没有浏览器下载或本机资源管理器定位动作。

### 当前工程归档与下载

`ToolsPageView → DevHomePage → ProjectArchiveWorkflow` 只承接原 `UploadCurrentSubmit_Click` 中当前工程的归档准备职责，`includeResults` 默认 true 对应原 `UploadResults`。S3 上传、上传链接生成、远端模型编号下载/打开均不属于此入口；浏览器 ZIP 下载是 Web 新增适配，不能等同于原 `DownloadModelSubmit_Click`，也不能据此标记完整 DevHomePage 已迁移。

准备前冻结工程会话/路径/XML/修订/Core、后端实例/版本及结果选项，由 `project.open` 确认保存文件与当前 XML 相同。未保存输入、其他在途任务及仅在会话加载的外部 Core/HDF 引用会阻止归档；所选资产不影响整个工程目录的来源身份。Host 将所含文件复制到私有目录，在暂存工程上执行原 `Project.ZipFullProject`，保留已保存外部连接元数据而不追读这些外部来源，并复核源目录清单和 ZIP 内容。原工程 `.ras` SHA、暂存保存后的归档内 `.ras` SHA、归档清单 SHA 分别记录；清单 SHA 不是 ZIP 自身的字节摘要。 原 `CoreLayerBase` 加载后要求模型内部名称与文件名一致；若由此触发 `NeedsSave`，暂存工程保存会更新相应 HDF 的 `Name` 和 `Date Last Modified` 元数据。因此归档内模型不保证逐字节等于源文件；这些保存只发生在私有副本，不能把任意数据集变化归为预期差异。

Host 只在 ZIP 核验、暂存模型释放及暂存目录清理完成后，将 `.pending` 文件原子发布为 token 对应的完整 ZIP。`ServerBackend → SessionBackend.projectArchiveUrl` 生成 `GET /api/project-archives/{token}`：服务只接受规范 UUID，从私有根目录读取已发布文件，不接收任意路径，响应为 `application/zip` 并禁用缓存。点击下载只表示已请求浏览器传输，不能宣称用户本地保存完成。`developer.projectArchive.cleanup` 由用户显式触发，始终使用原回执 token 与捕获后端；下载和切页不自动删除产物，清理未确认时保留回执并允许重试。

归档任务在已验证终态前保留来源保护。取消请求不代替取消终态，竞态返回完成回执时仍保留真实 ZIP；未知传输与已返回但无效回执分别展示，后者保留原始回执且不能使用未核验 token 下载或清理。恢复只核对原 HTTP ID，不重发准备请求。切换来源后历史回执明确标记原工程，并继续通过其原后端下载/清理，不提交当前工程状态。

`currentImplementation` 与本表只记录当前源码中的组件挂载、操作名、状态所有者和持久化边界；这是静态关系图，不代表构建成功、浏览器/Electron 交互、Host 实际运行或真实文件路径验收。旧 C#/SwiftUI 内容仍是来源契约，模块存在、来源契约或预览草稿不代表完整交互或持久化。旧 `VectorSymbologyEditor` 仍只是条件求值预览；独立的图层样式编辑器和 C# 地图绘图链路已在上表记录源码接线，但仍需单独运行验收。

## 来源基线与可比口径

`resources/editor-schemas.json` 是本仓库已有的来源基线，`sourceRepository` 指向 `JamesLinYJ/HEC-RAS-2025-Renamed`。其中 517 个 inventory 项具有唯一稳定 C# `source` 路径，代表资源支持的 C# UI 基线；文件还包含 224 个 schema 和 9 个 table。该 517 项不包括所有额外代码创建控件、弹窗或 backing/support 类型。Renamed 来源仓库中的 `macos/Tools/audit-ui-rewrite.py` 与 supplemental review 数据不是本项目里的 React 覆盖清单。

`currentImplementation.uiMounts` 是按功能关系分组的组件挂载记录，不是逐源类 inventory。TS/TSX basename 与 517 个 source ID 相同，只能作为候选文件线索；要比对挂载范围，可从 `frontend/src/main.tsx` 递归追踪字面量相对 import/export/dynamic import，并显式展开如 `RasContent.tsx` 中 `../features/*/index.tsx` 的 Vite glob。未解析 import、其他 glob/代码生成入口及同名但语义不同的文件须单独标记。`QuickProjectImportControl` / `QuickProjectImportWorkflow`、共享会话引用 registry / `SessionBackend` 与 `cumulusAuthentication.ts` 等是适配、辅助或基础设施；`SessionHdfImportControl` 没有对应的原 C# import-control 类。`CumulusImportSource` 与 `CumulusLoginWindow` 对应原 C# 控件职责，但同名或接线本身不证明完整功能迁移。此类 Web 胶水不增加原 C# UI 基线的迁移项。文件名、schema 数或可达模块数都不能证明功能已实现或推出迁移完成率；严格的 full/partial/unimplemented/unverified 状态需要逐 stable source ID 核对实际 React mount、可用行为与对应验收证据。

## 迁移参考契约范围

从下节起保留既有 C#、SwiftUI 与后端的领域关系，作为后续逐文件迁移的参考要求。下文的 SwiftUI 类型、macOS 系统能力和进程实现描述属于来源实现，不代表当前 React 界面已实现它们。JSON 中 `domainRelations` 等既有集合具有相同参考范围；当前 React 边界单独放在 `currentImplementation` 中。

## 来源主调用关系

`SwiftUI View → WorkspaceStore / editor session → typed provider → BackendProcessClient → C# Host service → domain model / algorithm`

界面使用 SwiftUI 的窗口、导航、表格、表单、菜单和文件面板。工程地图和图表由对应画布呈现。C# 后端通过 JSON 请求响应及任务事件交互；模型文件、计算算法和文件格式的语义保留在后端。

后端进程的阻塞管道读写、退出等待及有界事件队列的生产者背压运行在 Dispatch 拥有的队列中，不能占用 Swift 协作线程池。事件消费者通过 continuation 挂起；容量仍为八条，保持顺序且不丢帧。取消唤醒等待者并清理对应子进程，已接受的终止回执继续排空；大量计算或地图读取并发时，不得因生产者等待消费而饿死消费任务和无关请求。

## 所有权

| UI 领域 | 状态所有者 | 持久化边界 |
| --- | --- | --- |
| 项目、资产目录、窗口路由 | `WorkspaceStore`、`ProjectDocument` | 项目 XML；当前会话和窗口状态分别管理 |
| 几何、拓扑、网格和涵洞编辑 | 对应 editor / child session | 子草稿、撤销栈与父几何保存事务绑定 |
| 边界条件、时间序列和网格输入 | Boundary / Spatial editor session | 当前输入身份、时间范围及父 HDF 保存 |
| 地形、糙率和分类数据 | 对应 source session | 源文件修订、关联对象与父编辑事务 |
| 保存的地图和图层树 | map-scoped source context | 精确地图索引、名称、类型、节点 locator 与 `.vis` 修订 |
| 结果、历史帧和实时计算 | Result session、compute run owner | 精确 Result 路径、输出块、逻辑 slot、profile 和计算读取租约 |
| 渲染样式、运行时绘图选项 | 当前渲染对象或样式 session | 保存样式与临时 renderer 选项分别处理 |
| 数据表、选择、剪贴板 | 表或矢量层 session | 稳定行列 ID、实际源 feature 身份与当前编辑代次 |
| 独立图表和剖面窗口 | 被捕获源上下文的 window session | 原窗口的源对象和当前请求，不借用其他活动窗口的数据 |
| 系统操作与凭据 | 窄平台服务 | macOS 文件面板、Keychain、终端和系统 URL 操作 |

## 几何草稿与矢量来源

几何拓扑子会话拥有 topology 导入和 Arc / Region 元数据列变更。Shapefile 导入只接受实际折线和面要素：预览捕获当前 Geometry 源修订及拓扑草稿身份，提交时重新读取所选源文件族并验证同一草稿，之后将项目坐标中的要素作为一条可撤销的拓扑变更写入子草稿。合并相交弧、保留未合并弧和清理外部弧都是这条变更的选项；只有父 Geometry 保存会写入 HDF。

Arc 和 Region 的用户自定义 metadata 列通过同一拓扑子会话变更，并进入相同的 undo / redo 记录。列权限沿用来源的 Arc 与 ControlPoint 默认列判断；Node 不提供列增删改。ObservableTable 负责列类型、行默认值、通知和保存跟踪。列变更不会创建独立 HDF 保存或临时父会话，父几何保存时和普通几何字段、图像及其他拓扑编辑一并提交。

通用折线与面地理处理可以选择已加载的 File Geodatabase 矢量子图层。来源捕获绑定当前项目、持久化地图、完整 .vis 修订、精确 GdbVectorLayer locator、持久化子图层名和类型，以及覆盖整个 .gdb 数据文件清单的 SHA 修订；目录不能伪装成单一文件，也不能只凭显示名称选子图层。工具来源取自该地图已接受的来源树与已完成的矢量读取批次，不依赖侧栏是否显示或当前树模式；项目会话、完整地图工作流或可视化修订变化后，旧批次不得再捕获来源。Host 重新解析保存的地图子节点，打开 OGR 中同名且同类型的真实图层，核对来源清单和目标 CRS 后复用既有地理处理算法。折线与面几何来自该地图图层实际加载的要素；项目坐标 XY 与源坐标 Z/M 按原图层表示传递。操作只读源目录，结果仍经既有文件族暂存发布或剪贴板路径；取消和过期响应继续服从原地图/来源/操作代次检查。

## 地图来源与栅格计算

Add Map Layer 的文件面板允许多选，按用户选择顺序逐项调用原有矢量、GDAL 栅格或 File Geodatabase 导入。批次捕获同一项目会话、地图对象及源树上下文，逐项记录实际来源修订、可视化提交回执、失败和重复选择；单项来源失败可继续，取消或所有者过期则停止后续项。GDB 仍通过目录检查页确认实际子图层，批次等待其真实提交结果。关闭检查页不会丢弃已开始的提交回执；退役操作最终失败时释放批次等待，不把失败当成已导入。 空项目的首个有效图层与 Project 地图在同一真实提交中建立；只有核对实际 `.vis` 行、修订与预备工程 XML SHA 后，批次才采纳新地图身份。所有输入均失败时不创建空地图。连接预备快照按实际提交的节点顺序序列化，缺少 Description 的新项目也必须生成相同 XML 与 SHA。 调用方可等待本次导入 Task 返回的真实批次回执；全局展示属性只在捕获的项目会话仍匹配时更新，不能靠轮询该属性判断另一个批次或已退役项目的提交结果。

文件型 GDAL 栅格以规范化根路径和 GDAL 实际打开的本地文件清单为身份。清单按路径、长度和 SHA-256 确定修订，因此 VRT 的所有本地依赖都参与检查；无法按本地文件校验的 `/vsi*` 来源不导入。GDAL 元数据选择原有的 SingleBand、RGB Image 或离散栅格 renderer，预览和地图绘制调用相应保留的 renderer；`.vis` 只记录原来源及 MapType，不转换或快照化栅格。GDB 另有共享目录 manifest：父目录下的数据文件修订覆盖向量层与栅格子数据集，向量子项按真实层身份读取，栅格子项保留 GDAL 子数据集连接字符串并在数据集生命周期内读取。

当前 React 栅格计算器挂在 `ToolsPageView → tools-tab-raster-calculator → RasterCalculatorEditor`。它经 `map.document.list` / `map.tree.load` 读取保存地图，再对选中的 Project、Buildable、Geometry 或 Plan 地图调用 `raster.calculator.inspect`。Host 返回 Terrain 与地图输出 Result 来源、来源文件依赖修订、NoData 选项、Profile 和按计算器来源求得的真实 `validExtent`，并绑定工程 XML、地图及 `.vis` 身份。表达式、输入、常量、NoData 行为、图层名、范围/分辨率和结果 profile 是编辑器草稿；默认预览范围是所选输入的有效范围交集，也可输入有限的世界坐标范围。每边最多 1536 像元，总量最多 2,097,152。

`raster.calculator.evaluate` 按当前配方和所选 profile 产生 Host `RasterRenderLayer` 的 RGBA 帧。`validExtent` 是计算来源的真实数据范围；计算器面板预览范围是用户给定的计算视口；地图上的帧范围则来自当前相机 viewport，随地图缩放、平移、尺寸或 profile 变化重新求值。Web 只接收用于绘制的 RGBA 像素，不提供数值像元采样或 Identify 值读取接口。

`useRasterCalculatorSessions` 把 Add to Map 结果放进当前工程会话的内存 registry；`MapFeaturePage` 与 `MapLayerTree` 将其作为临时地图层显示，提供选择、缩放、显隐、透明度、动态 profile、编辑配方和移除。Save to Existing 按当前图层 ID/revision 与精确工程会话和地图身份进行比较更新，只允许静态替换静态、动态替换动态；更新后保留原图层 ID、名称、显隐和透明度。静态/动态不兼容时保留原层，用户可另加新层。Add/Replace、显隐、透明度、profile 和 Remove 都只改变会话内存状态，不写入保存地图、`.ras`、`.vis`、模型 HDF、输入栅格或输出像元；registry 以工程 `projectSessionID` 和路径为范围，切换会话时清空，浏览器/应用重启后不恢复。

添加后的图层按当前地图与相机 viewport 重新调用 inspect/evaluate。若 Host 报告 `sourceChanged`，前端清掉旧帧、重新 inspect，再要求原配方仍精确匹配原 source ID 与 revision；不会自动以同名或新修订来源替换配方。若来源身份变化，图层不能继续发布，须用户重新创建或明确重绑配方。图层隐藏、被移除、当前地图/项目不再匹配或 owner/generation 改变时，前端清掉相应帧、来源 capture 和状态，并中止过期请求。

从地图请求编辑携带临时图层 ID、revision 与 request ID。若编辑器已有未完成草稿，界面让用户取消请求、暂留当前配方并暂缓请求，或放弃草稿后载入；取消/暂缓/成功消费时通过 `finishEditRequest` 清除匹配请求，编辑动作中断时也将其暂缓，避免误覆盖草稿或丢失目标。配方继续以 `tools.raster-calculator` 参与工程上下文保护。该会话层对应原计算器 AddToMap / SaveExisting 的实时显示/更新意图；当前实现保留静态/动态类型兼容约束，但 Web 图层是会话对象，不是序列化进 `.ras` / `.vis` 的持久地图图层。以上是当前源码接线和 Host 契约的说明，不代表运行验收。

### 栅格来源的地理处理与范围选择

折线/面地理处理中的 `SnapToHighGround`、`ClipRasterToPolygon`、`RasterCutStats`、`LaplacianTerrain`、`ResampleRaster`、`RoundRaster`、`NoDataLaplacianFill` 和 `FlatAreaDetector` 共享同一精确栅格来源捕获。SwiftUI 输入选择列出当前地图已检查的单波段 GDAL 栅格、当前活动地图中可用且未计算的 Result slot/profile，以及地图中已勾选、可见、可用的 Terrain 关联；取消选择不会回退到其他隐藏或合成来源。地图 GDAL 栅格只通过 metadata-only `rasterSymbology.extent` 检查 `.vis` 与真实来源修订并读出物理范围，不把 RGBA 绘制帧当数值。后端复用真实 float/double GDAL sampler（double 按既有 converter 转为 float），Result 复用 `ResultMapResampler.AsSingleProfile` 的实际 profile 样本，Terrain 复用由当前关联 owner 重新解析出的 Terrain sampler。

Result 捕获绑定当前 map/node、选择覆盖与 context token、逻辑 slot、选中的 Result 路径和 SHA、当前已完成渲染的地图 profile、该 slot 实际映射的 profile、地图类型、关联 Terrain 路径和修订及有效物理范围。普通 Result 只在节点 profile 已稳定且与当前渲染来源状态一致时可捕获；原生时间轴切换先按请求 profile 渲染并安装新帧，随后异步保存 .vis，所以已渲染的 profile 可以暂时领先于保存的 profile。Host 按精确 Result block 校验运行时 profile；MultiResult 子 slot 仍按首个 Result 的实际时间键和原 10 ms 匹配，不按序号猜测。写入租约保护任一 Result/Terrain 时拒绝捕获或执行。Terrain 捕获绑定已保存或当前草稿工程 XML、地图和 Geometry/Plan/Result owner locators、精确 Terrain 关联、来源修订、依赖修订及物理范围；Host 重新解析该关联并在读取后复核所有者和来源。任一来源在执行期间过期都会使操作失败，不会将旧样本安装到输出。

`FlatAreaDetector` 与 `LaplacianTerrain` 的裁剪范围选择包含默认“不裁剪”、当前地图视图、可用矢量来源，以及上述当前地图栅格、Terrain 和 Result 的真实来源范围；源范围变化或失效会清除旧选择。Flat-area 的取消令牌从 Host 传到种子采样、tile provider/flood fill 和 dense/tiled contour 循环；FloodFillHelper 在取消时归还边界种子池句柄并清理 tile buffer，后续可安全复用。取消在发布结果前被检查，Raster TIFF 的部分目标文件由既有异常清理路径删除。

## 图层创建与导出

创建 Shapefile 使用保留的 writer 在同级暂存目录生成 `.shp/.shx/.dbf` 和适用的 `.prj`。操作先准备完整文件族回执并校验项目、地图及目标路径，再发布 sidecar，最后重命名 `.shp` 作为提交点；已存在文件不会覆盖。同步发布失败只撤销本次写入且摘要仍匹配的成员。界面在地图导入前保留创建回执；导入失败后的 Retry 只重用该回执进入既有地图来源导入事务，不再次运行 writer。该回执只属于当前应用会话，不是持久化工程记录。

RGB GeoTIFF 导出绑定精确的地图栅格节点、源文件修订、`.vis` 修订、符号样式和绘图选项。预览与写出沿用 `RasterRenderLayer` 和 `RgbTiffExporter.ExportRgba`，支持完整栅格范围或当前视图。Depth Shapefile 导出绑定精确 Result / MultiResult 节点、当前结果选择上下文、slot 0、结果路径与修订、实际 profile 及其读取租约；轮廓由 ResultMapRenderLayer、深度 Mapper 和 ResultMapResampler 生成，并沿用负水深和简化设置。两种导出都通过 duplex 任务报告进度并响应取消，输出端采用不覆盖提交且先生成文件回执；只有重新核实原始来源后才发布。Local 投影是有效项目状态：RGB TIFF 保留正确地理变换但不伪造 CRS，深度 Shapefile 不写入虚构的 `.prj`。

### 计划导出的本地提交与可选模型库上传

Plan 导出分成本地生成和可选上传两个阶段。源 `Project.ExportPlanAsNewProject` 先完成导出并提交本机 ZIP；成功后返回的回执绑定项目规范路径、所选 Plan 路径、项目会话 ID、源修订、可选 `CoreLayerRequestContext.identityToken`、ZIP 路径、SHA-256、字节数及 receipt ID。目录导出没有 ZIP 内容摘要，不能进入上传阶段。源与已确认的 ZIP 目标摘要保护本地发布；方案创建和导出共用项目操作租约。Swift provider 在确认回执仍对应这次捕获的请求后，将其保留在当前会话，再启动上传；上传失败不会撤销已经提交的本地 ZIP。

上传需要用户显式选择并输入 Model ID，不从文件名或其他项目推断；上传 owner 使用当前应用 UID，缺少 UID 时按源约定使用 `anonymous`。只有 ZIP 导出且现有开发者 S3 凭据可用时才开放该选项；目录导出或未配置凭据时禁用，并显示配置说明。凭据沿用 `WorkspaceS3KeychainStore`，不写入项目文件，Host 错误文本会对凭据脱敏。上传启动前与 Host 都核对回执、ZIP 内容摘要和长度、项目/Plan/会话/源修订及核心图层上下文；Host 重新摘要同一 ZIP 后，通过 `ModelLibraryClient.PushAsync(..., force: false)` 上传，避免替换模型库中的既有对象。选择上传时，导出与上传分别占用进度范围的前后半段，并经 duplex 通道响应当前阶段的取消；本地提交之后取消或上传失败时，ZIP 和会话回执仍保留。

## 错误报告模块清单

错误日志附带的 `Modules` 清单由 .NET Host 在自身进程内生成，范围是已加载的托管程序集和该后端进程枚举到的原生模块；可读取的文件模块记录 SHA-256，动态程序集、无法枚举或无法读取的项目以状态信息表达。清单不包含 Swift 界面进程的可执行映像，也不代表整个 macOS 的进程或模块列表。

普通“Report Bug”保留 normal origin。`WorkspaceStore` 初始化时先用初始 nil document 和当前项目 session 初始化通用 `ToolProvider`，因此首次未打开项目的报告也能通过会话校验。没有持久化活动项目时，错误报告可从 `RecentProjectHistory` 的首项只读解析已保存 `.ras`，捕获规范路径、项目 SHA、XML 和 Geometry 候选；解析使用独立 `ProjectDocument`，不安装到 `WorkspaceStore`，也不写入当前草稿。报告 invocation、项目会话、最近项、计算状态或报告生命周期变化会使候选失效；文件错误只隐藏可选 Geometry 候选，不阻止报告表单打开。Geometry 初始保持 None，只有用户显式选择后才调用现有 Host capture，校验项目与 Geometry 修订；提交仍只由显式 Submit 触发。

## 外部核心图层引用

外部 Geometry / Boundary Condition 核心图层引用是会话内来源，不由项目文件格式持久化。每个请求都要显式携带匹配的 `CoreLayerRequestContext`，其中包含项目会话、规范化项目路径、引用类型、来源路径与 SHA-256、引用 ID 和身份 token；后端不得从任意文件路径自动认领此类来源。项目会话或路径变化时清空前端引用表，模型或来源保存成功后再以真实修订重基。Add Reference 只将明确捕获的来源交给当前地图操作；Copy File 复制来源文件但不隐式改动地图。每个重建 Project 的 provider 都要显式传播上下文。独立模型编辑的显式文件路径请求仍沿用其既有契约，不会将该文件隐式登记为项目引用。

引用管理界面列出当前会话的核心图层，并提供明确的 Reload 与 Remove。Remove 只移除精确匹配的内存引用，不删除来源、不写项目文件，也不要求其余来源仍可读取。Reload 固定读取该引用原来的物理文件，先核对项目修订及完整捕获结构，再以读取前后的 SHA 与原类型校验新的名称和修订；它不通过重新打开整个旧引用集合来恢复单个来源。返回的完整下一上下文必须与前端独立计算的结果相同，且异步响应到达时原上下文仍为当前值，才能替换这一条引用。普通模型、地图与保存请求仍严格核对全部所用引用。

Result / MultiResult 的 `DepthTimesVelocitySquared` 类型使用 `.vis` serializer 标识 `D*V^2`。同一个精确 Result 节点、逻辑选择 slot、profile 和源修订进入 `DepthVelocitySquaredMapper`；它从该 profile 的实际深度与速度 magnitude 数组计算 `Depth × Velocity × Velocity`，任一输入为 `-9999` 时保留 no-data。`ResultMapRenderLayer` 的 RGBA 绘制、点采样/hover 与动态剖面都复用这一 Mapper，默认视觉色带沿用 R 的单位通用默认：USC 范围 0–20、SI 范围 0–7，单位为 USC `ft³/s²` 或 SI `m³/s²`。R 的 `DVSquaredDefaultValues` 0–30 是类型默认值元数据，不是显示色带上限。MultiResult picker 只暴露该精确类型。该类型不合成 Legacy 动态 Max：分别取深度和速度的最大值再相乘并不等于 `max(Depth × Velocity²)`，因此不匹配类型的最大值请求明确返回不可用。

引用恢复只刷新目录与地图来源身份，不自动重新加载模型编辑器。目标编辑器保留原 baseline、字段、未完成输入及子草稿，并显示冲突、阻止继续保存；其他编辑器的草稿不受影响。用户显式放弃并重新加载目标编辑器后，才采用新来源。多个损坏引用可以逐项移除或恢复，不通过清空整个项目草稿处理。

## 运行时绘制选项

绘制选项属于当前渲染对象，和保存的符号样式分开管理。结果对象以地图、节点、逻辑 slot、结果路径及修订定位；几何对象还包含父编辑器的拓扑和网格草稿摘要。改变这些身份后，旧选项请求和输出必须失效。

MultiResult 的“Force Horizontal 2D Rendering”是当前地图会话中的运行时选项。切换前同步捕获项目会话、精确地图、已接受的来源上下文和选中 MultiResult；选集或上下文改变后，旧切换与旧响应失效。存储结果栅格、速度粒子/箭头采样，以及 solver 正在写入的实时栅格和速度场，都把同一布尔值交给原 `ResultMapRenderLayer` / `GeometryMapPoints` 路径，从而使用原强制平面映射语义。每个 MultiResult 节点在各自的地图会话中独立；它不写 `.vis`，当前 map session 内刷新可保留值，新建会话从原默认值开始。

MultiResult 共享关键帧使用当前地图来源快照中所有已勾选的活动 MultiResult。初次加载默认以首个活动 MultiResult 为锚点；刷新时保留仍活动的锚点和可用关键帧，锚点退出活动集合后才回到首个活动项。所选关键帧按原 `KeyedProfileState` 规则投影到每个活动 MultiResult 的子 Result：以剖面名称中的时刻匹配，容差为 10 毫秒，多个候选取来源顺序中的首个，不按相同序号兜底。各节点自己的 Result slot、来源选择和原 profile 选择保持不变；共享浏览只控制当前地图渲染，不写 `.vis`。捕获绑定项目会话、地图、已接受的结果来源上下文、锚点 slot 0 的结果路径与修订，或精确计算运行租约、受保护回调路径及已发布时间元数据，并包含视口、节点代次和 Force Horizontal 状态。整组响应只有在所有节点仍属于同一捕获时才一并发布；滑动时先清除旧组帧。没有时间匹配的子 Result 显示不可用。写入中的 Result 只使用 writer 已发布的 profile metadata 与现有 live target；不读取受保护 HDF，也不以旧组帧补新时间。Link Plans 是独立的显式操作，不替代共享关键帧。

TIN 的流模式使用连接会话、递增帧序号及原始 streamable 数据摘要定位对象。类型化数据通过受控缓存交给独立后端进程重放；它不属于项目地图节点，也不写入项目文件。连接关闭或替换时释放对应对象。

矢量绘图的临时像素和标注按原图层次序替换该层画面，原数据所有者继续提供要素命中、属性表和文件导出。边界条件附着网格面的选项只对包含真实结果网格的边界对象启用；堤防剖面桩号来自所选地形输入的实际纵断面。

几何绘图可以读取已保存源，也可以读取唯一且已加载的父编辑器草稿。存在编辑器但尚未完成加载、正在处理编辑或出现多个匹配编辑器时，不能回退到已保存源。原有样式和运行时线宽缩放随绘图请求传递；相机分辨率与线宽缩放分别管理。

File Geodatabase 的身份是目录内实际数据文件的有序路径、长度和内容摘要；读取锁文件不参与身份。导入预览、向量及栅格子数据集枚举、提交和后续读取复用同一 manifest，并由当前地图的 `.vis` 写入事务发布父数据集及精确子图层。栅格子项的 filename 是 GDAL 连接字符串，不能按普通路径摘要；向量仍保留原层名称、顺序、FID、属性类型、XYZM 及外环与孔洞关系。

GDB 栅格子项的文件名是 GDAL 子数据集连接字符串，不能按普通文件路径读取或计算单文件摘要。读取和样式请求须从精确父子节点重新解析连接字符串，在 GDAL 数据集生命周期内读取子数据集，并在返回前复核地图、`.vis` 和共享目录修订。

地图右键的背景瓦片复制动作捕获点击世界坐标、项目会话、地图、背景设置、投影及相机。后台按同一瓦片服务与缓存策略读取真实矢量瓦片，返回源摘要及协议 JSON。只有响应仍属于该点击上下文时才能写入系统剪贴板。

## 要素列表与地图菜单

地图画布和要素列表行使用同一个 `MapVectorFeatureActionCoordinator` 构造菜单快照、核对所有者和执行命令。列表行按稳定要素身份定位，不以筛选后的显示位置推断源行；右键已选要素保留选集，按 Control 可加入选集。菜单捕获精确项目会话、地图工作流、序列化来源上下文、图层规范路径及修订、所选要素身份。工程 XML、地图 `.vis` 或核心图层引用变化后，即使旧行仍在界面缓存中，也不能执行旧菜单动作。

涵洞属性编辑把混合值占位、无关联值和明确的用户选择分开保存。用户选择首行已有的关联值，仍表示将该值应用到捕获的全部选中行；修改进入现有涵洞撤销草稿，由父 Geometry 保存。选集、来源或子草稿上下文变化时保留未提交输入，要求显式放弃并重新加载，不能把旧输入应用到新的行。

## 多选属性命令

多选属性的混合值与首行值不是同一状态。源控件允许“应用到全部”时，用户选择首行已有的枚举值也必须执行原 ForceSet 命令，不能被普通字段 setter 的相等判断吞掉。堤防属性的 Mode / ModType 在 Terrain 父草稿中更新精确选中的 PropertySets；拓扑 Arc 的 ConstrainStretching / LaplaceBC 在 Geometry 拓扑子草稿中更新精确源 Arc。Region 多选的 PatchType / Multipoint ForceSet 以及数值字段输入/重置绑定当前精确 Region 行集，映射为源顺序的 FID，并进入 Geometry 拓扑子草稿。上述操作都不单独写 HDF，由原父保存事务持久化。Arc 的混合枚举显示为 Default，显式再次选择 Default 仍须对全部所选 Arc 执行命令。Bias 清除沿用原 ForceResetBias：将每条所选 Arc 的 Bias 设为 float.NaN，不把它转换为零或空字符串；该变更进入同一拓扑 undo / redo。

请求绑定源修订、当前草稿与完整选择集；普通表格行 ID 必须先按当前来源映射到稳定要素 ID，不能把显示位置当成 Arc 序号。异步返回须仍匹配原编辑器、输入代次和选择代次；换选集后不得把旧命令结果安装为新选择的值。

## 涵洞草稿与共享地图相机

主地图的涵洞绘制、高亮、命中和后端悬停使用同一个经过验证的父 Geometry 子草稿。存在同源编辑器时，只接受唯一且已加载、可编辑、非忙的父编辑器，以及匹配模型请求和源修订的涵洞子会话；尚未就绪时由地图任务加载该子会话，不隐式保存。重复编辑器或旧项目／旧来源不能回退认领保存数据。Result 来源始终读取捕获的嵌入 Geometry。

草稿范围安装到精确 map capture 所属的 ProjectGeometryMapSession.visiblePoints，所有覆盖层和指针换算复用 MapState.transformPoints。不能只给某个 Canvas 增加范围点。已有相机 referencePoints 保持不变，用户重置／缩放全图后才采用草稿范围；来源或编辑器所有权消失时清除对应覆盖。

## 涵洞结果时间序列

结果涵洞入口以接受的地图来源快照、精确 Result 节点及逻辑 slot 0 定位 HDF，并以嵌入 Geometry 中的原始序号定位所选涵洞。流量过程线读取 `OutputBlock.Times` 和各时次的 `Flow[fid]`；出口流速过程线读取相同时间轴上的 `OutletVelocity[fid]`。数据不经过地图像素或空间剖面重采样，单位来自该结果的嵌入单位制，NoData 保持原语义。复制操作保留涵洞几何及属性。

独立图窗拥有已读取的数据快照与其来源身份。关闭或更换来源会使旧加载代次失效；读取后来源发生变化时，已读图表继续保留并标明过期状态。图表不是可保存到模型的编辑草稿。

## 网格定位与文件导出

网格工具捕获父 Geometry 的精确模型请求、源修订、网格及拓扑子草稿摘要，以及二维流动区的源身份。查找单元、面或节点按原网格索引读取真实坐标和视域；局部地图直接高亮返回的完整单元多边形、面折线或节点，不依赖抽样预览中存在该元素，也不移动其他地图的相机。

单元中心点与外围面 Shapefile 沿用实际网格及保留的文件写入器。外围面属性按原字段保留 `Face`、`CellA`、`CellB`、`Length`，长度取原折线属性；有效项目投影生成对应 `.prj`，Local 项目不伪造坐标参考系。文件族在临时位置完成验证，预备完整回执，并于提交前再次核对来源、核心图层上下文与取消状态；发布不覆盖已有目标，以 `.shp` 最后发布标识完整文件族。提交后保留真实文件回执，后续界面过期或清理失败不能把已保存文件当成未提交重新导出。

## 网格属性曲线与跨结果时间序列

单元和面的属性曲线绑定准确的 Geometry 资产、源修订、网格及拓扑草稿 SHA、流域区域和元素索引。缺少属性表时，可由图表入口触发现有子会话计算，页面持有任务、进度和取消状态。完成后核对原项目、Core 引用、源模型、拓扑和选择，捕获计算后的新草稿 SHA，再精确定位同一元素并读取曲线；父级保存负责最终 HDF 持久化。

跨结果曲线从用户选定地图的已接受来源快照捕获地图请求、选择覆盖、项目及 Core 上下文，异步期间不重新读取全局活动地图。来源可以是当前 Geometry 草稿或选定 Result 的嵌入网格，后端按流域区域序号、有效属性表网格哈希和单元／面数量匹配结果。单元读取原 Depth 序列，面读取原 Velocity 序列；保留各结果自身时间轴和单位，跳过正在计算的受保护结果。来源、选择或地图上下文变化使旧响应失效。

时间序列与结果采样线继续调用现有 PostProcessor 转置和读取算法，但各请求拥有独立临时派生缓存，避免独立后端进程同时创建或覆盖默认 PostProcess.h5。缓存先关闭全部 HDF 资源再清理；它不是模型提交，也不改写原 Result。

计算写入期间，独立 Result 的保存树行保留在地图中，但不作为可读图表来源。后端仅用已验证且在保护集合内的计算回调路径，按原 `.vis` 编码规则确认唯一行，赋予不序列化的内部保护标记；同名来源歧义仍拒绝。该标记不填充可读 AssetID，不读取受保护 HDF，也不让普通缺失身份的 Result 绕过检查。

## 地图悬浮信息

普通指针悬浮绑定地图工作流、可见来源节点、源修订、结果选择与剖面、Geometry 草稿和指针代次。向量要素在当前渲染图层内命中实际 FID，不能按显示名称或相同文件路径拼接不同实例。结果、地形、连续／离散栅格与网格信息调用原有来源采样器；栅格值不得从渲染颜色反推。缺失值保持缺失，真实零值保持有效；底层分块点采样将未指定的采样参数规范为默认值，显式保留缓冲区的采样参数仍用于多来源组合。

GDB 栅格悬浮请求携带完整目录 manifest、精确地图与父子节点及子数据集连接身份，后端在实际数据集和重采样器生命周期内将项目坐标转换到来源投影，读取原始数值，返回前复核同一来源。连接字符串不按普通文件路径读取，NoData 和范围外点不产生虚构数值。

模型折线提示单独携带 renderer 类型及来源所有权。涵洞沿用源 renderer 的真实要素索引和名称；边界线保留来源序号与原有标签回调。已保存 Geometry、唯一父编辑器草稿及 Result 内嵌来源分别捕获；父涵洞草稿携带摘要，边界草稿携带序列化来源状态，Result 必须绑定接受的 slot 0。不能把显示名称、分页位置或画布像素当成来源身份。

配置与悬浮捕获共用包含 Result 上下文与 slot 选择的地图来源工作流身份；基础地图身份不能替代它，否则已渲染的 Geometry、Boundary 和关联图层会在取值前被错误过滤。

SwiftUI 会话先显示本地向量命中，再合并后端数值；旧指针、旧来源或旧地图响应不得替换当前提示。提示采用系统玻璃材质且不接收鼠标事件，工具交互、离开地图或关闭来源时清理。

## 时间序列表格粘贴

时间序列表格的菜单与粘贴命令共用 NativeDataGridClipboard 的 TSV 解码。操作捕获项目会话、编辑器与资产、源修订、边界 payload、表格与数据集、稳定行列 ID、排序／筛选后的可见顺序、列权限、显示单位与选区。先验证完整粘贴计划，再在当前 MainActor 同步写入原 ModelEditorSession 草稿，不单独提交 HDF。

右键粘贴明确绑定点击单元格：点击属于真实选区时保留该选区，位于选区之外或边界框空洞中时以点击单元格为起点；工具栏和快捷键使用当前选区。不能把选区边界框当成实际选中成员。

单值填充所选矩形，多格按选区起点偏移；不齐行中未提供的尾部格保持原值。越界计划整体拒绝，只读或不合法选择值明确计数。数值采用现有单位输入草稿：有效输入按当前显示单位进入模型草稿，非法数值文本保留并阻止保存。时间字符串与协议标识保持原样；过期选区或来源拒绝执行旧计划。

## DSS 网格序列导入

边界条件的空间网格导入页检查原 DSS 文件，在私有临时副本上调用已有 Hec.Dss catalog 和网格导入器。仅接受 Grid 记录，按 A/B/C/F 分组；使用 UnCondensedPaths 保留各实际时刻，稳定序列身份绑定原规范路径、文件 SHA 和完整记录集合。临时副本路径不得持久化。标量输入选择一组完整序列；风场选择同一文件中的两组不同 X/Y 序列，后端再次核对时间轴、网格、投影、单位和数据类型。

导入复用现有边界模型草稿与暂存 HDF 保存事务。源 DSS 在读取前后和提交前复核 SHA。取消通过同一后端双工进程传递，与最终文件替换共用提交锁；目标 HDF 在所有依赖读取完成后、提交锁内紧邻替换前再次复核修订，依赖校验期间发生的目标变更必须拒绝提交。提交完成后仍接收真实回执。GDAL 与 Cumulus 沿用各自已有导入契约。

## 关闭或切换编辑器后的保存回执

模型保存捕获资产、项目 Core 引用和提交回调，在界面代次检查前交付已提交的规范快照。项目回调只在原项目会话中处理，并要求注册表仍保留捕获的精确旧引用；窗口关闭不丢弃已提交的引用更新，新引用或新项目不能被旧回执覆盖。快照安装、草稿确认和界面刷新仍要求原编辑器资产与代次一致。

## HDF 读取资源

HDF 文件句柄由取得它的 reader／writer 持有。失败打开和关闭后的句柄均为无效状态；重复或并发释放不能再次关闭同一句柄，终结器不能使异常逃逸。导入器必须先确认文件成功打开，再读取工程元数据或模型组。来源校验失败保持原文件与目标目录不变，不能通过屏蔽底层诊断伪装成功。

## 地图渲染选项与加载顺序

结果所属的边界条件行通过原生开关控制附着网格面。界面只接受当前项目会话、地图来源工作流、可见树行、精确 locator 与单一结果 capture；加载状态及错误归属该 capture。附着面来自所选结果的真实网格，选项保留在地图渲染会话中，不修改项目 XML、`.vis` 或 HDF。

背景地图窗口与结果、风场粒子控件共享当前 MapState 的粒子会话。设置入口先启用粒子；没有速度来源时仍允许配置，取得真实目标后才采样。粒子参数沿用原控件范围：流速比例系数 1–1,000，最大寿命 0–5 秒，粒子数 100–10,000。输入草稿保留非法原文并显示字段错误；已有会话值超出界面范围时原样显示，不静默截断，只有用户输入合法值后才更新会话。运行时引擎仍保留有限性、非负寿命、正比例系数及粒子缓冲安全边界，不能把它当作界面输入范围。每次设置修改先核对精确 MapState 所有者；迟到的取样响应继续受来源身份和请求代次约束。参数属于地图会话运行时状态，不写入 `.vis`。设置界面可滚动，切换来源不另建一套粒子算法。

几何地图每轮为每个来源加载一页，再继续未完成的来源。大网格不能阻塞后续来源的首份元数据及可见要素；暂停在当前页结束后停止全部来源，恢复沿每个来源自己的游标继续。错误保留该来源已加载页面和失败游标，取消或视口变化不得安装旧页面。

## 降雨图层与悬停

降雨是边界条件下可独立勾选的地图子节点。普通边界使用项目边界 HDF，结果所属边界使用所选结果 slot 0 的嵌入 HDF；两者不能混用来源路径。检查、绘图和悬停携带完整 MapTree 身份、树结果选择、图表结果选择、项目会话、Core 引用、可视化修订、来源 SHA 与计算读取租约。地图上下文 token 和降雨 capture token 分别校验，不互相替代。

时间轴取自原降雨 resampler 的 Times 与 ProfileCount，独立于水力结果时刻。常量来源保留原始哨兵时间索引，界面显示常量而不显示哨兵日期。绘制使用原 BandedRenderLayer、最近邻插值及单位对应的降雨色带；零值可透明，但悬停仍返回零。NoData、非有限值和越界采样不产生数值提示。检查不依赖相机；检查期间移动地图只更新待绘制视口，响应到达后绘制最新视口。帧请求仍按各来源代次取消、校验和安装。

来源是否仍有效的回调只读取源树行的身份、选择与可见状态，不解析绘图载荷。绘图行可以查询会话的当前载荷；载荷有效性检查不能反过来解析同一绘图行，避免状态校验递归。

## 来源属性校验与编辑所有者

源属性校验由 ModelEditorSession 持有，而非字段视图生命周期。每个请求绑定当前模型、来源修订、父草稿、表格与所选源行、字段及输入值；未完成、缺少精确身份或返回错误的当前请求阻止保存及相关计算，警告不阻止。值变化立即使旧结果失效，不能在异步校验开始前短暂放行。离开待校验或错误选区必须先修正输入，防止选区 RPC 将未校验字段固化到父草稿后丢失错误所有者。Host 的分阶段写入和属性校验仍保留为最终持久化边界。

前端校验范围是用户已编辑的来源字段。持久化的继承值可用 NaN／空文本表示；仅选中记录或挂载属性视图不能把这些未编辑值转成新的输入错误。字段一旦编辑，校验所有权仍属于父草稿；卸载视图、改回空文本或尝试切换记录不能清除当前错误。

## GIS 临时项目与速度箭头

GIS Mode 复用项目替换确认、会话安装和清理，创建 URL 为空的真实项目 XML，并设 Web Mercator 与原始 CONUS 范围。底图立即启用默认样式，Liberty 查询仅为可取消的尽力选择，不能阻塞创建或关闭底图。迟到响应必须核对文档、项目会话和地图所有者；显式保存才选择磁盘路径。

速度箭头与速度粒子各有独立的启用状态、采样代次及地图级会话。箭头从当前 Result 或 MultiResult 的 slot 0 读取原 VelocityMapper 和地形裁剪，允许该结果当前显示 Depth 等其他类型；粒子接口继续保留 Velocity 类型约束。两者不能借用对方的启用状态。箭头画布与其他图层共享相机，并保留来源、时刻、视口、租约与过时响应检查；Wind 不是原箭头注解的来源。

## 区域属性输入与校验

活动区域属性编辑器的原始文本与校验任务由 GeometryTopologySession 持有，切换行或卸载字段视图不会丢掉未应用输入。每个请求绑定资产、来源修订、拓扑草稿、精确区域、字段、文本与输入代次；迟到响应不能替换新值的校验。待校验、未应用文本和错误阻止父保存及网格准备，显式放弃只清除未应用输入。 每次校验任务另持独立 token；清理任务句柄时只接受该 token，不以可重复的请求 ID 代替任务身份。放弃输入后即使重建相同来源值的请求，旧的已取消任务晚返回也不能移除新任务或解除其待校验状态。

推荐范围与绝对范围分别处理。推荐范围外的值显示原始警告，在绝对范围内仍可应用；错误和类型错误继续由 Host 拒绝。应用后保留拓扑撤销与父事务边界，不能因视图重建而暂时放行保存。

## 河槽 Version 6 几何导入

当前 Web 入口为 `EditorWorkbench → TerrainLayersEditor → ChannelModTerrainInputProperties → ChannelModParentEditor → ChannelV6ImportControl`。它仅向已加载且没有真实 Channel feature 行的选中河槽提供 Version 6 `.hdf` 导入；导入后显示原全局设置，撤销回到空输入后可重新检查来源。原 `ChannelModParentEditor.Connect` 只连接 `Import6_Click`；全托管源码中的 `Import2025_Click` 只有定义及其私有调用，没有找到事件注册。因此本入口只承接已证明可达的 V6 行为，不能把 Classic 处理函数的存在当作完整原 UI 路由的证据。

文件路径指 C# 服务所在计算机可读取的绝对路径。`workspace.file.inspect` 返回规范路径、非空长度和 SHA-256；检查与导入捕获原路径文本、工程会话/XML/修订、后端实例及能力代次、父模型草稿、精确输入和输入列表。父编辑器先读取选中空河槽的拓扑，再计算导入专用身份：输入 ID、地形修订和完整 `sourceState` 的 SHA。这个身份与拓扑撤销日志的 `draftIdentity` 分别校验，不能互换。检查后父草稿或来源变化必须重新检查，不能沿旧回执开始导入。

`model.channelV6GeometryImport` 复制并校验用户 HDF 的私有输入副本，复用 `ModelConverter.MigrateGeometry` 的 Classic 与 `Generate1DLines.IfNeeded` 分支。原转换器可能补写辅助线，因此用户文件保持在转换副本之外；转换前后及返回前复核原文件 SHA。无横断面的转换结果拒绝进入草稿。暂存迁移文件位于父地形 import token 目录；序列化状态只保存 payload ID、文件名和来源/迁移 SHA，不保存临时绝对路径。

完成回执中的 Snapshot、剖面、Topology 和 import token 只由捕获的父 `EditorWorkbench` 一并接纳；已有未保存字段和表格仍保留在同一 `ModelDraft`。导入状态由父级 controller 持有，界面隐藏或子控件卸载不会重发任务。`ChannelModTopologyEditor` 沿用父 `model.terrainTopology` 的撤销和重做，跨请求重建拓扑与剖面；撤销后 payload 仍归父草稿所有，以便重做。只有父 `model.save` 写入地形 HDF，并以真实保存回执推进来源修订。

Terrain 父保存先写入规范来源文件同目录的暂存副本，关闭写入句柄后，由新的 `ModelContext` 重开该 HDF 并准备完整保存回执。选中输入按已保存顺序重新定位，以原资产路径和暂存文件 SHA 生成保存后的输入身份与表格；干净 `sourceState` 不再依赖导入 payload、待追加输入或旧撤销日志。名称遵循原 Terrain loader 的文件名规则，`TerrainCatalogSummary/Context_Filename` 保留所属工程或会话 loader 的原来源路径，随机暂存路径不能成为回执中的资产身份。已有 session-only Terrain 引用的修订、名称、reference ID 和 Core identity token 也在发布前准备，其他引用保持原捕获。

发布前再次核对工程修订、已有 Core 会话捕获、原文件 SHA、输入依赖、暂存 SHA，以及请求路径仍指向同一规范来源。准备或核验失败不会发布本次 HDF，也不会消费父导入 token。保存替换规范目标文件，保留用户路径上的符号链接；别名改指其它文件会拒绝发布。发布后直接返回已从关闭的暂存 HDF 核验过的回执，不再通过发布后的 `LoadAsync` 或 `RebaseContext` 决定保存是否成功。清理异常随已保存回执返回中文警告和原 import token，保留可明确清理的来源；不能把已落盘的保存报告为可重新提交的普通失败。

带已接纳渠道 payload 的父保存由 `channelV6ParentSave → RasMainWindow/ModelSaveLease` 保留完整请求、原网关、原 HTTP 操作 ID 和精确父草稿。它使用独立于普通草稿及导入任务的写入 owner；普通 owner 注销、页面清理和来源退役不能提前释放未知保存，也不能由前端提前退役或清理已接纳的 payload。重连服务不会替换未改变的已加载草稿，续查始终使用原捕获的网关和任务。已通过基本快照校验的返回值先缓存，再尝试根级回执接纳；根回调拒绝或抛错时保留该快照和租约，不重复 POST。根级接纳核对同一来源、资产、租约与操作 ID，只消费一次；父级随后安装已保存状态并退役旧 payload。缺失编号、任务冲突、未知传输和未接纳回执均持续保护草稿，不能当作取消。若 Host 已成功保存后才丢失 HTTP 回执，服务端可能已完成暂存清理；这与前端主动丢弃原任务不同，必须继续核对原保存结果。

取消请求与确认取消分别处理。HTTP 状态失联只续查原操作 ID，未确认导入或清理持续保护父草稿。未接纳的完成回执通过 `model.channelV6GeometryImportDiscard` 清理，必须携带这次响应的 draft token、payload ID 与迁移 SHA；Host 只删除匹配文件，仅在目录为空时移除目录。同 token 下其他已接纳 payload、导入批次及撤销历史不能被递归删除。已接纳 payload 的所有权随父保存、显式放弃或来源退役结束；清理失败/失联保留原来源与回执，清理重试不会再次执行导入。以上描述源码中的所有权与保存边界，实际 HDF 往返、浏览器操作及 Electron 运行需要分别验证。

Geometry 的已确认保存回执通过 `onContextCommit(source, committed)` 接纳，避免自身未保存状态把新修订当作离开来源而阻止更新。新的来源 owner 重新读取干净快照，旧撤销和重做历史随之退役。

## 地形多边形地图编辑

Terrain Inputs 中已选 Polygon 或 Levee 的属性入口导航到现有 Terrain Modifications 页，复用同一父编辑器与输入选择。工作台在进入页面或已选输入类型变化时显示对应的 Raster Modifications、Levees 或 Channels 分区；不另建模型会话，也不把当前输入替换为列表首项。

地形输入元数据携带原始已保存的矢量输入索引。源索引与精确输入 ID、地图节点、资产路径、SHA 一起核对，不能从当前表格排序或名称重建。新增但未保存的输入没有源索引，界面提示先保存；原程序的新增路径同样先保存输入再重建地图上下文。

地图编辑复用原始可观察多边形集合和编辑操作，支持三角形、矩形放置以及自定义多边形绘制。选择、删除、撤销与重做的结构命令日志存于父地形草稿的 PolygonTopologyDrafts；普通输入 Name 字段仍由 ModelEditor 的父草稿字段单独持有，不能并入或被结构日志覆盖。多边形设置也以独立草稿值保留。所有修改仍由父地形保存提交。坐标从地图反投影到源 CRS 后编辑，显示时再投影回地图；宽高使用地形单位。真实 OSR 转换预检失败或返回非有限值时拒绝操作，不能把失败的转换误当作恒等映射。

相机由活动的 TerrainInputMapCanvas 根据当前画布尺寸、共用地图坐标点、缩放、平移和设备渲染比例构造 MapSourceRenderViewport，再发布给 TerrainInputMapSession；不能从侧栏、图层范围或上次视口推算。地图视图/标签页变为非活动时 Canvas 发布 nil 并清除相机，正在进行的多边形编辑随之结束。取消勾选精确多边形来源会取消其读请求、清除该来源的临时要素和选择并结束编辑；地图仍活动时保留共享相机。

地图命中选择仅更新选集，不把父模型标为已修改，也不推进模型编辑修订。地图浮动工具条和地图点击共用 TerrainPolygonMapEditingActions 的精确会话检查，提供选择、放置、绘制、撤销重做、删除、完成和停止。参数保留原始输入，未应用或非法值阻止放置、绘制和父保存；校验归属当前地图会话，应用有效参数、停止或隐藏地图会解除该会话的限制。保存仍调用同一已注册父 Terrain 编辑器的原保存事务。空闲时平移或缩放更新真实相机，读写请求进行中切换相机则淘汰旧请求。

Polygon Features 表使用 `TerrainPolygonTopologyEditSession.Features()` 返回的同一 feature ID 作为行 ID，修改类型和值来自同一拓扑 feature 元数据。表格选择只有在当前已注册父编辑器、原始输入索引、来源修订、地图节点和相机代次仍匹配时才同步到地图；其选择回执绑定本次接受的选择代次、提交前后父编辑器身份及精确行 ID，不能只凭代次递增重基。地图点击、放置、删除及历史重放会将后端返回的精确选择写回父表；异步拓扑事务中并发选择变化仍会拒绝旧响应。增删和撤销重做后重新从同一拓扑会话生成行；提交旧草稿身份的索引选择会被拒绝，避免借位选中新 feature。`Select on Map` 通过当前父编辑器启动现有地图会话并定位所选几何，修改仍只由父 Terrain 保存提交。

Polygon Features rows reuse the exact feature IDs and metadata returned by `TerrainPolygonTopologyEditSession.Features()`. Table selections reach the map only while the registered parent editor, original input index, source revision, map node, and camera generation remain current. The accepted selection receipt binds the current selection generation, the parent editor identity before and after the response, and the exact row IDs; a generation increment alone cannot rebase the capture. Concurrent selection changes during a topology request still reject its stale response. Map hits, placement, deletion, and history replay return the selected IDs to the parent table. Rows are regenerated from the same topology session after insertions, deletions, undo, and redo; commands carrying an older draft identity are rejected so index-backed IDs cannot select a feature that inherited an index. `Select on Map` starts the existing map session through the registered parent editor and focuses the selected geometry; edits still commit only through the parent Terrain save.

## 隐藏 FlowArea 检查器

检查器从完整地图来源树捕获精确 Geometry、FlowArea 和可选的 Result/MultiResult 祖先，拥有独立的网格错误会话，不要求图层勾选或已有渲染 capture。结果来源还绑定 slot 0、结果路径与 SHA、树选择、项目 Core 上下文和计算读取租约；专用解析不能放宽普通地图的可见节点规则。

检查只读真实网格。设置只通过已有 `.vis` 事务写入并接收回执，不修改图层可见性或来源 HDF。默认节点的空路径只能沿已验证的真实祖先关系物化，不能依靠显示名称匹配另一图层；计算写入保护中的结果不得打开读取。

## 分类图层编辑透明度

`Edit Mode Opacity` 属于所选地图中的精确 Classification 可视化节点，Host 将其作为源属性 `RenderInEditMode` 持久化在该地图的 `.vis` 中；缺省值为 50，保存缺省值时移除该可选属性。原程序只在分类几何编辑期间把完整 Classification 底图的颜色透明度改为该值，退出后恢复；选中输入的前景栅格保留原始颜色，普通地图渲染也不读取此设置。

SwiftUI 滑块草稿的视图身份绑定完整关联 capture、Classification HDF SHA 与已保存的整数值。切换 map/tree owner、源修订或已保存值时必须重建控件，不能把前一节点的未应用数值带到新节点。

SwiftUI 控件位于当前地图的选中 Classification 来源详情。提交捕获项目会话、地图 ID、完整 `.vis` 身份与修订、精确节点 locator、关联 HDF 路径及 SHA；Host 只允许 0–100，并在提交前再次核对 HDF 修订。真实的 Project map 使用固定 `MapState.projectMapID`，仍须通过保存地图身份与节点所有权校验，不能因其是 Project map 而排除。分类几何编辑地图只对名为 `Classification` 的完整底图 raster 应用该透明度；selected-input raster、源 HDF 像元及其他地图图层均不改。编辑器读取设置时还要确认活动地图选择与当前编辑 HDF 的路径和修订完全相同，否则使用源默认 50，不借用同名节点的值。Host 成功回执代表 `.vis` 已提交；之后若地图 owner 变化或刷新失败，应保留“已保存但视图未刷新”的状态，不能提示重试并造成重复写入。

## 不变量

- 名称相同不代表对象相同。异步请求捕获项目会话、资产路径、源 SHA、map locator、当前草稿与请求代次；响应只能写回仍匹配的所有者。
- 子编辑器不越过父事务单独保存。输入错误或未完成文本保留在展示草稿中，阻止保存和计算，不改写已确认模型值。
- 项目 XML、HDF、地图 `.vis`、全局偏好和临时渲染状态各有保存边界，不互相冒充。
- 计算期间保护实际写入路径。历史结果只有与写入租约不相交时才能读取；没有实际 profile 的 slot 不构造伪造帧。
- 矢量身份保留源 feature / component、XYZM、孔洞、属性类型和 CRS；屏幕坐标或显示名称不能代替源身份。
- 取消和过时响应不得覆盖新草稿。成功提交后的刷新失败必须保留真实提交回执，不能重试成第二次写入。
- 中文与英文只用于展示；源键、枚举存储值、路径、标识符和工程数据维持原语义。

配套 JSON 列出领域关系、状态作用域、服务流程、字段身份、地图工具和数据表契约。新增路由应明确真实数据所有者、提交边界及异步响应的有效条件。

## Plan、Result 与 Terrain HDF 会话引用（保留来源契约；React Add-to-map 已挂载）

Host 为 Plan、Result、Terrain HDF 提供独立的 `SessionHdfReferenceCapture` 与 `CoreLayerRequestContext.sessionHdfReferences`，重建 `Project` 时核对路径、文件 SHA、实际类型和名称。当前 Web 的 `SourceManagerControl → SessionHdfImportControl` 已接入 Add-to-map：`workspace.file.inspect` 捕获来源后调用 `workspace.import.sessionHdf.inspect`，读取当前工程保存地图，以 `workspace.references.compose` 核验下一个上下文，再对所选地图执行 `map.tree.perform(addSessionHdf)`；只有地图回执验证通过且父 `RasMainWindow` 接受当前租约，才把新上下文放入内存 registry。来源文件保留原路径，不复制，也不写入 `.ras` 成员关系。面板显示已有 session HDF 引用，但当前没有 Remove 按钮。`CoreLayerImportDialog` 是另一条针对 Geometry / Boundary Condition 的 `workspace.import.coreLayer.*` 工作流。以下分组、保存重基与引用移除的来源细节仍需与当前 Add-only Web UI 区分。

当前 Web 的 Add 具体要求已保存工程、服务端可见的绝对 HDF 路径、无未保存草稿、已登记的完整 Host 能力和兼容的保存地图。它同时捕获 HDF SHA、工程 XML/修订、地图目录/`.vis` 修订与引用身份；地图提交前会刷新地图目录，防止将过期选择写入树。`workspace.references.compose` 本身只生成并验证 capability，不登记全局状态、不写文件；`map.tree.perform` 回执到达后，父状态回调才采纳新 context。这里的 Web 流程只添加，不提供删除；Host compose DTO 虽支持移除 reference ID，不能据此描述为已有 Remove UI。

外部 Result 的 `ResultPlanName` 仅是该 Result HDF 内嵌 Plan 的分组标签，不会将这个 Plan 加入 `Project.Plans`，也不会因同名而归属到工程已有 Plan。Catalog 将已捕获的外部 Result 从工程 Result 分组中分开，并按内嵌 Plan 名称构造独立的会话 `MultiResult` 分组。分组的 `SessionHdfResultOwner` 绑定按规范化路径排序后确定的外部 Result 捕获、项目会话、工程路径、Result 路径与 SHA、内嵌 Plan 名和完整 Core 上下文 token；它只标识分组，不能代替被选 Result。slot 的 Latest 按原 `MultiResultVisualContext` 规则在该外部组内选择（有当前计算结果时遵循原默认项，否则取 `ComputeSummary.EndTime` 最新项）；明确选择则保留仍有效的精确 Result 路径与 SHA。地图和图表解析时分别核对分组 owner 与选中 slot，因而两个内嵌 Plan 同名的外部 Result 可以共享一个真实分组，而同名工程 Plan 或工程 Result 不会被混入该 owner。

保存重基来源语义要求只在 Host 已提交后采用真实 HDF SHA/名称重算匹配捕获的 reference ID 和 identity token，且继续保留原 `sourcePath`。当前 React 已通过 `model.save → ModelSnapshot.coreLayerContext` 消费 Host 的重基回执：Host 的 `ModelEditorAdapter` 将 Geometry / Boundary Condition 引用，或同路径 Plan / Terrain session HDF capture，重新校验并重基；Result capture 不允许按模型保存路径重基。前端只在工程/资产 owner 和原 context 仍匹配时采纳新快照与 token；草稿编辑本身不推进引用 SHA。

原生 SwiftUI 引用管理器的 Remove 只删除与当前完整上下文和项目修订精确匹配的那一项内存捕获，不删除文件、不保存 `.ras` 或 `.vis`。它会更新会话资产并失效、刷新相应 Catalog / 地图树；仍打开同一 HDF 的编辑器保留原 baseline、字段、撤销状态和草稿，但标记引用冲突并禁止经旧 owner 保存，用户需明确丢弃并重新加载。其他编辑器草稿不受影响。原生工程关闭或替换在既有未保存草稿／忙碌状态确认通过后，会停止 HDF 文件的 security-scoped URL 访问、关闭引用管理器、清除导入 presentation 与管理状态，并重置两类 Core 引用；registry 发现项目会话 ID 或规范化工程路径变化时也会清空两组引用。原有草稿保存／丢弃确认门槛不变。这些 UI 生命周期细节属于保留的原生来源契约，当前 React Web 没有相同的 Session HDF 管理器。

## 地图提交回执

地图来源操作与节点设置先在旧 `.vis` 修订仍有效时，从最终内存 XML 生成完整响应树，并在提交前再次核对来源。临时文件刷盘后、原子替换前计算最终 SHA；写入成功后直接返回已准备的回执，不再重开地图、读树或重新求目标文件哈希。前端把后续刷新、修订接受和会话引用登记失败与真实提交分开：已经提交的操作保留回执并关闭可重试的导入表单，明确提示需要刷新。项目切换后不把旧会话引用登记到新工程。

保留的 SwiftUI 来源仍定义了绑定操作 UUID、工程会话、导入表单与目标地图的写入授权；当前 React Add-to-map 使用 `RasMainWindow` 的项目引用变更租约，按原 context 身份及无 dirty owner 条件发放，阻止导航/工程上下文改动，并在地图回执被采纳后释放。该 UI 租约不代替 Host 的工程、引用、地图与 `.vis` 修订核验，也没有向 Host 冒充 Swift provider 授权。

## 新建边界条件默认值

默认名称由原有唯一文件名逻辑产生，默认时间来自当前工程已有边界条件的时间范围；空工程沿用原业务默认时间。后端捕获创建前已有的边界条件路径，检查时仍重新求文件 SHA，避免把本次暂存输出当作输入。界面按表单、编辑器、工程会话及来源上下文保存字段触摸状态，只向未修改字段填入当前检查结果；迟到的响应不能覆盖用户输入，相同值不会引发再次编辑或检查循环。

## 普通面图层编辑

普通面文件编辑由 `MapState.polygonEditor` 按地图持有。`WorkspaceMapInteractionProvider` 将原工具 ID `Polygon.Add`、`Polygon.Edit`、`Polygon.Sketch` 和命令 `Polygon.Save`、`Polygon.Discard` 路由到同一 session；session 捕获项目会话、地图、图层、规范源路径及源修订。地图输入与异步来源检查都复核原捕获 owner，过期响应不得改写新地图的草稿。

GeoJSON 通过有序 JSON 文档树编辑原 `Feature` / `FeatureCollection` 中明确定位的 Polygon 或 MultiPolygon 坐标，未编辑的属性、ID、未知字段、空值、数值词法、孔洞、额外坐标维度及其它几何成员保留；写入前比较源 SHA，并使用协调的原子文件替换。删除单个根 `Feature` 会转换成空 `FeatureCollection`，保留无关根字段并移除仅属于 Feature 的成员；GeometryCollection 中的子几何删除会明确拒绝，避免丢弃其它几何。Shapefile 的每条 polygon 记录及其所有环作为一个面要素，属性、FID 顺序、XYZM、投影和完整来源文件族由第一方 C# 读取与写回服务处理。多环记录按 reader 的记录级布局先写全部环的 XY，再写一份覆盖整条记录的 Z range/数组和可选 M range/数组；不能在各环之间插入 Z/M 段。每个源 ring 使用 reader 给出的精确 Z/M 维度标志；PolygonZ 的可选 M 数组按记录写入，不能从其它记录的 M presence 推断。传输层用显式维度标志和 JSON `null` 保留 M 的 NoData 槽位，Host 只在内存几何数组中将其转为 NoData。DBF schema 由现有 Shapefile 来源加载器作为独立的图层级有序结构随来源快照读取，不依赖首条 feature；编辑会话从该结构捕获字段顺序、原始类型、宽度和精度。共享 `MapShapefileAttributeTableDraftWriter` 由普通 Polygon 与既有 Polyline Shapefile 保存共用，不另建简化 serializer；它原样保留有序字段描述符（原始类型、宽度、小数位），并按来源记录 ordinal 复制继承行的完整原始记录字节；复制前核对 typed attributes 未被几何草稿改动。新增行遵从来源 descriptor 和现有 Table/DBF 默认值；整数和定点 decimal 使用精确整数/decimal格式化，数值无法装入原字段宽度时明确失败，未识别类型的继承字节仍被保留。Host 用第一方 DBF writer/reader 回传每条已保存记录的实际值、语义类型、isSet/null 状态。空层新增时立即把这份 schema/default 放入草稿 feature，保存回执读回的新值同步到活动地图层和属性表。服务先写入同目录暂存文件族、重读并逐项核验，再以 family capture 作乐观并发条件原子替换并返回新 family。删除最后一条记录时，Host 输出保留原 5/15/25 类型的空 SHP/SHX、零行 typed DBF 和原 PRJ 字节，使地图节点仍可继续添加面；原 R 对空集合的处理是删除来源文件族。来源文件不能直接由 Swift 的简化 writer 重写。

会话持有多边形草稿、选择、草绘预览及撤销/重做历史。保存先提交文件，再通过 `didSaveGeoJSON` / `didSaveShapefileFamily` 将精确新修订回执交给 Store；只有 Store 接受回执，地图图层缓存才更新。Host 成功响应即表示文件已提交；此后如几何回执验证或 Store 刷新失败，会话会退休旧草稿并要求重新加载，避免把已提交编辑作为可重试操作。Save All 和关闭地图标签时，Store 依据草稿创建时捕获的原始源 owner 保存；即使用户已选择另一图层，也不临时改动 `selectedLayerID`。空 SHP 保留原类型、DBF schema 和 CRS，重开后仍能从真实 Add 工具继续添加。File Geodatabase polygon 在原程序没有可写编辑会话或保存调用者，保持只读选择。

## 地图输入、草稿与文件归属

调色板操作、鼠标、拖拽、悬停和 S/M 临时草绘按键都捕获工程、地图、来源工作流、上下文、图层路径与修订、可视身份和选择。拖拽始终沿用开始时的 owner；切换来源后退出旧预览与临时模式，不将排队输入转交当前活动地图。Filter／Smooth／Offset 预览不写撤销历史，顶点拖拽在结束时形成一次撤销。保存全部或关闭标签时按草稿最初的源 owner 保存，即使用户已选到另一图层。

Shapefile 层级字段结构与每条记录／路径的 Z/M 存在性独立保存。全为 NoData 的 M 数组与无 M 数组不同，协议使用明确标志和 JSON null；文件按每条记录计算可选 M 段与 SHX 偏移。同一面记录的环使用一致维度，先写全部闭合环 XY，再写记录级 Z 段和可选 M 段。继承 DBF 字段与记录保留原字节，新值不能装入原字段时拒绝提交；GeoJSON 合并的额外坐标按源顶点归属保留，不凭相同 XY 猜测。

分类透明度写入精确 `.vis` 节点，捕获使用包含来源上下文的工作流身份。提交后失效的聚合相机参考缓存不等于画布范围无效：只有当前来源提供的真实有效 bounds、画布当前有限范围和像素尺寸同时存在，才继续发布实际画布 viewport；不得拿来源边界合成一帧充当用户画布。

地图来源树先于矢量 IO 完成时，树行仍保存由精确地图与节点 ID 得到的 source-vector 实例 ID；绘制组合只连接当前已加载、经校验且属于该地图的候选层，未加载时不生成几何。这样异步加载完成后无需再次读树即可显示并捕获来源。工具激活在来源捕获复核通过后同步发布同一地图的活动工具，不能依赖菜单是否挂载或视图 onChange 的调度。

地图的物理来源缓存与节点显示实例分别持有身份：前者按精确地图、规范文件路径和修订唯一定位，后者由 sourceVectorBindings 生成可视节点 ID。保存回执先验证原节点与上下文归属，再以旧修订比较替换该地图唯一的物理缓存；不能拿显示实例 ID 查找原文件缓存。面编辑器提交后的图层使用新修订，旧 family 仅用于核对提交前基准。替换和类型筛选必须保留源投影、Shapefile 类型、字段结构及逐要素坐标／属性元数据。

## USGS 地形瓦片导入

原 `USGSTerrainImportControl` 对查询的每个 `ObservableUSGSItem` 单独持有 `ConstructedResampler`、原始下载 TIFF 路径、投影转换后的预览文件路径和 `IsImporting` 状态。该布尔值默认为 false；只有下载成功并构造 resampler 后才置 true。`TryDownloadAndParseUSGSTile` 已有 `ConstructedResampler` 时会复用同一个 resampler、重新加入临时栅格预览并恢复 `IsImporting`，不会重新下载。`RequestTileHide` 仅在实际有 resampler 时将这条 item 的 `IsImporting` 设为 false，并把它持有的同一个 resampler 交给 `Oui_TileHide`，从临时地形预览栅格集合移除；最终导入按钮只提交当前 `IsImporting` 的产品。查询边界轮廓是另一层，不等同于被隐藏的 resampler。

Swift 查询快照保留 provider 返回的 opaque Product ID、原始 HTTP(S) `DownloadURL`、metadata URL、查询范围、发布日期、分辨率和投影边界。筛选对当前快照应用原标题/分辨率别名前缀搜索、原分辨率分类、发布日期 `>=` / `<=` 比较，以及 All / Selected / Tiles To Import；选择地图边界命中的是筛选后同一 Product ID。 Metadata 操作只打开查询返回的 metadata URL。每次请求捕获工程会话、地图与 workflow、Terrain 编辑器与完整草稿身份、规范源路径与 SHA、输入 ID 和 extent；迟到的查询、下载和栅格预览在变更 owner 后不能发布或进入导入表单。

每个下载回执保留该批次中逐产品的原始 provider DownloadURL / metadata URL、原始源 TIFF 路径和 SHA、实际 resampler 所用转换 TIFF 的路径与 GDAL 源修订、精确缩放及舍入设置，以及查询所针对的 Terrain input ID；批次回执另绑定 Terrain 路径、Project 路径、工程修订、extent 和一个拥有本批次临时目录的 cleanup token。产品只有在整批回执通过 exact owner 校验并被当前流程接收后才进入 `IsImporting` 集合。导入对话框用来源 renderer 为每个已暂存且正在导入的产品显示独立栅格帧，边界轮廓仍单独绘制。Hide Tile 同时把该 Product ID 从当前可导入集合和它自己的栅格帧显示中移除；它保留该产品的原始 TIFF、转换预览、URL 和 SHA，并由所属批次 token 继续持有。再次选择 Download Tile 会复用已暂存的原始文件与缓存帧；尚未下载时才按同一个查询 owner 下载。未下载产品默认不可导入，也不能执行 Hide Tile。该状态只属于这次 USGS 导入流程，不改项目地图可见性，也不写 `.vis`。

Import Staged Tiles 只把当前完整 owner 下已下载且未隐藏的 raw TIFF 交给既有 `TerrainInputImportSheet`，为每个文件恢复其原始 Float 缩放与 ThirtySeconds 舍入，并保留 R 的 `AutoStitchData=true` 默认及导入页原有的 Stitch Tiles 开关；隐藏产品不会进入这次追加。用户关闭拼接时每个 TIFF 分别追加，用户保留默认拼接时由既有地形导入服务处理多文件拼接；Hide/Download 及完整逐产品回执都在该选择之前生效。URL 和下载 receipt 属于临时会话，地形输入只有在既有 Add to Terrain 提交成功后才进入当前模型草稿，保存模型后才写入 HDF。取消、owner 变更或流程关闭按每次下载的 cleanup token 删除该 token 拥有的临时目录；正在读取源文件的地形追加完成后才执行清理，失败时保留可重试状态。

### USGS 源 TIFF 保留与工程下载目录

原 `USGSBehaviors.OpenUSGSImporterAction` 将 `Project.EnsureSpecificDownloadDirectory("USGS Terrain")` 传给导入控件；`Project.DefaultDownloadsDirectory` 是 `ProjectDirectory/Downloads`，所以默认目录为 `.ras` 工程所在目录下的 `Downloads/USGS Terrain`。原控件对每个产品分别保存 `OriginalFilename`，导入结束时总是删除临时 `ReprojectedFilename`，并仅在 `KeepDownloadedFiles == false` 时删除导入按钮当前选中的产品原始 TIFF；隐藏产品会被移出 `IsImporting` 集合，因此不属于这次保留集合。该选项默认关闭。USGS source helper 保留原始下载文件，并将重投影预览写到单独的临时 TIFF。

Swift 的 Keep Downloaded Files 默认关闭。开启时，表单把本次精确工程/地形 owner、目标目录和保留选项冻结进导入 handoff；默认目标对应上述工程目录，也可用 macOS 文件夹面板明确选择其它本地目录。工程目录由现有 `authorizeProjectFolder` 授权；用户选择的目录通过现有 `chooseFolder` 保持 security-scoped 访问。进入地形追加表单前，只处理 `handoff.files` 当前选中的产品集合；同批次但已隐藏的产品不会复制。每个文件必须能唯一匹配其 cleanup token、项目与 Terrain owner、源路径、SHA 和原始 input ID。复制在追加操作前开始，因而后续追加成功、失败或取消都不会改变本次选择的原始源文件保留结果。源文件和副本在复制前后均核对 SHA；同目录暂存文件使用 macOS exclusive rename 原子发布，不覆盖已有目标，重名时保留提供者 URL 中的 TIFF 文件名并加序号。保留失败时临时源仍归原 token 所有并可重试；无法确认身份时只阻塞相关 token 的清理，不把隐藏或未验证文件扩展进副本集合，需由用户明确丢弃后才能清理。普通 Terrain 导入 receipt、工程 HDF 和 `.ras` 不写这些下载设置。

保留目录与待完成的源文件保留任务属于当前 `ModelEditorSession` 的 USGS cleanup session，选项不是工程持久属性；每次导入重新选择时都捕获目录。关闭开关不创建长期副本，保留 staging cleanup token 的现有清理行为。若用户明确选了自定义路径，工程切换不会把该路径重新解释为新工程的默认目录。复制失败时界面提供重试；身份无法验证或用户放弃重试时必须显式选择丢弃暂存源，清理不会先删除唯一可恢复的下载文件。

关闭地图标签的保存事务先捕获精确地图、MapState、工程文档对象、会话、规范路径、工程修订与已验证 Core 上下文；关闭期间仅该地图的身份复核可使用快照，并再次核对当前文档与 registry 完全匹配。普通上下文／引用列表 getter 和其它写入仍遵守关闭保护，事务结束即清除快照。GeoJSON 读取结果绑定请求地图；绑定显示优先使用该地图唯一物理缓存，不让旧全局缓存遮盖新保存内容，也不接受同一地图的歧义来源。


## 模型编辑器保存关闭与折线回执

模型编辑器在设置关闭状态之前捕获精确编辑器、主标签类型、工程文档对象、会话、规范工程路径及修订、资产路径及来源修订和已验证 Core 上下文。该上下文只经本次 `EditorEdits` 传入 `ModelSaveRequest`，不进入长期草稿，也不修改全局 provider 的 getter。成功提交后，registry 按原捕获引用接受新修订；只有仍属于同一关闭事务的树刷新才使用刚接受的新上下文。编辑器已经关闭也不取消真实提交回执，迟到回执仍须匹配原引用，不能覆盖后来替换的引用。后续刷新失败保留保存成功状态，避免用户重复写文件。

折线放弃操作只有在来源与 owner 复核成功后，才恢复基线并清除绘制、端点连接、DeepConnect 和顶点拖拽预览，旧异步预览的代次同时退役。分割或其它替换操作始终限定到捕获的 path identities，不能凭输出数量与图层总记录数相同而扩大修改范围；单条来源分割出的两段各保留原有有序属性。GeoJSON 简化按精确保留顶点的单调顺序关联原始 Z/M，平滑按原顶点序号关联，保留额外维度的数值词法、空值与 JSON 未知成员，现有后端算法仅处理 XY。
跨 Feature 的 GeoJSON 折线 Merge 以精确 feature/component identity 选择输入，复用 PreferClosest 端点合并及逐顶点来源 lineage。输出链与未连通的已选线都按原来源集合的“删除再追加”语义生成新 LineString/id，不继承任一输入属性；字段按原 line table 的首次出现顺序建 schema，新 Table 行每列初值为 null，GeoJSON writer 将其序列化为空字符串。若选中项没有形成任何连接链，沿用原操作语义不改动来源行；GeoJSON 与 Shapefile 地图会话都把该空回执当作已处理的无操作，不显示失败提示，也不改变既有脏状态或撤销栈。未选择的 Feature 与根级未知 JSON 保持原 AST 字节词法，额外坐标维度按来源 feature/path/vertex provenance 保留；重复身份、重复属性字段及不同坐标维数拒绝提交。Map palette 只有当前源至少含两条无歧义可编辑线时启用，选择和提交仍复核原 map/source owner；草稿进入同一撤销、重做、保存回执流程。

GeoJSON 文件已保存时，编辑器叠加层可能先携带新修订，物理缓存仍携带旧 SHA。Store 只接受同一地图、工作流、节点、规范源路径和唯一物理缓存的新旧修订回执，不把真实提交误判为未保存，也不因用户当前选择变化将回执交给另一图层。

## 概念网格地图编辑与父 Geometry 事务

地图工具面板和输入提供器捕获精确工程会话、已保存地图、来源上下文、ConceptualMesh 节点、Geometry HDF 路径及 SHA、父编辑器和子草稿身份，再通过类型化协议调用原始拓扑操作。普通 Geometry 编辑器打开同一资产时，复用并接管地图创建的父事务，不建立第二份拓扑草稿。有效空网格仍可绘制；加载失败或不可用数据不能冒充空网格。选择只改变选集；添加、Sketch、节点与内部点移动、分割、反向、过滤、平滑、SmartAdd、DeepConnect 进入同一父撤销日志。多节点移动在拖动起点捕获精确节点集合。

后端返回合法无修改回执时，须核对请求、地图、来源和草稿仍匹配，且 dirty/undo/redo 与原快照一致，随后保留本地原快照、完成诊断和选中诊断。不能为了表示成功制造脏状态。关闭地图仅淘汰输入所有权与临时绘制，不丢弃已注册父 Geometry 的子草稿；最终由父编辑器保存提交 HDF。

DeepConnect 的持久来源身份遵循原始读取器：GeoJSON 线和 Polygon 外环使用按类型过滤后的读取序号，PathIndex 为零；Shapefile Polygon 使用包含空记录槽位的展平环序号，Polyline 保持原始记录和 part 序号。身份由完整源元数据计算，不能由可见列表重排推断。目标路径和两个锚点经原始渲染器／源读取器复核后才生成有向子线。SmartAdd 和 DeepConnect 从已有 arc 起步时，将 SplitArcOperation 与 NewArcOperation 放入同一父日志，不改目标矢量文件或地图 .vis。

Node hits precede internal arc vertices, which precede arc segments, matching the retained Sketch/Edit routers. Node hits use a square six-cell window; internal points and segments use a five-cell Euclidean radius. Stable source FID/path/subelement ordering breaks equal-distance ties independently of render-array order. A successful exact-owner polyline discard also retires that owner's pending provider lasso/vertex records so a late drag-end cannot restore discarded selection or edits.

## GeoJSON map coordinates and source coordinates

The loader and both editing sessions share the captured backend client. Display, hit testing and draft edits use forward-transformed project XY. Saving inverse-transforms a temporary document before the existing source-SHA compare-and-swap write. The session retains source bytes and a map-space baseline; exact unchanged JSON coordinate paths recover the original XY tokens, while Z/M/null values and opaque JSON remain source data. Polygon and MultiPolygon rings already numerically closed in the current map remain exactly closed after inverse projection and source-token restoration, including nested GeometryCollections and vertex index shifts. Repair only the closing XY tokens when numeric closure was lost; preserve open rings, numerically equal differing lexemes and all Z/M/null or opaque bytes. Persisted map-row presentation paths select the verified file format, while every actual vector read and source-family fingerprint uses the canonical physical path. GeoJSON aliases may point to extensionless or differently named files; a validated format hint selects the retained reader without reading through the mutable alias. Shapefile sidecars always come from the captured canonical family. Line and polygon editing scopes retain the format verified from the unique visible persisted map row and its physical-source binding. Palette, DeepConnect, IO dispatch and save receipts use that captured format while reads, writes, SHA and compare-and-swap remain bound to the canonical source; a caller-supplied format hint alone cannot authorize a neutral extension. The raw source-file SHA is verified independently of the aggregate file-family revision. Requests exceeding 250,000 positions are chunked under the same source/map capture and identical projection metadata, with cancellation checked between requests. A missing owner or failed transformation rejects the operation; it never falls back silently to raw XY. Discard reloads both baselines, and successful saves advance them only with the actual committed source revision.

工程替换的丢弃阶段持有关闭状态，直到所有子草稿确认已放弃。随后在同一个 MainActor 执行片段内解除关闭状态并执行捕获的替换动作，让新建 GIS 或异步打开工程使用各自的保护条件；失败的丢弃仍保留原工程及待确认动作。测试必须等待关闭与打开两个阶段完成，且新工程文档实际存在，不能把过渡期的空文档当成替换成功。

Saved Geometry map captures include the full source workflow identity (base map plus Result selection context); ConceptualMesh editor preparation compares that capture against the full current source workflow, while the separate map capture retains the base map identity. Comparing the two different identity domains would incorrectly disable all topology tools even when the loaded Geometry, parent draft and source revision are current.

Generic point, polyline and polygon map rows reuse the shared diagnostics component for source messages and load errors. Its identity includes the project session, map, exact node and source locator, diagnostic revision and status, so a refreshed or replaced source cannot inherit another source’s copied text or expansion state. GeoJSON display parsing validates finite numeric XY separately from nullable Z/M; explicit missing extra ordinates retain their declared path dimensions and never become invented zero values.

Terrain inputs explicitly distinguish HDF-persistable types from transient helpers through INonPersistableTerrainInput. The loader registry omits transient types before constructor inspection, and definition generation rejects persisting them. StitchTerrainInput has no reconstructing Save/TryInit contract; the working USGS Stitch command instead merges TIFF tiles and imports a normal raster input. Constant/Raster HDF type identifiers and source paths remain the read/write contract.

Topology global SizeFunction and PatchType are persistent mesh settings. Their existing TopoGlobal_ HDF attributes use the H5Metadata enum name/alias convention and recover the declared enum type when read, with compatibility for defined primitive integral values. Unknown values fail explicitly rather than silently restoring defaults; absence still uses the existing defaults. Control-point and arc datasets keep their existing paths. Nullable extra ordinate insertion on lines/polygon edges propagates unknown values as null while preserving original endpoint tokens and refusing malformed dimensions.

Region property validation workers retire only their own still-pending marker on cancelled/stale terminal exits, checked by task token and request ID. Raw pending text remains subject to save/mesh preparation gates; cleanup never validates it and never clears a newer same-field request. Discarded raw input cannot retain an orphaned worker pending marker.

Polygon ring decoding retains nullable source ordinates separately from its numeric compatibility view. Finite numeric XY remains mandatory, full numeric rings keep their original numeric dimensions, and a ring containing null extra values exposes only finite XY in the compatibility view. Source-coordinate metadata preserves declared dimensions, closure equality and nulls; add-polygon eligibility reads that metadata rather than mistaking a nullable extended ring for a two-dimensional one. Map editing continues to use the source AST and captured projected coordinates.

Polyline merge click and lasso handlers clear transient selection after a verified disconnected-only no-op. A click pair also retires its first-pick preview so the next click cannot reuse the old candidate. Exact owner, source revision, draft generation, cancellation and unchanged source checks precede cleanup; rejected or failed requests cannot retire a different operation, and no-op cleanup creates no dirty state or undo entry.

Preparing a Polyline editor seeds map selection only for a new editor scope, changed source/map capture or explicit sheet presentation. Repeated preparation for the same owner preserves the editor selection so hover and a late discarded gesture cannot resurrect an old captured selection. Ordinary picks still resolve exact current source components.

Localization format templates resolve their resource directly before substituting arguments. Completed diagnostic parsing may recognize known sentence frames and format them, but template formatting never re-enters that parser: the English resource often equals its key and parsing a placeholder as dynamic data would recurse. Opaque type, path and percent-containing values remain arguments.

Shapefile merge targets may be distinct parts of the same exact source record. Repeated record identities require unique retained part ordinals and identical typed/display attributes referencing the same original DBF row. Each merged chain inherits that row only when every contributor shares it; cross-record chains use the existing new-row defaults. Unselected parts retain their own geometry and row reference. The Host writer copies the referenced raw DBF row for every flattened output, preserving original descriptors and XYZM/no-data geometry.

A mixed GeoJSON file may own separate persisted point, line and polygon nodes on the same map. The physical-source reader validates the whole captured node set and chooses a deterministic captured node for its one file-scoped projection request. It retains all geometry kinds so the native map can expand exact geometry-bucket bindings; multiple compatible nodes are valid source ownership, while missing or incompatible nodes reject the batch.

## RAS 命令行环境

The Tools menu opens an interactive `zsh` in the project folder captured when the command is requested. The prompt uses the current application bundle's `Contents/Resources/CLI/ras` and the GDAL tool/data directories reported by that bundle's Host. If the project session or URL changes while the Host request is pending, the stale request is discarded and cannot open a prompt against the replacement project.

The startup script exports `HECRAS_APP_PATH`, `RASROOT`, `GDAL_DATA`, `PROJ_LIB`, and a `PATH` prefixed with the bundled RAS and GDAL tool directories. It accepts `proj.db` from the reported GDAL data directory, or from exactly one distinct physical database in the bundled Backend tree. Symlink aliases to the same file count once. It does not search machine-wide or development installations. Missing, external, or ambiguous bundled paths produce localized errors.

Before opening the interactive prompt, the script runs `ras --version` and `gdalinfo --version`; a failed preflight stops startup. The temporary `.command` file is created in a per-launch UUID directory with owner-only permissions, and that private directory is cleaned on failure. The script removes itself and its directory before `exec` replaces it with interactive, non-login zsh; this loads the user's `.zshrc` without running login startup that could reset `PATH`.

The launch-plan tests build fake `.app` bundle layouts with executable stubs and ordinary `proj.db` files. They cover bundled Host/CLI/GDAL paths, external-path rejection, absent and ambiguous databases, deduplication of symlink aliases, and missing project folders. Shell quoting and environment values are read back through non-interactive `/bin/zsh -f -c`. Tests do not launch Terminal, `ras`, or `gdalinfo`.

## 地图子图层与模型编辑

`SourceMapLayerRow` 保留序列化节点的完整祖先 locators；`MapSourceRenderRecord` 保留同一来源树的节点资产、祖先和属性。PrecipitationLayer 只有在当前工程/地图中找到唯一、完整匹配的 BoundaryCondition 与可选 Plan owner 时，才进入该边界条件模型；Result 内嵌降水保持只读检查，展示当前时刻、slot 和来源修订。TerrainInput 通过唯一 Terrain 祖先、相同物理模型路径和当前来源节点进入所属地形模型。显示名称不能代替节点路径或文件身份。

点击打开时先刷新来源树，再校验项目会话、地图工作流、资产代次、当前节点和 owner 捕获。缺失、歧义或过期时不打开其他模型。编辑继续由现有模型会话管理：降水位于 Spatial 页，地形输入位于 Terrain Inputs 页，子草稿仍由父事务保存。

共享 `Ras.Runtime` 只持有 HDF/DSS/GDAL 初始化代码；本机运行库部署包由 Host 与 CLI 可执行项目引用并传递至其实际运行目录，不在共享类库输出目录安装运行库。

## 普通数值输入草稿

NumericPropertyItem、NumberBoxPropertyItem 与普通 number 字段共享 session-owned 数值草稿。owner 绑定 scoped fieldID，context 包含 editor/load/source identity、类型、边界、模型值、选择代次和 mixed 状态。非法或未完成的原文不写入模型，控件重建后仍可显示；`hasIncompleteEditorInput` 将其纳入保存、计算准备、工具准备与关闭草稿检查。当前模型、源修订或选择改变时，旧原文标记为过期，不能写给新 owner；用户需明确丢弃或在原 context 内修正。

有限数值同时遵守 Int32/Float/Double 的实际源类型范围。源本来为空且无 mixed 标记时才接受未改变的空白，不能用空白覆盖已有数值。有效输入或 Stepper 只清除此字段的草稿；Escape 不清除未解决原文。明确确认“丢弃草稿并重载”会先走已有父事务 discard，再重载同一模型，清除该会话的原文；写入锁、其他 owner 或未完成子操作会拒绝，不清原文。普通文本保留其 Escape 恢复行为；特殊 Geometry region 和带单位数值继续使用各自既有输入事务。隐藏字段的非法原文显示在页内摘要，可逐项明确丢弃。

粒子最大寿命输入保持原界面的闭区间 `0...5` 秒。零寿命表示无存活粒子，清除 live buffer、transition 和 trail 后返回空帧；调回正寿命恢复既有动画算法。负数和非有限数值仍拒绝，粒子缓冲上限不变。

macOS 发布阶段按 Mach-O 实际 load dependencies 检查非系统绝对路径，`LC_ID_DYLIB` 的自身安装身份不当作外部依赖。只有输出目录内唯一且 family、compatibility/current ABI 完全匹配的库可以替换为相对 `@loader_path`；缺失、歧义和越界使构建失败。修改后的映像重新签名并严格验证，最终 bundle 从全新目录组装。


## 结果粒子与计算实时数据

每张地图共用粒子开关和速度倍率。涵洞粒子读取同一份设置；普通二维流速粒子的模式、寿命及数量不影响涵洞算法。涵洞按源 barrel ordinal 对齐 Flow、成对 OutsideDepth 与 OutletVelocity；流量符号决定方向，绝对流量小于 0.001 时绘制中点环。大小和生成间隔使用当前地图相机的屏幕分辨率，与选中的结果栅格类型无关。

已保存结果按来源 SHA、读取租约及选中 profile 加载。未序列化的嵌入 Geometry 默认子节点可以没有自身 XML 路径，但必须保留非空父节点路径、精确结果资产与修订、节点类型及名称；已序列化子节点仍须是该父节点的直接子节点。计算期间使用独立的 writer 状态：只在保留的 OutputBlock.TimestepAdded 生产回调中复制数组和嵌入 Geometry，保留已产出 profile 的私有快照供时间轴回看，不另开正在写入的 HDF。单个 profile 的读取失败不污染其余 profile。

实时帧必须匹配工程会话、计算 owner/context/run、地图上下文、精确节点与结果路径、slot 0、OutputBlock、profile 名称及时间和当前 viewport。Result 节点还须在 locator 与显式选择中指向同一个 writer Result；MultiResult 使用其精确 Plan owner。目标序列标识订阅集合的代次，同一代次允许数据和 unavailable 更新，较旧代次不得覆盖新帧。来源失效清除当前帧并提供诊断；普通 running 进度不清帧。取消、失败、完成、关闭或更换所有者后清理 writer 状态，只有可用的稳定结果修订才能恢复已保存结果读取。

结果采样复用现有 GeometryMapPoints 与 VelocityMapper。每个 mesh cache 只把自己的有效深度缓冲转换为水位，不处理其他 mesh 的空缓冲。已保存结果可按各 mesh 的数据采样；活动 OutputBlock 的写入缓存只支持单个 FlowArea，多区域实时流速会报告不可用，不能拼接未产出的数组。

涵洞历史帧的行数据最多保留 32 MiB 内存，后续行写入本次计算私有的临时二进制文件，按原 profile 序号读取。有效位区分读取失败的空洞与真实零值；切换或回看时不接触正在写入的 HDF。计算运行释放时关闭文件并清理临时存储。

## Web 背景地图

`MapFeaturePage → RasBackgroundMaps → OSMStylePicker` 对应原底图开关与样式选择；`useBackgroundMapRendering → RasProjectMap → BackgroundMapScene` 将现有 C# 底图服务返回的真实像素、道路等路径和文字绘制在工程图层下方。设置按已保存地图分别保留在当前视图，不写 `.ras` 或 `.vis`。OSM 样式、预览和描述从服务读取；信息密度、标注、卫星透明度随请求提交。缓存目录和大小属于 C# 服务端，只有用户明确点击清理才调用清理操作，核对清理回执后刷新底图。

读请求绑定工程会话、路径、XML 与修订，地图身份及 `.vis` 修订，原后端实例及能力代次、设置和视口。Projection 从工程 XML 读取并由 C# 核验；Local 局部坐标、未转换的本地源坐标或无已保存地图时不叠加底图。切换来源立即隐藏旧画面；平移缩放取消旧读取并合并频繁请求，已核验的旧画面只可按原工程坐标保留在原位置。关闭或取消不会让迟到回包重新安装图像。

SVG 采用返回的几何、裁剪、多段路径、透明度、虚线、线间隙、顺序、字体和光晕；RGBA 通过现有图像转换器显示。栅格按原 `RasterDefinition` 的方形像元规则核验：锚定视口左上角，像元大小取宽/列数与高/行数的较大值，实际右界或下界可相应扩展。绘制使用返回的实际范围，不拉伸回请求范围；卫星透明度已写入像素 alpha，不再次乘入设置值。来源错误、无覆盖和部分成功分别显示，不以背景色或占位图声称来源成功。浏览器字体栅格化和文字碰撞布局不宣称与原生绘制器逐像素相同。底图终态使用服务器已有的有限 64 × 1024 × 1024 字符的大地图结果上限，普通响应和进度上限不变。源码关系、真实服务返回和浏览器实际绘制仍分开核验。

## Web 在线要素查询

`MapFeaturePage → FeatureQueryImporter` 通过同一个对话框分别挂载原 NLD、NSI、NFHL、OSM 的同名 Control/Dialog；OSM 的 builder、clause、filter、operator、suggestion 也保留各自文件。查询与后台预览由共享工作流协调，不把这些适配文件计为额外原 UI。NSI 继续使用原查询规则：单个多边形可能简化，多个多边形使用外包范围；来源身份精确核验不等于查询结果按所有原始边界精确裁剪。

`featureQuery.query` 只创建绑定原保存地图的私有暂存。`featureQuery.preview` 只读取 ResultId 所属 manifest 成员并核验 SHA，返回 ProjectWorld 几何与属性；预览按完整要素限制数量、坐标和总响应字节，显示实际总数及截断情况。导出仍处理全量暂存。GeoJSON 导出按原 writer 转 WGS84，预览不再次投影。`featureQuery.discard` 使用原 map capture，支持换图后安全清理；同一结果的 preview/save/discard 由跨 worker 租约互斥。

参数和查询结果属于普通 `map.query:` 草稿；导出进行中或结果未知时另外保留 `map.write:feature-query-export:` 写保护和原 HTTP 任务编号。恢复只读取原任务。明确点击加入地图后，逐来源核验已导出文件族、申请父可视化写租约、推进 `.vis` 修订并核对实际地图来源读回。只有服务回执、父接纳和来源读回都成立才报告已加入；任何未知状态停止后续图层并保留原任务/租约。文件导出回执与地图加入回执分别保存，便于核对各自的持久化边界。

地图导入只接纳工程目录内的来源或已登记的外部连接。独立导出仍可选择其他现存服务端目录；界面在输出目录旁说明加入地图的限制，并在已确认失败时保留原服务原因和路径。

## 结果组设置与拓扑快捷字段

`ResultsSectionLayerProperties` 对应原结果组的类型选择、共享关键帧来源与重新推送。类型变更调用同一个 `setResultMapType` 地图事务，只写已解析来源、勾选且祖先可见的 MultiResult 组行；不写独立 Result 行、隐藏组或 HDF 数据。父级可视化租约一直保留到回执接纳及地图树和结果目录读回完成。传输结果未知时保存原操作编号，续查不重新提交写入。

共享关键帧是当前视图状态。锚点带原 Result slot、路径、修订和完整时刻元数据；Host 按时间匹配每个活动组。悬停、速度及分析使用已核对的目标组自己的 ordinal，不复用锚点序号。没有匹配帧时保持不可用。当前前端接通已保存 HDF 的时间轴，尚未消费计算任务的 live callback 时刻元数据；Host 的受保护元数据接口不能当作前端实时同步已完成的证据。

`TopologyArcQuickEditPicker` 和 `TopologyRegionQuickEditPicker` 只编辑当前精确单选 FID。字段能力和枚举来自 Host；多选、只读及无当前选择有明确状态。未完成的数字输入留在控件本地，提交后只进入父 TopologyDraft。匹配的非结构化字段回执可延续原选择，但不会恢复用户途中改掉的选择。弧段 ConstantSize 空文本沿原语义写 NaN，PointCount 空值写 -1；非空数字仍受原校验。撤销/重做以 `featureKind=history` 请求现有接口，父 `model.save` 才写入 HDF。 父保存仅省略同来源、同修订、账本完整且明确未修改的网格草稿，避免空账本的旧拓扑指纹阻断正常保存；已修改或状态不明的网格仍交 Host 校验，保存冲突保留全部草稿与历史。
