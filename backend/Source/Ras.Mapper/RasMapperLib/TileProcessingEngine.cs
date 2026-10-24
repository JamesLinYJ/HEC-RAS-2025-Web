using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TiffAssist;
using Utility;
using Utility.Progress;

namespace RasMapperLib;

public class TileProcessingEngine
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__13_002D0
	{
		public int maxDepth;

		public int tilesCompleted;

		public TileProcessingEngine tileProcessingEngine;

		public _Closure_0024__13_002D0(_Closure_0024__13_002D0 other)
		{
			if (other != null)
			{
				maxDepth = other.maxDepth;
				tilesCompleted = other.tilesCompleted;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			checked
			{
				while (!tileProcessingEngine._stopRequested || tileProcessingEngine._tileBag.Count != 0)
				{
					if (tileProcessingEngine._tileBag.Count == 0)
					{
						Thread.Sleep(50);
						continue;
					}
					maxDepth = Math.Max(maxDepth, tileProcessingEngine._tileBag.Count);
					tilesCompleted++;
					tileProcessingEngine._reporter.ReportProgress((int)Math.Round((double)(100 * (tilesCompleted + tileProcessingEngine._noDataTileBag.Count)) / (double)tileProcessingEngine._numTiles));
					Tuple<int, float[]> result = null;
					if (tileProcessingEngine._tileBag.TryTake(out result))
					{
						tileProcessingEngine._tw.WriteTile(result.Item1, result.Item2);
						tileProcessingEngine._bufferWritten(result.Item2);
					}
				}
				Stopwatch stopwatch = Stopwatch.StartNew();
				_ = tileProcessingEngine._noDataTileBag.Count;
				while (tileProcessingEngine._noDataTileBag.Count > 0)
				{
					int result2 = 0;
					if (tileProcessingEngine._noDataTileBag.TryTake(out result2))
					{
						tileProcessingEngine._tw.WriteRawTile(result2, tileProcessingEngine._noDataBytes);
					}
				}
				stopwatch.Stop();
			}
		}
	}

	private ConcurrentBag<Tuple<int, float[]>> _tileBag;

	private ConcurrentBag<int> _noDataTileBag;

	private FloatTiffWriter _tw;

	private byte[] _noDataBytes;

	private bool _stopRequested;

	private int _numTiles;

	private Task _tileProcessingTask;

	private ProgressReporter _reporter;

	private Action<float[]> _bufferWritten;

	public int QueuedTileCount => _tileBag.Count;

	public TileProcessingEngine(FloatTiffWriter tw, Action<float[]> bufferFlushed, ProgressReporter prog)
	{
		_tileBag = new ConcurrentBag<Tuple<int, float[]>>();
		_noDataTileBag = new ConcurrentBag<int>();
		_stopRequested = false;
		_tw = tw;
		_numTiles = _tw.GetNumTiles();
		_reporter = prog;
		_bufferWritten = bufferFlushed;
		int tileWidth = _tw.GetTileWidth();
		checked
		{
			float[] floatValues = new float[tileWidth * tileWidth - 1 + 1];
			Buffers.ArraySetAllVB(floatValues, -9999f);
			_noDataBytes = new byte[floatValues.Count() * 4 - 1 + 1];
			Buffer.BlockCopy(floatValues, 0, _noDataBytes, 0, _noDataBytes.Count());
			_noDataBytes = _tw.GetRawTile(0, _noDataBytes);
		}
	}

	public void Start()
	{
		_tileProcessingTask = GetTileProcessingEngine();
		_tileProcessingTask.Start();
	}

	private Task GetTileProcessingEngine()
	{
		_Closure_0024__13_002D0 capturedLocals = default;
		_Closure_0024__13_002D0 capturedLocals2 = new _Closure_0024__13_002D0(capturedLocals);
		capturedLocals2.tileProcessingEngine = this;
		capturedLocals2.maxDepth = 0;
		capturedLocals2.tilesCompleted = 0;
		return new Task(checked([SpecialName] () =>
		{
			while (!capturedLocals2.tileProcessingEngine._stopRequested || capturedLocals2.tileProcessingEngine._tileBag.Count != 0)
			{
				if (capturedLocals2.tileProcessingEngine._tileBag.Count == 0)
				{
					Thread.Sleep(50);
				}
				else
				{
					capturedLocals2.maxDepth = Math.Max(capturedLocals2.maxDepth, capturedLocals2.tileProcessingEngine._tileBag.Count);
					capturedLocals2.tilesCompleted++;
					capturedLocals2.tileProcessingEngine._reporter.ReportProgress((int)Math.Round((double)(100 * (capturedLocals2.tilesCompleted + capturedLocals2.tileProcessingEngine._noDataTileBag.Count)) / (double)capturedLocals2.tileProcessingEngine._numTiles));
					Tuple<int, float[]> result = null;
					if (capturedLocals2.tileProcessingEngine._tileBag.TryTake(out result))
					{
						capturedLocals2.tileProcessingEngine._tw.WriteTile(result.Item1, result.Item2);
						capturedLocals2.tileProcessingEngine._bufferWritten(result.Item2);
					}
				}
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			_ = capturedLocals2.tileProcessingEngine._noDataTileBag.Count;
			while (capturedLocals2.tileProcessingEngine._noDataTileBag.Count > 0)
			{
				int result2 = 0;
				if (capturedLocals2.tileProcessingEngine._noDataTileBag.TryTake(out result2))
				{
					capturedLocals2.tileProcessingEngine._tw.WriteRawTile(result2, capturedLocals2.tileProcessingEngine._noDataBytes);
				}
			}
			stopwatch.Stop();
		}));
	}

	public void QueueTileForWriting(int n, float[] values)
	{
		_tileBag.Add(new Tuple<int, float[]>(n, values));
	}

	public void QueueNoDataTile(int n)
	{
		_noDataTileBag.Add(n);
	}

	public void WaitForExit()
	{
		_stopRequested = true;
		_tileProcessingTask.Wait();
	}

	public void WaitUntilBuffersDeplete(int tileCount = 50)
	{
		while (QueuedTileCount > tileCount)
		{
			Thread.Sleep(50);
		}
	}
}
