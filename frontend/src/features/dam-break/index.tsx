import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties, ReactNode } from 'react';
import { useFloodCompute } from './useFloodCompute';
import { sampleResult } from './results';
import type { SceneControls } from './FloodScene';
import { FloodIcon } from './FloodIcon';
import { clamp, DEFAULT_SCENARIO, minuteLabel, parseScenario, rainAt, SCENARIO_KEY, scenarioPresets, scenarioStage, sceneDuration, type FloodScenario } from './scenario';
import './floodStudio.css';

const Scene = lazy(() => import('./FloodScene').then(module => ({ default: module.FloodScene })));

function initialScenario(): FloodScenario {
  try { return parseScenario(JSON.parse(localStorage.getItem(SCENARIO_KEY) ?? 'null')) ?? { ...DEFAULT_SCENARIO }; }
  catch { return { ...DEFAULT_SCENARIO }; }
}

function Parameter({ label, value, unit, min, max, step = 1, onChange }: { label: string; value: number; unit: string; min: number; max: number; step?: number; onChange: (value: number) => void }) {
  return <label className="flood-parameter"><span className="flood-parameter-heading"><span>{label}</span><span className="flood-parameter-value"><output>{value}</output><span>{unit}</span></span></span>
    <input type="range" aria-label={label} min={min} max={max} step={step} value={value} style={{ '--range-progress': `${(value-min)/(max-min)*100}%` } as CSSProperties} onChange={e=>onChange(Number(e.target.value))} />
  </label>;
}

function RainChart({ scenario, minute }: { scenario: FloodScenario; minute: number }) {
  const bars = useMemo(()=>Array.from({length:32},(_,i)=>rainAt(scenario,(i+.5)/32*scenario.stormDuration)),[scenario]);
  return <div className="flood-rain-chart">
    <div className="flood-chart-caption"><span>降雨过程线</span><span>mm/h</span></div>
    <svg viewBox="0 0 240 86" role="img" aria-label={`雨型，峰值 ${scenario.rainfall} 毫米每小时，历时 ${scenario.stormDuration} 分钟`}>
      {[18,42,66].map(y=><line key={y} x1="0" y1={y} x2="240" y2={y} stroke="var(--flood-line)" strokeDasharray="3 4"/>)}
      {bars.map((v,i)=><rect key={i} x={i*7.5+1} y={75-v/120*64} width="5" height={Math.max(.5,v/120*64)} rx="1.5" fill={i/32*scenario.stormDuration<minute?'#527f98':'#c5d3da'}/>)}
      <line x1={clamp(minute/scenario.stormDuration)*240} x2={clamp(minute/scenario.stormDuration)*240} y1="8" y2="79" stroke="#b65a3b" strokeWidth="1.5"/>
    </svg><div className="flood-chart-scale"><span>00:00</span><span>{Math.floor(scenario.stormDuration/120).toString().padStart(2,'0')}:{(scenario.stormDuration/2%60).toString().padStart(2,'0')}</span><span>{minuteLabel(scenario.stormDuration).slice(0,5)}</span></div>
  </div>;
}

function SectionTitle({ number, icon, title, children }: { number: string; icon: Parameters<typeof FloodIcon>[0]['name']; title: string; children?: ReactNode }) {
  return <div className="flood-section-heading"><span className="flood-section-number">{number}</span><h3>{title}</h3><span className="flood-section-icon"><FloodIcon name={icon} size={17}/></span>{children}</div>;
}

export default function FloodStudio() {
  const [scenario,setScenario]=useState(initialScenario);
  const [minute,setMinute]=useState(0);
  const [reducedMotion,setReducedMotion]=useState(()=>window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const [playing,setPlaying]=useState(false);
  const [speed,setSpeed]=useState(120);
  const [showRain,setShowRain]=useState(true);
  const [showContours,setShowContours]=useState(true);
  const [showBuildings,setShowBuildings]=useState(true);
  const [tab,setTab]=useState<'rain'|'breach'>('rain');
  const [dialog,setDialog]=useState<'about'|null>(null);
  const [saveState,setSaveState]=useState('场景草稿保存在此浏览器');
  const [notice,setNotice]=useState('');
  const sceneControls=useRef<SceneControls|null>(null);
  const stage=useRef<HTMLDivElement>(null);
  const modal=useRef<HTMLDialogElement>(null);
  const timeRef=useRef(minute);timeRef.current=minute;
  const compute=useFloodCompute(scenario);
  const modelScenario=compute.result?.scenario??scenario;
  const duration=compute.result?compute.result.durationSeconds/60:sceneDuration(scenario);
  const currentStage=scenarioStage(modelScenario,minute);
  const sample=useMemo(()=>compute.result?sampleResult(compute.result,minute*60):null,[compute.result,minute]);
  const run=async(resume=false)=>{setPlaying(false);if(await compute.run(resume)){timeRef.current=0;setMinute(0);setPlaying(!reducedMotion);}};


  useEffect(()=>{const title=document.title;document.title='洪水推演 · 降雨与溃坝';return()=>{document.title=title;};},[]);

  useEffect(()=>{const query=window.matchMedia('(prefers-reduced-motion: reduce)');const update=()=>{setReducedMotion(query.matches);if(query.matches)setPlaying(false);};query.addEventListener('change',update);return()=>query.removeEventListener('change',update);},[]);
  useEffect(()=>{
    try{localStorage.setItem(SCENARIO_KEY,JSON.stringify(scenario));setSaveState('场景草稿保存在此浏览器');}
    catch{setSaveState('浏览器存储不可用，可导出场景参数');}
    setMinute(value=>Math.min(value,duration));
  },[scenario,duration]);
  useEffect(()=>{
    if(!playing)return;
    let id=0,last=0,published=0;
    const tick=(now:number)=>{
      if(document.hidden){last=0;id=requestAnimationFrame(tick);return;}
      const dt=last?Math.min((now-last)/1000,.1):0;last=now;
      timeRef.current=Math.min(duration,timeRef.current+dt*speed/60);
      if(now-published>65||timeRef.current>=duration){setMinute(timeRef.current);published=now;}
      if(timeRef.current>=duration){setPlaying(false);return;}
      id=requestAnimationFrame(tick);
    };
    id=requestAnimationFrame(tick);return()=>cancelAnimationFrame(id);
  },[playing,speed,duration]);
  useEffect(()=>{if(dialog)modal.current?.showModal();else modal.current?.close();},[dialog]);
  const update=<K extends keyof FloodScenario>(key:K,value:FloodScenario[K])=>{setPlaying(false);setScenario(s=>({...s,[key]:value}));};
  const scrub=(value:number)=>{setPlaying(false);timeRef.current=value;setMinute(value);};
  const togglePlay=()=>{if(!compute.result)return;if(minute>=duration){timeRef.current=0;setMinute(0);}setPlaying(v=>!v);};
  const toggleRef=useRef(togglePlay);toggleRef.current=togglePlay;
  useEffect(()=>{const key=(event:KeyboardEvent)=>{if(event.code!=='Space'||event.repeat||event.altKey||event.metaKey||event.ctrlKey)return;const target=event.target as HTMLElement;if(target.closest('button,input,select,textarea,dialog,[contenteditable="true"]'))return;event.preventDefault();toggleRef.current();};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key);},[]);
  const preset=(key:keyof typeof scenarioPresets)=>{setScenario({...scenarioPresets[key]});scrub(0);};
  const exportScenario=()=>{
    const blob=new Blob([JSON.stringify({schema:'flood-studio/scenario/v2',scenario},null,2)],{type:'application/json'});
    const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='降雨溃坝场景.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);setNotice('已导出场景参数');
  };
  const exportResult=()=>{
    if(!compute.result)return;
    const {decoded: _decoded,...result}=compute.result;
    const url=URL.createObjectURL(new Blob([JSON.stringify(result)],{type:'application/json'}));
    const a=document.createElement('a');a.href=url;a.download=`洪水推演-${result.runId.slice(0,8)}.json`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  };
  const fullscreen=async()=>{try{if(document.fullscreenElement)await document.exitFullscreen();else await stage.current?.requestFullscreen();}catch{setNotice('当前浏览器不支持全屏，可以使用视角工具放大场景。');}};

  return <div className="flood-studio" data-testid="flood-studio">
    <header className="flood-header">
      <div className="flood-brand"><span className="flood-logo"><FloodIcon name="water" size={24}/></span><span><strong>洪水推演</strong><small>FLOOD STUDIO</small></span></div>
      <div className="flood-header-divider"/>
      <span className="flood-breadcrumb">降雨与溃坝</span>
      <div className="flood-header-actions"><span className={`flood-server ${compute.connected?'is-connected':''}`}><i/>{compute.connected?'已连接':'未连接'}</span>
        {!compute.connected?<button className="flood-button flood-outline" onClick={()=>void compute.connect()}>重新连接</button>:compute.busy?<><span role="status">{compute.canceling?'正在取消…':`推演中 ${Math.round(compute.progress*100)}%`}</span><button className="flood-button flood-outline" disabled={compute.canceling} onClick={compute.cancel}>取消</button></>:<button className="flood-button flood-primary" disabled={compute.uncertain&&!compute.recovery} onClick={()=>void run(Boolean(compute.recovery))}><FloodIcon name="play" size={16}/>{compute.recovery?'继续查看':compute.result?'重新推演':'开始推演'}</button>}
      </div>
    </header>

    <div className="flood-workspace">
      <aside className="flood-sidebar" aria-label="场景参数">
        <fieldset className="flood-parameters" disabled={compute.busy||compute.uncertain}><div className="flood-sidebar-intro"><h2>场景设置</h2></div>
        <label className="flood-preset-label">场景预设<select aria-label="场景预设" value={Object.entries(scenarioPresets).find(([,s])=>s.name===scenario.name)?.[0]??'storm'} onChange={e=>preset(e.target.value as keyof typeof scenarioPresets)}><option value="storm">暴雨 · 渐进溃坝</option><option value="sudden">突发 · 快速溃坝</option><option value="slow">持续降雨 · 缓慢发展</option></select></label>
        <div className="flood-mobile-tabs" role="tablist" aria-label="参数类别"><button role="tab" aria-selected={tab==='rain'} onClick={()=>setTab('rain')}>降雨过程</button><button role="tab" aria-selected={tab==='breach'} onClick={()=>setTab('breach')}>溃坝条件</button></div>
        <section className={`flood-input-section ${tab==='rain'?'is-mobile-active':''}`}>
          <SectionTitle number="01" icon="rain" title="降雨过程"/>
          <RainChart scenario={scenario} minute={minute}/>
          <div className="flood-segmented" aria-label="雨峰位置">{(['front','middle','back'] as const).map((p,i)=><button key={p} aria-pressed={scenario.rainPattern===p} onClick={()=>update('rainPattern',p)}>{['前峰型','中峰型','后峰型'][i]}</button>)}</div>
          <Parameter label="峰值雨强" value={scenario.rainfall} unit="mm/h" min={0} max={120} step={5} onChange={v=>update('rainfall',v)}/>
          <Parameter label="降雨历时" value={scenario.stormDuration} unit="min" min={60} max={240} step={15} onChange={v=>update('stormDuration',v)}/>
        </section>
        <section className={`flood-input-section ${tab==='breach'?'is-mobile-active':''}`}>
          <SectionTitle number="02" icon="dam" title="溃坝条件"/>
          <div className="flood-breach-diagram" aria-label="溃口形态示意"><svg viewBox="0 0 238 55" role="img" aria-label="逐步下切的矩形溃口"><path d="M0 48 24 14H94V43H144V14H214L238 48" fill="#e8e6e0" stroke="#aba79f" strokeWidth="1.2"/><path d="M94 14V43H144V14" fill="none" stroke="#b85d3d" strokeWidth="1.8" strokeDasharray="3 3"/><path d="M93 51h52m-52-3v6m52-6v6" stroke="#868a86"/><text x="119" y="12" textAnchor="middle" fill="#a95337" fontSize="10">渐进下切</text></svg></div>
          <Parameter label="开始溃决" value={scenario.breachStart} unit="min" min={5} max={120} step={5} onChange={v=>update('breachStart',v)}/>
          <Parameter label="溃口形成历时" value={scenario.breachDuration} unit="min" min={1} max={60} onChange={v=>update('breachDuration',v)}/>
          <Parameter label="最终溃口宽度" value={scenario.breachWidth} unit="m" min={15} max={100} step={5} onChange={v=>update('breachWidth',v)}/>
          <Parameter label="初始库水深" value={scenario.reservoirLevel} unit="m" min={8} max={26} onChange={v=>update('reservoirLevel',v)}/>
        </section>
        <div className="flood-sidebar-footer"><span><FloodIcon name="check" size={14}/>{saveState}</span><button className="flood-button flood-export" onClick={exportScenario}><FloodIcon name="download" size={15}/>导出场景参数</button></div>
        </fieldset>
      </aside>

      <main className="flood-main">
        <div className="flood-page-heading"><div><div className="flood-section-kicker"><span/>{scenario.name}</div><h1>降雨与溃坝</h1></div><button className="flood-info-button" aria-label="操作帮助" onClick={()=>setDialog('about')}><FloodIcon name="info" size={19}/><span>操作帮助</span></button></div>
        <div className="flood-stage" ref={stage}>
          <Suspense fallback={<div className="flood-scene-message">正在加载三维引擎…</div>}><Scene geometry={compute.geometry} sample={sample} minute={minute} playing={playing} active={true} showRain={showRain} showContours={showContours} showBuildings={showBuildings} reducedMotion={reducedMotion} controls={sceneControls}/></Suspense>
          <div className="flood-scene-title"><span className="flood-scene-badge"><span/>河谷场景</span><span>{compute.result?compute.stale?'参数已修改 · 待重新推演':'推演结果':'待推演'}</span></div>
          <div className="flood-layer-tools" aria-label="场景图层">
            <button aria-pressed={showRain} onClick={()=>setShowRain(v=>!v)} title="显示降雨"><FloodIcon name="rain" size={18}/><span>降雨</span></button>
            <button aria-pressed={showContours} onClick={()=>setShowContours(v=>!v)} title="显示等高线"><FloodIcon name="terrain" size={18}/><span>等高线</span></button>
            <button aria-pressed={showBuildings} onClick={()=>setShowBuildings(v=>!v)} title="显示居民点"><FloodIcon name="cube" size={18}/><span>居民点</span></button>
          </div>
          <div className="flood-scene-annotation"><span className="flood-scene-stage-number">0{currentStage.index+1}</span><div><strong>{compute.result?currentStage.title:compute.geometry?'河谷就绪':'准备河谷'}</strong><span>{compute.result?minuteLabel(minute):'设置参数，开始推演'}</span></div></div>
          <div className="flood-orbit-tools" aria-label="视角控制">
            <button aria-label="放大场景" onClick={()=>sceneControls.current?.zoom(1)}><FloodIcon name="plus" size={18}/></button><button aria-label="缩小场景" onClick={()=>sceneControls.current?.zoom(-1)}><FloodIcon name="minus" size={18}/></button><hr/>
            <button aria-label="俯视场景" title="俯视" onClick={()=>sceneControls.current?.overhead()}><FloodIcon name="layers" size={18}/></button><button aria-label="重置视角" title="重置视角" onClick={()=>sceneControls.current?.reset()}><FloodIcon name="reset" size={17}/></button><button aria-label="全屏场景" onClick={()=>void fullscreen()}><FloodIcon name="expand" size={17}/></button>
          </div>
          <div className="flood-scene-footer"><span><FloodIcon name="cube" size={14}/>拖动旋转 · 滚轮缩放</span><span className="flood-water-legend"><i/>水体<span>浅</span><b/><span>深</span></span><span className="flood-north">N <span>↑</span></span></div>
        </div>

        <section className="flood-playback" aria-label="场景播放控制">
          <div className="flood-playback-top"><div className="flood-play-controls"><button className="flood-play" disabled={!compute.result} aria-label={playing?'暂停动画':'播放动画'} onClick={togglePlay}><FloodIcon name={playing?'pause':'play'} size={20}/></button><button className="flood-rewind" disabled={!compute.result} aria-label="回到起点" onClick={()=>scrub(0)}><FloodIcon name="reset" size={17}/></button><div className="flood-time"><output aria-label="场景时刻">{minuteLabel(minute)}</output><span>/ {minuteLabel(duration)}</span></div></div><div className="flood-playback-label"><span className={playing?'is-playing':''}/>{!compute.result?'等待推演':playing?'正在回放':'已暂停'}<select aria-label="播放速度" value={speed} onChange={e=>setSpeed(Number(e.target.value))}>{[30,60,120,240].map(v=><option value={v} key={v}>{v}×</option>)}</select></div></div>
          <div className="flood-timeline"><div className="flood-timeline-background"><span style={{width:`${modelScenario.stormDuration/duration*100}%`}}/><i title="溃决开始" style={{left:`${modelScenario.breachStart/duration*100}%`}}/></div><input disabled={!compute.result} aria-label="场景时间轴" type="range" min="0" max={duration} step="0.1" value={minute} style={{'--range-progress':`${minute/duration*100}%`} as CSSProperties} onChange={e=>scrub(Number(e.target.value))}/></div>
          <div className="flood-time-markers"><span>00:00</span><button disabled={!compute.result} onClick={()=>scrub(modelScenario.breachStart)}><i/>溃决 {minuteLabel(modelScenario.breachStart).slice(0,5)}</button><span>{minuteLabel(duration/2).slice(0,5)}</span><span>{minuteLabel(duration).slice(0,5)}</span></div>
        </section>

        <div className="flood-insights">
          <article><span className="flood-metric-icon"><FloodIcon name="rain"/></span><div><span>当前雨强</span><strong>{sample?.rainRate.toFixed(1)??'—'}<small>mm/h</small></strong></div></article>
          <article><span className="flood-metric-icon"><FloodIcon name="water"/></span><div><span>累计降雨</span><strong>{sample?.rainTotal.toFixed(1)??'—'}<small>mm</small></strong></div></article>
          <article className="flood-stage-metric"><span className="flood-metric-icon amber"><FloodIcon name="dam"/></span><div><span>过坝流量</span><strong>{sample?.discharge.toFixed(1)??'—'}<small>m³/s</small></strong></div></article>
        </div>
        <footer className="flood-page-footer"><span>空格 播放 / 暂停 · 拖动时间轴回看</span><span>{sample?`淹水面积 ${(sample.wetArea/1e6).toFixed(3)} km² · 最大水深 ${sample.maxDepth.toFixed(2)} m`:''}</span>{compute.result&&<button onClick={exportResult}><FloodIcon name="download" size={14}/>导出结果</button>}</footer>
        {reducedMotion&&<p className="flood-motion-note">已减少动态效果</p>}
        {compute.message&&<p className="flood-compute-status" role="status">{compute.message}</p>}
        {notice&&<div className="flood-notice" role="status">{notice}<button aria-label="关闭提示" onClick={()=>setNotice('')}><FloodIcon name="close" size={14}/></button></div>}
      </main>
    </div>

    <dialog ref={modal} className="flood-dialog" onCancel={()=>setDialog(null)} onClose={()=>setDialog(null)}>
      <div className="flood-dialog-heading"><h2>操作帮助</h2><button aria-label="关闭" onClick={()=>setDialog(null)}><FloodIcon name="close"/></button></div>
      <p>设置降雨与溃坝条件，点击“开始推演”。完成后，拖动时间轴回看水位和流量变化。</p><p>拖动场景旋转视角，滚轮缩放。空格播放或暂停。</p><button className="flood-button flood-primary" onClick={()=>setDialog(null)}>继续探索</button>
    </dialog>
  </div>;
}
