# UI 状态与服务契约

这份设计说明与 [UI_RELATIONSHIPS.json](UI_RELATIONSHIPS.json) 区分当前 React 实现与迁移时需要保留的来源契约。它们描述状态、关系和持久化边界，不是测试日志或完成率统计。当前产品架构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 当前 React 调用关系

`RasMainWindow → RasContent → 功能页 → BackendGateway / ServerBackend → ASP.NET Core 操作队列 → C# 工作进程 → 领域模型 / 算法`

浏览器与 Electron 使用相同的 React 页面及 HTTP 网关。`RasMainWindow.tsx` 创建 `ServerBackend`、管理语言、活动页面、工程上下文与未保存草稿所有者；`RasContent.tsx` 在切换页面时保留已挂载页面，计算页首次打开后也继续保留其会话。默认语言为简体中文，显式切换英文后保存语言偏好。

| 当前领域 | 实现入口 | 状态与提交边界 |
| --- | --- | --- |
| 工程与资产目录 | `features/project/ProjectExplorerControl.tsx`、`ProjectTreeMode.tsx` | 打开和保存采用真实 XML 与修订；目录刷新重新绑定选择，不能清空活动模型 |
| 模型表单 | `features/editors/EditorWorkbench.tsx`、`modelDraft.ts` | 草稿绑定工程会话、源文件与修订；保存只提交实际变化并采纳匹配源身份的回执 |
| 几何、拓扑与涵洞 | `features/geometry/index.tsx`、`useGeometryWorkbench.ts` | 表格、撤销历史及子编辑属于当前几何草稿；父保存才提交模型 |
| 地图与图层 | `features/map/MapFeaturePage.tsx`、`RasProjectMap.tsx` | 工程地图与本地 GeoJSON 预览具有不同持久化边界；本地导入和绘制不自动成为已保存工程资产 |
| 结果与用户序列 | `features/results/ResultsWorkspace.tsx` | 服务端 HDF 读取与本地 CSV / JSON 明确区分；本地单元格草稿在切页时保留，导出前校验 |
| 空间数据工具 | `features/tools/index.tsx` 及各工具组件 | 能力来自服务端公布的操作集合；参数校验、执行与输出文件需要分别确认 |
| 计划计算 | `features/compute/ComputeProofPage.tsx` | 完整源模型先检查，再消费一次检查身份启动计算；取消等待服务端终态，终态后重新检查并保留原回执 |
| 桌面与浏览器能力 | `desktop/main.mjs`、`frontend/src/platform/ServerBackend.ts` | Electron 管理自己启动的服务进程；浏览器通过 HTTP 上传工程，不把用户设备文件路径误当服务器路径 |

`WorkbenchContext.projectRevision` 标识 `.ras` 文件，`selectedAsset.revision` 标识模型或结果；保存工程不能覆盖模型修订。`onContextCommit` 只允许活动上下文采纳对应源的保存回执。`onDirtyStateChange` 汇总草稿所有者，工程切换与关闭须经过相应处理。

HTTP 操作的受理 ID、传输状态、求解器进度与终态分别管理。终态仍可能带有未读 `hasMore` 分页，网关须排空后才能读取结果或错误。取消请求的 HTTP 回执只证明请求已送达；只有后续操作终态能确认取消。状态失联时保留操作 ID，不能显示计算已取消或重新提交同一个任务。

当前地图由 `MapFeaturePage` 持有地图文档和画布相机，`usePersistedVectorLayers`、`usePersistedRasterLayers`、`useMapResultFrames` 分别读取已勾选的真实矢量、栅格与结果。C# 返回的工程坐标与浏览器直接读取的源坐标具有不同标记；未确认坐标系的本地预览不能与工程图层错误叠加。`RasProjectMap` 在同一工程坐标画布显示实际要素和 RGBA 图像，`MapTimeAxis` 控制同一份结果读取状态，不再创建第二个结果会话。Result 首帧读取全幅范围，之后才按相机视口请求；时刻采用原生 profile 索引与时间，播放等待上一帧确认完成。结果历史路径与修订是当前会话的显式选择，不因 `.vis` 保存就假定为持久化选择。

`useMapHoverIdentify → map.hover.identify → MapHoverIdentifyService → MapDynamicProfileService.SampleForMapNode` 使用同一个已确认的 Result 帧所有者。MultiResult 请求传 slot 0 时间轴索引，响应保留各子来源按时间匹配后的原生 profile 索引；`mapResultHoverIdentity` 只接纳与当前绘制帧路径、修订、类型和原生时刻一致的标签。Host 按具体节点重新绑定共享的 Result 来源，避免普通 Result 与 MultiResult 同时可见时串用时刻。更换节点、来源或时间先清除旧标签，迟到响应不能恢复它；鼠标离开和隐藏来源也使捕获失效。

结果图层的显隐使用 `MapLayerTree → map.tree.perform(visibility) → MapTreeService.SetSourceVisibility`，保存回执确认后才更新树。原 `RasTreeBase.SerializeDefault` 无论是否勾选都保存 Type/Name，因此 Result、MultiResult、Terrain、NValue 和 Classification 取消勾选后仍是当前地图成员，必须保留精确 locator 并可重新显示；移除来源属于单独的删除动作。显隐保存会使旧渲染帧失效，同一工程会话与地图的显式比较来源仍保留，并由服务端对新 `.vis` 修订重新验证 locator、路径与 SHA。

浏览器的“导入工程图层”经 `ServerBackend.uploadMapSources` 把用户选择的 GeoJSON、完整 Shapefile 文件族或自包含 GeoTIFF 放入工程 `Map Sources` 的独立目录；工程 SHA、会话和文件名先校验，文件夹发布后再逐项进入原有地图导入事务。每次 `.vis` 提交都推进修订，失败后先重读地图再允许继续写入。上传成功与地图导入成功分别处理；已复制的来源不会因后续失败而冒充已保存地图。直接“导入 GeoJSON”和绘制仍属于浏览器草稿。刷新图层只重新读取工程地图；切换到新计算结果时也会刷新来源树。

工程树的 Geometry / Plan 复制采用 `project.modelAction.inspect → perform`：捕获工程 XML/SHA、节点身份与原 HDF SHA，使用原 `DuplicateBySaveAsH5` 生成暂存副本，核对后发布并提交工程关联。复制与 `project.save` 共用本机跨 worker 的写锁；回执只有在文件与工程关联均确认后才更新上下文。右键仅更新树内选择，菜单“打开对象”与双击会显式进入相应编辑页；复制当前已保存文件不会把编辑器草稿写进副本。

组件入口存在、schema 字段可渲染及后端公布操作都不等于全部交互已接通。每条来源契约仍需用对应 React 操作与实际数据单独核验。

## 同名控件的挂载与保存边界

| UI 入口 | 子控件 | 真实数据与提交 |
| --- | --- | --- |
| `BoundaryConditionLineEditorPage` | `BoundaryConditionLineEditor`、`LinePayloadsList`、`TimeWindow` 及各载荷编辑器 | 新建和移除走 `model.boundaryTransform`，返回草稿保留原快照基线，显式保存才提交 HDF |
| `TerrainLayersEditor` | 栅格、函数地形、渠道与 Levee 属性编辑器 | 按选中输入的 schema、scope 和实际表渲染，沿用字段与单元格草稿回调 |
| `GeometryEditor → GeometryTopologyArcsPage / GeometryTopologyRegionsPage / GeometryTopologyNodesPage` | `TopologyArcsEditor`、`TopologyRegionsEditor`、`TopologyNodesEditor`、`ConceptualMeshTopologyArcsEditor`、`ConceptualMeshTopologyRegionsEditor`、`ArcsTable`、`RegionsTable`、`DetailedArcHeader`、`DetailedControlPointHeader`、`ControlPointEditor`、`NodeEditor` 等 | 表格选择通过稳定行 ID/FID 共享给细节与 quick-edit/recipe；支持的拓扑动作更新当前 Geometry 草稿，节点坐标保持只读，父 `model.save` 才提交 HDF |
| `ProjectTreeModeReactView` | 独立的 Project 与 Catalog 两种目录视图；Project 展示工程模型/备份等节点，外部连接节点由 Catalog 模式读取 | `project.tree.load` 与 `catalog.tree.load` 不能互换；连接枚举和提交读回使用 `catalog.tree.load` 的 Catalog 树；无 assetID 的连接/分组由树维护本地选择，清空工作台资产不清空该选择。状态按工程会话隔离，快照刷新需重新匹配节点及路径身份 |
| `SchemaFieldRenderer` | 文本、日期、数字、下拉、段落、颜色及 `PlanTimeWindowModePickerPropertyItem` | 原始字段键、只读、条件显示及中文验证由 schema 分派；Plan 模式值仍进入父模型草稿 |
| `TimeSeriesPayloadChart` | `ChartView`、`ChartPlot`、`ChartAxisControl`、`ChartLegend`、`ChartScrollbar` | 使用真实序列、时刻和稳定 series ID，空值保持断线，范围经父回调更新 |
| `SourceManagerControl` | 核心图层、Terrain 和 Classification 导入器 | 来源检查与写入分别执行，保留工程会话、规范路径、来源修订及保存回执 |
| `ImportExportToolControl` | `PlanExporterControl`、`RasterImporter`、`DepthShapeExporterDialog`、`RgbTiffExporterDialog` | 计划与栅格工具沿各自事务运行；Depth/RGB 先检查和预览，点击后才让 Host 写服务器输出路径 |
| `GeoprocessingToolControl` | `ReprojectionToolControl` 及栅格属性摘要 | 投影搜索、识别和坐标转换来自服务端，属性来自实际栅格检查 |

控件拆分不自动提供原 UI 的全部命令。父级尚未提供的结构编辑、混合值重置、地图几何操作、结果捕获或会话引用事务，必须保持明确的能力边界，不能显示成功回执。`workspace.file.inspect` 为用户明确输入的服务端文件路径捕获规范路径和 SHA；核心图层检查与复制继续验证该捕获，不要求用户手填 SHA。

关联地图层由 `useAssociatedMapLayers → map.associations.load` 读取。所有者来自当前所选 Result 的 `treeRoots`，Geometry、Plan、Result 与分类/NValue/Terrain 关联节点保持各自 locator。图像沿返回的 world viewport 绘制，实际像素尺寸受聚合预算限制；鼠标识别携带该帧的来源 SHA、依赖、所有者及上下文。相机、显隐、来源或会话变化先使旧捕获失效。

地形 HDF 的文件 SHA 与动态剖面的完整依赖摘要是不同身份。鼠标识别先核验 HDF SHA，再按 HDF、栅格和 sidecar 的依赖摘要匹配剖面；所有采样完成后再次核验依赖。前端回执沿用绘制帧所捕获的 HDF SHA，不把依赖摘要误当文件 SHA。

分类输入由 `ClassificationLayersEditor` 按来源种类挂载栅格分类属性、映射表或多边形属性，分类字段与解释表变更经 snapshot 对齐后进入 `ModelDraft`；糙率输入由 `ManningsNEditor` 按模式挂载常数或分类糙率编辑器，`model.manningTransform` 的 NLCD 默认值只更新匹配分类行的草稿。`nvalue.preview` 只读；分类导入、映射行增删、多边形地图点选及持久化透明度没有对应写入操作。空间时序以 rows 为权威，同一 table-backed 序列的 samples 仅作图表投影，不在结构变换和保存时重复提交。新增测站保留原行、测站和 payload GUID 身份。

拓扑页分别挂载 `TopologyArcsEditor`、`TopologyRegionsEditor` 和 `TopologyNodesEditor`。弧段标签、点坐标对话框、快速缩放与 Recipe 操作都属于当前可撤销草稿；Recipe 检查与应用之间核对 revision、draftState 和 draftIdentity。`RasCatalog` 的树行、树模式及详情面板共享现有选择和展开状态，不建立第二份工程会话。地图时间轴的结果选择、比较槽和滑条共享唯一 `useMapResultFrames`，首尾帧导航仍使用服务端 step ID。

`MapFeaturePage` 只把已确认连续栅格帧的精确 `rasterSource` 传给 `RasterSymbologyEditor`，直方图、颜色带与应用调用真实符号服务；`.vis` 提交回执必须匹配请求的上一修订和来源 SHA。只用于当前渲染、尚未持久化的 plot options 保持只读。水力工具页的 Wilcock 控件与单位计算器分别调用 `wilcock.load/evaluate` 和 `unitCalculator.load/convert`；数值运算留在 C#，前端只管理输入、工况、请求与真实结果显示。


## 专用界面的挂载与数据边界

`ProjectExplorerControl` 分别加载 `project.tree.load`（Project 模式）与 `catalog.tree.load`（Catalog 模式）；两棵树服务不同目录视图。外部连接节点只在 Catalog 树中枚举，连接提交回读也必须走 Catalog 树，不能用 Project 树验证。

| UI 挂载 | 状态所有者与读取 | 写入或只读边界 |
| --- | --- | --- |
| `ProjectExplorerControl → ProjectExplorerMainPageTiled → FirstLaunchScreen / NewProjectPage / ProjectOpenProjectDialog` | 首页动作由工程工作区持有；最近工程仅把成功打开的服务器路径存入浏览器 `localStorage`（最多 12 项）。新建表单的 `NewProjectDraft` 是弹窗草稿，父目录初始为空。 | `projection.evaluate` 只验证；`project.create` 需要用户输入 C# 服务主机上的绝对父目录，不接受浏览器本机路径。只有完整创建回执且原工程会话仍匹配时才切换上下文；没有持久化的默认起始目录。 |
| `MapFeaturePage → MapDocumentTabs → NewMapFlyout` | 工作区工程会话/XML/修订与当前地图 `.vis` 修订是请求所有者；地图类型、菜单与请求代次是弹层本地状态。 | `map.document.create` 以工程 XML SHA 和预期 `.vis` 修订创建保存地图；地图类型、索引、名称及前后修订匹配并完成树读取后才安装新标签。写 `.vis`，不代替工程 `.ras` 保存。 |
| `MapFeaturePage → VectorSymbologyEditor` | 仅从已验证持久化矢量捕获挂载，绑定活动地图节点、规范 GeoJSON 路径与来源 SHA；原始要素序号和条件规则是临时草稿。 | `vector.symbology.inspect/evaluate` 只检查属性及由 C# 求值预览。当前 UI 明确不写 GeoJSON/Shapefile 或 `.map` 样式，也不把预览应用到地图；草稿可参与切换提示，但没有样式保存回执。 |
| `ResultsWorkspace → ResultNodeDetails → PlanNodeDetails` | 详情按当前 Result HDF 路径/修订及工程修订读取。`PlanNodeDetails` 持有局部描述草稿，按工程会话/路径和 Result 路径/修订注册独立 dirty owner。导入的用户数据草稿另按工程会话/路径存入内存 `ResultsDraftStore`；只有选择用户数据源时才向父级登记其 dirty 状态。地图 `MultiResultNodeDetails` 仅在活动树节点为 MultiResult 时出现，比较源属于当前地图结果会话。 | `result.planSettings.save` 仅允许精确匹配 Plan 描述在工程/HDF 修订一致、父提交可用且没有其它 dirty owner 时写入；检查会排除该描述自己的 dirty owner，因此可以保存自身草稿。取消更改恢复服务端描述；用户数据的“还原”恢复本次导入基线，CSV 导出则把导出数据设为新本地基线，均不写 HDF。Plan 保存回执更新选中 Result HDF 修订，不写 `.ras`；地形关联、关联列表、候选和内嵌字段/图片只读。地图比较源增删仅改当前会话，不写 `.vis`。 |
| `SchemaFieldRenderer → InputDateTimePropertyItem / DateTimeInputBox / DateInputBox` | 普通模型日期字段使用受控文本值；识别的日期时间形式保留原分隔符和时区后缀，不能识别的原文仍按原样显示。 | 输入只通过 `onFieldChange` 更新 `EditorWorkbench` 的模型草稿，校验通过后由 `model.save` 写入。专用 `BoundaryConditionTimeWindow` 仍使用 `datetime-local` 转换；结果表的 `TimeSeriesPayloadDateTimeColumn` 只是只读时间标签。 |
| `EditorWorkbench → BoundaryConditionLineEditorPage → BasicHydraulicBoundaryConditions → BoundaryConditionLineEditor / BoundaryConditionLinePayloadsList / NewPayloadDialog` | 当前模型快照、line/payload 身份、字段别名、表格/序列和 `ModelDraft.sourceState` 共用一个父编辑器所有者；切换边界线只改变局部选择。 | 新建/移除载荷经 `model.boundaryTransform` 得到修订绑定的草稿投影；字段、时段、表格与样本修改留在同一草稿中。只有父级 `model.save` 写入模型 HDF。 |
| `GeometryEditor → GeometryCulvertsPage → CulvertsEditor → CulvertEditor` | `geometry.culverts.load` 返回的稳定行 ID、源修订、服务端 `draftState` 和撤销状态归 Geometry 工作区；选行及子页是视图状态。 | `geometry.culverts.edit` 修改的是服务端草稿；父级 `model.save` 才提交 HDF。服务端能力不可用时的本地添加/覆写会保持 local-only，不能冒充可保存几何。 |
| `EditorWorkbench → TerrainLayersEditor → selected terrain-input detail/table editors` | `model.terrainTransform` 请求携带当前模型修订、`sourceState`、字段/行/样本和选中输入；返回的 terrain projection 与新 `sourceState` 留在 `ModelDraft`。 | 图层增删、排序、选择、行变换等是草稿转换，不直接写模型文件；校验通过后由同一 `model.save` 提交。 |
| `ToolsPageView → USGS 地形 tab → USGSTerrainImportControl / USGSTerrainImportDialog` | 页面按需挂载并绑定活动 Terrain、工程会话及源修订。查询范围、产品筛选/选择、拼接和逐来源舍入选项由当前导入工作流持有。 | USGS 查询、下载、导入检查、追加暂存/状态查询分开执行；确认的 `model.save` 才提交 Terrain HDF。Host 临时下载文件的清理是独立操作，须重新确认已保存 Terrain 不引用这些路径。普通工程内 Terrain 使用其现有 path/revision/sourceState；只有实际存在的会话外部引用才需要额外捕获。 |
| `ProjectExplorerControl → NewGeometryDialog / NewBoundaryConditionDialog / NewPlanDialog → useProjectModelCreation` | 三种模型共用弹窗字段与 `modelCreation.inspect` 身份；请求绑定当前工程会话、路径、XML、修订和 XML SHA。 | `modelCreation.commit` 在 Host 暂存并写入模型 HDF、核对工程与目录读回，失败时按文件身份与内容有条件回滚；Plan 写入 PlanAssociation，带可选 Terrain/N-Value 的 Geometry 写入关联元数据。新 Boundary Condition，以及没有可选 Terrain/N-Value 关联的新 Geometry，可能不改变 `.ras` XML/SHA；新模型 HDF 仍由 `assetRevision` 标识。只有匹配的父上下文回执才切换当前选择。 |
| `ProjectExplorerControl → MigrateProjectControl` | 用户输入的旧版 `.prj` 路径、`projectMigration.inspect` 返回的源 SHA/修订与独立目标目录绑定迁移确认。 | `projectMigration.execute` 调用 Host 转换器，旧源保持只读，输出写到另一个目录；`project.open` 读回输出并经父工程会话校验后才切换上下文。迁移不是改写当前 `.ras` 的 `project.save`。 |
| `EditorWorkbench → SpatialDataSummaryPage → SpatialDataSummary / SpatialBoundaryConditionsPage → GriddedDataControl → GriddedDataImporter / GriddedDataImporterDialog / GriddedDataImporterSlider / GriddedDataImporterTimeline; SpatialBoundaryConditionRasterImporter uses the importer dialog` | 页面按 Boundary Conditions 快照修订读取支持图层的检查信息与帧；请求绑定当前模型 owner、修订、图层、时刻/分量。测站与空间输入行/字段属于 `ModelDraft`；帧缓存与可取消读取属于空间页。 | `model.gridInspect` / `model.gridFrame` 只检查来源与读取 Host 帧，帧选择复用 `onGridFrame`。栅格导入需用户二次确认，并仅在工作台返回确认后关闭；`model.gridImport` 使用已保存快照并立即提交模型，拒绝脏或无效草稿，且需新修订与导入读回后才能刷新基线。测站/输入结构用 `model.spatialTransform` 更新父草稿，之后仍须 `model.save`。DSS 与需要账户认证的 Cumulus 导入当前不可用。 |
| `EditorWorkbench → PlanEditorControl → PlanSettingsPage → SchemaFieldRenderer → PlanTimeWindowModePickerPropertyItem` | Host schema 的 `ViewModel_SelectedPlan_TimeWindowMode` 字段、choices/title 与 `planTimeWindowFieldIDs` 提供真实 enum 值和 mode/start/end 字段身份；React 受控控件把原值写回同一 `fieldKey`。 | 模式选择仅更新 `ModelDraft`，不会自行改写开始/结束时间。`EditorWorkbench` 把模式与时间范围的实际变更作为 `planTimeWindowEdits` 交给 `model.save`；Result Plan 仍只读。PlanSettings 挂载 PropertyItem，新建方案的 `NewPlanControl` 按原 C# 组合独立 `PlanTimeWindowModePicker`；已挂载控件不包含原属性头的动态图标表现。 |
| `GeometryEditor → GeometryTopologySummaryPage / GeometryTopologyArcsPage / GeometryTopologyRegionsPage / GeometryTopologyNodesPage` | `TopologyControl` 导航到拓扑页；弧段/区域详情挂载 `TagList`，弧线和区域表之后挂载 `TagFilterControl`，概览页挂载 `TopologyStatus`、`TopologyBuildResultViewer` 与 `TopologyProperties`。表格选集仍映射稳定源行 ID/FID。 | 标签新建/赋值/移除通过 `topology.edit` 作用于当前 Geometry 草稿和撤销历史；`topology.filter` 捕获来源修订和草稿、验证返回 FID 后只更新表格选集。全局缩放因子按 Host 支持范围更新弧线/控制点属性并留在草稿，父 `model.save` 才提交 Geometry HDF。筛选只支持弧线和区域；跨页选集缩放与节点坐标编辑未接通。 |
| `TerrainLayersEditor → LeveeModTerrainInputEditor → LeveeModEditor / LeveeModPropertiesEditor` | 只在所选 Terrain 输入确实返回 `LeveeModsTable` 或 `LeveeModPropertiesEditor/PropertySets` 时挂载对应子编辑器；两者读取同一选中输入的表、行和 schema。 | Levee 属性/行变更回到 `ModelDraft` 的字段、行或 `model.terrainTransform` 投影，不单独保存；父 `model.save` 是 Terrain HDF 持久化边界。 |
| `MapFeaturePage → MapLayerTree → RasTreeV2 → MapTreeNode → type-specific tree nodes` | 文件夹、Geometry、Plan、Result、MultiResult、Terrain、NValue、Classification、Boundary Condition、Terrain input、FlowArea、Culvert barrel、Point/Polyline/Polygon 与通用节点按节点类型挂载；选择和展开状态属于当前地图树。 | 工程来源显隐经 `map.tree.perform` 保存 `.vis` 并核对前后修订；隐藏不删除树成员，移除是独立动作。本地预览开关、`RasMapMinimap` 拖动和 `ZoomToPointDialog` 坐标导航只改变当前相机，不写 `.vis` 或 `.ras`。 |
| `SourceManagerControl → AttributeTableEditorControl` | `workspace.file.inspect` 捕获主文件 SHA；Shapefile 另由 `map.vector.captureShapefile` 捕获 `.shp/.shx/.dbf` 与可选 `.prj` 文件族，`attributeTable.load` 读取表。读取前后复核主文件和文件族修订。 | `attributeTable.transform` 仅返回内存预览（增删改列/单元格）；此属性表流程没有写回 Shapefile 的动作。地图文件导入是另一条 `uploadMapSources → map.vector.importShapefile` 流程，不能把预览编辑算作磁盘写入。 |
| `ImportExportToolControl → DepthShapeExporterDialog / RgbTiffExporterDialog` | 两个对话框分别绑定保存地图中的 Result/profile 或 RGB 栅格来源、检查所得来源身份与参数，以及精确匹配的 Host 预览。 | `depthExport.execute` 与 `rasterExport.execute` 只有点击导出后才执行 Host 文件写入；用户提供的是 Host 可解析的绝对输出路径。完成回执描述服务器产物，Web 不宣称已下载到浏览器本机；此处记录源码调用链，不代表运行时验收。 |
| `ProjectExplorerControl → ExternalConnectionManager / ExternalConnectionDetails / DssExternalConnectionPreview` | 工程上下文提供 XML/session/revision 身份；连接节点和选择来自 `catalog.tree.load` 的 Catalog 模式，不能从 `project.tree.load` 的 Project 树读回。Result 类型选项来自真实 `ObservableResult` 节点。 | `project.connection.inspect` 返回 `canCommit/validationMessage`、规范路径、来源存在性/文件数/摘要、source-references 摘要及 before/after 身份；`project.connection.commit` 提交 `expectedConnectionSHA256`、`expectedSourceSHA256`、必填 `expectedSourceReferencesSHA256`，新增还提交 `expectedNormalizedName`。Host 重新打开工程后用 `CatalogTreeService.LoadCatalog(Mode=Catalog)` 验证新增或删除，`project.tree.load` 不包含外部连接节点。提交回执包含新工程快照、`previousProjectRevision`、before/after、`sourceCapture`、`previousSourceReferencesSHA256` 和 `sourceReferencesSHA256`；客户端校验回执后才调用 `onContextCommit`。Result 按原工程格式保存为指向实际 HDF 的 File connection。`.dss` 目录/记录预览走 `dss.catalog/read`，只读。绝对路径属于 Host 所在机器；断开来源仍可按原连接身份移除。 |
| `EditorWorkbench → ClassificationEditor / NValueEditor → ClassificationLayersEditor / ManningsNEditor` | 分类栅格、映射表、多边形坐标属性和糙率常数/分类表按当前模型快照、选中输入、模式及 `ModelDraft` 字段/行挂载。 | `model.classificationTransform`、`model.manningTransform` 返回受捕获模型修订约束的草稿投影；普通字段/表格编辑仍由父 `model.save` 持久化。`nvalue.preview` 是只读预览。分类导入、映射行增删、地图点选多边形编辑及持久化透明度仍未接通。 |
| `BoundaryConditionEditor → AirPressurePage → AirPressureHeader / AirPressureControl` | Header 与压力字段/单位读取同一 Boundary Conditions 快照并共享父草稿回调。 | 输入只更新 `ModelDraft`，由 `model.save` 持久化；单位呈现不产生独立写入。 |
| `ResultsWorkspace → CrossSectionEditorWindow / CrossSectionEditorControl; DynamicProfilePlot → ProfileSampleLinePlot / ScrubbedDataDisplay` | 独立断面窗口按当前 Geometry 来源读取实际站距、高程和岸线样本；采样线按 Result 来源、用户输入的工程 XY 与 Flow/CumulativeVolume 选择计算。 | 经典断面只读；采样线按各来源原生时刻、单位和 NoData 展示，不是空间网格剖面且不依赖地图点击。Scrubber 显示真实时刻与样本数值；这些结果界面不写模型 HDF。 |
| `SourceManagerControl → VectorImporterDialog → NewShapefileDialog / ShapefileAttributeTableViewer` | 工作流要求从当前 Host 工程地图目录显式选择已保存地图；创建或导入绑定项目会话/修订、地图身份与 `.vis` 修订。导入捕获规范 Shapefile 路径及 `.shp/.shx/.dbf` 和可选 `.prj` 完整文件族。 | `map.shapefile.create` 只创建文件并返回创建回执，不表示图层已导入；`map.vector.importShapefile` 再次检查来源文件族和目标地图后写 `.vis`。只有刷新 `map.document.list/map.tree.load` 并确认精确 locator 后才记为导入成功。属性表转换仅为内存预览；服务器绝对路径不等于浏览器本地文件路径。 |
| `ImportExportToolControl → PlanExporterDialog / PlanPickerDialog / ReprojectionToolDialog → ProjectionSummary` | 计划导出检查捕获工程会话、Plan 路径/修订、参数及 Host SHA；投影搜索、输入识别与坐标转换结果均按服务响应和稳定表格行身份显示。 | 只有显式导出才运行 `planExport.executeDuplex`，输出路径由 Host 解析且执行前重查来源。`reprojection.transform` 只返回坐标表和 Host 投影回执，不写文件；CRS 不从文本猜测。 |
| `MapFeaturePage → RasMapElementsOverlay → RasMapLegend → OverlayItem / MapOverlayChrome; contextual menu → RasContextualActionsBar; inspector → RasLayerPropertiesPane` | 图例只显示当前地图、精确栅格来源已成功应用 Host snapshot 的分级值和颜色；属性面板显示已选 GeoJSON 要素属性/CRS，或工程树节点类型、名称与诊断。 | `rasterSymbology.apply` 核对 `.vis` 修订和来源后才发布新图例；palette 保存/删除不会替代已应用分级。覆盖层跟随地图显示设置。属性检查不写项目数据；只在确有已加载范围时提供缩放。 |
| `ComputeProofPage → PlanComputeControl / RealTimeMonitor → LiveMessageList / MessageControl` | 反馈控件读取 ComputeProofPage 的唯一运行状态、进度样本与服务端终态；消息显示真实 message、sequence、timestamp。 | 复用 `plan.compute.inspect/execute` 与原取消/终态回执流程，不建立独立计算会话。剩余时间是线性估算，监控消息有时间窗与最近 200 条上限及省略计数；未增加暂停/恢复动作。 |

`currentImplementation` 与本表只记录当前源码中的组件挂载、操作名、状态所有者和持久化边界；这是静态关系图，不代表构建成功、浏览器/Electron 交互、Host 实际运行或真实文件路径的验收。旧 C#/SwiftUI 集合仍是迁移来源，不能据此声称 React 覆盖。模块存在、来源契约或预览草稿也不代表持久化能力。矢量条件符号目前只到 Host 求值预览，不能写成地图样式保存已接通。

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

Raster Calculator 图层的稳定 owner 是项目会话、地图 ID 和地图类型；完整 `MapTreeMapIdentity` 与 map-workflow identity 构成可变的 capture epoch。epoch 改变会取消旧生成并对新地图上下文重新 inspect，同一 owner 的可编辑配方、图层名、可见状态和 profile 选择得以保留。只有每个配方来源的 ID、修订、可用状态、来源类型和 map type 仍匹配时才可用新 context token 重算；来源变化或检查失败会清除旧栅格及范围，同时保留可编辑配方并显示错误。引用点取自所有来源的完整范围 min/max 对角点，不从当前 viewport 猜测。

编辑窗口也按这一稳定 owner 保留尚未保存的表达式、输入、常量、名称与 profile 草稿。来源变化后须由用户明确采用同一 source ID、类型与 map type 的当前版本，刷新不会静默改写配方中的来源修订。Add to Map 与 Save Existing 都重新检查来源、执行实际求值并在注册图层前再次核对上下文和来源；任何异步阶段的请求代次或草稿修订变化都会拒绝旧响应，已有预览不能跳过提交检查。

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

## 河槽旧几何导入

河槽几何导入界面当前开放 Version 6 HDF 入口；2025 Classic Geometry 入口保持禁用。两种文件格式不能互换，导入必须使用对应的读取与迁移路径。

文件选择捕获原始 HDF 的规范路径和 SHA，复制并校验私有输入副本后，复用已有 ModelConverter 将副本迁移为 Classic 几何，向精确选中的空河槽输入导入剖面与拓扑。原转换器可能补写辅助线，因此不能直接传入用户原文件；转换前后继续复核原文件 SHA。移植不依赖 Windows 迁移可执行文件。迁移文件使用父地形现有导入 token 的私有目录；草稿只携带经校验的文件名、来源 SHA 与迁移 SHA，不持久化临时绝对路径。

导入是父地形草稿操作。撤销和重做同时恢复剖面及拓扑，跨服务请求可重放；父保存才写入地形 HDF 并清理暂存目录。取消、来源变化或过时回包不能安装快照，未接纳的 token 沿父地形清理机制释放。原所有者已退役时，即使迟到响应复用其旧 token，也再次清理，防止后台导入重建已清理的目录；仍活动的父所有者保留自己的既有 token。

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

## 当前工程的 Plan、Result 与 Terrain HDF 会话引用

`SessionHdfReferenceCapture` 在 `CoreLayerReferenceRegistry` 中使用独立字典保存 Plan、Result、Terrain 的精确会话引用；它与 Geometry / Boundary Condition 的 `CoreLayerReferenceCapture` 分开验证，但作为 `CoreLayerRequestContext.sessionHdfReferences` 一部分，随需要重建当前 `Project` 的目录、地图、编辑器和图表请求显式传递。捕获绑定项目会话、规范化工程路径、HDF 类型、原始及规范化源路径、源 SHA-256、名称、reference ID；Result 还记录其 HDF 内嵌 Plan 的名称。后端在新建 `Project` 前校验上下文，并在附加每个 Plan、Result 或 Terrain 前复核精确路径、类型、SHA 和实际名称。没有上下文时不会从任意路径发现或认领会话 HDF。`sessionHdfReferences` 缺省解码为空数组；空数组不会向 `core-layer-context-v1` 的摘要材料添加新段，因此旧 Core 引用集合的 identity token 保持逐字节兼容。非空时才追加 `session-hdf-references-v1` 及按 reference ID 排序的捕获字段。

从 Catalog 选择 HDF 时，界面先捕获当前项目会话、工程修订、源文件 SHA 和完整 Core 上下文，再通过 `workspace.import.sessionHdf.inspect` 识别实际 HDF 类型。用户明确选择 Add 后，Host 先用包含新引用的完整上下文重新打开 `Project`，确认该路径可以附加，再登记到当前会话并刷新资产、Catalog 和地图来源树。适用的当前地图才执行对应的 `addSessionHdf` 操作；没有地图或该地图类型不支持该资产时，会话引用仍可加入。Add 不复制 HDF、不写 `.ras` 项目成员关系，也不将会话引用伪装成已保存工程成员。

外部 Result 的 `ResultPlanName` 仅是该 Result HDF 内嵌 Plan 的分组标签，不会将这个 Plan 加入 `Project.Plans`，也不会因同名而归属到工程已有 Plan。Catalog 将已捕获的外部 Result 从工程 Result 分组中分开，并按内嵌 Plan 名称构造独立的会话 `MultiResult` 分组。分组的 `SessionHdfResultOwner` 绑定按规范化路径排序后确定的外部 Result 捕获、项目会话、工程路径、Result 路径与 SHA、内嵌 Plan 名和完整 Core 上下文 token；它只标识分组，不能代替被选 Result。slot 的 Latest 按原 `MultiResultVisualContext` 规则在该外部组内选择（有当前计算结果时遵循原默认项，否则取 `ComputeSummary.EndTime` 最新项）；明确选择则保留仍有效的精确 Result 路径与 SHA。地图和图表解析时分别核对分组 owner 与选中 slot，因而两个内嵌 Plan 同名的外部 Result 可以共享一个真实分组，而同名工程 Plan 或工程 Result 不会被混入该 owner。

Plan 或 Terrain 的保存只有在 Host 提交成功后，才由已提交的编辑器快照回执重基对应会话捕获。Store 核对原项目会话、工程修订、完整捕获上下文和同一个源路径，使用提交回执中的实际 HDF SHA 与名称计算新的 reference ID 和 identity token，然后使资产、Catalog 与地图树重新读取。捕获保留原始 `sourcePath`，不会改成复制位置；编辑中的草稿不推进引用 SHA。Result 作为结果来源不走 Plan / Terrain 模型保存重基路径。

引用管理器的 Remove 只删除与当前完整上下文和项目修订精确匹配的那一项内存捕获，不删除文件、不保存 `.ras` 或 `.vis`。它会更新会话资产并失效、刷新相应 Catalog / 地图树；仍打开同一 HDF 的编辑器保留原 baseline、字段、撤销状态和草稿，但标记引用冲突并禁止经旧 owner 保存，用户需明确丢弃并重新加载。其他编辑器草稿不受影响。工程关闭或替换在既有未保存草稿／忙碌状态确认通过后，会停止 HDF 文件的 security-scoped URL 访问、关闭引用管理器、清除导入 presentation 与管理状态，并重置两类 Core 引用；registry 发现项目会话 ID 或规范化工程路径变化时也会清空两组引用。原有草稿保存／丢弃确认门槛不变。

## 地图提交回执

地图来源操作与节点设置先在旧 `.vis` 修订仍有效时，从最终内存 XML 生成完整响应树，并在提交前再次核对来源。临时文件刷盘后、原子替换前计算最终 SHA；写入成功后直接返回已准备的回执，不再重开地图、读树或重新求目标文件哈希。前端把后续刷新、修订接受和会话引用登记失败与真实提交分开：已经提交的操作保留回执并关闭可重试的导入表单，明确提示需要刷新。项目切换后不把旧会话引用登记到新工程。

会话 HDF 导入占用工作区忙碌状态时，只有绑定操作 UUID、工程会话、导入表单和目标地图的当前授权，才能执行该导入自己的地图提交。普通地图写入仍被阻止；其他写入器运行、重复获取、错误工程／表单／地图与过期释放均不能越过保护。Provider 发出请求前还会核对授权与请求上下文中的工程和地图身份。

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
