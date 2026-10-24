import type { MapResultFramesController } from './useMapResultFrames';
import { ProfileStateSlider } from './ProfileStateSlider';

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
