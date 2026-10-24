using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.Vectors.Meshes;

public class VectorMeshTimeSeriesInputs
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

	public VectorMeshTimeSeriesInputs(Mesh mesh, MeshPropertyTables props, int profileCount)
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
				PlanarRegression planarRegression = new PlanarRegression(facePoint.Point);
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
				Span<FaceValues> faceValuesSpan = new FaceValues[facePoint.Faces.Count];
				for (int n = 0; n < ProfileCount; n++)
				{
					Span<float> slice = nodeWaterSurfaces.Slice(facePoint.Faces.Count * n, facePoint.Faces.Count).Span;
					for (int intervalIndex = 0; intervalIndex < facePoint.Faces.Count; intervalIndex++)
					{
						faceValuesSpan[intervalIndex] = faceValuesSpanArray[intervalIndex].Span[n];
					}
					for (int intervalIndex2 = 0; intervalIndex2 < facePoint.Faces.Count; intervalIndex2++)
					{
						_ = facePoint.Faces[intervalIndex2];
						_ = ref faceValuesSpanArray[intervalIndex2].Span[n];
						if (slice[intervalIndex2] != -9999f)
						{
							continue;
						}
						planarRegression.Clear();
						int ccwFaceByLocalIndexFacePointTerm = intervalIndex2;
						FaceValues faceValues;
						do
						{
							ccwFaceByLocalIndexFacePointTerm = facePoint.CCWFaceByLocalIndex(ccwFaceByLocalIndexFacePointTerm);
							faceValues = faceValuesSpanArray[ccwFaceByLocalIndexFacePointTerm].Span[n];
						}
						while (faceValues.IsHydraulicallyConnected && ccwFaceByLocalIndexFacePointTerm != intervalIndex2);
						int cwFaceByLocalIndexFacePointTerm = intervalIndex2;
						if (cwFaceByLocalIndexFacePointTerm != ccwFaceByLocalIndexFacePointTerm)
						{
							faceValues = faceValuesSpanArray[cwFaceByLocalIndexFacePointTerm].Span[n];
							while (faceValues.IsHydraulicallyConnected)
							{
								cwFaceByLocalIndexFacePointTerm = facePoint.CWFaceByLocalIndex(cwFaceByLocalIndexFacePointTerm);
								faceValues = faceValuesSpanArray[cwFaceByLocalIndexFacePointTerm].Span[n];
							}
						}
						int ordinal = cwFaceByLocalIndexFacePointTerm;
						do
						{
							int faceIndex3 = facePoint.Faces[ordinal];
							FaceValues faceValues2 = faceValuesSpan[ordinal];
							double valueB = ((Mesh.Faces[faceIndex3].fpA != fPPrev) ? ((double)faceValues2.ValueB) : ((double)faceValues2.ValueA));
							if (valueB != -9999.0)
							{
								Point pt = Mesh.FaceSegment(faceIndex3).Midpoint();
								planarRegression.Add(pt, valueB);
							}
							slice[ordinal] = (float)valueB;
							ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
						}
						while (ordinal != ccwFaceByLocalIndexFacePointTerm);
						int faceIndex4 = facePoint.Faces[ccwFaceByLocalIndexFacePointTerm];
						FaceValues faceValues3 = faceValuesSpan[ccwFaceByLocalIndexFacePointTerm];
						if (!faceValues3.IsHydraulicallyConnected)
						{
							double valueA2 = ((Mesh.Faces[faceIndex4].fpA != fPPrev) ? ((double)faceValues3.ValueA) : ((double)faceValues3.ValueB));
							if (valueA2 != -9999.0)
							{
								Point pt2 = Mesh.FaceSegment(faceIndex4).Midpoint();
								planarRegression.Add(pt2, valueA2);
							}
						}
						if (planarRegression.Count > 0)
						{
							float solveZ = (float)planarRegression.SolveZ();
							ordinal = cwFaceByLocalIndexFacePointTerm;
							do
							{
								slice[ordinal] = solveZ;
								ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
							}
							while (ordinal != ccwFaceByLocalIndexFacePointTerm);
						}
						if (cwFaceByLocalIndexFacePointTerm == ccwFaceByLocalIndexFacePointTerm)
						{
							break;
						}
					}
				}
			}
		}
		void EnsureFace(int ordinal2)
		{
			if (!FaceWaterSurfaces.ContainsKey(ordinal2))
			{
				Face face = Mesh.Faces[ordinal2];
				int cellA = face.cellA;
				Memory<float> cellWaterSurface = CellWaterSurfaces[cellA];
				Span<float> cellWaterSurfaceSpan = cellWaterSurface.Span;
				FaceValues[] faceWaterSurfaces = new FaceValues[ProfileCount];
				FaceWaterSurfaces[ordinal2] = faceWaterSurfaces;
				int otherCell = face.OtherCell(cellA);
				if (otherCell == -1)
				{
					for (int intervalIndex3 = 0; intervalIndex3 < ProfileCount; intervalIndex3++)
					{
						float valueA = cellWaterSurfaceSpan[intervalIndex3];
						faceWaterSurfaces[intervalIndex3] = new FaceValues
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
					for (int intervalIndex4 = 0; intervalIndex4 < ProfileCount; intervalIndex4++)
					{
						float cellWSA = cellWaterSurfaceSpan[intervalIndex4];
						float cellWSB = cellWaterSurface2[intervalIndex4];
						faceWaterSurfaces[intervalIndex4] = Mesh.MetadataCache.FaceComputes.WSEL_Adjusted(ordinal2, cellWSA, cellWSB, PropertyTables);
					}
				}
			}
		}
	}
}
