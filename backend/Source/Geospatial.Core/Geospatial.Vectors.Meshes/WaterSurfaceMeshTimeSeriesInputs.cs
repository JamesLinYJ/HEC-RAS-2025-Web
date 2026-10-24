using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors.Meshes;

public class WaterSurfaceMeshTimeSeriesInputs
{
	public Mesh Mesh { get; }

	public MeshPropertyTables PropertyTables { get; }

	public int ProfileCount { get; }

	public Dictionary<int, Memory<float>> CellWaterSurfaces { get; set; }

	public Dictionary<int, Memory<FaceValues>> FaceWaterSurfaces { get; set; }

	public Dictionary<int, Memory<float>> NodeWaterSurfaces { get; set; }

	public int FacePointSpinnerCount(int fpIdx)
	{
		return Mesh.FacePoints[fpIdx].Faces.Count;
	}

	public WaterSurfaceMeshTimeSeriesInputs(Mesh mesh, MeshPropertyTables props, int profileCount)
	{
		if (props == null || props.CellMinimumElevations == null || props.FaceMinimumElevations == null)
		{
			throw new ArgumentNullException("props or some necessary internal buffer.");
		}
		Mesh = mesh;
		PropertyTables = props;
		ProfileCount = profileCount;
		CellWaterSurfaces = new Dictionary<int, Memory<float>>();
		FaceWaterSurfaces = new Dictionary<int, Memory<FaceValues>>();
		NodeWaterSurfaces = new Dictionary<int, Memory<float>>();
	}

	public void Add(Memory<int> cells, Action<Memory<int>, Memory<float>[]> getCellTimeSeries)
	{
		Span<int> cellsSpan = cells.Span;
		HydraulicMeshComputer metadataCache = Mesh.MetadataCache;
		int[] oneRingCells = Mesh.GetOneRingCells(cells).ToArray();
		Memory<float>[] floatSpanArray = new Memory<float>[oneRingCells.Length];
		for (int i = 0; i < oneRingCells.Length; i++)
		{
			int key = oneRingCells[i];
			float[] cellWaterSurfaces = new float[ProfileCount];
			CellWaterSurfaces[key] = cellWaterSurfaces;
			floatSpanArray[i] = cellWaterSurfaces;
		}
		getCellTimeSeries(oneRingCells.AsMemory(), floatSpanArray);
		for (int j = 0; j < cellsSpan.Length; j++)
		{
			int cellIndex = cellsSpan[j];
			Cell cell = Mesh.Cells[cellIndex];
			Span<int> faces = cell.Faces.Span;
			for (int k = 0; k < faces.Length; k++)
			{
				int fIdx = faces[k];
				EnsureFace(fIdx);
			}
			Mesh.CellFacePoints(cellIndex);
			for (int l = 0; l < faces.Length; l++)
			{
				int faceIndex = faces[l];
				int fPPrev = Mesh.Faces[faceIndex].GetFPPrev(cellIndex);
				if (NodeWaterSurfaces.ContainsKey(fPPrev))
				{
					continue;
				}
				FacePoint facePoint = Mesh.FacePoints[fPPrev];
				new PlanarRegression(facePoint.Point);
				Memory<float> nodeWaterSurfaces = new float[facePoint.Faces.Count * ProfileCount];
				nodeWaterSurfaces.Span.Fill(-9999f);
				NodeWaterSurfaces[fPPrev] = nodeWaterSurfaces;
				Memory<FaceValues>[] faceValuesSpanArray = new Memory<FaceValues>[facePoint.Faces.Count];
				for (int m = 0; m < facePoint.Faces.Count; m++)
				{
					int faceIndex2 = facePoint.Faces[m];
					EnsureFace(faceIndex2);
					faceValuesSpanArray[m] = FaceWaterSurfaces[faceIndex2];
				}
				Span<FaceValues> localFaceWSEL = new FaceValues[facePoint.Faces.Count];
				for (int n = 0; n < ProfileCount; n++)
				{
					Span<float> slice = nodeWaterSurfaces.Slice(facePoint.Faces.Count * n, facePoint.Faces.Count).Span;
					for (int intervalIndex = 0; intervalIndex < facePoint.Faces.Count; intervalIndex++)
					{
						localFaceWSEL[intervalIndex] = faceValuesSpanArray[intervalIndex].Span[n];
					}
					metadataCache.NodeComputes.WSEL_Local(fPPrev, localFaceWSEL, slice);
				}
			}
		}
	}

	private void EnsureFace(int fIdx)
	{
		if (FaceWaterSurfaces.ContainsKey(fIdx))
		{
			return;
		}
		Face face = Mesh.Faces[fIdx];
		int cellA = face.cellA;
		Memory<float> cellWaterSurface = CellWaterSurfaces[cellA];
		Span<float> cellWaterSurfaceSpan = cellWaterSurface.Span;
		FaceValues[] faceWaterSurfaces = new FaceValues[ProfileCount];
		FaceWaterSurfaces[fIdx] = faceWaterSurfaces;
		int otherCell = face.OtherCell(cellA);
		if (otherCell == -1)
		{
			for (int i = 0; i < ProfileCount; i++)
			{
				float valueA = cellWaterSurfaceSpan[i];
				faceWaterSurfaces[i] = new FaceValues
				{
					ValueA = valueA,
					ValueB = -9999f,
					HydraulicConnection = HydraulicConnection.None
				};
			}
		}
		else
		{
			cellWaterSurface = CellWaterSurfaces[otherCell];
			Span<float> cellWaterSurface2 = cellWaterSurface.Span;
			for (int j = 0; j < ProfileCount; j++)
			{
				float cellWSA = cellWaterSurfaceSpan[j];
				float cellWSB = cellWaterSurface2[j];
				faceWaterSurfaces[j] = Mesh.MetadataCache.FaceComputes.WSEL_Adjusted(fIdx, cellWSA, cellWSB, PropertyTables);
			}
		}
	}
}
