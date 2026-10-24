namespace Geospatial.Rasters;

public struct SamplingParams
{
	public static readonly SamplingParams Default = new SamplingParams(InterpolationMode.Nearest, initBufferToNDV: true);

	public static readonly SamplingParams WithoutBufferClear = new SamplingParams(InterpolationMode.Nearest, initBufferToNDV: false);

	public InterpolationMode Mode { get; }

	public float ResampleCellSizeRatio { get; }

	public bool InitializeBufferToNoData { get; }

	public bool InterpolationModeRequiresNeighboringCells
	{
		get
		{
			if (Mode != InterpolationMode.AreaAveraged && Mode != InterpolationMode.Hybrid && Mode != InterpolationMode.Linear)
			{
				return Mode == InterpolationMode.Smooth;
			}
			return true;
		}
	}

	public bool IsNull()
	{
		return ResampleCellSizeRatio == 0f;
	}

	public SamplingParams(InterpolationMode mode, bool initBufferToNDV, float resampleCSRatio = 1f)
	{
		Mode = mode;
		InitializeBufferToNoData = initBufferToNDV;
		ResampleCellSizeRatio = resampleCSRatio;
	}

	public SamplingParams CloneWithoutBufferClear()
	{
		return new SamplingParams(Mode, initBufferToNDV: false, ResampleCellSizeRatio);
	}

	public SamplingParams CloneWithBufferClear()
	{
		return new SamplingParams(Mode, initBufferToNDV: true, ResampleCellSizeRatio);
	}
}
