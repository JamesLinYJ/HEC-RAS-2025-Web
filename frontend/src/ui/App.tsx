import { lazy, Suspense, useEffect, useState } from 'react';
const Workbench = lazy(() => import('../interface/RasMainWindow').then(m => ({ default: m.RasMainWindow })));
const FloodStudio = lazy(() => import('../features/dam-break'));
export function App() {
  const [flood, setFlood] = useState(() => window.location.hash === '#/dam-break');
  useEffect(() => { const update = () => setFlood(window.location.hash === '#/dam-break'); window.addEventListener('hashchange', update); return () => window.removeEventListener('hashchange', update); }, []);
  return <Suspense fallback={<div role="status">正在加载…</div>}>{flood ? <FloodStudio /> : <Workbench />}</Suspense>;
}
