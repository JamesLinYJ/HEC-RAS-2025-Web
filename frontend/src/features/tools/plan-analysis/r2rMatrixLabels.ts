const propertyLabels: Record<string, string> = {
  SolverType: '求解器', TimeStepping: '时间推进方式', EquationSet: '方程组', ComputeTimestep: '计算时间步长',
  BaseOutputInterval: '基本输出间隔', TimeWindowMode: '时间窗模式', InternalStartTime: '计划开始时间',
  InternalComputeDurationSeconds: '计划计算时长（秒）', CoreCount: 'CPU 核心数', ParallelMethod: '并行方式',
  UseGPUAdaptive: 'GPU 自适应', LtsLevels: '局部时间步层数', TimestepSafetyFactor: '时间步安全系数',
  UseMinAdvectiveTimeStep: '启用最小对流时间步', MinAdvectiveTimeStep: '最小对流时间步',
  MaxPercentWetAdvTimeStep: '对流时间步最大湿单元比例', UseFBF: '启用 FBF', Theta: 'Theta 权重',
  ImplicitAdaptiveTimestepping: '隐式自适应时间步', MaxCourant: '最大 Courant 数', ImplicitFactor: '隐式系数',
  UsePartialGrid: '启用局部网格', ActiveThresholdPartGrid: '局部网格激活阈值', BufferCountPartGrid: '局部网格缓冲单元数',
  ElevationThresholdPartGrid: '局部网格高程阈值', MaxInnerIterations: '最大内迭代次数', VolumeTolerance: '体积容差',
  VolumeMaxTolerance: '体积最大容差', VolumeRMSTolerance: '体积 RMS 容差', MaxOuterIterations: '最大外迭代次数',
  WaterSurfaceTolerance: '水面容差', WaterSurfaceMaxTolerance: '水面最大容差', WaterSurfaceRMSTolerance: '水面 RMS 容差',
  UseAdvection: '启用对流', AdvectionMethod: '对流方式', AdvectionImplicitFactor: '对流隐式系数',
  UseDiffusion: '启用扩散', TurbulenceModel: '紊流模型', BaseEddyViscosity: '基本涡黏性',
  LongMixCoeff: '纵向混合系数', TransMixCoeff: '横向混合系数', HorizMixCoeff: '水平混合系数', Latitude: '纬度',
  DevOptions: '开发选项', ExtraOutput: '额外输出', LinearFrictionCoefficient: '线性摩擦系数',
  SensitivityRun: '敏感性计算', ShortID: '简短标识',
};
export function r2rPropertyLabel(name: string): string { return propertyLabels[name] ?? `参数 ${name}`; }
export const r2rRuleLabel = (name: string): string => ({
  'GPU + Implicit': '排除 GPU 与隐式推进', 'GPU + DWE': '排除 GPU 与扩散波方程',
  'DWE + Explicit': '排除扩散波与显式推进', 'Non-positive timestep': '排除非正时间步',
  'Output interval mismatch': '检查输出间隔整倍数',
} as Record<string, string>)[name] ?? `组合规则 ${name}`;

const reasons: Record<string, string> = {
  'The GPU processor only supports explicit time-stepping.': 'GPU 仅支持显式时间推进。',
  'The GPU processor does not support the Diffusion Wave Equation (DWE).': 'GPU 不支持扩散波方程（DWE）。',
  'The Diffusion Wave Equation (DWE) only supports implicit time-stepping.': '扩散波方程（DWE）仅支持隐式时间推进。',
  'The compute timestep must be greater than zero.': '计算时间步长必须大于零。',
  'The output interval must be a whole multiple of the compute timestep.': '输出间隔必须是计算时间步长的整数倍。',
  'explicit solver only': '仅显式求解器使用', 'implicit solver only': '仅隐式求解器使用',
  'the minimum advective timestep is switched off': '最小对流时间步已关闭',
  'adaptive implicit time-stepping is switched off': '隐式自适应时间步已关闭',
  'the partial grid is switched off': '局部网格已关闭',
  'the water surface loop is disabled (no outer iterations)': '水面迭代已关闭（无外迭代）',
  'advection is switched off': '对流已关闭', 'turbulence is switched off': '紊流已关闭',
  'the mixing coefficients belong to the Smagorinsky closure': '混合系数仅属于 Smagorinsky 闭合模型',
  'the GPU solver does not use CPU cores': 'GPU 求解器不使用 CPU 核心数',
  'the GPU solver does not use CPU threads': 'GPU 求解器不使用 CPU 线程参数', 'GPU solver only': '仅 GPU 求解器使用',
  'the time window is inherited from the boundary conditions': '时间窗继承自边界条件',
  'File already exists': '文件已存在，按当前设置跳过', Cancelled: '已取消',
};
export function r2rReason(value: string | null): string {
  if (!value) return '';
  if (reasons[value]) return reasons[value];
  const sameRun = /^Same run as (.+) \(differs only in ignored values\)$/.exec(value);
  return sameRun ? `与 ${sameRun[1]} 相同，仅不适用的参数值不同，已合并。` : `服务端说明：${value}`;
}
export const r2rStatusLabel = (value: string): string => ({
  Written: '已写入', Skipped: '已跳过', Failed: '失败', Excluded: '已排除', Cancelled: '已取消',
  NotRun: '未执行', Writing: '正在写入',
} as Record<string, string>)[value] ?? value;
