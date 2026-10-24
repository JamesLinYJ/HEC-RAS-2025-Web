using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Systems.Rendering;
using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing.Handlers;

internal sealed class ReverseDirectionPreviewCache
{
	private Polyline _source;

	private long _version = long.MinValue;

	private double _cellSize;

	private Polyline _reversed;

	private List<Segment> _chevrons;

	public void GetOrCompute(Polyline source, RasterDefinition window, out Polyline reversed, out List<Segment> chevrons)
	{
		long versionXY = source.VersionXY;
		double cellSize = window.CellSize;
		if (_source == source && _version == versionXY && _cellSize == cellSize)
		{
			reversed = _reversed;
			chevrons = _chevrons;
			return;
		}
		_source = source;
		_version = versionXY;
		_cellSize = cellSize;
		_reversed = source.Clone();
		_reversed.Reverse();
		_chevrons = RepeatedSymbolRenderer.GetChevronsAlongLine(window, _reversed, (int _, Polyline pl) => pl, new CompactList<FeatureSegmentSubset>(new FeatureSegmentSubset(0, _reversed.SegmentCount - 1)
		{
			Fid = 0
		}));
		reversed = _reversed;
		chevrons = _chevrons;
	}
}
