using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using H5Assist;
using Utility;
using Utility.Extensions;
using Utility.Hashing;

namespace Ras.Layers.Terrains;

public class StitchTerrainInput : TerrainFeatureInput<StitchFeature>, ITerrainInput, IDataHash, IDisposable, ISpatialVersion, INonPersistableTerrainInput
{
	private Versioned<IResample<float>> _resamplerVersioned = new Versioned<IResample<float>>();

	public string TypeName => "Stitch";

	protected override string HDF5Group => "Stitch";

	public IResample<float> GetResampler()
	{
		return _resamplerVersioned.Get(VersionXY, BuildResampler);
	}

	private IResample<float> BuildResampler()
	{
		Tin tin = GetTin();
		double[] tinElevations = GetTinElevations(tin);
		return new TinResampler(tin, tinElevations);
	}

	public bool IsValid(out string err)
	{
		err = "";
		return true;
	}

	public bool TryInit(Terrain terrain, H5Reader hr, string h5Path)
	{
		return true;
	}

	public void Save(H5Writer hw, string h5Path)
	{
	}

	public void Dispose()
	{
	}

	public StitchTerrainInput(Terrain terrain)
	{
		Terrain = terrain;
	}

	private Tin GetTin()
	{
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory();
		delaunayTinFactory.KeepDeletedTriangles = false;
		foreach (StitchFeature feature in Features)
		{
			List<Polygon> polygons = new List<Polygon>();
			foreach (Polygon ring in feature.Polygon.Rings)
			{
				Polygon polygon = ring.Clone();
				polygon.Densify(feature.SampleSpacing);
				polygons.Add(polygon);
			}
			ComplexPolygon.TryCreateComplexPolygon(polygons, out var poly, checkIntersections: false);
			delaunayTinFactory.AddPerimeter(poly);
			Extent extent = feature.Extent;
			for (double xComponent = extent.Left; xComponent <= extent.Right; xComponent += feature.SampleSpacing)
			{
				for (double yComponent = extent.Bottom; yComponent <= extent.Top; yComponent += feature.SampleSpacing)
				{
					Point pt = new Point(xComponent, yComponent);
					if (feature.Polygon.ContainsPoint_Slow(pt))
					{
						delaunayTinFactory.AddMassPoint(pt);
					}
				}
			}
		}
		return delaunayTinFactory.ComputeTIN();
	}

	private double[] GetTinElevations(Tin tin)
	{
		float[] floatValues = new float[tin.PointCount];
		float? noDataValue = Terrain.NoDataValue;
		if (noDataValue.HasValue)
		{
			Array.Fill(floatValues, noDataValue.Value);
		}
		List<EdgeDefinition> allPerimeterEdges = tin.GetAllPerimeterEdges();
		HashSet<int> intSet = new HashSet<int>();
		foreach (EdgeDefinition allPerimeterEdge in allPerimeterEdges)
		{
			intSet.Add(allPerimeterEdge.APointIndex);
			intSet.Add(allPerimeterEdge.BPointIndex);
		}
		int[] intSetArray = intSet.ToArray();
		Point[] points = new Point[intSetArray.Length];
		for (int i = 0; i < intSetArray.Length; i++)
		{
			int index = intSetArray[i];
			points[i] = tin.Points[index];
		}
		float[] floatValuesArray = new float[intSetArray.Length];
		int tillLayer = Terrain.Layers.IndexOf(this);
		Terrain.SamplePoints(tillLayer, points, floatValuesArray);
		for (int j = 0; j < intSetArray.Length; j++)
		{
			int ordinal = intSetArray[j];
			floatValues[ordinal] = floatValuesArray[j];
		}
		double[] tinElevations = new double[tin.PointCount];
		for (int k = 0; k < tin.PointCount; k++)
		{
			float tinElevations2 = floatValues[k];
			if (noDataValue.HasValue && tinElevations2 == noDataValue.Value)
			{
				tinElevations[k] = double.NaN;
			}
			else
			{
				tinElevations[k] = tinElevations2;
			}
		}
		new TinLaplacian(tin).SolveLaplacian(tinElevations);
		return tinElevations;
	}
}
