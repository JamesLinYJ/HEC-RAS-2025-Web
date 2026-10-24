using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.Vectors.Meshes;

namespace Geospatial.Vectors.Rasterizers;

public class FlatMeshMap : MeshMap
{
	public List<FlatCellMap> Cells;

	public FlatMeshMap(Mesh mesh, int meshIdx, MeshPropertyTables tables, List<FlatCellMap> cellList)
		: base(mesh, meshIdx, tables, null, null)
	{
		Cells = cellList;
	}

	public override List<int> UniqueCells()
	{
		return Cells.Select((FlatCellMap cell) => cell.Index).ToList();
	}

	public void ComputeCellValuesFlat(Memory<float> cellInputValues, Memory<float> output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		Parallel.ForEach(Cells, (FlatCellMap flatCell) =>
		{
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				float minElev = minElevs[flatCell.Index];
				cellWaterSurfaces = clip.CellWaterSurfaces;
				if (cellWaterSurfaces.Span[flatCell.Index] <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			flatCell.SimplePaint(cellInputValues.Span[flatCell.Index], output, clip.PixelDepths);
		});
	}

	protected void ComputeCellValuesFlat(Vector[] cellInputValues, Vector[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		Parallel.ForEach(Cells, (FlatCellMap flatCell) =>
		{
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				float minElev = minElevs[flatCell.Index];
				cellWaterSurfaces = clip.CellWaterSurfaces;
				if (cellWaterSurfaces.Span[flatCell.Index] <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			flatCell.SimplePaint(cellInputValues[flatCell.Index], output, clip.PixelDepths);
		});
	}

	private void ComputeFromFaceScalarValues(float[] faceScalarValues, float[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] scalar_FromFaces_SimpleAverage = HydraulicMesh.CellComputes.Scalar_FromFaces_SimpleAverage(faceScalarValues);
		ComputeCellValuesFlat(scalar_FromFaces_SimpleAverage, output, clip, renderOptions);
		throw new NotImplementedException();
	}

	protected void ComputeFromFacePerpValues(VectorMeshInputs inputs, VectorMeshOutputs outputs, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		Parallel.ForEach(Cells, (FlatCellMap flatCell) =>
		{
			int index = flatCell.Index;
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				cellWaterSurfaces = clip.CellWaterSurfaces;
				float cellWaterSurface = cellWaterSurfaces.Span[index];
				float minElev = minElevs[index];
				if (cellWaterSurface <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			Vector cellValue_FacePerpLeastSquares = inputs.ComputeCellValue_FacePerpLeastSquares(index);
			flatCell.SimplePaint(cellValue_FacePerpLeastSquares, outputs, clip.PixelDepths);
		});
	}
}
