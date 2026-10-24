using System.Collections.Generic;
using System.Linq;
using Utility.Extensions;

namespace Geospatial.Rasters;

public class PyramidDefinition
{
	private List<RasterDefinition> _pyramidLevels;

	public List<RasterDefinition> PyramidLevels => _pyramidLevels;

	public RasterDefinition BasePyramidLevel
	{
		get
		{
			if (_pyramidLevels.IsNullOrEmpty())
			{
				return RasterDefinition.NullRaster;
			}
			return _pyramidLevels[0];
		}
	}

	public PyramidDefinition(IList<RasterDefinition> pyramidLevels)
	{
		_pyramidLevels = pyramidLevels.ToList();
	}

	public bool CanResampleEfficiently(int maxPxCount)
	{
		return PyramidLevels.Last().CellCountLong <= maxPxCount;
	}
}
