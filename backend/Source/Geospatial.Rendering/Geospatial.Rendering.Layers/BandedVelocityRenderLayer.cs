using System.Buffers;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility;
using Utility.Interfaces;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public class BandedVelocityRenderLayer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider, IRasterRenderLayer, IMutableProfileStateProvider
{
	private ITimeSeriesVelocityResampler<float> _resampler;

	private RasterSymbology _symb = RasterSymbology.DefaultRasterSymbology();

	private ProfileState _prevProfileState;

	private Versioned<ProfileState> _profileState = new Versioned<ProfileState>();

	public ITimeSeriesVelocityResampler<float> Resampler => _resampler;

	public RasterSymbology RasterSymbology
	{
		get
		{
			return _symb;
		}
		set
		{
			_symb = value;
			IncrementRenderState();
		}
	}

	public IProfileState ProfileState => _profileState.Get((_resampler as ISpatialVersion)?.VersionXY ?? (-1), BuildProfileState);

	public override Extent ValidExtent => _resampler.ValidExtent;

	public event IMutableProfileStateProvider.ProfileStateChangedEvent ProfileStateChanged;

	public BandedVelocityRenderLayer(ITimeSeriesVelocityResampler<float> resampler)
	{
		_resampler = resampler;
		RequestSeparateRenderFrame = true;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		if (_resampler == null || request == null || RasterSymbology == null || !ProfileState.IsRegularProfile)
		{
			return false;
		}
		int cellCount = request.RasterQuery.CellCount;
		IMemoryOwner<float> data = SharedMemoryPool<float>.Rent(cellCount, out var mem);
		IMemoryOwner<float> data2 = SharedMemoryPool<float>.Rent(cellCount, out var mem2);
		IMemoryOwner<float> data3 = SharedMemoryPool<float>.Rent(cellCount, out var mem3);
		_resampler.ResampleVxVy(RasterSymbology.AsResampleArgs(request.RasterQuery), ProfileState.CurrentProfileIndex, mem, mem2, mem3);
		request.SetRasterData_TransferOwnership(this, data);
		request.SetRasterData_TransferOwnership(this, data2);
		request.SetRasterData_TransferOwnership(this, data3);
		if (request.Reporter.CancelRequested)
		{
			return false;
		}
		RasterRenderHelper.Render(mem3, request, RasterSymbology, _resampler.NoDataValue.GetValueOrDefault());
		return true;
	}

	private ProfileState BuildProfileState()
	{
		ProfileState profileState = new ProfileState(() =>
		{
			int profileCount = _resampler.ProfileCount;
			List<string> stringList = new List<string>(profileCount);
			for (int i = 0; i < profileCount; i++)
			{
				stringList.Add(_resampler.Times[i].ToString("yyyy-MM-dd HH:mm:ss"));
			}
			return stringList;
		});
		profileState.ProfileChanged += OnProfileChanged;
		if (_prevProfileState != null)
		{
			_prevProfileState.ProfileChanged -= OnProfileChanged;
		}
		_prevProfileState = profileState;
		ProfileStateChanged?.Invoke(this, profileState);
		return profileState;
	}

	private void OnProfileChanged(int idx)
	{
		IncrementRenderState();
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + ((_resampler is ISpatialVersion spatialVersion) ? spatialVersion.VersionXY : 0);
	}
}
