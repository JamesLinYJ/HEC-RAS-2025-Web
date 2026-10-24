using System;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Mapping;

namespace Ras.Rendering;

public class StaticMeshResampler : IResample<float>, ISurface<float>
{
	private Mesh _mesh;

	private Geometry _geom;

	private float[] _valuePerCell;

	public float? NoDataValue => -9999f;

	public Extent ValidExtent => _mesh.Extent;

	public StaticMeshResampler(Geometry geom, float[] valuePerCell)
	{
		_geom = geom;
		_mesh = geom.FlowAreaLayer.Features[0].Mesh;
		_valuePerCell = valuePerCell;
	}

	public void UpdateCellValues(float[] valuePerCell)
	{
		_valuePerCell = valuePerCell;
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		GeometryMapPoints geometryMapPoints = new GeometryMapPoints(_geom, args.Raster);
		outData.Span.Fill(NoDataValue.Value);
		if (!geometryMapPoints.HasMapped2DCells)
		{
			return;
		}
		if (geometryMapPoints.SlopingMapped2DAreas.Count > 1)
		{
			throw new NotImplementedException();
		}
		foreach (SlopingMeshMap slopingMapped2DArea in geometryMapPoints.SlopingMapped2DAreas)
		{
			foreach (SlopingCellMap cell in slopingMapped2DArea.Cells)
			{
				int cellIndex = cell.CellIndex;
				if (cellIndex < _valuePerCell.Length)
				{
					float valuePerCell = _valuePerCell[cellIndex];
					cell.SimplePaint(valuePerCell, outData, Memory<float>.Empty);
				}
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		throw new NotImplementedException();
	}
}
