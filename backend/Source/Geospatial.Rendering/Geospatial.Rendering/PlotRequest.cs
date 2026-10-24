using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using Geospatial.Rasters;
using Geospatial.Rendering.Helpers;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems;
using Utility;
using Utility.Progress;

namespace Geospatial.Rendering;

public class PlotRequest : ColorBufferProviderBase, IDisposable
{
	public readonly object RasterDataLock = new object();

	public Dictionary<IRenderLayer, IMemoryOwner<float>> RasterData;

	public CommonData CommonData;

	private bool _isDisposed;

	private bool _isInitialized;

	public List<ILabelDefinition> Labels;

	public string DebugLabel;

	private static FileStream _fs;

	public bool IsDisposed => _isDisposed;

	public RasterDefinition RasterQuery { get; private set; }

	public ProgressReporter Reporter { get; set; } = ProgressReporter.None();

	public int SplatCount { get; private set; }

	protected override int ColorBufferSize => RasterQuery.CellCount;

	public static PlotRequest CreateUninitialized()
	{
		return new PlotRequest();
	}

	protected PlotRequest()
	{
	}

	public PlotRequest(RasterDefinition query)
	{
		if (query.IsNull())
		{
			throw new ArgumentNullException("query");
		}
		RasterQuery = query;
	}

	public PlotRequest(RasterDefinition query, Memory<int> loanedColorBuffer)
		: base(loanedColorBuffer)
	{
		if (query.IsNull())
		{
			throw new ArgumentNullException("query");
		}
		_isInitialized = true;
		RasterQuery = query;
	}

	public void Reset(RasterDefinition query)
	{
		if (_isInitialized && !_isDisposed)
		{
			throw new InvalidOperationException("Cannot re-use a PlotRequest that hasn't been disposed yet.");
		}
		_isDisposed = false;
		RasterQuery = query;
		Clear();
	}

	public void Clear()
	{
		ColorBufferARGB.Span.Fill(0);
		SplatCount = 0;
		DisposeInternalResources();
		CommonData?.Clear();
		lock (RasterDataLock)
		{
			RasterData?.Clear();
		}
		Labels?.Clear();
	}

	public void ClearAndSplat(Memory<int> bufARGB)
	{
		Buffers.ArrayCopyUnsafe(bufARGB, ColorBufferARGB);
		SplatCount = 1;
		DisposeInternalResources();
		CommonData?.Clear();
		lock (RasterDataLock)
		{
			RasterData?.Clear();
		}
		Labels?.Clear();
	}

	public void Splat(Memory<int> bufARGB)
	{
		Splat(bufARGB.Span);
	}

	public void Splat(Span<int> bufARGB)
	{
		if (SplatCount == 0)
		{
			Buffers.ArrayCopyUnsafe(bufARGB, ColorBufferARGB.Span);
		}
		else
		{
			ImageOperations.MergeColorBuffers(bufARGB, ColorBufferARGB.Span);
		}
		SplatCount++;
	}

	public void SplatPremultiplied(Span<int> bufARGB)
	{
		if (SplatCount == 0)
		{
			Buffers.ArrayCopyUnsafe(bufARGB, ColorBufferARGB.Span);
		}
		else
		{
			ImageOperations.MergeColorBuffersPremultiplied(bufARGB, ColorBufferARGB.Span);
		}
		SplatCount++;
	}

	public void SplatPremultipliedMultiply(Span<int> bufARGB)
	{
		if (SplatCount == 0)
		{
			Buffers.ArrayCopyUnsafe(bufARGB, ColorBufferARGB.Span);
		}
		else
		{
			ImageOperations.MergeColorBuffersPremultipliedMultiply(bufARGB, ColorBufferARGB.Span);
		}
		SplatCount++;
	}

	public void AlertDirectSplat()
	{
		SplatCount++;
	}

	public void Splat(byte[,] bufRGB)
	{
		ImageOperations.MergeColorBuffers(bufRGB, ColorBufferARGB);
		SplatCount++;
	}

	public void Splat(Memory<byte> r, Memory<byte> g, Memory<byte> b, byte inputAlpha = byte.MaxValue)
	{
		ImageOperations.MergeColorBuffers(r, g, b, ColorBufferARGB, inputAlpha);
		SplatCount++;
	}

	public void Splat(Memory<byte> brightness, byte inputAlpha = byte.MaxValue)
	{
		ImageOperations.MergeColorBuffers(brightness, ColorBufferARGB, inputAlpha);
		SplatCount++;
	}

	public void Splat(int[] bufARGB, PixelRange validDataRange)
	{
		if (bufARGB == null || bufARGB.Length != RasterQuery.CellCount)
		{
			throw new ArgumentException("bufARGB incorrectly sized.");
		}
		SplatCount++;
		throw new NotImplementedException();
	}

	public void SplatWindow(int[] bufARGB, PixelRange window)
	{
		if (bufARGB == null || bufARGB.Length != window.CellCount)
		{
			throw new ArgumentException("bufARGB incorrectly sized.");
		}
		SplatCount++;
		throw new NotImplementedException();
	}

	public void SetRasterData_TransferOwnership(IRenderLayer sender, IMemoryOwner<float> data)
	{
		lock (RasterDataLock)
		{
			if (RasterData == null)
			{
				RasterData = new Dictionary<IRenderLayer, IMemoryOwner<float>>();
			}
			RasterData[sender] = data;
		}
	}

	public void AddLabel(ILabelDefinition label)
	{
		if (Labels == null)
		{
			Labels = new List<ILabelDefinition>();
		}
		Labels.Add(label);
	}

	protected virtual void DisposeInternalResources()
	{
		Dictionary<IRenderLayer, IMemoryOwner<float>> rasterData = RasterData;
		if (rasterData == null)
		{
			return;
		}
		lock (RasterDataLock)
		{
			foreach (KeyValuePair<IRenderLayer, IMemoryOwner<float>> rasterData2 in rasterData)
			{
				rasterData2.Value.Dispose();
			}
			RasterData = null;
		}
	}

	private void DisposeInternalResources_NoLock()
	{
		Dictionary<IRenderLayer, IMemoryOwner<float>> rasterData = RasterData;
		if (rasterData == null)
		{
			return;
		}
		foreach (KeyValuePair<IRenderLayer, IMemoryOwner<float>> rasterData2 in rasterData)
		{
			rasterData2.Value.Dispose();
		}
		RasterData = null;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				DisposeInternalResources();
			}
			else
			{
				DisposeInternalResources_NoLock();
			}
			DisposeColorBuffer();
			_isDisposed = true;
		}
	}

	private void ValidateNotDisposed()
	{
		if (_isDisposed)
		{
			throw new ObjectDisposedException(GetType().FullName);
		}
	}

	~PlotRequest()
	{
		try
		{
			if (!_isDisposed)
			{
				Dispose(disposing: false);
			}
		}
		catch
		{
		}
	}

	[SupportedOSPlatform("windows")]
	public void DebugView()
	{
		_fs?.Dispose();
		string createIds = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
		_fs = new FileStream(createIds, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.DeleteOnClose);
		Bitmap bitmap = new Bitmap(RasterQuery.Cols, RasterQuery.Rows);
		long ordinal = 0L;
		long ordinal2 = 0L;
		long ordinal3 = 0L;
		long ordinal4 = 0L;
		Span<int> colorBufferARGB = ColorBufferARGB.Span;
		for (int i = 0; i < RasterQuery.Rows; i++)
		{
			for (int j = 0; j < RasterQuery.Cols; j++)
			{
				Color color = Color.FromArgb(colorBufferARGB[i * RasterQuery.Cols + j]);
				ordinal += color.R;
				ordinal2 += color.G;
				ordinal3 += color.B;
				ordinal4 += color.A;
				bitmap.SetPixel(j, i, color);
			}
		}
		if (ordinal != 0L || ordinal2 != 0L || ordinal3 != 0L || ordinal4 != 0L)
		{
			int cellCount = RasterQuery.CellCount;
			_ = (float)ordinal / (float)cellCount;
			_ = (float)ordinal2 / (float)cellCount;
			_ = (float)ordinal3 / (float)cellCount;
			_ = (float)ordinal4 / (float)cellCount;
			bitmap.Save(_fs, ImageFormat.Png);
			bitmap.Dispose();
			_fs.Flush(flushToDisk: true);
			Process.Start("explorer.exe", createIds);
		}
	}

	[SupportedOSPlatform("windows")]
	public void DebugSaveAs(string fn)
	{
		using FileStream stream = new FileStream(fn, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
		Bitmap bitmap = new Bitmap(RasterQuery.Cols, RasterQuery.Rows);
		long ordinal = 0L;
		long ordinal2 = 0L;
		long ordinal3 = 0L;
		long ordinal4 = 0L;
		Span<int> colorBufferARGB = ColorBufferARGB.Span;
		for (int i = 0; i < RasterQuery.Rows; i++)
		{
			for (int j = 0; j < RasterQuery.Cols; j++)
			{
				Color color = Color.FromArgb(colorBufferARGB[i * RasterQuery.Cols + j]);
				ordinal += color.R;
				ordinal2 += color.G;
				ordinal3 += color.B;
				ordinal4 += color.A;
				bitmap.SetPixel(j, i, color);
			}
		}
		if (ordinal != 0L || ordinal2 != 0L || ordinal3 != 0L || ordinal4 != 0L)
		{
			int cellCount = RasterQuery.CellCount;
			_ = (float)ordinal / (float)cellCount;
			_ = (float)ordinal2 / (float)cellCount;
			_ = (float)ordinal3 / (float)cellCount;
			_ = (float)ordinal4 / (float)cellCount;
			bitmap.Save(stream, ImageFormat.Png);
			bitmap.Dispose();
		}
	}

	[SupportedOSPlatform("windows")]
	public string DebugSaveAsBase64()
	{
		using MemoryStream memoryStream = new MemoryStream();
		Bitmap bitmap = new Bitmap(RasterQuery.Cols, RasterQuery.Rows);
		long ordinal = 0L;
		long ordinal2 = 0L;
		long ordinal3 = 0L;
		long ordinal4 = 0L;
		Span<int> colorBufferARGB = ColorBufferARGB.Span;
		for (int i = 0; i < RasterQuery.Rows; i++)
		{
			for (int j = 0; j < RasterQuery.Cols; j++)
			{
				Color color = Color.FromArgb(colorBufferARGB[i * RasterQuery.Cols + j]);
				ordinal += color.R;
				ordinal2 += color.G;
				ordinal3 += color.B;
				ordinal4 += color.A;
				bitmap.SetPixel(j, i, color);
			}
		}
		if (ordinal == 0L && ordinal2 == 0L && ordinal3 == 0L && ordinal4 == 0L)
		{
			bitmap.Dispose();
			return string.Empty;
		}
		int cellCount = RasterQuery.CellCount;
		_ = (float)ordinal / (float)cellCount;
		_ = (float)ordinal2 / (float)cellCount;
		_ = (float)ordinal3 / (float)cellCount;
		_ = (float)ordinal4 / (float)cellCount;
		bitmap.Save(memoryStream, ImageFormat.Png);
		bitmap.Dispose();
		return Convert.ToBase64String(memoryStream.ToArray());
	}
}
