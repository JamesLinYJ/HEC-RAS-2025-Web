using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Helpers;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class BandedRenderLayer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider, IRasterRenderLayer, IMutableProfileStateProvider
{
	private static readonly ILogger _logger = LogHelper.GetLogger("BandedRenderLayer");

	private IMultiBandResampler<float> _resampler;

	private RasterSymbology _rasterSymb = RasterSymbology.DefaultRasterSymbology();

	private Versioned<ProfileState> _profileState = new Versioned<ProfileState>();

	private readonly object _pfNameLock = new object();

	private List<string> _pfNames;

	private WeakHandler _profileStateProfileChangedHandler;

	public RasterSymbology RasterSymbology
	{
		get
		{
			return _rasterSymb;
		}
		set
		{
			_rasterSymb = value;
			IncrementRenderState();
		}
	}

	public ProfileState ProfileState => _profileState.Get((_resampler as ISpatialVersion)?.VersionXY ?? (-1), BuildProfileState);

	IProfileState IProfileStateProvider.ProfileState => ProfileState;

	public int ProfileCount => ProfileState.ProfileCount;

	public List<string> ProfileNames => LazyThreadsafe<List<string>>.AllocateOnce(ref _pfNames, _pfNameLock, this, (BandedRenderLayer bandedRenderLayer) =>
	{
		List<string> stringList = new List<string>(bandedRenderLayer.ProfileCount);
		for (int i = 0; i < bandedRenderLayer.ProfileCount; i++)
		{
			stringList.Add(bandedRenderLayer.ProfileSerializer(i));
		}
		return stringList;
	});

	public Func<int, string> ProfileSerializer { get; set; } = (int i) => i.ToString();

	public override Extent ValidExtent => _resampler.ValidExtent;

	public event IMutableProfileStateProvider.ProfileStateChangedEvent ProfileStateChanged;

	public BandedRenderLayer(IMultiBandResampler<float> resampler)
	{
		if (resampler == null)
		{
			throw new ArgumentNullException("resampler");
		}
		_resampler = resampler;
		RequestSeparateRenderFrame = true;
	}

	public ProfileState BuildProfileState()
	{
		ProfileState profileState = new ProfileState(() =>
		{
			int profileCount = _resampler.ProfileCount;
			List<string> stringList = new List<string>(profileCount);
			for (int i = 0; i < profileCount; i++)
			{
				stringList.Add(ProfileSerializer(i));
			}
			return stringList;
		});
		_profileStateProfileChangedHandler?.Unsubscribe();
		_profileStateProfileChangedHandler = WeakHandler.TrySubscribe(profileState, "ProfileChanged", this, (BandedRenderLayer ths, int idx) =>
		{
			ths.ProfileState_ProfileChanged(idx);
		});
		ProfileStateChanged?.Invoke(this, profileState);
		return profileState;
	}

	private void ProfileState_ProfileChanged(int newPfIdx)
	{
		IncrementRenderState();
	}

	public void ReplaceResampler(IMultiBandResampler<float> resampler)
	{
		if (resampler == null)
		{
			throw new ArgumentNullException("resampler");
		}
		_resampler = resampler;
		lock (_pfNameLock)
		{
			_pfNames = null;
		}
		IncrementRenderState();
	}

	public void SetGrayscaleLinearSymbology(double min, double max)
	{
		RasterSymbology rasterSymbology = new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Grayscale, new double[2] { min, max }));
		rasterSymbology.HillshadeProperties.IsEnabled = true;
		rasterSymbology.HillshadeProperties.ZFactor = 3f;
		RasterSymbology = rasterSymbology;
	}

	public RasterSymbology GetDefaultTerrainHistoSymbology(int profileIndex)
	{
		Histogram histogram = Histogram.ComputeTiled(_resampler.AsSingleProfile(profileIndex), 1048576, 256);
		if (histogram.IsNull())
		{
			_logger.LogWarning("SetDefaultTerrainHistoSymbology {LayerName} Null histogram - possibly bad resample data (NaN/Inf/NoData)", "Layer Name Redacted");
			return new RasterSymbology();
		}
		if (histogram.IsSingleValue())
		{
			return new RasterSymbology();
		}
		return new RasterSymbology(new InterpolatedSurfaceFill(ColorPalettes.Terrain, histogram))
		{
			HillshadeProperties = 
			{
				IsEnabled = true,
				ZFactor = 3f
			}
		};
	}

	public void SetDefaultTerrainHistoSymbology(int profileIndex)
	{
		RasterSymbology = GetDefaultTerrainHistoSymbology(profileIndex);
	}

	public void AddDefaultMapHover(string name)
	{
		AddMapHover((PlotRequest plotReq, HitTestArgs hta) =>
		{
			float samplePoint = _resampler.SamplePoint(hta.QueryPoint, ProfileState.CurrentProfileIndex);
			return new MapHover(hta.QueryPoint, name + ": " + samplePoint);
		});
	}

	protected override bool RenderInternal(PlotRequest plot, ref object cache)
	{
		if (_resampler == null || plot == null || RasterSymbology == null || !ProfileState.IsRegularProfile)
		{
			return false;
		}
		IMemoryOwner<float> data = SharedMemoryPool<float>.Rent(plot.RasterQuery.CellCount, out var mem);
		if (cache == null)
		{
			cache = new ResampleCache();
		}
		_resampler.Resample(RasterSymbology.AsResampleArgs(plot.RasterQuery), ProfileState.CurrentProfileIndex, mem, cache as ResampleCache);
		plot.SetRasterData_TransferOwnership(this, data);
		if (plot.Reporter.CancelRequested)
		{
			return false;
		}
		RasterRenderHelper.Render(mem, plot, RasterSymbology, _resampler.NoDataValue.GetValueOrDefault());
		return true;
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + ((_resampler is ISpatialVersion spatialVersion) ? spatialVersion.VersionXY : 0);
	}
}
