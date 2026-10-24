using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Rasters;
using Utility;

namespace Geospatial.Rendering.TiledRenderingSystem;

[DebuggerDisplay("{DebuggerDisplayString()}")]
public struct TileKey : IEquatable<TileKey>
{
	public class Comparer : IEqualityComparer<TileKey>
	{
		public bool Equals(TileKey x, TileKey y)
		{
			if (x.Level == y.Level)
			{
				return x.TileIndex.Equals(y.TileIndex);
			}
			return false;
		}

		public int GetHashCode(TileKey obj)
		{
			return obj.GetHashCode();
		}
	}

	public static bool LogAllocations { get; set; }

	private static AllocationTracker _tracker;

	public int Level { get; }

	public Pixel TileIndex { get; }

	public TileKey(int level, Pixel tileIndex)
	{
		Level = level;
		TileIndex = tileIndex;
		Initialize();
	}

	public TileKey(RenderTask task)
		: this(task.PyramidLevel, task.TileIndex)
	{
	}

	public void Initialize()
	{
		if (LogAllocations)
		{
			if (_tracker == null)
			{
				_tracker = new AllocationTracker();
			}
			_tracker.LogAllocation();
		}
	}

	public bool Equals(TileKey other)
	{
		if (Level == other.Level)
		{
			return TileIndex.Equals(other.TileIndex);
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj is TileKey other)
		{
			return Equals(other);
		}
		return false;
	}

	private string DebuggerDisplayString()
	{
		return $"Level: {Level}, TileIndex: {TileIndex}";
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Level, TileIndex);
	}
}
