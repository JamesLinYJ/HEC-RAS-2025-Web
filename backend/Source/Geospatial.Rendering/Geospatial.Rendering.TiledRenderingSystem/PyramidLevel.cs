using System;
using Geospatial.Rasters;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class PyramidLevel : IEquatable<PyramidLevel>
{
	private static PyramidLevel _dynamic;

	public readonly RasterDefinition WorldRaster;

	public readonly RasterDefinition WorldTileRaster;

	public static PyramidLevel Dynamic
	{
		get
		{
			if (_dynamic == null)
			{
				_dynamic = new PyramidLevel(int.MinValue, RasterDefinition.NullRaster, RasterDefinition.NullRaster);
			}
			return _dynamic;
		}
	}

	public int Level { get; private set; }

	public double CellSize => WorldRaster.CellSize;

	public double TileCellSize => WorldTileRaster.CellSize;

	public bool IsInvalidated { get; private set; }

	public PyramidLevel(int level, RasterDefinition worldRaster, RasterDefinition tileRaster)
	{
		Level = level;
		WorldRaster = worldRaster;
		WorldTileRaster = tileRaster;
	}

	public void Shift(int shift)
	{
		Level += shift;
		if (Level < 0 || Level > 22)
		{
			Invalidate();
		}
	}

	private void Invalidate()
	{
		IsInvalidated = true;
	}

	public bool IsValid()
	{
		if (!IsInvalidated && !WorldRaster.IsNull() && WorldRaster.IsValid())
		{
			return WorldTileRaster.IsValid();
		}
		return false;
	}

	public bool Equals(PyramidLevel other)
	{
		if (other != null && Level == other.Level)
		{
			return CellSize == other.CellSize;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Level, CellSize, WorldRaster, WorldTileRaster);
	}

	public static implicit operator int(PyramidLevel level)
	{
		return level.Level;
	}

	public override string ToString()
	{
		return Level + ": " + CellSize.ToString("#.####") + " (" + TileCellSize.ToString("#.####") + ")";
	}
}
