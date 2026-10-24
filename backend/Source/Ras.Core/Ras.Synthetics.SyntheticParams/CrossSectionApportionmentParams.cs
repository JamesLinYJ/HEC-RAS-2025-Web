using System;
using Geospatial.Vectors;

namespace Ras.Synthetics.SyntheticParams;

public class CrossSectionApportionmentParams : InOutPlanarParams
{
	public readonly float[] Stations;

	public readonly float[] BedElevations;

	public CrossSectionApportionmentParams(float[] stations, float[] bedElevations)
	{
		Stations = stations;
		BedElevations = bedElevations;
	}

	public override float TerrainElevation(Point pt)
	{
		return SimplePlanarTerrain(pt) + InterpolateBedElevation((float)pt.Y);
	}

	private float InterpolateBedElevation(float y)
	{
		int stationLength = Stations.Length;
		if (y <= Stations[0])
		{
			return BedElevations[0];
		}
		if (y >= Stations[stationLength - 1])
		{
			return BedElevations[stationLength - 1];
		}
		int binarySearch = Array.BinarySearch(Stations, y);
		if (binarySearch >= 0)
		{
			return BedElevations[binarySearch];
		}
		int binarySearchTerm = ~binarySearch - 1;
		float interpolationFraction = (y - Stations[binarySearchTerm]) / (Stations[binarySearchTerm + 1] - Stations[binarySearchTerm]);
		return BedElevations[binarySearchTerm] + interpolationFraction * (BedElevations[binarySearchTerm + 1] - BedElevations[binarySearchTerm]);
	}
}
