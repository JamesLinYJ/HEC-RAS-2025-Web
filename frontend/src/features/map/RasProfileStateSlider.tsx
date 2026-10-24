import type { MapResultFramesController } from './useMapResultFrames';
import { ProfileStateSlider } from './ProfileStateSlider';
import type { RasterCalculatorSessionLayer } from '../../shared/rasterCalculatorSession';

interface RasProfileStateSliderProps {
  language: 'zh' | 'en';
  mapID: string;
  controller: MapResultFramesController;
}

/** Couples the active map's profile selector with the result-frame playback controls. */
export function RasProfileStateSlider({ language, mapID, controller }: RasProfileStateSliderProps) {
  if (controller.steps.length === 0) return null;

  const zh = language === 'zh';
  const selectedStepIndex = controller.steps.findIndex((step) => step.id === controller.selectedProfileIndex);
  const rangeValue = selectedStepIndex >= 0 ? selectedStepIndex : 0;
  const currentStep = controller.steps[rangeValue] ?? null;
  const isPlaying = controller.isPlaying;
  const playLabel = zh ? '播放' : 'Play';
  const pauseLabel = zh ? '暂停' : 'Pause';

  return <div className="map-time-track" aria-label={zh ? '当前时刻' : 'Selected time'} style={{
    height: 26, display: 'flex', alignItems: 'center', gap: 10,
    background: 'transparent',
  }}>
    <ProfileStateSlider
      language={language}
      mapID={mapID}
      controller={controller}
      rangeValue={rangeValue}
      currentStep={currentStep}
    />
    {(controller.canPlay || isPlaying) && (
      <button
        type="button"
        className="map-time-play"
        data-testid={`map-result-${isPlaying ? 'pause' : 'play'}-${mapID}`}
        aria-label={isPlaying ? pauseLabel : playLabel}
        onClick={isPlaying ? controller.pause : controller.play}
        disabled={!isPlaying && controller.status === 'loading'}
        style={{ flex: '0 0 auto' }}
      >
        {isPlaying ? pauseLabel : playLabel}
      </button>
    )}
  </div>;
}

interface CalculatorProfileStateSliderProps {
  language: 'zh' | 'en';
  layer: RasterCalculatorSessionLayer;
  onSelectProfile: (profileIndex: number) => void;
}

/** Session-only counterpart of the native slider: profile state belongs to the selected calculator context. */
export function CalculatorProfileStateSlider({ language, layer, onSelectProfile }: CalculatorProfileStateSliderProps) {
  if (!layer.isDynamic || layer.profiles.length === 0) return null;
  const selectedIndex = Math.min(Math.max(layer.profileIndex, 0), layer.profiles.length - 1);
  const current = layer.profiles[selectedIndex] ?? null;
  const stepsID = `map-calculator-profile-steps-${layer.id}`;
  const choose = (index: number) => {
    if (index >= 0 && index < layer.profiles.length) onSelectProfile(layer.profiles[index].index);
  };
  const zh = language === 'zh';
  return <div className="map-time-track map-calculator-profile-track" aria-label={zh ? '计算器图层时刻状态' : 'Calculator layer profile state'}
    data-testid={`map-calculator-profile-track-${layer.id}`}>
    <button type="button" aria-label={zh ? '首个时刻' : 'First profile'} title={zh ? '首个时刻' : 'First profile'}
      data-testid={`map-calculator-profile-first-${layer.id}`} onClick={() => choose(0)} disabled={selectedIndex <= 0}>⏮</button>
    <button type="button" aria-label={zh ? '上一个时刻' : 'Previous profile'} title={zh ? '上一个时刻' : 'Previous profile'}
      data-testid={`map-calculator-profile-previous-${layer.id}`} onClick={() => choose(selectedIndex - 1)} disabled={selectedIndex <= 0}>◀</button>
    <input type="range" min={0} max={Math.max(layer.profiles.length - 1, 0)} step={1} value={selectedIndex}
      aria-label={zh ? '计算器图层时刻' : 'Calculator layer profile'}
      data-testid={`map-calculator-profile-${layer.id}`} data-profile-index={current?.index ?? ''}
      list={stepsID} onChange={event => choose(Number(event.currentTarget.value))} />
    <button type="button" aria-label={zh ? '下一个时刻' : 'Next profile'} title={zh ? '下一个时刻' : 'Next profile'}
      data-testid={`map-calculator-profile-next-${layer.id}`} onClick={() => choose(selectedIndex + 1)} disabled={selectedIndex >= layer.profiles.length - 1}>▶</button>
    <button type="button" aria-label={zh ? '最后一个时刻' : 'Last profile'} title={zh ? '最后一个时刻' : 'Last profile'}
      data-testid={`map-calculator-profile-last-${layer.id}`} onClick={() => choose(layer.profiles.length - 1)} disabled={selectedIndex >= layer.profiles.length - 1}>⏭</button>
    <datalist id={stepsID}>{layer.profiles.map(profile => <option key={profile.index} value={profile.index} label={profile.time} />)}</datalist>
    <span className="map-time-current" data-testid={`map-calculator-profile-time-${layer.id}`}>{current?.time ?? (zh ? '无可用时刻' : 'No profiles')}</span>
  </div>;
}
