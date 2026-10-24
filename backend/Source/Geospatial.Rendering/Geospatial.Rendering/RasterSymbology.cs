using System;
using System.ComponentModel;
using System.Threading;
using Geospatial.Rasters;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Geospatial.Rendering;

public class RasterSymbology : IMutableSpatialVersion, ISpatialVersion
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RasterSymbology");

	private long _version;

	public const bool DefaultIsSurfaceFillEnabled = true;

	public const byte DefaultAlpha = byte.MaxValue;

	public const float DefaultSmoothingRadius = 15f;

	public const InterpolationMode DefaultInterpolationMode = InterpolationMode.Linear;

	public const float DefaultRenderRatio = 1f;

	private InterpolatedSurfaceFill _sfill;

	private HillshadeProperties _hillshadeProps;

	private ContourProperties _contourProps;

	private float _smoothingRadius = 15f;

	private float _resampleCellSizeRatio = 1f;

	private InterpolationMode _interpMode = InterpolationMode.Linear;

	private bool _isSurfaceFillEnabled = true;

	private VersionTracker _surfaceFillVersionTracker;

	private VersionTracker _contourPropertiesVersionTracker;

	private byte _alpha = byte.MaxValue;

	public long VersionXY => _version + _surfaceFillVersionTracker.GetVersion(SurfaceFill) + _contourPropertiesVersionTracker.GetVersion(ContourProperties);

	public bool IsSurfaceFillEnabled
	{
		get
		{
			return _isSurfaceFillEnabled;
		}
		set
		{
			if (_isSurfaceFillEnabled != value)
			{
				_isSurfaceFillEnabled = value;
				IncrementVersion();
			}
		}
	}

	public InterpolatedSurfaceFill SurfaceFill
	{
		get
		{
			return _sfill;
		}
		set
		{
			if (_sfill != value)
			{
				_surfaceFillVersionTracker.Assign(ref _sfill, value);
			}
		}
	}

	public InterpolationMode InterpolationMode
	{
		get
		{
			return _interpMode;
		}
		set
		{
			if (_interpMode != value)
			{
				_interpMode = value;
				IncrementVersion();
			}
		}
	}

	public float ResampleCellSizeRatio
	{
		get
		{
			return _resampleCellSizeRatio;
		}
		set
		{
			if (_resampleCellSizeRatio != value && !(value < 0f))
			{
				_resampleCellSizeRatio = value;
				IncrementVersion();
			}
		}
	}

	public HillshadeProperties HillshadeProperties
	{
		get
		{
			return _hillshadeProps;
		}
		set
		{
			if (_hillshadeProps != value)
			{
				if (HillshadeProperties != null)
				{
					HillshadeProperties.PropertyChanged -= SubPropertyChanged;
				}
				_hillshadeProps = value;
				HillshadeProperties.PropertyChanged += SubPropertyChanged;
				IncrementVersion();
			}
		}
	}

	public ContourProperties ContourProperties
	{
		get
		{
			return _contourProps;
		}
		set
		{
			if (_contourProps != value)
			{
				if (ContourProperties != null)
				{
					ContourProperties.PropertyChanged -= SubPropertyChanged;
				}
				_contourPropertiesVersionTracker.Assign(ref _contourProps, value);
				ContourProperties.PropertyChanged += SubPropertyChanged;
				IncrementVersion();
			}
		}
	}

	public float SmoothingRadius
	{
		get
		{
			return _smoothingRadius;
		}
		set
		{
			if (_smoothingRadius != value && !(value < 0f))
			{
				_smoothingRadius = value;
				IncrementVersion();
			}
		}
	}

	public byte Alpha
	{
		get
		{
			return _alpha;
		}
		set
		{
			if (_alpha != value)
			{
				_logger.LogTrace("Symbology.Alpha: {SourceValue} -> {TargetValue}", _alpha, value);
				_alpha = value;
				IncrementVersion();
			}
		}
	}

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	public static RasterSymbology DefaultRasterSymbology()
	{
		return new RasterSymbology();
	}

	public RasterSymbology()
		: this(InterpolatedSurfaceFill.DefaultSurfaceFill())
	{
	}

	public RasterSymbology(InterpolatedSurfaceFill surfaceFill)
	{
		HillshadeProperties = new HillshadeProperties();
		ContourProperties = new ContourProperties();
		SurfaceFill = surfaceFill;
	}

	public void PaintSurface(Memory<float> values, Memory<int> colorBuf, float noDataValue)
	{
		if (SurfaceFill == null)
		{
			_logger.LogError("SurfaceFill is null for RasterSymbology.");
		}
		SurfaceFill?.Paint(values, colorBuf, noDataValue, Alpha);
	}

	private void SubPropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		IncrementVersion();
	}

	public ResampleArgs AsResampleArgs(RasterDefinition query, bool userShouldFlushData = true)
	{
		return new ResampleArgs(query, new SamplingParams(InterpolationMode, userShouldFlushData, ResampleCellSizeRatio));
	}

	public RasterSymbology Clone()
	{
		return new RasterSymbology(_sfill.Clone())
		{
			HillshadeProperties = HillshadeProperties.Clone(),
			ContourProperties = ContourProperties.Clone(),
			SmoothingRadius = SmoothingRadius,
			InterpolationMode = InterpolationMode,
			ResampleCellSizeRatio = ResampleCellSizeRatio,
			IsSurfaceFillEnabled = IsSurfaceFillEnabled,
			Alpha = Alpha
		};
	}

	public void CloneInto(RasterSymbology cloneIntoSymb)
	{
		cloneIntoSymb.SurfaceFill = _sfill.Clone();
		cloneIntoSymb.HillshadeProperties = HillshadeProperties.Clone();
		cloneIntoSymb.ContourProperties = ContourProperties.Clone();
		cloneIntoSymb.SmoothingRadius = SmoothingRadius;
		cloneIntoSymb.InterpolationMode = InterpolationMode;
		cloneIntoSymb.ResampleCellSizeRatio = ResampleCellSizeRatio;
		cloneIntoSymb.IsSurfaceFillEnabled = IsSurfaceFillEnabled;
		cloneIntoSymb.Alpha = Alpha;
	}

	public RasterSymbology CloneDesaturated(float fractionOfOriginalColor)
	{
		InterpolatedSurfaceFill surfaceFill = ((fractionOfOriginalColor != 1f) ? SurfaceFill.CloneDesaturated(fractionOfOriginalColor) : SurfaceFill.Clone());
		return new RasterSymbology(surfaceFill)
		{
			HillshadeProperties = HillshadeProperties.Clone(),
			ContourProperties = ContourProperties.Clone(),
			SmoothingRadius = SmoothingRadius,
			InterpolationMode = InterpolationMode,
			ResampleCellSizeRatio = ResampleCellSizeRatio,
			IsSurfaceFillEnabled = IsSurfaceFillEnabled,
			Alpha = Alpha
		};
	}

	protected void IncrementVersion()
	{
		Interlocked.Increment(ref _version);
	}

	public void ForceSetVersion(long version)
	{
		_version = version;
	}
}
