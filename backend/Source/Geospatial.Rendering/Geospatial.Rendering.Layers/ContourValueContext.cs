using System;
using Geospatial.Rasters;

namespace Geospatial.Rendering.Layers;

public readonly struct ContourValueContext
{
	public readonly RasterDefinition ExtendedRaster;

	public readonly ReadOnlyMemory<float> ExtendedValues;

	public readonly ContourLayerStats LayerStats;

	public bool HasGutter
	{
		get
		{
			if (!ExtendedRaster.IsNull() && ExtendedRaster.CellCount > 0 && !ExtendedValues.IsEmpty)
			{
				return ExtendedValues.Length >= ExtendedRaster.CellCount;
			}
			return false;
		}
	}

	public ContourValueContext(RasterDefinition extendedRaster, ReadOnlyMemory<float> extendedValues, ContourLayerStats layerStats)
	{
		ExtendedRaster = extendedRaster;
		ExtendedValues = extendedValues;
		LayerStats = layerStats;
	}
}
