# 架构与迁移约定

## 文件边界

界面逐文件迁移：一个原 C# UI 文件对应一个同名 TS/TSX 文件，按原功能与目录分组管理；生成的 XAML binding 在该页面内变为 React state、props 和事件回调。原本独立的 ViewModel 保持独立 TS 文件。公共基础控件保留其独立职责，不能把多个页面合并为一个大型入口。新增 React 入口、浏览器传输和平台适配不强行对应原文件，放在明确的共享或平台目录。

## 运行分工

React 管理布局、导航、表单草稿、交互和展示；C# 管理已确认模型、校验、文件格式、事务和计算语义。浏览器及 Electron 共用 React 界面与 ASP.NET Core HTTP 接口。Electron 可以启动自己的本机服务，也可以连接已启动的服务。

ASP.NET Core 服务为每个操作分配任务 ID，提供按序读取进度、终态和双工控制的接口。原后端在独立 C# 工作进程内执行，保留既有静态状态与 HDF/GDAL 资源寿命的隔离。页面忽略过期响应；发出取消请求后继续等待服务端确认，任务中断与求解器确认取消分别处理。

任务已进入终态时仍可能有未读进度，客户端必须排空 `hasMore` 分页后才读取最终结果或错误。网络重连和取消请求重试通过独立传输状态报告，不能计为求解进度；无法确认任务终态时保留任务 ID 并显示“状态未确认”。每次计算消费一次检查身份，确认终态后重新载入检查，保留原计算回执供查看。

C# 后端复用已经修复的业务和计算源码，保留已有 NuGet 身份与数据兼容。HDF、DSS、GDAL 运行库按服务端平台部署。目前随仓库的运行库覆盖 macOS arm64；其他平台需要对应包及独立运行验证。WASM 源码保留为后续可选方向，默认构建与运行不依赖它。

浏览器可上传工程目录或 ZIP，保留相对目录与关联数据文件；服务端返回自己的实际工程路径。服务器路径打开是另一种明确入口。上传、工程打开、新建保存、模型读写和计划计算分别保留真实回执与源修订校验。

## 数据与会话

工程、资产和异步操作使用项目会话、实际路径、源修订及请求代次识别对象，不能凭展示名推断身份。非法输入保留在草稿中，阻止保存和计算；子草稿由父事务统一提交。真实保存回执在随后刷新失败时仍保留。

`WorkbenchContext.projectRevision` 只标识 `.ras` 工程文件；`selectedAsset.revision` 标识当前模型或结果文件，两者独立保存与核对。工程保存回执不能覆盖模型修订。树目录刷新只更新树状态，不充当用户取消选择；源文件内容修改则按已确认回执更新对应资产。

保存地图通过只读 `map.document.list` 发现，绑定当前工程修订与 XML SHA，并用同一 `.vis` 修订读取各地图树。列表和树读取前后再次核对目录身份；缺失 `.vis` 返回空列表且不创建文件，读取错误不能伪装成空工程。新建与显隐提交都使用已确认的可视化修订，并更新整个地图目录的共同版本。

地图来源上传与 `.vis` 关联提交分开进行。`importMapSources.ts` 先核对上传回执的工程路径、修订及会话，再串行调用各格式的原 C# 导入器，并用前一次保存回执的可视化修订提交下一来源。目录上传保留 `webkitRelativePath`；GDB 修订标识整个稳定文件清单，不能用某个内部文件的 SHA 或目录路径替代。目录上传先写私有 staging，再校验工程身份并原子发布；不完整上传只清理自己的 staging。

`usePersistedGdbLayers.ts` 通过 `map.gdb.catalog` 捕获完整 GDB 身份，再将精确地图和叶节点 locator 交给 `map.gdb.source.load`。画布使用 C# 返回的工程 XY；捕获对象保留原始 XYZM、递归几何和属性状态。GDB 栅格由 `usePersistedRasterLayers.ts` 使用同一完整目录身份和精确 GDAL 子数据集 connection string 读取：连续浮点单波段调用 `rasterMap.load`，保留原符号渲染器的 straight-alpha RGBA；整数分类与三/四波段 Byte 影像调用 `map.raster.layer.load`，沿用原 Discrete/Image renderer 的 premultiplied RGBA 并在生成 PNG 前还原直通 alpha。三种类型都返回有界的整幅来源范围，不套用普通文件栅格的 viewport 接口。XML 快照按需生成，先接住其异步结果再读取 catalog；关闭工程、切图和新请求代次都会中止旧读取。

`MapTreeGdbCatalogs` 只存在于一次 `BuildTree` 中，同一规范 GDB 路径的所有栅格子节点共用一次目录捕获，目录读取失败也不会被每个兄弟节点反复重试。所有根、文件夹及保留的未知节点递归共用同一个对象。最初捕获目录的一般读取错误保留为图层诊断；整棵树返回前，逐个重新验证已成功捕获的完整 GDB 来源，来源变化或最终无法复核均使整次读取失败。缓存不跨地图操作或请求共享，不使用按时间失效的进程级目录缓存。

GDB 帧仅能凭 `map.tree.perform` 的显隐保存回执跨越 `.vis` 修订复用：前修订必须匹配当前地图，且工程会话/XML/修订、地图、精确 locator、图层类型和 GDB manifest 都保持一致。重绑同时更新外层 raster identity 与嵌套 GDB map/node；隐藏帧沿合法回执链保留，零个可见图层也会推进该链。刷新、未知地图改动或来源变化会丢弃缓存重新读取。这样连续勾选图层不会给串行 C# worker 重复排入所有栅格读取。分类栅格在 SVG 放大时使用 nearest/pixelated 显示，避免浏览器把离散类别插值成渐变；连续栅格和影像保留平滑显示。

`useMapHoverIdentify.ts` 把当前工程坐标下、已成功载入且有效可见的栅格身份送入 `map.hover.identify`；GDB 捕获直接复用绘图时已校验的完整 manifest。与原 C# 注解同名的 `ToolTipAnnotation.tsx` 在指针下显示 Host 返回的标签，UInt32 数值始终保留字符串。仅 Select 非拖拽且无绘制草稿时采样；源坐标 GeoJSON 模式不采样工程影像。当前已确认的 Result render snapshot 也可参与，必须匹配同一 context token、来源修订与 profile；其他关联图层种类尚未接入该前端提示。工程、地图、来源、选择、时刻、镜头和指针变化均作废旧响应，NoData 不伪造数值。坐标栏从屏幕指针与当前镜头推导工程 XY，程序缩放或切换图层范围后也不会保留旧镜头的坐标。

Result 悬浮请求的 `profileSelections` 使用当前节点时间轴索引；MultiResult 时间轴由 slot 0 锚定，各子结果按原有 10 ms 时间容差匹配自己的原生索引。`mapResultHoverIdentity.ts` 将响应与当前实际绘制帧的路径、SHA、地图类型和原生 profile 逐项核对，不能用 slot 0 的序号替代子结果索引。Host 的 `SampleForMapNode` 按精确节点解析来源和时刻；同一个 HDF 同时出现在普通 Result 和 MultiResult 中时，不沿用图表目录去重时保留的另一个节点。公共动态剖面接口仍保留原图表的来源去重语义。

悬浮采样保留来源 NoData，网格外不会出现人为的深度零；动态图表保留既有 chart-friendly 深度空值规则。时间轴可添加对比来源和移除末尾来源，最多 32 槽并保持从零开始连续编号；普通 Result 只有一槽。添加优先选择未使用的有效结果，全部已使用时回退最新的可读结果。两种操作复用带当前/预期上下文的 `map.resultSelection` 事务及原请求代次和取消逻辑，确认后才替换目录与渲染状态。

Result 与 MultiResult 的地图成员身份独立于显示勾选状态。取消勾选只移除 `IsChecked`，保留 `.vis` 中的 Type/Name 行及精确节点路径；这与原 `RasTreeBase.SerializeDefault` 的行为一致。否则只有默认显示选项的普通 Result 会从树中消失，无法重新勾选。每次显隐保存仍使用原修订检查与回执，重新加载后保留同一节点及勾选状态。

Result 单次绘图的全部预览帧共用 8 Mi 像素预算；来源或显示选项较多时按比例降低预览分辨率，保留世界范围、来源修订及原生时刻，不修改求解数据。对应 `map.resultRender` 工作进程终态单行上限为 64 MiB，可容纳该 RGBA base64 与元数据；进度行及其他操作仍采用 8 MiB 上限。该区分避免两个默认 1024 × 1024 结果帧被进度协议额度误拒绝。

`useMapResultFrames.ts` 与时间轴共用所选 Result、原生输出时刻及 terrain 身份。`useMapVelocityField.ts` 在当前帧确认后调用 `map.resultVelocityArrowsField`，校验 request/context/source/map、Result 修订、profile、terrain 与水平渲染模式；MultiResult 速度使用原 C# 的 slot 0 语义。计算仍在写入的 Result 不可读取。速度按 little-endian Float32 的 vx/vy/magnitude 三元组解码，NoData 和非有限值会跳过；`MapVelocityArrows.tsx` 仅作屏幕间距抽样与方向绘制，不修改求解值。镜头变化防抖，时刻和来源切换立即作废旧请求，停用时清除覆盖层。

工程弹窗可以上传完整目录或 ZIP。只有实际上传成功、且当前工程属于该工作区返回的工程清单时，才显示该工作区的 ZIP 下载链接；下载由服务端流式传输，前端不把完整大型归档缓存在内存。上传、打开与创建请求分别捕获工程会话和源快照，过期响应不能自动切换新会话。

边界条件时序的 `model.seriesTransform` 返回同一源修订的草稿快照，只有显式 `model.save` 才写入 HDF。带 `tableId` 的序列通过完整 `rows` 编辑，图表 `samples` 只是投影，不作为另一套修改同时提交；无 `tableId` 的 scalar 或 rating-curve 序列仍发送其真实 `samples`。新增行也必须通过数值和时间校验，删除行不得改写其他原始行。

切换页面保留编辑会话，切换工程或编辑对象前处理未保存草稿。界面缺少实际后端能力时显示能力状态，不生成模拟成功响应。浏览器选中的文件与实际写回的工程文件有不同的生命周期。

## 验证

分别验证 TypeScript、Web 构建、C# 完整编译、HTTP 操作、页面交互、Electron、真实工程 CPU 求解与文件格式往返。页面数量、空态和编译成功不能替代功能接通或工程模型验收。

GDB catalog、`.vis` 保存、树节点重读与渲染共享 `MapRasterLayerRendering.ResolveMapType`：float/double 单带为连续栅格，Byte/Int32/UInt32 单带为分类，三带且每带都是 Byte 为 RGB 影像；四带 Byte 在显式 R/G/B/Alpha 色彩解释下为 RGBA。OpenFileGDB 缺失各带色彩解释时，本导入器明确采用存储顺序 R/G/B/A 的影像映射策略，延续三带 Byte 的 RGB 约定；这不表示任意四带科学数据天然具有 RGBA 语义。其他驱动的未知或 CMYK 四带布局明确拒绝。Int16/UInt16 单带是本项目新增的分类兼容；两带及非 Byte 多带仍明确不支持。`WithOpenedDataset` 在回调前后核对工程、地图、精确子连接和完整 GDB manifest；连续符号编辑仍只接受浮点类型。

整数 GDAL reader 保留完整原始有符号/无符号数值，并拒绝无法由目标整数类型精确表示的 NoData 元数据。分类层从来源继承 NoData，显式非零 NoData 不会再把有效 0 误画为透明；缺失 NoData 时保持原有 0 sentinel。UInt32 使用 `UInt32DiscreteRasterRenderLayer` 和 `ColorMap<uint>` 保留完整类别键、NoData 和悬浮信息；不再经 Int32 饱和转换。Byte/Int16/UInt16 仍精确扩宽到 Int32。默认颜色由类别确定生成，用户指定的调色表键保持原值；有限色域的重复色不等于类别标识合并。

地图树与渲染帧的 ID 空间不同：`vis-map-*-tree-*` 定位保存的树行，`vis-map-*-node-*` 定位栅格读取帧。`PersistedRasterLayerImage.treeNodeID` 显式保留对应关系。图层树双击和属性面板“缩放到图层”使用当前会话已确认帧的 extent；不改写来源坐标、`.ras` 或 `.vis`。工程没有目标投影时，混合源坐标的全部适应视图可能很空；用户可缩放到单层检查实际数据。

四带影像保持原始 RGBA 顺序，逐像元 alpha 与图层 Opacity 以整数舍入相乘。重着色结果先还原为 straight ARGB 再进入 `PlotRequest.Splat`，Host 导出帧只预乘一次，前端编码 PNG 前再还原。四带 NoData 分别保留：显式 alpha NoData 匹配，或 R/G/B 都声明 NoData 且同时匹配时透明；无声明时有效黑色不被错误遮掉。原单参数 `ImageRenderLayer` 构造器保留，避免破坏现有托管程序集的调用签名。
