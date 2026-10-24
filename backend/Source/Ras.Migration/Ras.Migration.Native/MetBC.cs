using System.Collections.Generic;
using System.Diagnostics;
using Ras.Layers;

namespace Ras.Migration.Native;

[DebuggerDisplay("{Name}")]
public class MetBC
{
	public string Mode;

	public bool ExpandedView;

	public float Ratio = 1f;

	public float ConstValue;

	public string ConstUnits;

	public string PointInterpolation;

	public string GriddedSource;

	public string GriddedDssFile;

	public string GriddedDssPath;

	public string GriddedGdalFile;

	public string GriddedGdalGroup;

	public string GriddedGdalFolder;

	public string GriddedGdalFilter;

	public List<MetPointGauge> PointGauges = new List<MetPointGauge>();

	public string Name { get; set; }

	public bool TryParseFromMode(out SpatialDataType dt)
	{
		if (string.IsNullOrEmpty(Mode))
		{
			dt = SpatialDataType.Constant;
			return false;
		}
		if (Mode == "Constant")
		{
			dt = SpatialDataType.Constant;
			return true;
		}
		if (Mode == "Point")
		{
			dt = SpatialDataType.Point;
			return true;
		}
		if (Mode == "Gridded")
		{
			dt = SpatialDataType.Gridded;
			return true;
		}
		dt = SpatialDataType.Constant;
		return false;
	}

	public bool TryParseInterpolationFromMode(out MetInterpolationType type)
	{
		if (string.IsNullOrEmpty(Mode))
		{
			type = MetInterpolationType.Nearest;
			return false;
		}
		if (PointInterpolation == "Nearest")
		{
			type = MetInterpolationType.Nearest;
			return true;
		}
		if (PointInterpolation == "Bilinear" && Mode == "Gridded")
		{
			type = MetInterpolationType.Bilinear;
			return true;
		}
		if (PointInterpolation == "Inv Distance Sq" || (PointInterpolation == "Inverse Distance Squared" && Mode == "Point"))
		{
			type = MetInterpolationType.InverseDistanceSquared;
			return true;
		}
		if (PointInterpolation == "Inv Distance Sq (Restricted)" || (PointInterpolation == "Inverse Distance Squared Restricted" && Mode == "Point"))
		{
			type = MetInterpolationType.InverseDistanceSquaredRestricted;
			return true;
		}
		type = MetInterpolationType.Nearest;
		return false;
	}
}
