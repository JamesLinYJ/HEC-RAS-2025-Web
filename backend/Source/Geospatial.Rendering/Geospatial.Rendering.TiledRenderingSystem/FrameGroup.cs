using System;
using Geospatial.Rasters;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.Rendering.TiledRenderingSystem;

internal class FrameGroup : IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("FrameGroup");

	public static Pool<FrameGroup> SharedPool = new Pool<FrameGroup>(() => CreateUninitialized());

	public static readonly bool CacheIndividualFrames = false;

	public Frame[] Frames;

	public object[] CacheItems;

	public PlotRequest PlotRequest;

	private bool _isDisposed;

	public static FrameGroup CreateUninitialized()
	{
		return new FrameGroup
		{
			PlotRequest = PlotRequest.CreateUninitialized()
		};
	}

	public static FrameGroup Create(Frame[] frames, RasterDefinition rasterQuery, ProgressReporter reporter)
	{
		FrameGroup frameGroup = new FrameGroup();
		frameGroup.PlotRequest = PlotRequest.CreateUninitialized();
		frameGroup.Reset(frames, rasterQuery, reporter);
		return frameGroup;
	}

	public void Reset(Frame[] frames, RasterDefinition rasterQuery, ProgressReporter reporter)
	{
		_isDisposed = false;
		Frames = frames;
		if (CacheItems == null || frames.Length != CacheItems.Length)
		{
			CacheItems = new object[frames.Length];
		}
		Array.Fill(CacheItems, null);
		PlotRequest.Reset(rasterQuery);
		PlotRequest.Reporter = reporter;
	}

	public bool Plot(CommonData cd)
	{
		if (IsValid())
		{
			return PlotRequest.SplatCount > 0;
		}
		PlotRequest.Clear();
		PlotRequest.CommonData = cd;
		if (CacheIndividualFrames)
		{
			throw new NotImplementedException();
		}
		for (int i = 0; i < Frames.Length; i++)
		{
			Frame frame = Frames[i];
			object cache = CacheItems[i];
			try
			{
				frame.Layer.Render(PlotRequest, ref cache);
				if (PlotRequest.Reporter.CancelRequested)
				{
					frame.LastRenderState = -1L;
					break;
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Error painting frame.");
				continue;
			}
			frame.LastRenderState = frame.Layer.GetRenderState();
			CacheItems[i] = cache;
		}
		if (PlotRequest.Reporter.CancelRequested)
		{
			PlotRequest.Clear();
			return false;
		}
		return PlotRequest.SplatCount > 0;
	}

	public bool IsValid()
	{
		for (int i = 0; i < Frames.Length; i++)
		{
			if (!Frames[i].IsValid())
			{
				return false;
			}
		}
		return true;
	}

	~FrameGroup()
	{
		try
		{
			if (!_isDisposed)
			{
				Dispose(isDisposing: false);
			}
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		Dispose(isDisposing: true);
		GC.SuppressFinalize(this);
	}

	private void Dispose(bool isDisposing)
	{
		if (!_isDisposed)
		{
			_isDisposed = true;
			for (int i = 0; i < Frames.Length; i++)
			{
				Frames[i]?.Dispose();
				(CacheItems[i] as IDisposable)?.Dispose();
			}
			Frames = null;
			PlotRequest?.Dispose();
			PlotRequest = null;
		}
	}
}
