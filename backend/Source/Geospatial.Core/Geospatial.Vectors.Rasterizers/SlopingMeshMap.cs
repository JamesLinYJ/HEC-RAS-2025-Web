using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.X86;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.Vectors.Meshes;
using Utility;
using Utility.Extensions;

namespace Geospatial.Vectors.Rasterizers;

public class SlopingMeshMap : MeshMap
{
	public List<SlopingCellMap> Cells;

	public SlopingMeshMap(Mesh mesh, int meshIndex, MeshPropertyTables tables, List<SlopingCellMap> d2MapCells, List<int> faces = null, List<int> facepoints = null)
		: base(mesh, meshIndex, tables, faces, facepoints)
	{
		Cells = d2MapCells;
	}

	public override List<int> UniqueCells()
	{
		return Cells.Select((SlopingCellMap cell) => cell.CellIndex).ToList();
	}

	public void ComputeFromCellValues_SimpleHydraulicConnectivity(float[] cellInputValues, float[] cellWaterSurfaces, float[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count != 0)
		{
			float[][] facePointValues = null;
			ComputeFacepointValues_SimpleHydraulicConnectivity(cellWaterSurfaces, cellInputValues, ref facePointValues);
			ComputeFromFacepointValues(facePointValues, output, clip, renderOptions);
		}
	}

	public void ComputeFromCellValues_NoHydraulicConnectivity(float[] cellInputValues, float[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count != 0)
		{
			float[] facePointValues = null;
			ComputeFacepointValues_NoHydraulicConnectivity(cellInputValues, ref facePointValues);
			ComputeFromFacepointValues(facePointValues, output, clip, renderOptions);
		}
	}

	public void ComputeCellValuesFlat(Memory<float> cellInputValues, Memory<float> output, MeshClip clip, RenderOptions renderOptions)
	{
		float[] minElevs = Tables.CellMinimumElevations;
		System.Threading.Tasks.Parallel.ForEach(Cells, (SlopingCellMap flatCell) =>
		{
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				float minElev = minElevs[flatCell.CellIndex];
				cellWaterSurfaces = clip.CellWaterSurfaces;
				if (cellWaterSurfaces.Span[flatCell.CellIndex] <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			flatCell.SimplePaint(cellInputValues.Span[flatCell.CellIndex], output, clip.PixelDepths);
		});
	}

	public void ComputeFromFacepointValues(float[] fpValues, float[] output, MeshClip clip, RenderOptions renderOptions)
	{
		float[] minElevs = Tables.CellMinimumElevations;
		System.Threading.Tasks.Parallel.ForEach(Cells, (SlopingCellMap mapCell) =>
		{
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				float minElev = minElevs[mapCell.CellIndex];
				cellWaterSurfaces = clip.CellWaterSurfaces;
				if (cellWaterSurfaces.Span[mapCell.CellIndex] <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			float[] localNodeValues = Mesh.GetLocalNodeValues(mapCell.CellIndex, fpValues);
			mapCell.PaintByFacepoints(localNodeValues, output, clip.PixelDepths);
		});
	}

	protected void ComputeFromFacepointValues(Vector[] fpValues, Vector[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		System.Threading.Tasks.Parallel.ForEach(Cells, (SlopingCellMap mapCell) =>
		{
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				float minElev = minElevs[mapCell.CellIndex];
				cellWaterSurfaces = clip.CellWaterSurfaces;
				if (cellWaterSurfaces.Span[mapCell.CellIndex] <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			Vector[] localNodeValues = Mesh.GetLocalNodeValues(mapCell.CellIndex, fpValues);
			mapCell.PaintByFacepoints(localNodeValues, output, clip.PixelDepths);
		});
	}

	public void ComputeFromFacepointValues(float[][] fpValuesPerCell, float[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		System.Threading.Tasks.Parallel.ForEach(Cells, (SlopingCellMap mapCell) =>
		{
			Memory<float> cellWaterSurfaces = clip.CellWaterSurfaces;
			if (!cellWaterSurfaces.IsEmpty)
			{
				float minElev = minElevs[mapCell.CellIndex];
				cellWaterSurfaces = clip.CellWaterSurfaces;
				if (cellWaterSurfaces.Span[mapCell.CellIndex] <= minElev + renderOptions.MinWSPlotTolerance)
				{
					return;
				}
			}
			float[] localNodeValues = Mesh.GetLocalNodeValues(mapCell.CellIndex, fpValuesPerCell);
			mapCell.PaintByFacepoints(localNodeValues, output, clip.PixelDepths);
		});
	}

	public void ComputeFacepointValues_SimpleHydraulicConnectivity(float[] cellWaterSurface, float[] cellVariable, ref float[][] facePointValues)
	{
		FaceValues[] faceVar = new FaceValues[Mesh.FaceCount];
		FaceValues[] faceWS = new FaceValues[Mesh.FaceCount];
		Buffers.EnsureArraySize(ref facePointValues, Mesh.FacePointCount);
		float[][] lFacepointValue = facePointValues;
		if (!UniqueFacePoints.IsNullOrEmpty() && !UniqueFaces.IsNullOrEmpty())
		{
			Utility.Parallel.SmartFor(UniqueFaces.Count, (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					int uniqueFaceIndex = UniqueFaces[i];
					faceWS[uniqueFaceIndex] = HydraulicMesh.FaceComputes.WSEL(uniqueFaceIndex, cellWaterSurface, Tables);
				}
			}, 100);
			Utility.Parallel.SmartFor(UniqueFaces.Count, (int start, int finish) =>
			{
				FaceValues faceValues2 = default;
				for (int i = start; i <= finish; i++)
				{
					int uniqueFaceIndex2 = UniqueFaces[i];
					Face face = Mesh.Faces[uniqueFaceIndex2];
					FaceValues faceValues = faceWS[uniqueFaceIndex2];
					if (faceValues.IsHydraulicallyConnected)
					{
						faceValues2.HydraulicConnection = HydraulicConnection.DownhillDeep;
						faceValues2.ValueB = (faceValues2.ValueA = (cellVariable[face.cellA] + cellVariable[face.cellB]) / 2f);
					}
					else
					{
						faceValues2.HydraulicConnection = HydraulicConnection.None;
						faceValues2.ValueA = cellVariable[face.cellA];
						faceValues2.ValueB = cellVariable[face.cellB];
					}
					faceVar[uniqueFaceIndex2] = faceValues2;
				}
			}, 100);
			Utility.Parallel.SmartFor(UniqueFacePoints.Count, (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					int uniqueFacePoint = UniqueFacePoints[i];
					lFacepointValue[uniqueFacePoint] = HydraulicMesh.NodeComputes.WSEL_Global(uniqueFacePoint, faceVar);
				}
			}, 100);
			return;
		}
		Utility.Parallel.SmartFor(Mesh.FaceCount, (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				faceWS[i] = HydraulicMesh.FaceComputes.WSEL(i, cellWaterSurface, Tables);
			}
		}, 100);
		Utility.Parallel.SmartFor(Mesh.FaceCount, (int start, int finish) =>
		{
			FaceValues faceValues2 = default;
			for (int i = start; i <= finish; i++)
			{
				Face face = Mesh.Faces[i];
				FaceValues faceValues = faceWS[i];
				if (faceValues.IsHydraulicallyConnected)
				{
					faceValues2.HydraulicConnection = HydraulicConnection.DownhillDeep;
					faceValues2.ValueB = (faceValues2.ValueA = (cellVariable[face.cellA] + cellVariable[face.cellB]) / 2f);
				}
				else
				{
					faceValues2.HydraulicConnection = HydraulicConnection.None;
					faceValues2.ValueA = cellVariable[face.cellA];
					faceValues2.ValueB = cellVariable[face.cellB];
				}
				faceVar[i] = faceValues2;
			}
		}, 100);
		Utility.Parallel.SmartFor(Mesh.FacePointCount, (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				lFacepointValue[i] = HydraulicMesh.NodeComputes.WSEL_Global(i, faceVar);
			}
		}, 100);
	}

	public void ComputeFacepointValues_NoHydraulicConnectivity(float[] cellVariable, ref float[] facePointValues)
	{
		float[] faceVar = new float[Mesh.FaceCount];
		Buffers.EnsureArraySize(ref facePointValues, Mesh.FacePointCount);
		float[] lFacepointValue = facePointValues;
		if (!UniqueFacePoints.IsNullOrEmpty() && !UniqueFaces.IsNullOrEmpty())
		{
			Utility.Parallel.SmartFor(UniqueFaces.Count, (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					int uniqueFaceIndex = UniqueFaces[i];
					Face face = Mesh.Faces[uniqueFaceIndex];
					faceVar[uniqueFaceIndex] = (cellVariable[face.cellA] + cellVariable[face.cellB]) / 2f;
				}
			}, 100);
			Utility.Parallel.SmartFor(UniqueFacePoints.Count, (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					int uniqueFacePoint = UniqueFacePoints[i];
					FacePoint facePoint = Mesh.FacePoints[uniqueFacePoint];
					float faceVarSum = 0f;
					foreach (int face2 in facePoint.Faces)
					{
						faceVarSum += faceVar[face2];
					}
					lFacepointValue[uniqueFacePoint] = faceVarSum / (float)facePoint.Faces.Count;
				}
			}, 100);
			return;
		}
		Utility.Parallel.SmartFor(Mesh.FaceCount, (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				Face face = Mesh.Faces[i];
				faceVar[i] = (cellVariable[face.cellA] + cellVariable[face.cellB]) / 2f;
			}
		}, 100);
		Utility.Parallel.SmartFor(Mesh.FacePointCount, (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				FacePoint facePoint = Mesh.FacePoints[i];
				float faceVarSum2 = 0f;
				foreach (int face3 in facePoint.Faces)
				{
					faceVarSum2 += faceVar[face3];
				}
				lFacepointValue[i] = faceVarSum2 / (float)facePoint.Faces.Count;
			}
		}, 100);
	}

	private void ComputeFromFaceScalarValues(float[] faceScalarValues, float[] output, MeshClip clip, RenderOptions renderOptions)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] fpValues = HydraulicMesh.NodeComputes.Scalar_FromFaces(faceScalarValues, SpatialAveragingMode.SimpleAverage);
		ComputeFromFacepointValues(fpValues, output, clip, renderOptions);
		throw new NotImplementedException();
	}

	public void Render2D_8Stencil(VectorMeshInputs inputs, VectorMeshOutputs outputs, MeshClip clip, RenderOptions renderOptions, ParallelizationIntent intent = ParallelizationIntent.Parallel)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		if (intent == ParallelizationIntent.Serial || Cells.Count == 1)
		{
			for (int i = 0; i < Cells.Count; i++)
			{
				SolveCell(this, Cells[i], inputs, outputs, clip, renderOptions, minElevs);
			}
		}
		else
		{
			System.Threading.Tasks.Parallel.ForEach(Cells, (SlopingCellMap slopedCell) =>
			{
				SolveCell(this, slopedCell, inputs, outputs, clip, renderOptions, minElevs);
			});
		}
		static void SolveCell(SlopingMeshMap @this, SlopingCellMap slopedCell, VectorMeshInputs vectorMeshInputs, VectorMeshOutputs outputs2, MeshClip meshClip, RenderOptions renderOptions2, float[] floatValues)
		{
			int cellIndex = slopedCell.CellIndex;
			float cellWaterSurface = meshClip.CellWaterSurfaces.Span[cellIndex];
			float cellIndexTerm = floatValues[cellIndex];
			if (!(cellWaterSurface <= cellIndexTerm + renderOptions2.MinWSPlotTolerance))
			{
				Cell cell = @this.Mesh.Cells[cellIndex];
				int length = cell.Faces.Length;
				Span<Vector> vectors = stackalloc Vector[length];
				Span<Vector> fpLocalValues = stackalloc Vector[length];
				vectorMeshInputs.GetLocalFaceValues(cellIndex, vectors);
				vectorMeshInputs.GetLocalFacepointValues(cellIndex, fpLocalValues);
				slopedCell.PaintCell_8Stencil(fpLocalValues, vectors, meshClip.PixelDepths, outputs2);
			}
		}
	}

	public void Render2D_NodeDonatedWeights_WaterSurface(WaterSurfaceMeshInputs inputs, Memory<float> output, RenderOptions renderOptions, ParallelizationIntent intent = ParallelizationIntent.Parallel)
	{
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		if (intent == ParallelizationIntent.Serial || Cells.Count == 1)
		{
			for (int i = 0; i < Cells.Count; i++)
			{
				SolveCell(this, Cells[i], inputs, output, renderOptions, minElevs);
			}
		}
		else
		{
			System.Threading.Tasks.Parallel.ForEach(Cells, (SlopingCellMap slopedCell) =>
			{
				SolveCell(this, slopedCell, inputs, output, renderOptions, minElevs);
			});
		}
		unsafe static void SolveCell(SlopingMeshMap @this, SlopingCellMap slopedCell, WaterSurfaceMeshInputs waterSurfaceMeshInputs, Memory<float> floatSpan, RenderOptions renderOptions2, float[] floatValues)
		{
			Memory<float> cellWaterSurfaces = waterSurfaceMeshInputs.CellWaterSurfaces;
			Memory<float>[] facePointWaterSurfaces = waterSurfaceMeshInputs.FacePointWaterSurfaces;
			int cellIndex = slopedCell.CellIndex;
			Cell cell = @this.Mesh.Cells[cellIndex];
			Span<int> facesSpan = cell.Faces.Span;
			Face[] faces = @this.Mesh.Faces;
			FacePoint[] facePoints = @this.Mesh.FacePoints;
			float cellWaterSurface = cellWaterSurfaces.Span[cellIndex];
			float cellIndexTerm = floatValues[cellIndex];
			if (!(cellWaterSurface <= cellIndexTerm + renderOptions2.MinWSPlotTolerance))
			{
				int length = facesSpan.Length;
				float* lengthTerm = stackalloc float[length * 2];
				for (int j = 0; j < length; j++)
				{
					int ordinal = j;
					int faceIndex = facesSpan[j];
					int fPPrev = faces[faceIndex].GetFPPrev(cellIndex);
					lengthTerm[ordinal] = facePointWaterSurfaces[fPPrev].Span[facePoints[fPPrev].Faces.IndexOf(faceIndex)];
				}
				Span<FaceValues> faceWaterSurfaces = waterSurfaceMeshInputs.FaceWaterSurfaces.Span;
				for (int k = 0; k < length; k++)
				{
					int faceIndex2 = facesSpan[k];
					FaceValues faceValues = faceWaterSurfaces[faceIndex2];
					if (cellIndex == faces[faceIndex2].cellA)
					{
						lengthTerm[length + k] = faceValues.ValueA;
					}
					else
					{
						lengthTerm[length + k] = faceValues.ValueB;
					}
				}
				if (length == 4 && Avx.IsSupported)
				{
					slopedCell.PaintCell_8Stencil_4FaceAVX(lengthTerm, floatSpan.Span);
				}
				else
				{
					slopedCell.PaintCell_8Stencil(new Span<float>(lengthTerm, length), new Span<float>(lengthTerm + length, length), floatSpan.Span);
				}
			}
		}
	}

	public void Render2D_NodeDonatedWeights_WaterSurface(WaterSurfaceMeshTimeSeriesInputs inputs, Memory<float>[] output, RenderOptions renderOptions, ParallelizationIntent intent = ParallelizationIntent.Parallel)
	{
		Dictionary<int, Memory<float>> cellWaterSurface = inputs.CellWaterSurfaces;
		_ = inputs.NodeWaterSurfaces;
		if (Cells.Count == 0)
		{
			return;
		}
		float[] minElevs = Tables.CellMinimumElevations;
		_ = output[0].Length;
		if (intent == ParallelizationIntent.Serial)
		{
			for (int i = 0; i < Cells.Count; i++)
			{
				SolveCell(i);
			}
		}
		else
		{
			System.Threading.Tasks.Parallel.For(0, Cells.Count, (int cc) =>
			{
				SolveCell(cc);
			});
		}
		unsafe void SolveCell(int localCIdx)
		{
			SlopingCellMap slopingCellMap = Cells[localCIdx];
			int cellIndex = slopingCellMap.CellIndex;
			Cell cell = Mesh.Cells[cellIndex];
			Span<int> facesSpan = cell.Faces.Span;
			Span<PointRange> pixelRanges = slopingCellMap.PixelRanges.Span;
			Face[] faces = Mesh.Faces;
			FacePoint[] facePoints = Mesh.FacePoints;
			Span<float> cellWaterSurfaceSpan = cellWaterSurface[cellIndex].Span;
			float minElev = minElevs[cellIndex];
			int length = facesSpan.Length;
			float[] floatValues = new float[slopingCellMap.HighestPointIndex + 1];
			float* lengthTerm = stackalloc float[length * 2];
			int length2 = cellWaterSurfaceSpan.Length;
			for (int j = 0; j < length2; j++)
			{
				if (!(cellWaterSurfaceSpan[j] <= minElev + renderOptions.MinWSPlotTolerance))
				{
					for (int k = 0; k < length; k++)
					{
						int ordinal = k;
						int faceIndex = facesSpan[k];
						int fPPrev = faces[faceIndex].GetFPPrev(cellIndex);
						Span<float> nodeWaterSurface = inputs.NodeWaterSurfaces[fPPrev].Span;
						int count = Mesh.FacePoints[fPPrev].Faces.Count;
						int indexOf = facePoints[fPPrev].Faces.IndexOf(faceIndex);
						int index = count * j + indexOf;
						lengthTerm[ordinal] = nodeWaterSurface[index];
					}
					for (int l = 0; l < length; l++)
					{
						int faceIndex2 = facesSpan[l];
						FaceValues faceValues = inputs.FaceWaterSurfaces[faceIndex2].Span[j];
						if (cellIndex == faces[faceIndex2].cellA)
						{
							lengthTerm[length + l] = faceValues.ValueA;
						}
						else
						{
							lengthTerm[length + l] = faceValues.ValueB;
						}
					}
					if (length == 4 && Avx.IsSupported)
					{
						slopingCellMap.PaintCell_8Stencil_4FaceAVX(lengthTerm, floatValues);
					}
					else
					{
						slopingCellMap.PaintCell_8Stencil(new Span<float>(lengthTerm, length), new Span<float>(lengthTerm + length, length), floatValues);
					}
					for (int m = 0; m < slopingCellMap.PixelRanges.Length; m++)
					{
						PointRange pointRange = pixelRanges[m];
						for (int n = pointRange.QueryStartIndex; n <= pointRange.QueryEndIndex; n++)
						{
							output[n].Span[j] = floatValues[n];
						}
					}
				}
			}
		}
	}
}
