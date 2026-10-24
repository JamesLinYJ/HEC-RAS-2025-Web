export function completionStatus(state: string | null | undefined, language: 'zh' | 'en') {
  if (state === 'completed') return { text: language === 'zh' ? '已完成' : 'Completed', tone: 'success' as const };
  if (state === 'failed') return { text: language === 'zh' ? '计算失败' : 'Failed', tone: 'error' as const };
  if (state === 'running') return { text: language === 'zh' ? '正在计算' : 'Computing', tone: 'busy' as const };
  return undefined;
}
