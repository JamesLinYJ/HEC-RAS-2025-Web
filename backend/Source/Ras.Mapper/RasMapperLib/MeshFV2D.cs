using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using RasMapperLib.Render;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

[ComVisible(false)]
public class MeshFV2D : Feature
{
	public struct TestStruct
	{
		public int Value;

		public TestStruct(int v)
		{
			this = default;
			Value = v;
		}

		public void Increment()
		{
			checked
			{
				Value++;
			}
		}
	}

	public struct ProfileFaceRange
	{
		public int Face;

		public int FPStartIdx;

		public int FPEndIdx;

		public double StationStart;

		public double StationEnd;

		public ProfileFaceRange(int f, int fpStart, int fpEnd, double sStart, double sEnd)
		{
			this = default;
			Face = f;
			FPStartIdx = fpStart;
			FPEndIdx = fpEnd;
			StationStart = sStart;
			StationEnd = sEnd;
		}
	}

	public struct ProfileCellRange
	{
		public int Cell;

		public double StationStart;

		public double StationEnd;

		public ProfileCellRange(int c, double sStart, double sEnd)
		{
			this = default;
			Cell = c;
			StationStart = sStart;
			StationEnd = sEnd;
		}

		public static int CompareStationStart(ProfileCellRange a, ProfileCellRange b)
		{
			if (a.StationStart < b.StationStart)
			{
				return -1;
			}
			if (a.StationStart == b.StationStart)
			{
				return 0;
			}
			return 1;
		}
	}

	public struct PointCellElev
	{
		public PointM Point;

		public int CellIndex;

		public float Elevation;

		public PointCellElev(PointM p, int c, float elev)
		{
			this = default;
			Point = p;
			CellIndex = c;
			Elevation = elev;
		}
	}

	public struct CellFlowPathStruct
	{
		public int DownstreamCell;

		public int DownstreamFace;

		public int SinkCell;
	}

	private struct SinkStruct
	{
		public int Cell;

		public List<int> RidgeFaces;

		public int ContributingCellCount;

		private double _ContributingArea;

		private double _ContributingAreaElevation;

		public SinkStruct(int c)
		{
			this = default;
			Cell = c;
			RidgeFaces = new List<int>();
		}

		public void AddCell(double cellArea, double cellElevation)
		{
			checked
			{
				ContributingCellCount++;
				_ContributingArea += cellArea;
				_ContributingAreaElevation += cellArea + cellElevation;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__105_002D0
	{
		public float[] areas;

		public MeshFV2D meshFV2D;

		public _Closure_0024__105_002D0(_Closure_0024__105_002D0 other)
		{
			if (other != null)
			{
				areas = other.areas;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (!meshFV2D.CellIsVirtual(i))
				{
					areas[i] = (float)meshFV2D.CellPolygon(i).Area;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__128_002D0
	{
		public byte[] graph;

		public byte curIsland;

		public BitArray hasPushed;

		public Action<int> pushNeighbors;

		public MeshFV2D meshFV2D;

		public _Closure_0024__128_002D0(_Closure_0024__128_002D0 other)
		{
			if (other != null)
			{
				graph = other.graph;
				curIsland = other.curIsland;
				hasPushed = other.hasPushed;
				pushNeighbors = other.pushNeighbors;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int c)
		{
			foreach (int face in meshFV2D._cells[c].Faces)
			{
				int otherCell = meshFV2D.Faces[face].OtherCell(c);
				if (otherCell != -1)
				{
					graph[otherCell] = graph[c];
				}
			}
		}

		[SpecialName]
		internal int _Lambda_0024__1()
		{
			checked
			{
				int count = meshFV2D._cells.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (graph[i] == 0)
					{
						return i;
					}
				}
				return -1;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int startcell)
		{
			checked
			{
				curIsland = (byte)unchecked((uint)(curIsland + 1));
				graph[startcell] = curIsland;
				bool condition = false;
				bool condition2;
				do
				{
					condition2 = false;
					condition = !condition;
					int cellsTerm;
					int cellsTerm2;
					int sign;
					if (condition)
					{
						cellsTerm = 0;
						cellsTerm2 = meshFV2D._cells.Count() - 1;
						sign = 1;
					}
					else
					{
						cellsTerm = meshFV2D._cells.Count() - 1;
						cellsTerm2 = 0;
						sign = -1;
					}
					int ordinal = cellsTerm;
					int ordinal2 = cellsTerm2;
					int sign2 = sign;
					for (int i = ordinal; ((sign2 >> 31) ^ i) <= ((sign2 >> 31) ^ ordinal2); i += sign2)
					{
						if (!hasPushed[i] && graph[i] != 0)
						{
							pushNeighbors(i);
							hasPushed[i] = true;
							condition2 = true;
						}
					}
				}
				while (condition2);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__140_002D0
	{
		public SpatialIndex<Tuple<bool, int>> faceSpIdx;

		public MeshFV2D meshA;

		public MeshFV2D meshB;

		public Tuple<int, bool>[] faceMatchesLocal;

		public double tol;

		public int[] cellMatchesLocal;

		public _Closure_0024__140_002D0(_Closure_0024__140_002D0 other)
		{
			if (other != null)
			{
				faceSpIdx = other.faceSpIdx;
				meshA = other.meshA;
				meshB = other.meshB;
				faceMatchesLocal = other.faceMatchesLocal;
				tol = other.tol;
				cellMatchesLocal = other.cellMatchesLocal;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int binIdx)
		{
			List<Tuple<bool, int>> bin = faceSpIdx.Bins[binIdx];
			if (bin.IsNullOrEmpty())
			{
				return;
			}
			HashSet<int> intSet = new HashSet<int>();
			checked
			{
				int countOffset = bin.Count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					Tuple<bool, int> tuple = bin[i];
					if (!tuple.Item1)
					{
						continue;
					}
					int tupleTerm = tuple.Item2;
					Face face = meshA.Faces[tupleTerm];
					PointM pointM = meshA.FacePoints[face.fpA].PointM();
					PointM pointM2 = meshA.FacePoints[face.fpB].PointM();
					int ordinal = i + 1;
					int countOffset2 = bin.Count - 1;
					for (int j = ordinal; j <= countOffset2; j++)
					{
						Tuple<bool, int> tuple2 = bin[j];
						if (!tuple2.Item1 && !intSet.Contains(j))
						{
							int tuple2Term = tuple2.Item2;
							Face face2 = meshB.Faces[tuple2Term];
							PointM pointM3 = meshB.FacePoints[face2.fpA].PointM();
							PointM pointM4 = meshB.FacePoints[face2.fpB].PointM();
							if (pointM.EqualXY(pointM3) && pointM2.EqualXY(pointM4))
							{
								intSet.Add(j);
								faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: true);
								break;
							}
							double distanceTo = pointM.DistanceTo(pointM3);
							double distanceTo2 = pointM.DistanceTo(pointM4);
							double distanceTo3 = pointM2.DistanceTo(pointM3);
							double distanceTo4 = pointM2.DistanceTo(pointM4);
							if (distanceTo < tol && distanceTo4 < tol)
							{
								intSet.Add(j);
								faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: true);
								break;
							}
							if (distanceTo2 < tol && distanceTo3 < tol)
							{
								intSet.Add(j);
								faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: false);
								break;
							}
						}
					}
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				Cell cell = meshA.Cell(i);
				bool condition = true;
				int minValue = int.MinValue;
				foreach (int face3 in cell.Faces)
				{
					Tuple<int, bool> tuple = faceMatchesLocal[face3];
					if (tuple == null)
					{
						condition = false;
						break;
					}
					Face face = meshA.Faces[face3];
					Face face2 = meshB.Faces[tuple.Item1];
					int neighborCell;
					int neighborCell2;
					if (tuple.Item2)
					{
						neighborCell = face2.cellA;
						neighborCell2 = face2.cellB;
					}
					else
					{
						neighborCell = face2.cellB;
						neighborCell2 = face2.cellA;
					}
					if (face.cellA == i)
					{
						if (minValue == int.MinValue)
						{
							minValue = neighborCell;
						}
						else if (minValue != neighborCell)
						{
							condition = false;
							break;
						}
					}
					else if (minValue == int.MinValue)
					{
						minValue = neighborCell2;
					}
					else if (minValue != neighborCell2)
					{
						condition = false;
						break;
					}
				}
				if (condition)
				{
					cellMatchesLocal[i] = minValue;
				}
				else
				{
					cellMatchesLocal[i] = -1;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__144_002D0
	{
		public int cIdx;

		public MeshFV2D meshFV2D;

		public _Closure_0024__144_002D0(_Closure_0024__144_002D0 other)
		{
			if (other != null)
			{
				cIdx = other.cIdx;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__0(int fIdx)
		{
			return meshFV2D.Faces[fIdx].GetFPNext(cIdx);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__148_002D0
	{
		public List<int> collapse;

		public _Closure_0024__148_002D0(_Closure_0024__148_002D0 other)
		{
			if (other != null)
			{
				collapse = other.collapse;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(int ff)
		{
			return collapse.Contains(ff);
		}

		[SpecialName]
		internal bool _Lambda_0024__1(int ff)
		{
			return collapse.Contains(ff);
		}

		[SpecialName]
		internal bool _Lambda_0024__2(int ff)
		{
			return collapse.Contains(ff);
		}

		[SpecialName]
		internal bool _Lambda_0024__3(int ff)
		{
			return collapse.Contains(ff);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__149_002D0
	{
		public List<int> collapse;

		public _Closure_0024__149_002D0(_Closure_0024__149_002D0 other)
		{
			if (other != null)
			{
				collapse = other.collapse;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(int ff)
		{
			return collapse.Contains(ff);
		}

		[SpecialName]
		internal bool _Lambda_0024__2(int ff)
		{
			return collapse.Contains(ff);
		}

		[SpecialName]
		internal bool _Lambda_0024__3(int ff)
		{
			return collapse.Contains(ff);
		}

		[SpecialName]
		internal bool _Lambda_0024__4(int ff)
		{
			return collapse.Contains(ff);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__260_002D0
	{
		public int ccwFaceIdx;

		public _Closure_0024__260_002D0(_Closure_0024__260_002D0 other)
		{
			if (other != null)
			{
				ccwFaceIdx = other.ccwFaceIdx;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(FaceAroundFacePoint f)
		{
			return f.faceIndex == ccwFaceIdx;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__283_002D0
	{
		public FaceVelocityCoef[] facePointVelocityCoef;

		public MeshFV2D meshFV2D;

		public _Closure_0024__283_002D0(_Closure_0024__283_002D0 other)
		{
			if (other != null)
			{
				facePointVelocityCoef = other.facePointVelocityCoef;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				foreach (int face in meshFV2D.FacePoints[i].Faces)
				{
					facePointVelocityCoef[i].AddWeightedFaceNormal(meshFV2D.FaceNormal(face), meshFV2D.FaceInvLengths[face]);
				}
				facePointVelocityCoef[i].Complete();
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__292_002D0
	{
		public List<int> uniqueFacePts;

		public FacePointVels[] lFPVels;

		public float[] perpendicularFaceVelocities;

		public FaceValues[] faceWSs;

		public MeshFV2D meshFV2D;

		public _Closure_0024__292_002D0(_Closure_0024__292_002D0 other)
		{
			if (other != null)
			{
				uniqueFacePts = other.uniqueFacePts;
				lFPVels = other.lFPVels;
				perpendicularFaceVelocities = other.perpendicularFaceVelocities;
				faceWSs = other.faceWSs;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFacePt = uniqueFacePts[i];
				lFPVels[uniqueFacePt] = meshFV2D.ComputeFacePointVelocity_FacePerpLeastSquares_Weighted(uniqueFacePt, perpendicularFaceVelocities, faceWSs);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				lFPVels[i] = meshFV2D.ComputeFacePointVelocity_FacePerpLeastSquares_Weighted(i, perpendicularFaceVelocities, faceWSs);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__294_002D0
	{
		public List<int> uniqueFacePts;

		public FacePointVels[] lFPVels;

		public Tuple<Vector2D, Vector2D>[] faceTrueVels;

		public FaceValues[] faceWSs;

		public MeshFV2D meshFV2D;

		public _Closure_0024__294_002D0(_Closure_0024__294_002D0 other)
		{
			if (other != null)
			{
				uniqueFacePts = other.uniqueFacePts;
				lFPVels = other.lFPVels;
				faceTrueVels = other.faceTrueVels;
				faceWSs = other.faceWSs;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFacePt = uniqueFacePts[i];
				lFPVels[uniqueFacePt] = meshFV2D.ComputeFacePointVelocity_FaceTrueVelAvg_Weighted(uniqueFacePt, faceTrueVels, faceWSs);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				lFPVels[i] = meshFV2D.ComputeFacePointVelocity_FaceTrueVelAvg_Weighted(i, faceTrueVels, faceWSs);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__295_002D0
	{
		public MeshMap mapped2DArea;

		public Tuple<Vector2D, Vector2D>[] trueVels;

		public float[] facePerpVelocities;

		public FaceValues[] faceWSValues;

		public MeshFV2D meshFV2D;

		public _Closure_0024__295_002D0(_Closure_0024__295_002D0 other)
		{
			if (other != null)
			{
				mapped2DArea = other.mapped2DArea;
				trueVels = other.trueVels;
				facePerpVelocities = other.facePerpVelocities;
				faceWSValues = other.faceWSValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = mapped2DArea.UniqueFaces[i];
				trueVels[uniqueFaceIndex] = meshFV2D.ComputeFaceVelocity_CAveragedStencil(uniqueFaceIndex, facePerpVelocities, faceWSValues);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				trueVels[i] = meshFV2D.ComputeFaceVelocity_CAveragedStencil(i, facePerpVelocities, faceWSValues);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__302_002D0
	{
		public SlopingMeshMap slopedArea;

		public FaceValues[] faceWSValues;

		public FacePointVels[] fpVels;

		public Vector2D[] trueVels;

		public MeshFV2D meshFV2D;

		public _Closure_0024__302_002D0(_Closure_0024__302_002D0 other)
		{
			if (other != null)
			{
				slopedArea = other.slopedArea;
				faceWSValues = other.faceWSValues;
				fpVels = other.fpVels;
				trueVels = other.trueVels;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = slopedArea.UniqueFaces[i];
				FaceValues faceValues = faceWSValues[uniqueFaceIndex];
				if (faceValues.IsHydraulicallyConnected)
				{
					Face face = meshFV2D.Faces[uniqueFaceIndex];
					_ = ref meshFV2D._cells[face.cellA];
					FacePoint facePoint = meshFV2D.FacePoints[face.fpA];
					FacePoint facePoint2 = meshFV2D.FacePoints[face.fpB];
					Vector2D vector2D = fpVels[face.fpA].Velocity[facePoint.Faces.IndexOf(uniqueFaceIndex)];
					Vector2D vector2D2 = fpVels[face.fpB].Velocity[facePoint2.Faces.IndexOf(uniqueFaceIndex)];
					Vector2D vector2D3 = new Vector2D((vector2D.X + vector2D2.X) / 2f, (vector2D.Y + vector2D2.Y) / 2f);
					trueVels[uniqueFaceIndex] = vector2D3;
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				FaceValues faceValues = faceWSValues[i];
				if (faceValues.IsHydraulicallyConnected)
				{
					Face face = meshFV2D.Faces[i];
					_ = ref meshFV2D._cells[face.cellA];
					FacePoint facePoint = meshFV2D.FacePoints[face.fpA];
					FacePoint facePoint2 = meshFV2D.FacePoints[face.fpB];
					Vector2D vector2D = fpVels[face.fpA].Velocity[facePoint.Faces.IndexOf(i)];
					Vector2D vector2D2 = fpVels[face.fpB].Velocity[facePoint2.Faces.IndexOf(i)];
					Vector2D vector2D3 = new Vector2D((vector2D.X + vector2D2.X) / 2f, (vector2D.Y + vector2D2.Y) / 2f);
					trueVels[i] = vector2D3;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__303_002D0
	{
		public SlopingMeshMap slopedArea;

		public Vector2D[] faceTrueVels;

		public float[] faceMann;

		public float[] faceHydrRad;

		public Vector2D[] shears;

		public float factor;

		public _Closure_0024__303_002D0(_Closure_0024__303_002D0 other)
		{
			if (other != null)
			{
				slopedArea = other.slopedArea;
				faceTrueVels = other.faceTrueVels;
				faceMann = other.faceMann;
				faceHydrRad = other.faceHydrRad;
				shears = other.shears;
				factor = other.factor;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = slopedArea.UniqueFaces[i];
				float x = faceTrueVels[uniqueFaceIndex].X;
				float y = faceTrueVels[uniqueFaceIndex].Y;
				float magnitude = faceTrueVels[uniqueFaceIndex].Magnitude;
				float faceMann = this.faceMann[uniqueFaceIndex];
				float faceHydrRad = this.faceHydrRad[uniqueFaceIndex];
				if ((x == 0f && y == 0f) || faceHydrRad == 0f)
				{
					shears[uniqueFaceIndex] = default;
					continue;
				}
				float x2 = factor * magnitude * x * faceMann * faceMann / (float)System.Math.Pow(faceHydrRad, 0.33333333333333337);
				float y2 = factor * magnitude * y * faceMann * faceMann / (float)System.Math.Pow(faceHydrRad, 0.33333333333333337);
				shears[uniqueFaceIndex] = new Vector2D(x2, y2);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float x = faceTrueVels[i].X;
				float y = faceTrueVels[i].Y;
				float magnitude = faceTrueVels[i].Magnitude;
				float faceMann = this.faceMann[i];
				float faceHydrRad = this.faceHydrRad[i];
				if ((x == 0f && y == 0f) || faceHydrRad == 0f)
				{
					shears[i] = default;
					continue;
				}
				float x2 = factor * magnitude * x * faceMann * faceMann / (float)System.Math.Pow(faceHydrRad, 0.33333333333333337);
				float y2 = factor * magnitude * y * faceMann * faceMann / (float)System.Math.Pow(faceHydrRad, 0.33333333333333337);
				shears[i] = new Vector2D(x2, y2);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__370_002D0
	{
		public bool[] possibleWet;

		public float[] sourceVals;

		public float approachFactor;

		public MeshFV2D meshFV2D;

		public _Closure_0024__370_002D0(_Closure_0024__370_002D0 other)
		{
			if (other != null)
			{
				possibleWet = other.possibleWet;
				sourceVals = other.sourceVals;
				approachFactor = other.approachFactor;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__370_002D1
	{
		public float[] tn;

		public float[] tc;

		public _Closure_0024__370_002D0 outerClosure;

		public _Closure_0024__370_002D1(_Closure_0024__370_002D1 other)
		{
			if (other != null)
			{
				tn = other.tn;
				tc = other.tc;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					if (!outerClosure.possibleWet[i])
					{
						continue;
					}
					if (outerClosure.sourceVals[i] != -9999f)
					{
						tn[i] = outerClosure.sourceVals[i];
						continue;
					}
					Cell cell = outerClosure.meshFV2D._cells[i];
					float tc = this.tc[i];
					float tcSum = 0f;
					int ordinal = 0;
					foreach (int face2 in cell.Faces)
					{
						Face face = outerClosure.meshFV2D.Faces[face2];
						float minimumElevation = face.MinimumElevation;
						int otherCell = face.OtherCell(i);
						outerClosure.possibleWet[otherCell] = true;
						float tc2 = this.tc[otherCell];
						if ((double)tc2 > (double)minimumElevation + 0.01)
						{
							tcSum += tc2;
							ordinal++;
						}
					}
					if (ordinal == 0)
					{
						tn[i] = this.tc[i];
						continue;
					}
					float computed = tcSum / (float)ordinal;
					tn[i] = tc + (computed - tc) * outerClosure.approachFactor;
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__83_002D0
	{
		public List<int> duplicatePoints;

		public _Closure_0024__83_002D0(_Closure_0024__83_002D0 other)
		{
			if (other != null)
			{
				duplicatePoints = other.duplicatePoints;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int pt)
		{
			duplicatePoints.Add(pt);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__87_002D0
	{
		public PointM centroid;

		public _Closure_0024__87_002D0(_Closure_0024__87_002D0 other)
		{
			if (other != null)
			{
				centroid = other.centroid;
			}
		}

		[SpecialName]
		internal double _Lambda_0024__0(KeyValuePair<int, PointM> kvp)
		{
			return kvp.Value.DistanceTo(centroid);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__91_002D0
	{
		public int curPt;

		public _Closure_0024__91_002D0(_Closure_0024__91_002D0 other)
		{
			if (other != null)
			{
				curPt = other.curPt;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Tuple<int, int> kvp)
		{
			if (kvp.Item1 != curPt)
			{
				return kvp.Item2 == curPt;
			}
			return true;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__92_002D0
	{
		public DistinctSpatialIndex perimeterSpatialIndex;

		public MeshFV2D meshFV2D;

		public _Closure_0024__92_002D0(_Closure_0024__92_002D0 other)
		{
			if (other != null)
			{
				perimeterSpatialIndex = other.perimeterSpatialIndex;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(SegmentM segment)
		{
			List<int> elements = perimeterSpatialIndex.GetElements(segment);
			foreach (int element in elements)
			{
				if (meshFV2D._perimeter.Segment(element).Intersects(segment))
				{
					return true;
				}
			}
			return false;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__95_002D0
	{
		public DistinctSpatialIndex perimeterSpatialIndex;

		public MeshFV2D meshFV2D;

		public _Closure_0024__95_002D0(_Closure_0024__95_002D0 other)
		{
			if (other != null)
			{
				perimeterSpatialIndex = other.perimeterSpatialIndex;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(SegmentM segment)
		{
			double length = segment.Length;
			double tB = (length - meshFV2D._filterToleranceTIN) / length;
			segment.Scale(meshFV2D._filterToleranceTIN / length, tB);
			List<int> elements = perimeterSpatialIndex.GetElements(segment);
			foreach (int element in elements)
			{
				if (meshFV2D._perimeter.Segment(element).Intersects(segment))
				{
					return true;
				}
			}
			return false;
		}
	}

	private const string sVersion = "Version";

	public const string VERSION = "1.0";

	private const string sDataDate = "Data Date";

	private const string sDisableEdgeLines = "Disable Mesh Edges";

	private const string sCellNumbers = "Mesh Cell Numbers";

	private const string sFaceNumbers = "Mesh Face Numbers";

	private const string sFaceMinimumElevation = "Mesh Face Minimum Elevation";

	private const string sFacePointNumbers = "Mesh Face Point Numbers";

	private const string sFaceMidpoints = "Mesh Face Midpoints";

	private const string sRasterizeTin = "Rasterize Mesh as TIN";

	private const string sGraphConnectivity = "Mesh Graph (Connectivity)";

	private const string sCellCount = "Cell Count";

	public const string sCellSurfaceArea = "Cells Surface Area";

	public const string sCellVolumeElevation = "Cells Volume Elevation";

	public const string sFacesAreaElevation = "Faces Area Elevation";

	public const double DefaultMinFaceLengthRatio = 0.05;

	public static double ActualMinFaceLengthRatio;

	public const bool DefaultValidatePolygonContainsCell = false;

	public static bool ActualValidatePolygonContainsCell;

	public static bool USE_FACE_MIDSIDE_CACHING;

	private const bool EXTERNAL_FACE_SPLITTING = true;

	public const float DefaultMannN = 0.06f;

	public const double MinFaceLenSI = 0.003;

	public const double MinFaceLenUS = 0.01;

	private static readonly PlotOption _poDisableMeshEdges;

	private static readonly PlotOption _poCellNumbers;

	private static readonly PlotOption _poFaceNumbers;

	private static readonly PlotOption _poFaceMinElev;

	private static readonly PlotOption _poFacePointNumbers;

	private static readonly PlotOption _poRasterizeTIN;

	private static readonly PlotOption _poGraphConnectivity;

	private static readonly PlotOption _poFaceMidpoints;

	private static readonly List<PlotOption> _debugPlotOptions;

	private static readonly List<PlotOption> _plotOptions;

	public List<FacePoint> FacePoints;

	public List<Face> Faces;

	private Cell[] _cells;

	private Polygon _perimeter;

	private string _statusMessage;

	private List<int> _badIndexes;

	private MeshStatus _meshCompletionState;

	public bool FatalPerimeterError;

	private Extent _extent;

	private int _cellMaximumAreaIndex;

	private int _cellMinimumAreaIndex;

	private float _cellMaximumArea;

	private float _cellMinimumArea;

	private float _cellAverageArea;

	private double _cellsTotalArea;

	private bool _cellMinimumElevationSet;

	public BitArray SquareCells;

	private readonly double _filterToleranceTIN;

	public const int DefaultPixelRenderingCutoff = 5;

	public static int PixelRenderingCutoff;

	private ProgressReporter _progressReporter;

	public const bool COLLAPSE_PERIMETER_FACES = true;

	public const int MAX_ALLOWED_FACE_COUNT_PER_CELL = 8;

	public const int MAX_ALLOWED_FACE_COUNT_PER_FACEPOINT = 100;

	public const int MAX_ALLOWED_FACE_COUNT_PER_FACEPOINT_v6 = 8;

	private int _virtCellCt;

	private Vector2D[] _faceNormal;

	public float[] FaceInvLengths;

	public static bool TEST_INCLUDE_CANDIDATE_FACE_VELS;

	private const float siGamma = 9810f;

	private const float uscGamma = 62.4f;

	private FaceMidside[] _faceMidside;

	private float[] _faceCentroidStation;

	private List<PointM> prevPts;

	private List<Polygon> prevPolys;

	private float[] prevICSln;

	private float prevApprFactor;

	private int prevIterCt;

	public Polygon Perimeter => _perimeter;

	public int CellCount
	{
		get
		{
			if (_cells != null)
			{
				return _cells.Count();
			}
			return 0;
		}
	}

	public int NonVirtualCellCount
	{
		get
		{
			checked
			{
				if (_virtCellCt == -1)
				{
					if (_cells.IsNullOrEmpty())
					{
						return 0;
					}
					int count = _cells.Count() - 1;
					for (int i = 0; i <= count; i++)
					{
						if (CellIsVirtual(i))
						{
							_virtCellCt = i;
							break;
						}
					}
					if (_virtCellCt == -1)
					{
						_virtCellCt = _cells.Count();
					}
				}
				return _virtCellCt;
			}
		}
	}

	public int FacePointCount
	{
		get
		{
			if (FacePoints != null)
			{
				return FacePoints.Count;
			}
			return 0;
		}
	}

	public int FaceCount
	{
		get
		{
			if (Faces != null)
			{
				return Faces.Count;
			}
			return 0;
		}
	}

	public string StatusMessage => _statusMessage;

	public List<int> BadIndexes => _badIndexes;

	public MeshStatus MeshCompletionState => _meshCompletionState;

	public bool IsReadable
	{
		get
		{
			if (MeshCompletionState == MeshStatus.Complete)
			{
				return true;
			}
			if (MeshCompletionState == MeshStatus.FewerThanMinPerimeterPoints)
			{
				return false;
			}
			if (MeshCompletionState == MeshStatus.FewerThanMinNumCells)
			{
				return false;
			}
			if (MeshCompletionState == MeshStatus.PointsOutsidePerimeter)
			{
				return false;
			}
			if (MeshCompletionState == MeshStatus.DuplicatePoints)
			{
				return false;
			}
			if (MeshCompletionState == MeshStatus.FacePerimeterConnectionError)
			{
				return !FatalPerimeterError;
			}
			if (MeshCompletionState == MeshStatus.MaxFacesPerCellExceeded)
			{
				return true;
			}
			if (MeshCompletionState == MeshStatus.MaxFacesPerFacepointExceeded)
			{
				return true;
			}
			if (MeshCompletionState == MeshStatus.CellAreaTotalError)
			{
				return true;
			}
			if (MeshCompletionState == MeshStatus.DisconnectedGraphs)
			{
				return true;
			}
			if (MeshCompletionState == MeshStatus.MinFaceLenError)
			{
				return true;
			}
			return false;
		}
	}

	public int CellMaximumIndex => _cellMaximumAreaIndex;

	public double CellMaximumSize => _cellMaximumArea;

	public double CellMinimumSize => _cellMinimumArea;

	public double CellAverageSize => _cellAverageArea;

	public double AreaTolerance => CellMinimumSize / 10000.0;

	public double LengthTolerance => System.Math.Sqrt(CellMinimumSize) * 0.01;

	public override string SummaryText => "Mesh 2DFV";

	public override bool HasZ
	{
		get
		{
			if (_extent == null)
			{
				return false;
			}
			return !double.IsNaN(_extent.MinZ);
		}
	}

	public override bool HasM
	{
		get
		{
			if (_extent == null)
			{
				return false;
			}
			return !double.IsNaN(_extent.MinM);
		}
	}

	public override double MaxX
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MaxX;
		}
	}

	public override double MinX
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MinX;
		}
	}

	public override double MaxY
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MaxY;
		}
	}

	public override double MinY
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MinY;
		}
	}

	public override double MaxZ
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MaxZ;
		}
	}

	public override double MinZ
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MinZ;
		}
	}

	public override double MaxM
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MaxM;
		}
	}

	public override double MinM
	{
		get
		{
			if (_extent == null)
			{
				return double.NaN;
			}
			return _extent.MinM;
		}
	}

	public override bool MaxMinXYDefined
	{
		get
		{
			if (_extent == null)
			{
				return false;
			}
			return _extent.MaxMinXYDefined;
		}
	}

	public override bool UsesBrush => true;

	public override bool UsesPen => true;

	public override bool UsesPointSymbol => true;

	public override bool UsesSurfaceFill => false;

	public override int Count => CellCount;

	public static bool TrySetMinFaceLengthRatio(double v)
	{
		if (v > 0.0 && v < 1.0)
		{
			ActualMinFaceLengthRatio = v;
			return true;
		}
		return false;
	}

	public static void ResetMinFaceLengthRatio()
	{
		ActualMinFaceLengthRatio = 0.05;
	}

	static MeshFV2D()
	{
		ActualMinFaceLengthRatio = 0.05;
		ActualValidatePolygonContainsCell = false;
		USE_FACE_MIDSIDE_CACHING = false;
		_poDisableMeshEdges = new PlotOption("Disable Mesh Edges");
		_poCellNumbers = new PlotOption("Mesh Cell Numbers");
		_poFaceNumbers = new PlotOption("Mesh Face Numbers");
		_poFaceMinElev = new PlotOption("Mesh Face Minimum Elevation");
		_poFacePointNumbers = new PlotOption("Mesh Face Point Numbers");
		_poRasterizeTIN = new PlotOption("Rasterize Mesh as TIN", "", debugOption: true);
		_poGraphConnectivity = new PlotOption("Mesh Graph (Connectivity)", "", debugOption: true);
		_poFaceMidpoints = new PlotOption("Mesh Face Midpoints", "", debugOption: true);
		PixelRenderingCutoff = 5;
		TEST_INCLUDE_CANDIDATE_FACE_VELS = true;
		_debugPlotOptions = new List<PlotOption>();
		_plotOptions = new List<PlotOption>();
		_debugPlotOptions.Add(_poDisableMeshEdges);
		_debugPlotOptions.Add(_poCellNumbers);
		_debugPlotOptions.Add(_poFaceNumbers);
		_debugPlotOptions.Add(_poFaceMinElev);
		_debugPlotOptions.Add(_poFacePointNumbers);
		_debugPlotOptions.Add(_poFaceMidpoints);
		_debugPlotOptions.Add(_poRasterizeTIN);
		_debugPlotOptions.Add(_poGraphConnectivity);
		_plotOptions.AddRange(_debugPlotOptions.Where([SpecialName] (PlotOption po) => !po.IsDebugOption));
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return GetAvailablePlotOptions(debugMode);
	}

	public static List<PlotOption> GetAvailablePlotOptions(bool debugMode)
	{
		if (debugMode)
		{
			return _debugPlotOptions;
		}
		return _plotOptions;
	}

	private float MinAllowedFaceLength()
	{
		if (SharedData.Units == SharedData.EnumUnits.SIUnits)
		{
			return 0.003f;
		}
		return 0.01f;
	}

	public static MeshFV2D NullMesh()
	{
		return new MeshFV2D();
	}

	private MeshFV2D()
	{
		FacePoints = new List<FacePoint>();
		Faces = new List<Face>();
		_badIndexes = new List<int>();
		FatalPerimeterError = false;
		_cellMinimumElevationSet = false;
		_filterToleranceTIN = 0.01;
		_progressReporter = ProgressReporter.DebugPrint();
		_virtCellCt = -1;
		_faceNormal = null;
		_faceMidside = null;
		prevApprFactor = -1f;
		prevIterCt = -1;
		_cells = new Cell[0];
		Faces = new List<Face>();
		FacePoints = new List<FacePoint>();
		_perimeter = Polygon.NullPolygon();
		_extent = Extent.NullExtent;
		_meshCompletionState = MeshStatus.UnknownError;
		_statusMessage = "Mesh not created";
	}

	public MeshFV2D(string filename, string meshname, ProgressReporter reporter = null)
	{
		FacePoints = new List<FacePoint>();
		Faces = new List<Face>();
		_badIndexes = new List<int>();
		FatalPerimeterError = false;
		_cellMinimumElevationSet = false;
		_filterToleranceTIN = 0.01;
		_progressReporter = ProgressReporter.DebugPrint();
		_virtCellCt = -1;
		_faceNormal = null;
		_faceMidside = null;
		prevApprFactor = -1f;
		prevIterCt = -1;
		using H5Reader hr = new H5Reader(filename);
		ConstructFromHDF5(hr, meshname, reporter);
	}

	public MeshFV2D(H5Reader hr, string meshname, ProgressReporter reporter = null)
	{
		FacePoints = new List<FacePoint>();
		Faces = new List<Face>();
		_badIndexes = new List<int>();
		FatalPerimeterError = false;
		_cellMinimumElevationSet = false;
		_filterToleranceTIN = 0.01;
		_progressReporter = ProgressReporter.DebugPrint();
		_virtCellCt = -1;
		_faceNormal = null;
		_faceMidside = null;
		prevApprFactor = -1f;
		prevIterCt = -1;
		ConstructFromHDF5(hr, meshname, reporter);
		SharedData.SetCachedMesh(hr.Filename, meshname, this);
	}

	private void ConstructFromHDF5(H5Reader hr, string meshname, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		string meshGroupName = MakeMeshGroupName(meshname);
		if (!hr.ExistsAsGroup(meshGroupName))
		{
			string statusMessage = "Mesh error for " + meshname.InSingleQuotes() + " in " + hr.Filename.InSingleQuotes() + ". Edit and recompute this mesh.";
			_statusMessage = statusMessage;
			_meshCompletionState = MeshStatus.UnknownError;
			return;
		}
		checked
		{
			try
			{
				List<string> attributeNames = hr.GetAttributeNames(meshGroupName);
				double[] values = new double[1];
				if (attributeNames.Contains("Extents"))
				{
					hr.ReadAttribute(meshGroupName, "Extents", ref values);
					_extent = new Extent(values);
				}
				else
				{
					_extent = null;
				}
				if (attributeNames.Contains("Cell Maximum Index"))
				{
					hr.ReadAttributeScalar(meshGroupName, "Cell Maximum Index", ref _cellMaximumAreaIndex);
				}
				else
				{
					_cellMaximumAreaIndex = -1;
				}
				if (attributeNames.Contains("Cell Maximum Size"))
				{
					hr.ReadAttributeScalar(meshGroupName, "Cell Maximum Size", ref _cellMaximumArea);
				}
				else
				{
					_cellMaximumArea = 0f;
				}
				if (attributeNames.Contains("Cell Minimum Size"))
				{
					hr.ReadAttributeScalar(meshGroupName, "Cell Minimum Size", ref _cellMinimumArea);
				}
				else
				{
					_cellMinimumArea = 0f;
				}
				if (attributeNames.Contains("Cell Average Size"))
				{
					hr.ReadAttributeScalar(meshGroupName, "Cell Average Size", ref _cellAverageArea);
				}
				else
				{
					_cellAverageArea = 0f;
				}
				if (hr.ExistsAsDataset(meshGroupName + "/Perimeter"))
				{
					using HDFBufferedReader<double> hDFBufferedReader = new HDFBufferedReader<double>(hr, meshGroupName + "/Perimeter");
					PointMs pointMs = new PointMs();
					pointMs.Capacity = hDFBufferedReader.Rows;
					int count = hDFBufferedReader.Rows - 1;
					double col = default;
					double col2 = default;
					for (int i = 0; i <= count; i++)
					{
						hDFBufferedReader.ReadRow(ref col, ref col2);
						pointMs.Add(col, col2);
					}
					_perimeter = new Polygon(pointMs);
				}
				if (!hr.ExistsAsDataset(meshGroupName + "/Cells Center Coordinate"))
				{
					string statusMessage2 = "Mesh error for " + meshname.InSingleQuotes() + " in " + hr.Filename.InSingleQuotes() + ". Edit and recompute this mesh.";
					_statusMessage = statusMessage2;
					_meshCompletionState = MeshStatus.UnknownError;
					return;
				}
				using (HDFBufferedReader<double> hDFBufferedReader2 = new HDFBufferedReader<double>(hr, meshGroupName + "/Cells Center Coordinate"))
				{
					_cells = new Cell[hDFBufferedReader2.Rows - 1 + 1];
					int count2 = hDFBufferedReader2.Rows - 1;
					double col3 = default;
					double col4 = default;
					for (int j = 0; j <= count2; j++)
					{
						hDFBufferedReader2.ReadRow(ref col3, ref col4);
						_cells[j] = new Cell(col3, col4);
					}
				}
				reporter.ReportProgress(5);
				using (HDFBufferedReader<int> hDFBufferedReader3 = new HDFBufferedReader<int>(hr, meshGroupName + "/Cells Face and Orientation Info"))
				{
					using HDFBufferedReader<int> hDFBufferedReader4 = new HDFBufferedReader<int>(hr, meshGroupName + "/Cells Face and Orientation Values");
					int count3 = _cells.Count() - 1;
					int col5 = default;
					int col6 = default;
					int col7 = default;
					int col8 = default;
					for (int k = 0; k <= count3; k++)
					{
						Cell cell = _cells[k];
						hDFBufferedReader3.ReadRow(ref col5, ref col6);
						int[] intValues = (int[])(cell.Faces = new int[col6 - 1 + 1]);
						int count4 = col6 - 1;
						for (int l = 0; l <= count4; l++)
						{
							hDFBufferedReader4.ReadRow(ref col7, ref col8);
							intValues[l] = col7;
						}
						_cells[k] = cell;
					}
				}
				reporter.ReportProgress(30);
				if (hr.ExistsAsDataset(CellMinimumDatset(meshGroupName)))
				{
					using (HDFBufferedReader<float> hDFBufferedReader5 = new HDFBufferedReader<float>(hr, CellMinimumDatset(meshGroupName)))
					{
						int count5 = _cells.Count() - 1;
						for (int m = 0; m <= count5; m++)
						{
							Cell cell2 = _cells[m];
							if (cell2.Faces.Count > 1)
							{
								hDFBufferedReader5.ReadRow(ref cell2.MinimumElevation);
								_cells[m] = cell2;
							}
						}
					}
					_cellMinimumElevationSet = true;
				}
				using (HDFBufferedReader<int> hDFBufferedReader6 = new HDFBufferedReader<int>(hr, meshGroupName + "/Faces FacePoint Indexes"))
				{
					using HDFBufferedReader<int> hDFBufferedReader7 = new HDFBufferedReader<int>(hr, meshGroupName + "/Faces Cell Indexes");
					Faces.Capacity = hDFBufferedReader6.Rows;
					int count6 = hDFBufferedReader6.Rows - 1;
					for (int n = 0; n <= count6; n++)
					{
						Face face = new Face();
						hDFBufferedReader6.ReadRow(ref face.fpA, ref face.fpB);
						hDFBufferedReader7.ReadRow(ref face.cellA, ref face.cellB);
						Faces.Add(face);
					}
				}
				reporter.ReportProgress(55);
				using (HDFBufferedReader<int> hDFBufferedReader8 = new HDFBufferedReader<int>(hr, meshGroupName + "/Faces Perimeter Info"))
				{
					using HDFBufferedReader<double> hDFBufferedReader9 = new HDFBufferedReader<double>(hr, meshGroupName + "/Faces Perimeter Values");
					int count7 = Faces.Count - 1;
					int col9 = default;
					int col10 = default;
					double col11 = default;
					double col12 = default;
					for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
					{
						Face face2 = Faces[intervalIndex];
						hDFBufferedReader8.ReadRow(ref col9, ref col10);
						if (col10 > 0)
						{
							PointMs pointMs2 = new PointMs();
							int count8 = col10 - 1;
							for (int ordinal = 0; ordinal <= count8; ordinal++)
							{
								hDFBufferedReader9.ReadRow(ref col11, ref col12);
								pointMs2.Add(col11, col12);
							}
							face2.InternalPoints = pointMs2;
						}
					}
				}
				reporter.ReportProgress(63);
				if (hr.ExistsAsDataset(meshGroupName + "/Faces Minimum Elevation"))
				{
					using HDFBufferedReader<float> hDFBufferedReader10 = new HDFBufferedReader<float>(hr, meshGroupName + "/Faces Minimum Elevation");
					if (hDFBufferedReader10.Rows == Faces.Count)
					{
						int count9 = Faces.Count - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count9; intervalIndex2++)
						{
							hDFBufferedReader10.ReadRow(ref Faces[intervalIndex2].MinimumElevation);
						}
					}
				}
				using (HDFBufferedReader<double> hDFBufferedReader11 = new HDFBufferedReader<double>(hr, meshGroupName + "/FacePoints Coordinate"))
				{
					FacePoints.Capacity = hDFBufferedReader11.Rows;
					int count10 = hDFBufferedReader11.Rows - 1;
					double col13 = default;
					double col14 = default;
					for (int ordinal2 = 0; ordinal2 <= count10; ordinal2++)
					{
						hDFBufferedReader11.ReadRow(ref col13, ref col14);
						FacePoint pointM = new FacePoint(new PointM(col13, col14));
						FacePoints.Add(pointM);
					}
				}
				reporter.ReportProgress(80);
				using (HDFBufferedReader<int> hDFBufferedReader12 = new HDFBufferedReader<int>(hr, meshGroupName + "/FacePoints Face and Orientation Info"))
				{
					using HDFBufferedReader<int> hDFBufferedReader13 = new HDFBufferedReader<int>(hr, meshGroupName + "/FacePoints Face and Orientation Values");
					int count11 = FacePoints.Count - 1;
					int col15 = default;
					int col16 = default;
					int col17 = default;
					int col18 = default;
					for (int intervalIndex3 = 0; intervalIndex3 <= count11; intervalIndex3++)
					{
						FacePoint facePoint = FacePoints[intervalIndex3];
						hDFBufferedReader12.ReadRow(ref col15, ref col16);
						int[] faces = new int[col16 - 1 + 1];
						facePoint.Faces = faces;
						if (col16 > 0)
						{
							int count12 = col16 - 1;
							for (int intervalIndex4 = 0; intervalIndex4 <= count12; intervalIndex4++)
							{
								hDFBufferedReader13.ReadRow(ref col17, ref col18);
								facePoint.Faces[intervalIndex4] = col17;
							}
						}
						FacePoints[intervalIndex3] = facePoint;
					}
				}
				reporter.ReportProgress(100);
				stopwatch.Stop();
				_statusMessage = "Success: Existing mesh read from hdf file in " + (double)stopwatch.ElapsedMilliseconds / 1000.0 + " seconds.";
				_meshCompletionState = MeshStatus.Complete;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				string statusMessage3 = "Unknown error reading mesh from '" + hr.Filename + "'. Recompute this geometry.";
				_statusMessage = statusMessage3;
				_meshCompletionState = MeshStatus.UnknownError;
				ProjectData.ClearProjectError();
			}
		}
	}

	public MeshFV2D(Polygon perimeter, IPoints cellCenters, Polyline breakline = null, ProgressReporter reporter = null)
	{
		FacePoints = new List<FacePoint>();
		Faces = new List<Face>();
		_badIndexes = new List<int>();
		FatalPerimeterError = false;
		_cellMinimumElevationSet = false;
		_filterToleranceTIN = 0.01;
		_progressReporter = ProgressReporter.DebugPrint();
		_virtCellCt = -1;
		_faceNormal = null;
		_faceMidside = null;
		prevApprFactor = -1f;
		prevIterCt = -1;
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		checked
		{
			try
			{
				reporter.ReportMessage("Validating input data...");
				_perimeter = GetCCWPerimeter(perimeter);
				if (!ValidateInput(_perimeter, cellCenters))
				{
					return;
				}
				_extent = new Extent((IExtent)_perimeter);
				_filterToleranceTIN = GetFilterTolerance(_perimeter);
				DevelopmentTin perimeterTin = GetPerimeterTin(_perimeter);
				if (!ValidatePointsInsidePerim(cellCenters, perimeterTin))
				{
					return;
				}
				reporter.ReportProgress(2);
				Stopwatch stopwatch2 = Stopwatch.StartNew();
				Polygon polygon = GeoProcessing.BufferPolygon(_perimeter, -1.0 * (_filterToleranceTIN / 10.0));
				stopwatch2.Stop();
				_progressReporter.ReportMessage("Buffering Polygon took " + stopwatch2.ElapsedMilliseconds + " ms");
				DevelopmentTin developmentTin = null;
				if (polygon != null)
				{
					developmentTin = GetPerimeterTin(polygon);
				}
				else
				{
					developmentTin = perimeterTin;
					_progressReporter.ReportMessage("Error buffering polygon in mesh constructor");
				}
				reporter.ReportMessage("Creating the Mesh TIN...");
				DevelopmentTin meshTin = null;
				ProgressReporter reporter2 = reporter.WithProgressModifier(2, 0.6f);
				if (!GetValidateMeshTIN(cellCenters, ref meshTin, _extent, reporter2))
				{
					return;
				}
				reporter.ReportProgress(62);
				_cells = new Cell[meshTin.Points.Count - 1 - DelaunayTINFactory.SUPER_POINT_COUNT + 1];
				FacePoints = new List<FacePoint>((int)System.Math.Round(System.Math.Pow(2.0, System.Math.Floor(System.Math.Log(_cells.Count(), 2.0)))));
				reporter.ReportMessage("Setting Cell/Face connectivity...");
				DistinctSpatialIndex perimeterSpatialIndex = ComputePerimeterSpatialIndex();
				SetCellsAndFaces(meshTin, perimeterTin, developmentTin, perimeterSpatialIndex, reporter2);
				reporter.ReportProgress(89);
				meshTin = null;
				reporter.ReportMessage("Generating Facepoints...");
				if (!SetFacepoints(perimeterSpatialIndex, perimeterTin))
				{
					return;
				}
				reporter.ReportProgress(95);
				perimeterTin = null;
				InternalFacepointInsertions();
				reporter.ReportMessage("Validating Mesh connectivity...");
				if (!ValidateDCSubgraphs())
				{
					return;
				}
				reporter.ReportMessage("Applying Breaklines...");
				if (!ApplyBreaklinesOld(breakline))
				{
					return;
				}
				reporter.ReportProgress(98);
				bool needsAnotherPass = true;
				int ordinal = 0;
				int allCollapsibleFaceCountSum = 0;
				Stopwatch stopwatch3 = Stopwatch.StartNew();
				while (needsAnotherPass && ordinal < 8)
				{
					List<int> allCollapsibleFaces = GetAllCollapsibleFaces(ref needsAnotherPass);
					allCollapsibleFaceCountSum += allCollapsibleFaces.Count;
					if (allCollapsibleFaces.Count == 0)
					{
						break;
					}
					if (!CollapseFaces(allCollapsibleFaces))
					{
						return;
					}
					ordinal++;
				}
				stopwatch3.Stop();
				_progressReporter.ReportMessage("Collapsed " + allCollapsibleFaceCountSum + " faces in " + ordinal + " passes and " + stopwatch3.ElapsedMilliseconds + " ms");
				reporter.ReportMessage("Computing Cell statistics...");
				if (ActualValidatePolygonContainsCell)
				{
					List<int> badCells = ComputeCellStatistics_WithPgonCheck();
					if (!ValidatePointInCell(badCells))
					{
						return;
					}
				}
				else
				{
					ComputeCellStatistics();
				}
				reporter.ReportProgress(100);
				reporter.ReportMessage("Validating Facepoints and Cells...");
				if (!ValidateAreaCheck())
				{
					return;
				}
				AddVirtualCells();
				if (!ValidateAllFacepointsSet() || !ValidateMinFaceLength() || !ValidateNumFacesPerCell() || !ValidateNumFacesPerFacepoint())
				{
					return;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				_badIndexes = new List<int>();
				_meshCompletionState = MeshStatus.UnknownError;
				_statusMessage = "Unknown Error: " + ex2.Message;
				ProjectData.ClearProjectError();
				return;
			}
			stopwatch.Stop();
			_meshCompletionState = MeshStatus.Complete;
			_statusMessage = "Success: Created in " + Form.ElapsedTimeToString(stopwatch);
		}
	}

	public MeshFV2D(Polygon perimeter, Cell[] cells, Face[] faces, FacePoint[] facePoints)
	{
		FacePoints = new List<FacePoint>();
		Faces = new List<Face>();
		_badIndexes = new List<int>();
		FatalPerimeterError = false;
		_cellMinimumElevationSet = false;
		_filterToleranceTIN = 0.01;
		_progressReporter = ProgressReporter.DebugPrint();
		_virtCellCt = -1;
		_faceNormal = null;
		_faceMidside = null;
		prevApprFactor = -1f;
		prevIterCt = -1;
		_perimeter = GetCCWPerimeter(perimeter);
		_extent = new Extent((IExtent)_perimeter);
		_filterToleranceTIN = GetFilterTolerance(_perimeter);
		_cells = cells;
		Faces = faces.ToList();
		FacePoints = facePoints.ToList();
		AddVirtualCells();
		_meshCompletionState = MeshStatus.Complete;
		_statusMessage = "Success: Mesh created from import data";
	}

	private static double GetFilterTolerance(Polygon perim)
	{
		double unitsSharedDataTerm = ((SharedData.Units != SharedData.EnumUnits.SIUnits) ? 0.1 : 0.03);
		double deltaXY = perim.ComputeExtent().DeltaXY;
		if (double.IsNaN(deltaXY) || deltaXY == 0.0)
		{
			return unitsSharedDataTerm;
		}
		return System.Math.Min(unitsSharedDataTerm, deltaXY * 0.0001);
	}

	private void RMExport(string layerName, DevelopmentTin tin)
	{
		RMExport(layerName, tin.DebugExportTrisAsPolygonLayer(layerName));
	}

	private void RMExport<T>(string layerName, List<T> fts, Color? clr = null, int size = 1, byte alpha = byte.MaxValue) where T : Feature
	{
		if (!clr.HasValue)
		{
			clr = Color.Black;
		}
		FeatureLayer featureLayer = null;
		if ((object)typeof(T) == typeof(Polygon))
		{
			featureLayer = new PolygonFeatureLayer(layerName);
		}
		else if ((object)typeof(T) == typeof(Polyline))
		{
			featureLayer = new PolygonFeatureLayer(layerName);
		}
		else
		{
			if ((object)typeof(T) != typeof(MultiPoint))
			{
				return;
			}
			featureLayer = new PolygonFeatureLayer(layerName);
		}
		foreach (Feature ft in fts)
		{
			featureLayer.AddFeature(ft);
		}
		RMExport(layerName, featureLayer);
	}

	private void RMExport(string layerName, FeatureLayer nodeLayer)
	{
		if (SharedData.RasMapper != null)
		{
			nodeLayer.Checked = true;
		}
	}

	public bool IsComplete()
	{
		return MeshCompletionState == MeshStatus.Complete;
	}

	public void Stream(BinaryWriter bw)
	{
		_perimeter.Stream(bw);
		RasMapperLib.Mesh.Cell.Stream(bw, _cells);
		Face.Stream(bw, Faces);
		FacePoint.Stream(bw, FacePoints);
	}

	public static MeshFV2D Stream(BinaryReader br)
	{
		MeshFV2D meshFV2D = new MeshFV2D
		{
			_perimeter = Polygon.Stream(br),
			_cells = RasMapperLib.Mesh.Cell.Stream(br),
			Faces = Face.Stream(br).ToList(),
			FacePoints = FacePoint.Stream(br).ToList()
		};
		meshFV2D._extent = meshFV2D._perimeter.ComputeExtent();
		return meshFV2D;
	}

	public bool SanityCheck()
	{
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				Cell cell = _cells[i];
				foreach (int face3 in cell.Faces)
				{
					Face face = Faces[face3];
					if (face.cellA != i && face.cellB != i)
					{
						return false;
					}
				}
			}
			int count2 = Faces.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				Face face2 = Faces[j];
				if (face2.cellA != -1)
				{
					Cell cell2 = _cells[face2.cellA];
					bool condition = false;
					foreach (int face4 in cell2.Faces)
					{
						if (face4 == j)
						{
							condition = true;
						}
					}
					if (!condition)
					{
						return false;
					}
				}
				if (face2.cellB != -1)
				{
					Cell cell3 = _cells[face2.cellB];
					bool condition2 = false;
					foreach (int face5 in cell3.Faces)
					{
						if (face5 == j)
						{
							condition2 = true;
						}
					}
					if (!condition2)
					{
						return false;
					}
				}
				FacePoint facePoint = FacePoints[face2.fpA];
				FacePoint facePoint2 = FacePoints[face2.fpB];
				bool condition3 = false;
				foreach (int face6 in facePoint.Faces)
				{
					if (face6 == j)
					{
						condition3 = true;
					}
				}
				if (!condition3)
				{
					return false;
				}
				bool condition4 = false;
				foreach (int face7 in facePoint2.Faces)
				{
					if (face7 == j)
					{
						condition4 = true;
					}
				}
				if (!condition4)
				{
					return false;
				}
			}
			int count3 = FacePoints.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				FacePoint facePoint3 = FacePoints[k];
				if (facePoint3.Faces.Count != facePoint3.Faces.Distinct().Count())
				{
					return false;
				}
			}
			return true;
		}
	}

	public void LoadTo(MeshMath meshMath)
	{
		meshMath.SetCells(_cells);
		meshMath.SetFacepoints(FacePoints);
		meshMath.SetFaces(Faces);
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(_cells.Select([SpecialName] (Cell cell) => cell.Point).ToArray());
		Perimeter.MD5HashAppend(hash);
	}

	public static string CellMinimumDatset(string meshgroup)
	{
		return meshgroup + "/Cells Minimum Elevation";
	}

	public static bool GetCellMinimum(H5Reader hr, string meshgroup, ref float[] cellMinimum)
	{
		if (cellMinimum == null)
		{
			return false;
		}
		bool result = false;
		checked
		{
			if (hr.ExistsAsDataset(CellMinimumDatset(meshgroup)))
			{
				using HDFBufferedReader<float> hDFBufferedReader = new HDFBufferedReader<float>(hr, CellMinimumDatset(meshgroup));
				if (hDFBufferedReader.Rows > cellMinimum.Count())
				{
					int count = cellMinimum.Count() - 1;
					for (int i = 0; i <= count; i++)
					{
						hDFBufferedReader.ReadRow(ref cellMinimum[i]);
					}
					result = true;
				}
				else
				{
					result = false;
				}
			}
			return result;
		}
	}

	public void EnsureSquareCellsCached()
	{
		checked
		{
			if (SquareCells == null)
			{
				SquareCells = new BitArray(_cells.Count());
				int count = _cells.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					SquareCells[i] = IsCellSquare(i);
				}
			}
		}
	}

	private bool IsCellSquare(int cidx)
	{
		if (_cells[cidx].Faces.Count != 4)
		{
			return false;
		}
		List<int> cellFacePoints = CellFacePoints(cidx);
		FacePoint facePoint = FacePoints[cellFacePoints[0]];
		FacePoint facePoint2 = FacePoints[cellFacePoints[1]];
		FacePoint facePoint3 = FacePoints[cellFacePoints[2]];
		FacePoint facePoint4 = FacePoints[cellFacePoints[3]];
		double max = default;
		double min = default;
		RasMapperLib.Utilities.Math.ArrayMaxMin(new double[4]
		{
			facePoint.Point.X,
			facePoint2.Point.X,
			facePoint3.Point.X,
			facePoint4.Point.X
		}, ref max, ref min);
		double max2 = default;
		double min2 = default;
		RasMapperLib.Utilities.Math.ArrayMaxMin(new double[4]
		{
			facePoint.Point.Y,
			facePoint2.Point.Y,
			facePoint3.Point.Y,
			facePoint4.Point.Y
		}, ref max2, ref min2);
		float pointFacePointTerm = (float)(facePoint.Point.X - min);
		float pointFacePoint2Term = (float)(facePoint2.Point.X - min);
		float pointFacePoint3Term = (float)(facePoint3.Point.X - min);
		float pointFacePoint4Term = (float)(facePoint4.Point.X - min);
		float pointFacePointTerm2 = (float)(facePoint.Point.Y - min2);
		float pointFacePoint2Term2 = (float)(facePoint2.Point.Y - min2);
		float pointFacePoint3Term2 = (float)(facePoint3.Point.Y - min2);
		float pointFacePoint4Term2 = (float)(facePoint4.Point.Y - min2);
		float computed = pointFacePointTerm + pointFacePoint2Term + pointFacePoint3Term + pointFacePoint4Term;
		float computed2 = pointFacePointTerm2 + pointFacePoint2Term2 + pointFacePoint3Term2 + pointFacePoint4Term2;
		float computed3 = computed / 4f;
		float computed4 = computed2 / 4f;
		float systemTerm = System.Math.Abs(pointFacePointTerm - computed3);
		if (System.Math.Abs(System.Math.Abs(pointFacePointTerm - computed3) - systemTerm) > 0.001f || System.Math.Abs(System.Math.Abs(pointFacePointTerm2 - computed4) - systemTerm) > 0.001f)
		{
			return false;
		}
		if (System.Math.Abs(System.Math.Abs(pointFacePoint2Term - computed3) - systemTerm) > 0.001f || System.Math.Abs(System.Math.Abs(pointFacePoint2Term2 - computed4) - systemTerm) > 0.001f)
		{
			return false;
		}
		if (System.Math.Abs(System.Math.Abs(pointFacePoint3Term - computed3) - systemTerm) > 0.001f || System.Math.Abs(System.Math.Abs(pointFacePoint3Term2 - computed4) - systemTerm) > 0.001f)
		{
			return false;
		}
		if (System.Math.Abs(System.Math.Abs(pointFacePoint4Term - computed3) - systemTerm) > 0.001f || System.Math.Abs(System.Math.Abs(pointFacePoint4Term2 - computed4) - systemTerm) > 0.001f)
		{
			return false;
		}
		return true;
	}

	private bool GetValidateMeshTIN(IPoints cellCenters, ref DevelopmentTin meshTin, Extent MBR, ProgressReporter reporter)
	{
		List<int> intList = new List<int>();
		Action<int> markDuplicates = [SpecialName] (int pt) =>
		{
			intList.Add(pt);
		};
		MBR = RatioMBR(cellCenters, MBR);
		Stopwatch stopwatch = Stopwatch.StartNew();
		DelaunayTINFactory delaunayTINFactory = new DelaunayTINFactory(_filterToleranceTIN);
		meshTin = delaunayTINFactory.ComputeTIN(null, null, cellCenters, null, removeDeletedTriangles: false, double.NaN, reporter, markDuplicates, MBR);
		stopwatch.Stop();
		_progressReporter.ReportMessage("Mesh TIN computed in " + stopwatch.ElapsedMilliseconds + " ms.");
		return ValidateDuplicatePoints(meshTin, cellCenters, intList);
	}

	private bool GetValidateMeshTIN_NEW(IPoints cellCenters, Polyline breakline, Polygon perim, ref DevelopmentTin meshTin, Extent MBR, ProgressReporter reporter)
	{
		_Closure_0024__83_002D0 capturedLocals = default;
		_Closure_0024__83_002D0 capturedLocals2 = new _Closure_0024__83_002D0(capturedLocals);
		bool condition = true;
		capturedLocals2.duplicatePoints = new List<int>();
		Action<int> markDuplicates = [SpecialName] (int pt) =>
		{
			capturedLocals2.duplicatePoints.Add(pt);
		};
		int cellCt = cellCenters.PointMCount();
		MBR = RatioMBR(cellCenters, MBR);
		DelaunayTINFactory delaunayTINFactory = new DelaunayTINFactory(_filterToleranceTIN);
		if (breakline == null)
		{
			breakline = new Polyline(new PointMs());
		}
		breakline = breakline.FilterXY_PRESERVEPARTS(_filterToleranceTIN);
		DevelopmentTin developmentTin = delaunayTINFactory.ComputeTIN(null, null, cellCenters, null, removeDeletedTriangles: false, double.NaN, null, markDuplicates, MBR);
		if (condition)
		{
			RMExport("Base TIN", developmentTin);
		}
		PointMs faceSegs = null;
		List<int> facePlineStarts = null;
		GetNaiiveFaceSegments(developmentTin, ref faceSegs, ref facePlineStarts);
		Polyline polyline = new Polyline(faceSegs, facePlineStarts);
		checked
		{
			if (condition)
			{
				List<Polyline> polylines = new List<Polyline>();
				int count = breakline.SegmentCount() - 1;
				for (int ordinal = 0; ordinal <= count; ordinal++)
				{
					SegmentM segmentM = breakline.Segment(ordinal);
					polylines.Add(new Polyline(new PointMs(new PointM[2] { segmentM.A, segmentM.B })));
				}
				RMExport("Breakline Reconstruction", polylines);
			}
			DevelopmentTin developmentTin2 = delaunayTINFactory.ComputeTIN(perim, new Polyline[2] { breakline, polyline }.ToList(), cellCenters, null, removeDeletedTriangles: false, double.NaN, null, null, MBR);
			if (condition)
			{
				RMExport("Fully Enforced TIN", developmentTin2);
			}
			int massPointInsertionsStartHere = delaunayTINFactory.MassPointInsertionsStartHere;
			new Dictionary<int, byte>();
			Dictionary<int, byte> triangleEdgesBlocked = new Dictionary<int, byte>();
			List<int>[] cellTris = null;
			List<Polyline> blockedSegs = null;
			int[] triOwner = null;
			TrianglePass(developmentTin2, cellCt, massPointInsertionsStartHere, breakline, ref cellTris, ref triOwner, ref triangleEdgesBlocked, ref blockedSegs);
			if (condition)
			{
				RMExport("Blocked Edges", blockedSegs);
			}
			GetNeutralTriangles(developmentTin2, triOwner);
			List<Polygon> bucketPolys = new List<Polygon>();
			NeutralizeRecursive(developmentTin2, massPointInsertionsStartHere, triOwner, cellTris, triangleEdgesBlocked, bucketPolys);
			new List<Cell>(cellTris.Count() - 1);
			List<Polygon> polygons = new List<Polygon>(cellTris.Count() - 1);
			int count2 = cellTris.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				Polygon mergeTrianglesToPolygon = MergeTrianglesToPolygon(developmentTin2, cellTris[intervalIndex]);
				polygons.Add(mergeTrianglesToPolygon);
			}
			if (condition)
			{
				RMExport("Cell Polygons", polygons, Color.AliceBlue, 1, 60);
			}
			meshTin = delaunayTINFactory.ComputeTIN(perim, new Polyline[1] { breakline }.ToList(), cellCenters, null, removeDeletedTriangles: false, double.NaN, reporter, markDuplicates, MBR);
			return ValidateDuplicatePoints(meshTin, cellCenters, capturedLocals2.duplicatePoints);
		}
	}

	private Extent RatioMBR(IPoints cellCenters, Extent mbr)
	{
		if (cellCenters.PointMCount() < 100)
		{
			mbr = mbr.Ratio(3.0);
		}
		else if (cellCenters.PointMCount() < 1000)
		{
			mbr = mbr.Ratio(2.0);
		}
		else if (cellCenters.PointMCount() < 10000)
		{
			mbr = mbr.Ratio(1.1);
		}
		return mbr;
	}

	private List<int> Floodfill(DevelopmentTin devTin, int startFromTri, int[] triangleValues, Dictionary<int, byte> triangleEdgesBlocked, int stopIfTriangleIndexFound = -1)
	{
		int triangleValue = triangleValues[startFromTri];
		HashSet<int> intSet = new HashSet<int>();
		intSet.Add(startFromTri);
		List<int> intList = new List<int>();
		intList.Add(startFromTri);
		while (intList.Count > 0)
		{
			List<int> intListList = new List<int>();
			foreach (int ordinal in intList)
			{
				DevelopmentTriangle developmentTriangle = devTin.Triangles[ordinal];
				int at = developmentTriangle.at0;
				int at2 = developmentTriangle.at1;
				int at3 = developmentTriangle.at2;
				byte localValue = 0;
				triangleEdgesBlocked.TryGetValue(ordinal, out localValue);
				bool condition = false;
				bool condition2 = false;
				bool condition3 = false;
				if ((localValue & 1) > 0)
				{
					condition = true;
				}
				if ((localValue & 2) > 0)
				{
					condition2 = true;
				}
				if ((localValue & 4) > 0)
				{
					condition3 = true;
				}
				if (at != -1 && triangleValues[at] == triangleValue && !devTin.Triangles[at].IsDeleted() && !condition && !intSet.Contains(at))
				{
					intSet.Add(at);
					intListList.Add(at);
					if (at == stopIfTriangleIndexFound)
					{
						goto end_IL_0190;
					}
				}
				if (at2 != -1 && triangleValues[at2] == triangleValue && !devTin.Triangles[at2].IsDeleted() && !condition2 && !intSet.Contains(at2))
				{
					intSet.Add(at2);
					intListList.Add(at2);
					if (at2 == stopIfTriangleIndexFound)
					{
						goto end_IL_0190;
					}
				}
				if (at3 != -1 && triangleValues[at3] == triangleValue && !devTin.Triangles[at3].IsDeleted() && !condition3 && !intSet.Contains(at3))
				{
					intSet.Add(at3);
					intListList.Add(at3);
					if (at3 == stopIfTriangleIndexFound)
					{
						goto end_IL_0190;
					}
				}
			}
			intList = intListList;
			continue;
			end_IL_0190:
			break;
		}
		return intSet.ToList();
	}

	private void NeutralizeRecursive(DevelopmentTin devTin, int ptIndexStartFrom, int[] triOwner, List<int>[] cellTris, Dictionary<int, byte> triangleEdgesBlocked, List<Polygon> bucketPolys = null)
	{
		int neutralTrianglesTerm = -1;
		while (true)
		{
			List<int> neutralTriangles = GetNeutralTriangles(devTin, triOwner);
			if (neutralTriangles.Count == neutralTrianglesTerm)
			{
				PointMs pointMs = new PointMs();
				foreach (int neutralTriangle in neutralTriangles)
				{
					pointMs.Add(devTin.TriangleCentroid(neutralTriangle));
				}
				new MultiPoint(pointMs);
				break;
			}
			neutralTrianglesTerm = neutralTriangles.Count;
			if (neutralTriangles.Count != 0)
			{
				NeutralizeTriangles(devTin, ptIndexStartFrom, triOwner, cellTris, triangleEdgesBlocked, neutralTriangles, bucketPolys);
				continue;
			}
			break;
		}
	}

	private void NeutralizeTriangles(DevelopmentTin devTin, int ptIndexStartFrom, int[] triOwner, List<int>[] cellTris, Dictionary<int, byte> triangleEdgesBlocked, List<int> neutralTriangles, List<Polygon> bucketPolys = null)
	{
		BitArray bitArray = new BitArray(neutralTriangles.Count);
		checked
		{
			int count = neutralTriangles.Count - 1;
			_Closure_0024__87_002D0 capturedLocals = default;
			for (int i = 0; i <= count; i++)
			{
				if (bitArray[i])
				{
					continue;
				}
				int startFromTri = neutralTriangles[i];
				List<int> floodfill = Floodfill(devTin, startFromTri, triOwner, triangleEdgesBlocked);
				bucketPolys?.Add(MergeTrianglesToPolygon(devTin, floodfill));
				Dictionary<int, PointM> pointMByInt = new Dictionary<int, PointM>();
				foreach (int floodfill2 in floodfill)
				{
					bitArray[neutralTriangles.IndexOf(floodfill2)] = true;
					DevelopmentTriangle developmentTriangle = devTin.Triangles[floodfill2];
					int at = developmentTriangle.at0;
					int at2 = developmentTriangle.at1;
					int at3 = developmentTriangle.at2;
					int triOwner2 = ((at == -1) ? (-1) : triOwner[developmentTriangle.at0]);
					int triOwner3 = ((at2 == -1) ? (-1) : triOwner[developmentTriangle.at1]);
					int triOwner4 = ((at3 == -1) ? (-1) : triOwner[developmentTriangle.at2]);
					byte localValue = 0;
					triangleEdgesBlocked.TryGetValue(floodfill2, out localValue);
					bool condition = false;
					bool condition2 = false;
					bool condition3 = false;
					if ((localValue & 1) > 0)
					{
						condition = true;
					}
					if ((localValue & 2) > 0)
					{
						condition2 = true;
					}
					if ((localValue & 4) > 0)
					{
						condition3 = true;
					}
					if (triOwner2 != -1 && !condition && !pointMByInt.ContainsKey(triOwner2))
					{
						pointMByInt.Add(triOwner2, devTin.Points[ptIndexStartFrom + triOwner2]);
					}
					if (triOwner3 != -1 && !condition2 && !pointMByInt.ContainsKey(triOwner3))
					{
						pointMByInt.Add(triOwner3, devTin.Points[ptIndexStartFrom + triOwner3]);
					}
					if (triOwner4 != -1 && !condition3 && !pointMByInt.ContainsKey(triOwner4))
					{
						pointMByInt.Add(triOwner4, devTin.Points[ptIndexStartFrom + triOwner4]);
					}
				}
				if (pointMByInt.Count == 0)
				{
					continue;
				}
				if (pointMByInt.Count == 0)
				{
					throw new Exception("Disconnected group of triangles");
				}
				foreach (int floodfill3 in floodfill)
				{
					capturedLocals = new _Closure_0024__87_002D0(capturedLocals);
					capturedLocals.centroid = devTin.TriangleCentroid(floodfill3);
					cellTris[triOwner[floodfill3] = pointMByInt.OrderBy(capturedLocals._Lambda_0024__0).First().Key].Add(floodfill3);
				}
				foreach (int floodfill4 in floodfill)
				{
					int triOwner5 = triOwner[floodfill4];
					int cellTrisTriOwner5Term = cellTris[triOwner5].First();
					if (Floodfill(devTin, floodfill4, triOwner, triangleEdgesBlocked, cellTrisTriOwner5Term).Last() != cellTrisTriOwner5Term)
					{
						triOwner[floodfill4] = -1;
						cellTris[triOwner5].Remove(floodfill4);
					}
				}
			}
		}
	}

	private List<int> GetNeutralTriangles(DevelopmentTin devTin, int[] triOwner)
	{
		List<int> neutralTriangles = new List<int>();
		checked
		{
			int count = triOwner.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (triOwner[i] == -1 && !devTin.Triangles[i].IsDeleted())
				{
					neutralTriangles.Add(i);
				}
			}
			return neutralTriangles;
		}
	}

	private void TrianglePass(DevelopmentTin devTin, int cellCt, int ptIndexStartFrom, Polyline breakline, ref List<int>[] cellTris, ref int[] triOwner, ref Dictionary<int, byte> triangleEdgesBlocked, [Optional][DefaultParameterValue(null)] ref List<Polyline> blockedSegs)
	{
		checked
		{
			triOwner = new int[devTin.Triangles.Count - 1 + 1];
			cellTris = new List<int>[cellCt - 1 + 1];
			int count = cellCt - 1;
			for (int i = 0; i <= count; i++)
			{
				cellTris[i] = new List<int>(8);
			}
			blockedSegs = new List<Polyline>();
			triangleEdgesBlocked = new Dictionary<int, byte>();
			SpatialIndex<int> segmentSpatialIndex = SpatialIndex.GetSegmentSpatialIndex(breakline);
			int count2 = devTin.Triangles.Count - 1;
			double max = default;
			double min = default;
			double max2 = default;
			double min2 = default;
			for (int j = 0; j <= count2; j++)
			{
				DevelopmentTriangle developmentTriangle = devTin.Triangles[j];
				_ = developmentTriangle.at0;
				_ = developmentTriangle.at1;
				_ = developmentTriangle.at2;
				int p = developmentTriangle.p0;
				int p2 = developmentTriangle.p1;
				int p3 = developmentTriangle.p2;
				PointM pointM = devTin.Points[p];
				PointM pointM2 = devTin.Points[p2];
				PointM pointM3 = devTin.Points[p3];
				RasMapperLib.Utilities.Math.MaxMin3(pointM.X, pointM2.X, pointM3.X, ref max, ref min);
				RasMapperLib.Utilities.Math.MaxMin3(pointM.Y, pointM2.Y, pointM3.Y, ref max2, ref min2);
				List<int> elements = segmentSpatialIndex.GetElements(min - _filterToleranceTIN, max + _filterToleranceTIN, min2 - _filterToleranceTIN, max2 + _filterToleranceTIN);
				byte b = 0;
				unchecked
				{
					foreach (int element in elements)
					{
						SegmentM segmentM = breakline.Segment(element);
						PointM pt = pointM;
						double filterToleranceTIN = _filterToleranceTIN;
						double distance = 0.0;
						bool closeTo = segmentM.CloseTo(pt, filterToleranceTIN, ref distance);
						PointM pt2 = pointM2;
						double filterToleranceTIN2 = _filterToleranceTIN;
						distance = 0.0;
						bool closeTo2 = segmentM.CloseTo(pt2, filterToleranceTIN2, ref distance);
						PointM pt3 = pointM3;
						double filterToleranceTIN3 = _filterToleranceTIN;
						distance = 0.0;
						bool closeTo3 = segmentM.CloseTo(pt3, filterToleranceTIN3, ref distance);
						bool condition = false;
						bool condition2 = false;
						bool condition3 = false;
						if (closeTo && closeTo2)
						{
							condition = true;
						}
						if (closeTo2 && closeTo3)
						{
							condition2 = true;
						}
						if (closeTo3 && closeTo)
						{
							condition3 = true;
						}
						if (condition)
						{
							b |= 1;
							blockedSegs.Add(new Polyline(new PointMs(new PointM[2] { pointM, pointM2 })));
						}
						if (condition2)
						{
							b |= 2;
							blockedSegs.Add(new Polyline(new PointMs(new PointM[2] { pointM2, pointM3 })));
						}
						if (condition3)
						{
							b |= 4;
							blockedSegs.Add(new Polyline(new PointMs(new PointM[2] { pointM, pointM3 })));
						}
					}
					if (b != 0)
					{
						triangleEdgesBlocked.Add(j, b);
					}
				}
				int triOwner2 = p - ptIndexStartFrom;
				if (triOwner2 >= 0 && triOwner2 < cellCt)
				{
					cellTris[triOwner2].Add(j);
					triOwner[j] = triOwner2;
					continue;
				}
				int triOwner3 = p2 - ptIndexStartFrom;
				if (triOwner3 >= 0 && triOwner3 < cellCt)
				{
					cellTris[triOwner3].Add(j);
					triOwner[j] = triOwner3;
					continue;
				}
				int triOwner4 = p3 - ptIndexStartFrom;
				if (triOwner4 >= 0 && triOwner4 < cellCt)
				{
					cellTris[triOwner4].Add(j);
					triOwner[j] = triOwner4;
				}
				else
				{
					triOwner[j] = -1;
				}
			}
		}
	}

	private void GetNaiiveFaceSegments(DevelopmentTin baseTin, ref PointMs faceSegs, ref List<int> facePlineStarts)
	{
		List<PointM> pointMs = new List<PointM>(baseTin.Triangles.Count);
		checked
		{
			int count = baseTin.Triangles.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(baseTin.TriangleCircumcircle(i));
			}
			BitArray bitArray = new BitArray(baseTin.Triangles.Count);
			faceSegs = new PointMs((int)System.Math.Round((double)baseTin.Points.Count / 2.0));
			facePlineStarts = new List<int>();
			int ordinal = 0;
			int supeR_POINT_COUNTOffset = DelaunayTINFactory.SUPER_POINT_COUNT - 1;
			int count2 = baseTin.Triangles.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				DevelopmentTriangle developmentTriangle = baseTin.Triangles[j];
				int at = developmentTriangle.at0;
				int at2 = developmentTriangle.at1;
				int at3 = developmentTriangle.at2;
				int p = developmentTriangle.p0;
				int p2 = developmentTriangle.p1;
				int p3 = developmentTriangle.p2;
				PointM pA = pointMs[j];
				if (at != -1 && !bitArray[at] && p > supeR_POINT_COUNTOffset && p2 > supeR_POINT_COUNTOffset)
				{
					faceSegs.Add(new SegmentM(pA, pointMs[at]));
					facePlineStarts.Add(ordinal);
					ordinal += 2;
				}
				if (at2 != -1 && !bitArray[at2] && p3 > supeR_POINT_COUNTOffset && p2 > supeR_POINT_COUNTOffset)
				{
					faceSegs.Add(new SegmentM(pA, pointMs[at2]));
					facePlineStarts.Add(ordinal);
					ordinal += 2;
				}
				if (at3 != -1 && !bitArray[at3] && p > supeR_POINT_COUNTOffset && p3 > supeR_POINT_COUNTOffset)
				{
					faceSegs.Add(new SegmentM(pA, pointMs[at3]));
					facePlineStarts.Add(ordinal);
					ordinal += 2;
				}
				bitArray[j] = true;
			}
		}
	}

	private Polygon MergeTrianglesToPolygon(DevelopmentTin devTin, List<int> tris)
	{
		_Closure_0024__91_002D0 capturedLocals = default;
		_Closure_0024__91_002D0 capturedLocals2 = new _Closure_0024__91_002D0(capturedLocals);
		HashSet<int> trisSet = new HashSet<int>(tris);
		List<Tuple<int, int>> valueTupleList = new List<Tuple<int, int>>();
		foreach (int tri in trisSet)
		{
			DevelopmentTriangle developmentTriangle = devTin.Triangles[tri];
			int at = developmentTriangle.at0;
			int at2 = developmentTriangle.at1;
			int at3 = developmentTriangle.at2;
			int p = developmentTriangle.p0;
			int p2 = developmentTriangle.p1;
			int p3 = developmentTriangle.p2;
			if (!trisSet.Contains(at))
			{
				valueTupleList.Add(new Tuple<int, int>(p, p2));
			}
			if (!trisSet.Contains(at2))
			{
				valueTupleList.Add(new Tuple<int, int>(p2, p3));
			}
			if (!trisSet.Contains(at3))
			{
				valueTupleList.Add(new Tuple<int, int>(p3, p));
			}
		}
		PointMs pointMs = new PointMs(8);
		Tuple<int, int> tuple = valueTupleList.Last();
		capturedLocals2.curPt = tuple.Item1;
		int tupleTerm = tuple.Item2;
		pointMs.Add(devTin.Points[capturedLocals2.curPt]);
		valueTupleList.RemoveAt(checked(valueTupleList.Count - 1));
		while (true)
		{
			if (valueTupleList.Count == 0)
			{
				throw new NotImplementedException();
			}
			Tuple<int, int> tuple2 = valueTupleList.FirstOrDefault([SpecialName] (Tuple<int, int> kvp) => kvp.Item1 == capturedLocals2.curPt || kvp.Item2 == capturedLocals2.curPt);
			if (tuple2 == null)
			{
				throw new NotImplementedException();
			}
			valueTupleList.Remove(tuple2);
			if (tuple2.Item1 == capturedLocals2.curPt)
			{
				capturedLocals2.curPt = tuple2.Item2;
				pointMs.Add(devTin.Points[capturedLocals2.curPt]);
				if (capturedLocals2.curPt == tupleTerm)
				{
					break;
				}
			}
			else
			{
				capturedLocals2.curPt = tuple2.Item1;
				pointMs.Add(devTin.Points[capturedLocals2.curPt]);
				if (capturedLocals2.curPt == tupleTerm)
				{
					break;
				}
			}
		}
		Polygon polygon = new Polygon(pointMs);
		if (polygon.Area > 0.0)
		{
			polygon = polygon.Reverse();
		}
		return polygon;
	}

	private void SetCellsAndFaces(DevelopmentTin meshTIN, DevelopmentTin perimeterDevTIN, DevelopmentTin bufferedPerimeterDevTIN, DistinctSpatialIndex perimeterSpatialIndex, ProgressReporter reporter)
	{
		_Closure_0024__92_002D0 capturedLocals = default;
		_Closure_0024__92_002D0 capturedLocals2 = new _Closure_0024__92_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.perimeterSpatialIndex = perimeterSpatialIndex;
		Stopwatch stopwatch = Stopwatch.StartNew();
		Func<SegmentM, bool> func = [SpecialName] (SegmentM segment) =>
		{
			List<int> elements = capturedLocals2.perimeterSpatialIndex.GetElements(segment);
			foreach (int element in elements)
			{
				if (capturedLocals2.meshFV2D._perimeter.Segment(element).Intersects(segment))
				{
					return true;
				}
			}
			return false;
		};
		checked
		{
			int count = meshTIN.Triangles.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				if (unchecked(intervalIndex % 1000) == 0)
				{
					reporter.ReportProgress(100 * (int)System.Math.Round((double)intervalIndex / (double)meshTIN.Triangles.Count));
				}
				DevelopmentTriangle developmentTriangle = meshTIN.Triangles[intervalIndex];
				int[] intValues = new int[3] { developmentTriangle.p0, developmentTriangle.p1, developmentTriangle.p2 };
				int ordinal = 0;
				do
				{
					int supeR_POINT_COUNTSum = intValues[ordinal];
					supeR_POINT_COUNTSum -= DelaunayTINFactory.SUPER_POINT_COUNT;
					if (supeR_POINT_COUNTSum >= 0 && !_cells[supeR_POINT_COUNTSum].IsInitialized())
					{
						List<Tuple<int, int, int>> triEdgeTri = meshTIN.TriEdgeTri(supeR_POINT_COUNTSum + DelaunayTINFactory.SUPER_POINT_COUNT, intervalIndex);
						Cell cell = new Cell
						{
							Point = new Point2Double(meshTIN.Points[supeR_POINT_COUNTSum + DelaunayTINFactory.SUPER_POINT_COUNT]),
							Faces = new List<int>()
						};
						int count2 = triEdgeTri.Count - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
						{
							int triEdgeTriTerm = triEdgeTri[intervalIndex2].Item1;
							int cellB = triEdgeTri[intervalIndex2].Item2 - DelaunayTINFactory.SUPER_POINT_COUNT;
							int triEdgeTriTerm2 = triEdgeTri[intervalIndex2].Item3;
							bool condition = false;
							bool condition2 = false;
							if (cellB < 0)
							{
								continue;
							}
							Cell cell2 = _cells[cellB];
							if (cell2.IsInitialized())
							{
								foreach (int face4 in cell2.Faces)
								{
									if (Faces[face4].IsEitherCell(supeR_POINT_COUNTSum))
									{
										cell.Faces.Add(face4);
										int index = cell2.NextCWFaceByGlobalIndex(face4);
										if (Faces[index].OtherCell(cellB) == -1)
										{
											Face face = new Face();
											face.cellA = supeR_POINT_COUNTSum;
											Faces.Add(face);
											cell.Faces.Add(Faces.Count - 1);
										}
										break;
									}
								}
								continue;
							}
							Face face2 = new Face();
							face2.cellA = supeR_POINT_COUNTSum;
							face2.cellB = cellB;
							SegmentM segmentM = new SegmentM(meshTIN.Points[supeR_POINT_COUNTSum + DelaunayTINFactory.SUPER_POINT_COUNT], meshTIN.Points[cellB + DelaunayTINFactory.SUPER_POINT_COUNT]);
							PointM pointM = meshTIN.TriangleCircumcircle(triEdgeTriTerm);
							PointM pointM2 = meshTIN.TriangleCircumcircle(triEdgeTriTerm2);
							if (!bufferedPerimeterDevTIN.ContainsPoint(pointM) || meshTIN.Triangles[triEdgeTriTerm].IsDeleted())
							{
								condition = true;
							}
							if (!bufferedPerimeterDevTIN.ContainsPoint(pointM2) || meshTIN.Triangles[triEdgeTriTerm2].IsDeleted())
							{
								condition2 = true;
							}
							double lengthTimesActualMinFaceLengthRatio = segmentM.Length * ActualMinFaceLengthRatio;
							if (pointM.DistanceTo(pointM2) < lengthTimesActualMinFaceLengthRatio && !condition && !condition2)
							{
								continue;
							}
							SegmentM segmentM2 = new SegmentM(pointM, pointM2);
							if (!condition || !condition2 || func(segmentM2))
							{
								Faces.Add(face2);
								cell.Faces.Add(Faces.Count - 1);
								if (condition2)
								{
									Face face3 = new Face();
									face3.cellA = supeR_POINT_COUNTSum;
									Faces.Add(face3);
									cell.Faces.Add(Faces.Count - 1);
								}
							}
						}
						cell.Faces = cell.Faces.ToArray();
						_cells[supeR_POINT_COUNTSum] = cell;
					}
					ordinal++;
				}
				while (ordinal <= 2);
			}
			stopwatch.Stop();
			_progressReporter.ReportMessage("SetCellsAndFaces: " + stopwatch.ElapsedMilliseconds + " ms");
		}
	}

	private void AdHocFixPerimeterIssues(DevelopmentTin perimeterDevTIN)
	{
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				PointMs pointMs = PointsOnFace(i);
				bool condition = false;
				int count2 = pointMs.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					PointM point = pointMs[j];
					if (!perimeterDevTIN.ContainsPointBufferedFavorInBounds(point, _filterToleranceTIN / 10.0))
					{
						condition = true;
						break;
					}
				}
				if (condition)
				{
					_ = FacePoints[face.fpA];
					_ = FacePoints[face.fpB];
				}
			}
		}
	}

	private bool SetFacepoints(DistinctSpatialIndex perimeterSpatialIndex, DevelopmentTin perimDevTin)
	{
		_Closure_0024__95_002D0 capturedLocals = default;
		_Closure_0024__95_002D0 capturedLocals2 = new _Closure_0024__95_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.perimeterSpatialIndex = perimeterSpatialIndex;
		Stopwatch stopwatch = Stopwatch.StartNew();
		Dictionary<int, PointOnPolyline> pointOnPolylineByInt = new Dictionary<int, PointOnPolyline>();
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		float minAllowedFaceLength = MinAllowedFaceLength();
		float scaledLength = (float)(_perimeter.Length * 0.6);
		checked
		{
			int cellsTerm = _cells.Count() - 1;
			int ordinal = 0;
			while (true)
			{
				if (ordinal <= cellsTerm)
				{
					Cell cell = _cells[ordinal];
					int count = cell.Faces.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						if (Faces[cell.Faces[i]].GetFPPrev(ordinal) != -1)
						{
							continue;
						}
						bool isPoP = false;
						PointOnPolyline ptOnPoly = null;
						Point2Double pt = ComputeFPPrev(ordinal, i, _perimeter, ref ptOnPoly, ref isPoP, intListList);
						if (pt.IsUndefined())
						{
							continue;
						}
						FacePoint pt2 = new FacePoint(pt);
						List<int> intList2 = (List<int>)(pt2.Faces = new List<int>());
						FacePoints.Add(pt2);
						int countOffset = FacePoints.Count - 1;
						int ordinal2 = ordinal;
						Cell cell2 = _cells[ordinal];
						int faceIndex = cell.Faces[i];
						Face face = Faces[faceIndex];
						face.SetFPPrev(ordinal2, countOffset);
						intList2.Add(faceIndex);
						bool condition = false;
						while (face.OtherCell(ordinal2) != -1)
						{
							ordinal2 = face.OtherCell(ordinal2);
							cell2 = _cells[ordinal2];
							faceIndex = cell2.NextCCWFaceByGlobalIndex(faceIndex);
							face = Faces[faceIndex];
							int fPPrev = face.GetFPPrev(ordinal2);
							if (fPPrev != countOffset)
							{
								if (fPPrev != -1)
								{
									throw new Exception("Something went wrong here.");
								}
								face.SetFPPrev(ordinal2, countOffset);
								AddBeginning(intList2, faceIndex);
								continue;
							}
							goto IL_02a6;
						}
						if (!isPoP)
						{
							intListList.Add(ordinal);
						}
						if (pointOnPolylineByInt.ContainsKey(faceIndex))
						{
							PointOnPolyline pointOnPolyline = pointOnPolylineByInt[faceIndex];
							if (ptOnPoly == null || pointOnPolyline == null)
							{
								goto IL_084e;
							}
							PointMs pointMs = new PointMs();
							_perimeter.PointsBetweenExclusive(ptOnPoly, pointOnPolyline, pointMs);
							pointOnPolylineByInt.Remove(faceIndex);
							face.InternalPoints = pointMs;
							double length = FaceSegment(faceIndex).Length;
							if (length < (double)minAllowedFaceLength)
							{
								intList.Add(faceIndex);
							}
							else if (length > (double)scaledLength)
							{
								intList.Add(faceIndex);
							}
							face.SetFPPrev(ordinal2, countOffset);
						}
						else
						{
							pointOnPolylineByInt.Add(faceIndex, ptOnPoly);
							face.SetFPPrev(ordinal2, countOffset);
						}
						condition = true;
						if (!intList2.Contains(faceIndex))
						{
							AddBeginning(intList2, faceIndex);
						}
						goto IL_02a6;
						IL_02a6:
						if (!condition)
						{
							continue;
						}
						ordinal2 = ordinal;
						cell2 = _cells[ordinal];
						faceIndex = cell.NextCWFaceByLocalIndex(i);
						face = Faces[faceIndex];
						face.SetFPNext(ordinal2, countOffset);
						intList2.Add(faceIndex);
						while (face.OtherCell(ordinal2) != -1)
						{
							ordinal2 = face.OtherCell(ordinal2);
							cell2 = _cells[ordinal2];
							faceIndex = cell2.NextCWFaceByGlobalIndex(faceIndex);
							face = Faces[faceIndex];
							if (face.GetFPNext(ordinal2) != countOffset)
							{
								if (face.GetFPNext(ordinal2) != -1)
								{
									throw new Exception("Something went wrong here.");
								}
								face.SetFPNext(ordinal2, countOffset);
								intList2.Add(faceIndex);
								continue;
							}
							goto IL_0437;
						}
						if (!isPoP)
						{
							intListList.Add(ordinal);
						}
						if (pointOnPolylineByInt.ContainsKey(faceIndex))
						{
							PointOnPolyline pointOnPolyline2 = pointOnPolylineByInt[faceIndex];
							PointMs pointMs2 = new PointMs();
							if (ptOnPoly == null || pointOnPolyline2 == null)
							{
								goto IL_084e;
							}
							_perimeter.PointsBetweenExclusive(pointOnPolyline2, ptOnPoly, pointMs2);
							pointOnPolylineByInt.Remove(faceIndex);
							face.InternalPoints = pointMs2;
							double length2 = FaceSegment(faceIndex).Length;
							if (length2 < (double)minAllowedFaceLength)
							{
								intList.Add(faceIndex);
							}
							else if (length2 > (double)scaledLength)
							{
								intList.Add(faceIndex);
							}
							face.SetFPNext(ordinal2, countOffset);
						}
						else
						{
							pointOnPolylineByInt.Add(faceIndex, ptOnPoly);
							face.SetFPNext(ordinal2, countOffset);
						}
						if (!intList2.Contains(faceIndex))
						{
							intList2.Add(faceIndex);
						}
						IL_0437:;
					}
					ordinal++;
					continue;
				}
				int count2 = FacePoints.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (!FacePointIsPerimeter(j))
					{
						continue;
					}
					FacePoint facePoint = FacePoints[j];
					if (facePoint.Faces.Count == 3)
					{
						continue;
					}
					foreach (int face4 in facePoint.Faces)
					{
						intListList.Add(Faces[face4].cellA);
						intListList.Add(Faces[face4].cellB);
					}
				}
				if (intListList.Count == 0 && !CollapseFaces(intList))
				{
					return false;
				}
				Func<SegmentM, bool> func = [SpecialName] (SegmentM segment) =>
				{
					double length3 = segment.Length;
					double tB = (length3 - capturedLocals2.meshFV2D._filterToleranceTIN) / length3;
					segment.Scale(capturedLocals2.meshFV2D._filterToleranceTIN / length3, tB);
					List<int> elements = capturedLocals2.perimeterSpatialIndex.GetElements(segment);
					foreach (int element in elements)
					{
						if (capturedLocals2.meshFV2D._perimeter.Segment(element).Intersects(segment))
						{
							return true;
						}
					}
					return false;
				};
				int count3 = Faces.Count - 1;
				for (int intervalIndex = 0; intervalIndex <= count3; intervalIndex++)
				{
					if (FaceIsPerimeter(intervalIndex))
					{
						continue;
					}
					SegmentM segmentM = FaceSegment(intervalIndex);
					if (!func(segmentM))
					{
						continue;
					}
					List<PointIntersect> lineIntersectionMulti = _perimeter.GetLineIntersectionMulti(segmentM);
					if (lineIntersectionMulti.Count <= 1)
					{
						continue;
					}
					int getTravelDirectionPerimeterTerm;
					if (lineIntersectionMulti.Count > 2)
					{
						getTravelDirectionPerimeterTerm = _perimeter.GetTravelDirection(lineIntersectionMulti[0].APointOnPolyline, lineIntersectionMulti[1].APointOnPolyline, lineIntersectionMulti[2].APointOnPolyline);
					}
					else
					{
						if (lineIntersectionMulti.Count != 2)
						{
							continue;
						}
						getTravelDirectionPerimeterTerm = _perimeter.GetTravelDirectionShortestArc(lineIntersectionMulti[0].APointOnPolyline, lineIntersectionMulti[1].APointOnPolyline);
					}
					PointMs pointMs3 = new PointMs();
					int count4 = lineIntersectionMulti.Count - 2;
					for (int intervalIndex2 = 0; intervalIndex2 <= count4; intervalIndex2++)
					{
						PointIntersect pointIntersect = lineIntersectionMulti[intervalIndex2];
						PointIntersect pointIntersect2 = lineIntersectionMulti[intervalIndex2 + 1];
						PointM point = new SegmentM(pointIntersect.Point, pointIntersect2.Point).MidPoint();
						if (perimDevTin.ContainsPoint(point))
						{
							continue;
						}
						pointMs3.Add(pointIntersect.Point);
						int aSegmentIndex = pointIntersect.ASegmentIndex;
						int aSegmentIndexPointIntersect2Term = pointIntersect2.ASegmentIndex - getTravelDirectionPerimeterTerm;
						int ordinal3 = aSegmentIndexPointIntersect2Term;
						int ordinal4 = getTravelDirectionPerimeterTerm;
						for (int aSegmentIndex2 = aSegmentIndex; ((ordinal4 >> 31) ^ aSegmentIndex2) <= ((ordinal4 >> 31) ^ ordinal3); aSegmentIndex2 += ordinal4)
						{
							if (getTravelDirectionPerimeterTerm == 1)
							{
								pointMs3.Add(_perimeter.Segment(aSegmentIndex2).B);
							}
							else
							{
								pointMs3.Add(_perimeter.Segment(aSegmentIndex2).A);
							}
						}
						pointMs3.Add(pointIntersect2.Point);
						Face face2 = Faces[intervalIndex];
						if (face2.InternalPoints == null)
						{
							face2.InternalPoints = new PointMs(pointMs3.Count);
							pointMs3.CopyTo(face2.InternalPoints);
							Faces[intervalIndex] = face2;
						}
						else
						{
							face2.InternalPoints.AddRange(pointMs3);
						}
					}
				}
				int count5 = _cells.Count() - 1;
				for (int intervalIndex3 = 0; intervalIndex3 <= count5; intervalIndex3++)
				{
					Cell cell3 = _cells[intervalIndex3];
					HashSet<PointM> pointMsSet = null;
					int count6 = cell3.Faces.Count - 1;
					for (int intervalIndex4 = 0; intervalIndex4 <= count6; intervalIndex4++)
					{
						Face face3 = Faces[cell3.Faces[intervalIndex4]];
						if (face3.InternalPoints.IsNullOrEmpty())
						{
							continue;
						}
						if (pointMsSet == null)
						{
							pointMsSet = new HashSet<PointM>();
						}
						bool condition2 = false;
						int count7 = face3.InternalPoints.Count - 1;
						for (int intervalIndex5 = 0; intervalIndex5 <= count7; intervalIndex5++)
						{
							PointM internalPoint = face3.InternalPoints[intervalIndex5];
							if (pointMsSet.Contains(internalPoint))
							{
								condition2 = true;
								break;
							}
							pointMsSet.Add(internalPoint);
						}
						if (condition2)
						{
							intListList.Add(intervalIndex3);
							break;
						}
					}
				}
				break;
				IL_084e:
				FatalPerimeterError = true;
				break;
			}
			stopwatch.Stop();
			_progressReporter.ReportMessage("SetFacepoints took " + stopwatch.ElapsedMilliseconds + " ms");
			return ValidatePerimeterErrors(intListList);
		}
	}

	private void InternalFacepointInsertions()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int ordinal = 0;
		checked
		{
			int cellsTerm = _cells.Count() - 1;
			for (int i = 0; i <= cellsTerm && !CellIsVirtual(i); i++)
			{
				if (CellIsPerimeter(i))
				{
					double cellTrueArea = CellPolygonArea(i);
					while (SplitExternalCellMaintainIndexing(i, cellTrueArea))
					{
						ordinal++;
					}
				}
			}
			stopwatch.Stop();
			_progressReporter.ReportMessage("Splitting external faces (by injecting facepoints) split " + ordinal + " faces and took " + stopwatch.ElapsedMilliseconds + " ms");
		}
	}

	private bool SplitExternalCellMaintainIndexing(int cIdx, double cellTrueArea)
	{
		List<int> cellFacePoints = CellFacePoints(cIdx);
		if (cellFacePoints.Count >= 8)
		{
			return false;
		}
		PointMs pointMs = new PointMs(cellFacePoints.Select([SpecialName] (int fpIdx) => FacePoints[fpIdx].PointM()));
		pointMs.Add(pointMs.FirstPointM());
		double area = new Polygon(pointMs).Area;
		if (area < 0.0)
		{
			area *= -1.0;
		}
		if (area == 0.0)
		{
			area = 1E-15;
		}
		if (cellTrueArea / area < 1.2)
		{
			return false;
		}
		Cell cell = _cells[cIdx];
		int face2Term = -1;
		int faceInternalPtIdx = -1;
		double minValue = double.MinValue;
		checked
		{
			foreach (int face2 in cell.Faces)
			{
				if (!FaceIsPerimeter(face2))
				{
					continue;
				}
				Face face = Faces[face2];
				if (face.InternalPoints.IsNullOrEmpty())
				{
					continue;
				}
				int indexOf = cellFacePoints.IndexOf(face.GetFPNext(cIdx));
				pointMs.Insert(indexOf, default);
				int count = face.InternalPoints.Count - 1;
				for (int faceInternalPtIdx2 = 0; faceInternalPtIdx2 <= count; faceInternalPtIdx2++)
				{
					pointMs[indexOf] = face.InternalPoints[faceInternalPtIdx2];
					if (indexOf == 0)
					{
						pointMs[pointMs.Count - 1] = pointMs[0];
					}
					double absoluteArea = System.Math.Abs(new Polygon(pointMs).Area);
					if (absoluteArea > minValue)
					{
						face2Term = face2;
						faceInternalPtIdx = faceInternalPtIdx2;
						minValue = absoluteArea;
					}
				}
			}
			if (1.2 > minValue / area)
			{
				return false;
			}
			if (face2Term != -1)
			{
				InsertBoundaryFacepointMaintainIndexing(face2Term, cIdx, faceInternalPtIdx);
				return true;
			}
			return false;
		}
	}

	private void InsertBoundaryFacepointMaintainIndexing(int fIdx, int cIdx, int faceInternalPtIdx)
	{
		Face face = Faces[fIdx];
		Cell cell = _cells[cIdx];
		Face face2 = new Face(face.cellA, face.cellB);
		face2.SetFPPrev(cIdx, FacePoints.Count);
		int fPNext = face.GetFPNext(cIdx);
		face2.SetFPNext(cIdx, fPNext);
		FacePoint facePoint = FacePoints[fPNext];
		int index = facePoint.Faces.IndexOf(fIdx);
		facePoint.Faces[index] = Faces.Count;
		checked
		{
			if (faceInternalPtIdx < face.InternalPoints.Count - 1)
			{
				face2.InternalPoints = new PointMs(face.InternalPoints.GetRange(faceInternalPtIdx + 1, face.InternalPoints.Count - faceInternalPtIdx - 1));
			}
			Faces.Add(face2);
			PointM pt = face.InternalPoints[faceInternalPtIdx];
			FacePoint pt2 = new FacePoint(pt);
			pt2.Faces = new int[2]
			{
				Faces.Count - 1,
				fIdx
			};
			FacePoints.Add(pt2);
			face.SetFPNext(cIdx, FacePoints.Count - 1);
			if (faceInternalPtIdx > 0)
			{
				face.InternalPoints = new PointMs(face.InternalPoints.GetRange(0, faceInternalPtIdx));
			}
			else
			{
				face.InternalPoints = null;
			}
			Faces[fIdx] = face;
			int indexOf = cell.Faces.IndexOf(fIdx);
			List<int> faces = cell.Faces.ToList();
			faces.Insert(indexOf + 1, Faces.Count - 1);
			cell.Faces = faces.ToArray();
			_cells[cIdx] = cell;
		}
	}

	private void ComputeCellStatistics()
	{
		_cellMaximumAreaIndex = -1;
		_cellMinimumAreaIndex = -1;
		_cellMaximumArea = 0f;
		_cellMinimumArea = float.MaxValue;
		_cellsTotalArea = 0.0;
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				double cellPolygonArea = CellPolygonArea(i);
				if (cellPolygonArea > (double)_cellMaximumArea)
				{
					_cellMaximumArea = (float)cellPolygonArea;
					_cellMaximumAreaIndex = i;
				}
				if (cellPolygonArea < (double)_cellMinimumArea)
				{
					_cellMinimumArea = (float)cellPolygonArea;
					_cellMinimumAreaIndex = i;
				}
				_cellsTotalArea += cellPolygonArea;
			}
			_cellAverageArea = (float)(_cellsTotalArea / (double)_cells.Count());
		}
	}

	private List<int> ComputeCellStatistics_WithPgonCheck()
	{
		_cellMaximumAreaIndex = -1;
		_cellMinimumAreaIndex = -1;
		_cellMaximumArea = 0f;
		_cellMinimumArea = float.MaxValue;
		_cellsTotalArea = 0.0;
		List<int> cellStatistics_WithPgonCheck = new List<int>();
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!CellPolygon(i).Contains(Cell(i).Point.ToPointM()))
				{
					cellStatistics_WithPgonCheck.Add(i);
				}
				double cellPolygonArea = CellPolygonArea(i);
				if (cellPolygonArea > (double)_cellMaximumArea)
				{
					_cellMaximumArea = (float)cellPolygonArea;
					_cellMaximumAreaIndex = i;
				}
				if (cellPolygonArea < (double)_cellMinimumArea)
				{
					_cellMinimumArea = (float)cellPolygonArea;
					_cellMinimumAreaIndex = i;
				}
				_cellsTotalArea += cellPolygonArea;
			}
			_cellAverageArea = (float)(_cellsTotalArea / (double)_cells.Count());
			return cellStatistics_WithPgonCheck;
		}
	}

	private void AddVirtualCells()
	{
		List<Cell> cellsList = new List<Cell>();
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				if (face.cellB == -1)
				{
					Point2Double center;
					if (face.InternalPoints == null || face.InternalPoints.Count == 0)
					{
						center = FacePoints[face.fpA].Point.MidPoint(FacePoints[face.fpB].Point);
					}
					else
					{
						PointMs pointMs = new PointMs();
						pointMs.Add(new PointM(FacePoints[face.fpA].Point));
						pointMs.AddRange(face.InternalPoints);
						pointMs.Add(new PointM(FacePoints[face.fpB].Point));
						center = new Point2Double(pointMs.PointMAtFraction(0.5));
					}
					Cell center2 = new Cell(center);
					center2.Faces = new int[1] { i };
					cellsList.Add(center2);
				}
			}
			int cellsTerm = _cells.Count();
			ref Cell[] cells = ref _cells;
			cells = (Cell[])Utils.CopyArray(cells, new Cell[cellsTerm + cellsList.Count - 1 + 1]);
			int count2 = _cells.Count() - 1;
			for (int j = cellsTerm; j <= count2; j++)
			{
				_cells[j] = cellsList[j - cellsTerm];
				Faces[_cells[j].Faces[0]].cellB = j;
			}
		}
	}

	private void SplitInternalFacepoints()
	{
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				if (face.InternalPoints != null && face.InternalPoints.Count > 0)
				{
					_ = face.InternalPoints;
				}
			}
		}
	}

	public DistinctSpatialIndex ComputeCellSpatialIndex(bool skipVirtualCells)
	{
		if (!IsReadable)
		{
			return DistinctSpatialIndex.DefaultSpatialIndex();
		}
		DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(_extent, _cells.Count());
		checked
		{
			if (skipVirtualCells)
			{
				int count = _cells.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (!CellIsVirtual(i))
					{
						distinctSpatialIndex.Add(CellPolygon(i), i);
					}
				}
			}
			else
			{
				int count2 = _cells.Count() - 1;
				for (int j = 0; j <= count2; j++)
				{
					distinctSpatialIndex.Add(CellPolygon(j), j);
				}
			}
			return distinctSpatialIndex;
		}
	}

	public SpatialIndex<Tuple<int, float>> ComputeCellSpatialIndexWithAreas(bool skipVirtualCells)
	{
		SpatialIndex<Tuple<int, float>> spatialIndex = new SpatialIndex<Tuple<int, float>>(_extent);
		checked
		{
			if (skipVirtualCells)
			{
				int count = _cells.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (!CellIsVirtual(i))
					{
						Polygon polygon = CellPolygon(i);
						spatialIndex.Add(polygon, new Tuple<int, float>(i, (float)polygon.Area));
					}
				}
			}
			else
			{
				int count2 = _cells.Count() - 1;
				for (int j = 0; j <= count2; j++)
				{
					Polygon polygon2 = CellPolygon(j);
					spatialIndex.Add(polygon2, new Tuple<int, float>(j, (float)polygon2.Area));
				}
			}
			return spatialIndex;
		}
	}

	public float[] ComputeAllCellAreas()
	{
		_Closure_0024__105_002D0 capturedLocals = default;
		_Closure_0024__105_002D0 capturedLocals2 = new _Closure_0024__105_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		checked
		{
			capturedLocals2.areas = new float[_cells.Count() - 1 + 1];
			Utility.Parallel.SmartFor(_cells.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (!capturedLocals2.meshFV2D.CellIsVirtual(i))
					{
						capturedLocals2.areas[i] = (float)capturedLocals2.meshFV2D.CellPolygon(i).Area;
					}
				}
			});
			return capturedLocals2.areas;
		}
	}

	public DistinctSpatialIndex ComputeFaceSpatialIndex()
	{
		DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(_extent, Faces.Count);
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				if (face.fpA != -1 && face.fpB != -1)
				{
					Point2Double point = FacePoints[face.fpA].Point;
					Point2Double point2 = FacePoints[face.fpB].Point;
					if (face.InternalPoints != null && face.InternalPoints.Count > 0)
					{
						PointMs pointMs = new PointMs();
						pointMs.Add(new PointM(point));
						pointMs.AddRange(face.InternalPoints);
						pointMs.Add(new PointM(point2));
						distinctSpatialIndex.Add(new Polyline(pointMs), i);
					}
					else
					{
						distinctSpatialIndex.Add(new SegmentM(point, point2), i);
					}
				}
			}
			return distinctSpatialIndex;
		}
	}

	public DistinctSpatialIndex ComputeFacepointSpatialIndex()
	{
		DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(_extent, FacePoints.Count);
		checked
		{
			int count = FacePoints.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				FacePoint facePoint = FacePoints[i];
				if (!facePoint.IsNull())
				{
					distinctSpatialIndex.Add(facePoint.Point, i);
				}
			}
			return distinctSpatialIndex;
		}
	}

	private int MinLenFaceIdx(int fpIdx, ref float faceLen)
	{
		float maxValue = float.MaxValue;
		int result = -1;
		FacePoint facePoint = FacePoints[fpIdx];
		foreach (int face2 in facePoint.Faces)
		{
			Face face = Faces[face2];
			FacePoint facePoint2 = FacePoints[face.OtherFacepoint(fpIdx)];
			float pointFacePointTerm = (float)(facePoint.Point.X - facePoint2.Point.X);
			float pointFacePointTerm2 = (float)(facePoint.Point.Y - facePoint2.Point.Y);
			float maxValue2 = (float)System.Math.Sqrt(pointFacePointTerm * pointFacePointTerm + pointFacePointTerm2 * pointFacePointTerm2);
			if (maxValue2 < maxValue)
			{
				result = face2;
				maxValue = maxValue2;
			}
		}
		faceLen = maxValue;
		return result;
	}

	private float MinFaceLen(int fpIdx)
	{
		float maxValue = float.MaxValue;
		FacePoint facePoint = FacePoints[fpIdx];
		foreach (int face2 in facePoint.Faces)
		{
			Face face = Faces[face2];
			FacePoint facePoint2 = FacePoints[face.OtherFacepoint(fpIdx)];
			float pointFacePointTerm = (float)(facePoint.Point.X - facePoint2.Point.X);
			float pointFacePointTerm2 = (float)(facePoint.Point.Y - facePoint2.Point.Y);
			float systemTerm = (float)System.Math.Sqrt(pointFacePointTerm * pointFacePointTerm + pointFacePointTerm2 * pointFacePointTerm2);
			maxValue = System.Math.Min(maxValue, systemTerm);
		}
		return maxValue;
	}

	private float AverageFaceLen(int fpIdx)
	{
		float computed = 0f;
		FacePoint facePoint = FacePoints[fpIdx];
		foreach (int face2 in facePoint.Faces)
		{
			Face face = Faces[face2];
			FacePoint facePoint2 = FacePoints[face.OtherFacepoint(fpIdx)];
			float pointFacePointTerm = (float)(facePoint.Point.X - facePoint2.Point.X);
			float pointFacePointTerm2 = (float)(facePoint.Point.Y - facePoint2.Point.Y);
			float systemTerm = (float)System.Math.Sqrt(pointFacePointTerm * pointFacePointTerm + pointFacePointTerm2 * pointFacePointTerm2);
			computed += systemTerm;
		}
		return computed / (float)facePoint.Faces.Count;
	}

	private void AddBeginning(List<int> intList, int newElement)
	{
		List<int> intListList = new List<int>();
		intListList.AddRange(intList);
		intList.Clear();
		intList.Add(newElement);
		intList.AddRange(intListList);
	}

	private Point2Double ComputeFPPrev(int cellIdx, int localFaceIdx, Polygon ccwPerimeter, ref PointOnPolyline ptOnPoly, ref bool isPoP, List<int> badPoints)
	{
		Cell cell = _cells[cellIdx];
		int otherCell = Faces[cell.Faces[localFaceIdx]].OtherCell(cellIdx);
		int index = cell.NextCWFaceByLocalIndex(localFaceIdx);
		int otherCell2 = Faces[index].OtherCell(cellIdx);
		Point2Double result;
		if (otherCell == -1)
		{
			if (otherCell2 == -1)
			{
				throw new NotImplementedException();
			}
			Cell cell2 = _cells[otherCell2];
			SegmentM seg = new SegmentM(new PointM(cell.Point), new PointM(cell2.Point));
			ptOnPoly = Project(seg, ccwPerimeter, useRHR: true);
			isPoP = true;
			result = new Point2Double(ptOnPoly.PointM);
		}
		else
		{
			Cell cell3 = _cells[otherCell];
			if (otherCell2 == -1)
			{
				SegmentM seg2 = new SegmentM(new PointM(cell.Point), new PointM(cell3.Point));
				ptOnPoly = Project(seg2, ccwPerimeter, useRHR: false);
				isPoP = true;
				result = new Point2Double(ptOnPoly.PointM);
			}
			else
			{
				if (otherCell == otherCell2)
				{
					badPoints.Add(otherCell);
					return Point2Double.UndefinedPoint();
				}
				Cell cell4 = _cells[otherCell2];
				PointM a = new PointM(cell.Point);
				PointM b = new PointM(cell3.Point);
				PointM c = new PointM(cell4.Point);
				double X = default;
				double Y = default;
				CircleM.ComputeCenter(a, b, c, ref X, ref Y);
				isPoP = false;
				result = new Point2Double(X, Y);
			}
		}
		return result;
	}

	private PointOnPolyline Project(SegmentM seg, Polygon ccwperimeter, bool useRHR)
	{
		PointM vector = ((!useRHR) ? seg.PerpendicularUnitVector().NegateXY() : seg.PerpendicularUnitVector());
		PointM ptM = seg.MidPoint();
		return ccwperimeter.NearestPointOnPolyline(ptM, vector);
	}

	private List<int> GetCellsAroundFacepoint(int fp)
	{
		List<int> faces = FacePoints[fp].Faces.ToList();
		List<int> cellsAroundFacepoint = new List<int>();
		foreach (int faceIndex in faces)
		{
			if (Faces[faceIndex].fpB == fp)
			{
				cellsAroundFacepoint.Add(Faces[faceIndex].cellA);
			}
			else
			{
				cellsAroundFacepoint.Add(Faces[faceIndex].cellB);
			}
		}
		if (FacePointIsPerimeter(fp))
		{
			cellsAroundFacepoint.Add(Faces[faces.Last()].cellB);
		}
		return cellsAroundFacepoint;
	}

	private bool FacePointIsPerimeter(int fp)
	{
		FacePoint facePoint = FacePoints[fp];
		if (facePoint.Faces.IsNullOrEmpty())
		{
			return false;
		}
		foreach (int face in facePoint.Faces)
		{
			int cellB = Faces[face].cellB;
			if (cellB == -1)
			{
				return true;
			}
			if (CellIsVirtual(cellB))
			{
				return true;
			}
		}
		return false;
	}

	private void PopulateCellsWithInputPoints(IPoints cellCenters)
	{
		Cell cell = default;
		checked
		{
			_cells = new Cell[cellCenters.PointMCount() - 1 + 1];
			int count = cellCenters.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				cell.Point = new Point2Double(cellCenters.PointM(i));
				_cells[i] = cell;
			}
		}
	}

	private List<int> GetPointsOutsidePerimeter(IPoints points, DevelopmentTin perimeterTIN)
	{
		List<int> pointsOutsidePerimeter = new List<int>();
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!perimeterTIN.ContainsPoint(points.PointM(i)))
				{
					pointsOutsidePerimeter.Add(i);
				}
			}
			return pointsOutsidePerimeter;
		}
	}

	private DevelopmentTin GetPerimeterTin(Polygon perimeter)
	{
		double nearPointTolerance = 1E-06;
		return new DelaunayTINFactory(nearPointTolerance).ComputeTIN(perimeter, null, null, null, removeDeletedTriangles: false);
	}

	private Polygon GetCCWPerimeter(Polygon perimeter)
	{
		if (perimeter == null)
		{
			return null;
		}
		Polygon polygon = perimeter;
		if (polygon.Area > 0.0)
		{
			polygon = perimeter.Reverse();
		}
		return polygon;
	}

	private int GetFaceBetweenCells(int ca, int cb)
	{
		foreach (int face in _cells[ca].Faces)
		{
			if (Faces[face].OtherCell(ca) == cb)
			{
				return face;
			}
		}
		return -1;
	}

	private bool CellUsesFacePoint(int c, int fp)
	{
		foreach (int face in _cells[c].Faces)
		{
			if (Faces[face].fpA == fp)
			{
				return true;
			}
			if (Faces[face].fpB == fp)
			{
				return true;
			}
		}
		return false;
	}

	private int GetCommonFacePoint(int ca, int cb)
	{
		foreach (int face in _cells[ca].Faces)
		{
			if (CellUsesFacePoint(cb, Faces[face].fpA))
			{
				return Faces[face].fpA;
			}
			if (CellUsesFacePoint(cb, Faces[face].fpB))
			{
				return Faces[face].fpB;
			}
		}
		return -1;
	}

	private bool ValidateInput(Polygon perim, IPoints cells)
	{
		if (!perim.IsValid())
		{
			_statusMessage = "Need at least three points on perimeter of the 2D-area.";
			_meshCompletionState = MeshStatus.FewerThanMinPerimeterPoints;
			return false;
		}
		int dupIdx = -1;
		if (perim.HasConsecutiveIdenticalPoints(ref dupIdx))
		{
			_statusMessage = "Polygon error - please inspect your perimeter for duplicate points.";
			_meshCompletionState = MeshStatus.PerimeterPolygonError;
			_badIndexes = new int[1] { dupIdx }.ToList();
			return false;
		}
		if (cells.PointMCount() <= 1)
		{
			_statusMessage = "Need at least two cells in the 2D-area.";
			_meshCompletionState = MeshStatus.FewerThanMinNumCells;
			return false;
		}
		return true;
	}

	private bool ValidatePointsInsidePerim(IPoints cellCenters, DevelopmentTin perimeterDevTin)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<int> pointsOutsidePerimeter = GetPointsOutsidePerimeter(cellCenters, perimeterDevTin);
		stopwatch.Stop();
		_progressReporter.ReportMessage("Checked " + cellCenters.PointMCount() + " points against the perimeter polygon: " + stopwatch.ElapsedMilliseconds + " ms.");
		if (pointsOutsidePerimeter.Count > 0)
		{
			_statusMessage = pointsOutsidePerimeter.Count + " point(s) detected outside the perimeter of the 2D-area.";
			_progressReporter.ReportMessage(_statusMessage);
			_meshCompletionState = MeshStatus.PointsOutsidePerimeter;
			_badIndexes = pointsOutsidePerimeter;
			PopulateCellsWithInputPoints(cellCenters);
			return false;
		}
		return true;
	}

	private bool ValidateDuplicatePoints(DevelopmentTin meshTIN, IPoints cellCenters, List<int> duplicatePoints)
	{
		if (meshTIN == null)
		{
			_statusMessage = "Error creating mesh TIN.";
			_progressReporter.ReportMessage(_statusMessage);
			_meshCompletionState = MeshStatus.UnknownError;
			PopulateCellsWithInputPoints(cellCenters);
			return false;
		}
		if (duplicatePoints.Count > 0)
		{
			_statusMessage = duplicatePoints.Count + " near-duplicate point(s) detected.";
			_progressReporter.ReportMessage(_statusMessage);
			_meshCompletionState = MeshStatus.DuplicatePoints;
			_badIndexes = duplicatePoints;
			PopulateCellsWithInputPoints(cellCenters);
			return false;
		}
		return true;
	}

	private bool ValidatePerimeterErrors(List<int> pointOnPerimeterErrors)
	{
		if (pointOnPerimeterErrors.Count != 0)
		{
			pointOnPerimeterErrors = pointOnPerimeterErrors.Distinct().ToList();
			if (pointOnPerimeterErrors.Contains(-1))
			{
				pointOnPerimeterErrors.Remove(-1);
			}
			_meshCompletionState = MeshStatus.FacePerimeterConnectionError;
			_badIndexes = pointOnPerimeterErrors;
			_statusMessage = pointOnPerimeterErrors.Count + " face errors detected on the perimeter of the mesh.";
			_progressReporter.ReportMessage(_statusMessage);
			return false;
		}
		return true;
	}

	private bool ValidateSqueezeErrors(List<int> cellErrs)
	{
		if (cellErrs.Count != 0)
		{
			cellErrs = cellErrs.Distinct().ToList();
			if (cellErrs.Contains(-1))
			{
				cellErrs.Remove(-1);
			}
			_meshCompletionState = MeshStatus.CellAreaTotalError;
			_badIndexes = cellErrs;
			_statusMessage = cellErrs.Count + " cell errors detected in the mesh.";
			_progressReporter.ReportMessage(_statusMessage);
			return false;
		}
		return true;
	}

	private bool ValidateDCSubgraphs()
	{
		_Closure_0024__128_002D0 capturedLocals = default;
		_Closure_0024__128_002D0 capturedLocals2 = new _Closure_0024__128_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.hasPushed = new BitArray(_cells.Count());
		checked
		{
			capturedLocals2.graph = new byte[_cells.Count() - 1 + 1];
			capturedLocals2.curIsland = 0;
			capturedLocals2.pushNeighbors = [SpecialName] (int c) =>
			{
				foreach (int face in capturedLocals2.meshFV2D._cells[c].Faces)
				{
					int otherCell = capturedLocals2.meshFV2D.Faces[face].OtherCell(c);
					if (otherCell != -1)
					{
						capturedLocals2.graph[otherCell] = capturedLocals2.graph[c];
					}
				}
			};
			Func<int> func = [SpecialName] () =>
			{
				int count = capturedLocals2.meshFV2D._cells.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (capturedLocals2.graph[i] == 0)
					{
						return i;
					}
				}
				return -1;
			};
			Action<int> action = [SpecialName] (int startcell) =>
			{
				capturedLocals2.curIsland = (byte)unchecked((uint)(capturedLocals2.curIsland + 1));
				capturedLocals2.graph[startcell] = capturedLocals2.curIsland;
				bool condition = false;
				bool condition2;
				do
				{
					condition2 = false;
					condition = !condition;
					int capturedLocals3;
					int capturedLocals4;
					int sign;
					if (condition)
					{
						capturedLocals3 = 0;
						capturedLocals4 = capturedLocals2.meshFV2D._cells.Count() - 1;
						sign = 1;
					}
					else
					{
						capturedLocals3 = capturedLocals2.meshFV2D._cells.Count() - 1;
						capturedLocals4 = 0;
						sign = -1;
					}
					int ordinal = capturedLocals3;
					int ordinal2 = capturedLocals4;
					int sign2 = sign;
					for (int i = ordinal; ((sign2 >> 31) ^ i) <= ((sign2 >> 31) ^ ordinal2); i += sign2)
					{
						if (!capturedLocals2.hasPushed[i] && capturedLocals2.graph[i] != 0)
						{
							capturedLocals2.pushNeighbors(i);
							capturedLocals2.hasPushed[i] = true;
							condition2 = true;
						}
					}
				}
				while (condition2);
			};
			int func2 = func();
			while (func2 != -1 && capturedLocals2.curIsland < byte.MaxValue)
			{
				action(func2);
				func2 = func();
			}
			if (capturedLocals2.curIsland > 1)
			{
				Dictionary<int, List<int>> intListByInt = new Dictionary<int, List<int>>();
				int count2 = _cells.Count() - 1;
				for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
				{
					int key = capturedLocals2.graph[intervalIndex];
					if (!intListByInt.ContainsKey(key))
					{
						intListByInt.Add(key, new List<int>());
					}
					intListByInt[key].Add(intervalIndex);
				}
				int key2Term = -1;
				int key2Term2 = -1;
				foreach (int key2 in intListByInt.Keys)
				{
					if (intListByInt[key2].Count > key2Term2)
					{
						key2Term = key2;
						key2Term2 = intListByInt[key2].Count;
					}
				}
				List<int> badIndexes = new List<int>();
				foreach (int key3 in intListByInt.Keys)
				{
					if (key3 != key2Term)
					{
						badIndexes.AddRange(intListByInt[key3]);
					}
				}
				_badIndexes = badIndexes;
				_meshCompletionState = MeshStatus.DisconnectedGraphs;
				_statusMessage = badIndexes.Count + " cell(s) are not connected to the mesh.";
				return false;
			}
			return true;
		}
	}

	private bool ValidateAreaCheck()
	{
		double absoluteArea = System.Math.Abs(_perimeter.Area);
		double cellsTotalAreaOverAbsoluteArea = _cellsTotalArea / absoluteArea;
		if (System.Math.Abs(1.0 - cellsTotalAreaOverAbsoluteArea) > 1E-06)
		{
			_meshCompletionState = MeshStatus.CellAreaTotalError;
			if ((double)_cellMaximumArea > 0.01 * absoluteArea)
			{
				_badIndexes = new List<int>(new int[1] { _cellMaximumAreaIndex });
				_statusMessage = "Total area from all cells does not match the perimeter area. Possible face errors on the perimeter of the mesh. Cell " + _cellMaximumAreaIndex + " is potentially bad.";
			}
			else
			{
				_badIndexes = new List<int>();
				_statusMessage = "Total area from all cells does not match the perimeter area. Possible face errors on the perimeter of the mesh.";
			}
			_progressReporter.ReportMessage(_statusMessage);
			return false;
		}
		if (_cellMinimumArea < 0f)
		{
			_meshCompletionState = MeshStatus.CellAreaTotalError;
			_badIndexes = new List<int>(new int[1] { _cellMinimumAreaIndex });
			_statusMessage = "Some cells have negative area. Possible face errors on the perimeter of the mesh. Cell " + _cellMinimumAreaIndex + " is bad.";
			_progressReporter.ReportMessage(_statusMessage);
			return false;
		}
		return true;
	}

	private bool ValidatePointInCell(List<int> badCells)
	{
		if (badCells.Count == 0)
		{
			return true;
		}
		_meshCompletionState = MeshStatus.CellAreaTotalError;
		_badIndexes = badCells.ToList();
		_statusMessage = "Some cell points were outside of their representative polygon. This can occur when breaklines haven't been enforced properly, or they're using a large cell-size.";
		return false;
	}

	private bool ValidateMinFaceLength()
	{
		HashSet<int> intSet = new HashSet<int>();
		double systemMaxXTerm = System.Math.Max(Perimeter.MaxX - Perimeter.MinX, Perimeter.MaxY - Perimeter.MinY);
		float systemTerm = (float)System.Math.Min(0.001, systemMaxXTerm / 1000000.0);
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (FaceSegment(i).Length < (double)systemTerm)
				{
					Face face = Faces[i];
					if (face.cellA != -1 && !CellIsVirtual(face.cellA))
					{
						intSet.Add(face.cellA);
					}
					if (face.cellB != -1 && !CellIsVirtual(face.cellB))
					{
						intSet.Add(face.cellB);
					}
				}
			}
			if (intSet.Count > 0)
			{
				_meshCompletionState = MeshStatus.MinFaceLenError;
				_statusMessage = "Some faces were below the minimum-length constraints. (" + systemTerm + SharedData.ShortLengthUnits + ")";
				_badIndexes = intSet.ToList();
				return false;
			}
			return true;
		}
	}

	private bool ValidateAllFacepointsSet()
	{
		foreach (Face face in Faces)
		{
			if (face.fpB == -1 || face.fpA == -1)
			{
				_meshCompletionState = MeshStatus.UnknownError;
				_statusMessage = "Unknown Error: Some facepoints were not set correctly.";
				return false;
			}
		}
		return true;
	}

	private bool ValidateNumFacesPerCell()
	{
		List<int> badIndexes = new List<int>();
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (_cells[i].Faces.Count > 8)
				{
					badIndexes.Add(i);
				}
			}
			if (badIndexes.Count > 0)
			{
				_badIndexes = badIndexes;
				_meshCompletionState = MeshStatus.MaxFacesPerCellExceeded;
				_statusMessage = badIndexes.Count + " cell(s) with more than 8 sides.";
				return false;
			}
			return true;
		}
	}

	private bool ValidateNumFacesPerFacepoint()
	{
		List<int> badIndexes = new List<int>();
		int ordinal = 0;
		checked
		{
			int count = FacePoints.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (FacePoints[i].Faces.Count > 100)
				{
					ordinal++;
					badIndexes.AddRange(GetCellsAroundFacepoint(i));
				}
			}
			if (badIndexes.Count > 0)
			{
				_badIndexes = badIndexes;
				_meshCompletionState = MeshStatus.MaxFacesPerFacepointExceeded;
				_statusMessage = ordinal + " FacePoint(s) with more than " + 100 + " sides.";
				return false;
			}
			return true;
		}
	}

	private bool ValidateCell(int cidx)
	{
		if (CellPolygonArea(cidx) <= 0.0)
		{
			return false;
		}
		Cell cell = _cells[cidx];
		foreach (int face2 in cell.Faces)
		{
			Face face = Faces[face2];
			if (face.cellA != cidx && face.cellB != cidx)
			{
				return false;
			}
			FacePoint facePoint = FacePoints[face.fpA];
			FacePoint facePoint2 = FacePoints[face.fpB];
			if (!facePoint.Faces.Contains(face2))
			{
				return false;
			}
			if (!facePoint2.Faces.Contains(face2))
			{
				return false;
			}
		}
		if (CellPolygon(cidx).SelfIntersects())
		{
			return false;
		}
		return true;
	}

	public bool TryAutoFix(ref List<PointM> suggestedNewCells, ref List<int> suggestedRemoveCells)
	{
		suggestedNewCells = new List<PointM>();
		suggestedRemoveCells = new List<int>();
		checked
		{
			if (MeshCompletionState == MeshStatus.MaxFacesPerCellExceeded)
			{
				HashSet<int> intSet = new HashSet<int>();
				int count = NonVirtualCellCount - 1;
				for (int i = 0; i <= count; i++)
				{
					Cell cell = _cells[i];
					if (cell.Faces.Count <= 8)
					{
						continue;
					}
					List<int> intList = (from fidx in cell.Faces
						where Faces[fidx].InternalPoints.IsNullOrEmpty()
						orderby FaceSimpleLength(fidx) descending
						select fidx).ToList();
					int count2 = System.Math.Min(2, intList.Count) - 1;
					for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
					{
						int ordinal = intList[intervalIndex];
						if (!intSet.Contains(ordinal))
						{
							suggestedNewCells.Add(FaceSegment(ordinal).MidPoint());
							intSet.Add(ordinal);
						}
					}
				}
				return true;
			}
			return false;
		}
	}

	public static void CompareMeshes(MeshFV2D meshA, MeshFV2D meshB, ref Tuple<int, bool>[] faceMatches, ref int[] cellMatches)
	{
		_Closure_0024__140_002D0 capturedLocals = default;
		_Closure_0024__140_002D0 capturedLocals2 = new _Closure_0024__140_002D0(capturedLocals);
		capturedLocals2.meshA = meshA;
		capturedLocals2.meshB = meshB;
		Extent extent = capturedLocals2.meshA.GetExtent().UnionXY(capturedLocals2.meshB.GetExtent()).Ratio(1.01);
		capturedLocals2.tol = System.Math.Min(capturedLocals2.meshA.LengthTolerance, capturedLocals2.meshB.LengthTolerance);
		checked
		{
			faceMatches = new Tuple<int, bool>[capturedLocals2.meshA.FaceCount - 1 + 1];
			cellMatches = new int[capturedLocals2.meshA.NonVirtualCellCount - 1 + 1];
			capturedLocals2.faceMatchesLocal = faceMatches;
			capturedLocals2.cellMatchesLocal = cellMatches;
			capturedLocals2.faceSpIdx = new SpatialIndex<Tuple<bool, int>>(extent);
			int count = capturedLocals2.meshA.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2.faceSpIdx.Add(capturedLocals2.meshA.FaceSegment(i).MidPoint(), new Tuple<bool, int>(item1: true, i));
			}
			int count2 = capturedLocals2.meshB.Faces.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals2.faceSpIdx.Add(capturedLocals2.meshB.FaceSegment(j).MidPoint(), new Tuple<bool, int>(item1: false, j));
			}
			System.Threading.Tasks.Parallel.For(0, capturedLocals2.faceSpIdx.Bins.Count(), [SpecialName] (int binIdx) =>
			{
				List<Tuple<bool, int>> bin = capturedLocals2.faceSpIdx.Bins[binIdx];
				if (!bin.IsNullOrEmpty())
				{
					HashSet<int> intSet = new HashSet<int>();
					int count3 = bin.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						Tuple<bool, int> tuple = bin[k];
						if (tuple.Item1)
						{
							int tupleTerm = tuple.Item2;
							Face face = capturedLocals2.meshA.Faces[tupleTerm];
							PointM pointM = capturedLocals2.meshA.FacePoints[face.fpA].PointM();
							PointM pointM2 = capturedLocals2.meshA.FacePoints[face.fpB].PointM();
							int ordinal = k + 1;
							int count4 = bin.Count - 1;
							for (int l = ordinal; l <= count4; l++)
							{
								Tuple<bool, int> tuple2 = bin[l];
								if (!tuple2.Item1 && !intSet.Contains(l))
								{
									int tuple2Term = tuple2.Item2;
									Face face2 = capturedLocals2.meshB.Faces[tuple2Term];
									PointM pointM3 = capturedLocals2.meshB.FacePoints[face2.fpA].PointM();
									PointM pointM4 = capturedLocals2.meshB.FacePoints[face2.fpB].PointM();
									if (pointM.EqualXY(pointM3) && pointM2.EqualXY(pointM4))
									{
										intSet.Add(l);
										capturedLocals2.faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: true);
										break;
									}
									double distanceTo = pointM.DistanceTo(pointM3);
									double distanceTo2 = pointM.DistanceTo(pointM4);
									double distanceTo3 = pointM2.DistanceTo(pointM3);
									double distanceTo4 = pointM2.DistanceTo(pointM4);
									if (distanceTo < capturedLocals2.tol && distanceTo4 < capturedLocals2.tol)
									{
										intSet.Add(l);
										capturedLocals2.faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: true);
										break;
									}
									if (distanceTo2 < capturedLocals2.tol && distanceTo3 < capturedLocals2.tol)
									{
										intSet.Add(l);
										capturedLocals2.faceMatchesLocal[tupleTerm] = new Tuple<int, bool>(tuple2Term, item2: false);
										break;
									}
								}
							}
						}
					}
				}
			});
			Utility.Parallel.SmartFor(capturedLocals2.meshA.NonVirtualCellCount, [SpecialName] (int start, int fin) =>
			{
				for (int k = start; k <= fin; k++)
				{
					Cell cell = capturedLocals2.meshA.Cell(k);
					bool condition = true;
					int minValue = int.MinValue;
					foreach (int face3 in cell.Faces)
					{
						Tuple<int, bool> tuple = capturedLocals2.faceMatchesLocal[face3];
						if (tuple == null)
						{
							condition = false;
							break;
						}
						Face face = capturedLocals2.meshA.Faces[face3];
						Face face2 = capturedLocals2.meshB.Faces[tuple.Item1];
						int neighborCell;
						int neighborCell2;
						if (tuple.Item2)
						{
							neighborCell = face2.cellA;
							neighborCell2 = face2.cellB;
						}
						else
						{
							neighborCell = face2.cellB;
							neighborCell2 = face2.cellA;
						}
						if (face.cellA == k)
						{
							if (minValue == int.MinValue)
							{
								minValue = neighborCell;
							}
							else if (minValue != neighborCell)
							{
								condition = false;
								break;
							}
						}
						else if (minValue == int.MinValue)
						{
							minValue = neighborCell2;
						}
						else if (minValue != neighborCell2)
						{
							condition = false;
							break;
						}
					}
					if (condition)
					{
						capturedLocals2.cellMatchesLocal[k] = minValue;
					}
					else
					{
						capturedLocals2.cellMatchesLocal[k] = -1;
					}
				}
			});
		}
	}

	public bool ApplyBreaklinesOld(Polyline lines)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (lines == null || lines.SegmentCount() == 0)
		{
			return true;
		}
		float magneticRadiusFactor = 0.5f;
		if (RASD2FlowArea.EnableTestMagnetizationChanges)
		{
			magneticRadiusFactor = 1f;
		}
		int collapsedFaceCount = 0;
		Dictionary<int, Tuple<int, int>> intersectionMagnetization = null;
		Dictionary<int, Tuple<int, int>> magnetizeFacepointsEnsureNonColinear = MagnetizeFacepointsEnsureNonColinear(lines, ref collapsedFaceCount, magneticRadiusFactor, useBackoffMethod: false, ref intersectionMagnetization);
		if (magnetizeFacepointsEnsureNonColinear.Count == 0)
		{
			return true;
		}
		EnforceInternalPoints(lines, magnetizeFacepointsEnsureNonColinear, intersectionMagnetization);
		bool result = true;
		if (collapsedFaceCount > 0)
		{
			result = SqueezeNullFFP();
		}
		stopwatch.Stop();
		_progressReporter.ReportMessage("Applying breaklines (first pass) took " + stopwatch.ElapsedMilliseconds + " ms");
		return result;
	}

	private Dictionary<int, Tuple<int, int>> MagnetizeFacepointsEnsureNonColinear(Polyline lines, ref int collapsedFaceCount, [Optional][DefaultParameterValue(0.5f)] float magneticRadiusFactor, [Optional][DefaultParameterValue(false)] bool useBackoffMethod, [Optional][DefaultParameterValue(null)] ref Dictionary<int, Tuple<int, int>> intersectionMagnetization)
	{
		Dictionary<int, Tuple<int, int>> valueTupleByInt = new Dictionary<int, Tuple<int, int>>();
		intersectionMagnetization = new Dictionary<int, Tuple<int, int>>();
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(_perimeter);
		checked
		{
			int count = lines.PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int count2 = lines.PartSegmentCount(i) - 1;
				for (int j = 0; j <= count2; j++)
				{
					spatialIndex.Add(lines.PartSegment(i, j), new Tuple<int, int>(i, j));
				}
			}
			List<PointIntersect> allSelfIntersections = null;
			SpatialIndex<PointIntersect> spatialIndex2 = null;
			allSelfIntersections = lines.GetAllSelfIntersections();
			spatialIndex2 = new SpatialIndex<PointIntersect>(_perimeter);
			int count3 = allSelfIntersections.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				PointIntersect pointIntersect = allSelfIntersections[k];
				spatialIndex2.AddInBounds(pointIntersect.Point, pointIntersect);
			}
			collapsedFaceCount = 0;
			int count4 = FacePoints.Count - 1;
			PointM ptM = default;
			int partIndex = default;
			int localSegIdx = default;
			int partIndex2 = default;
			int localSegIdx2 = default;
			PointM point = default;
			for (int l = 0; l <= count4; l++)
			{
				if (FacePoints[l].IsNull() || FacePointIsPerimeter(l) || valueTupleByInt.ContainsKey(l))
				{
					continue;
				}
				float faceLen = 0f;
				int minLenFaceIdx = MinLenFaceIdx(l, ref faceLen);
				float averageFaceLen = AverageFaceLen(l);
				float systemFaceLenTerm = (float)System.Math.Min(faceLen, (double)averageFaceLen * 0.5);
				systemFaceLenTerm *= magneticRadiusFactor;
				FacePoint facePoint = FacePoints[l];
				Extent extent = new Extent(new PointM(facePoint.Point), systemFaceLenTerm * 2f, systemFaceLenTerm * 2f);
				List<Tuple<int, int>> elements = spatialIndex.GetElements(extent);
				if (elements.Count == 0)
				{
					continue;
				}
				double maxValue = double.MaxValue;
				Tuple<int, int> tuple = null;
				foreach (Tuple<int, int> element in elements)
				{
					SegmentM segmentM = lines.PartSegment(element.Item1, element.Item2);
					double x = facePoint.Point.X;
					double y = facePoint.Point.Y;
					SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
					float distanceToPoint = (float)segmentM.DistanceToPoint(x, y, ref relativePosition);
					if ((double)distanceToPoint < maxValue)
					{
						PointM pt = new PointM(facePoint.Point);
						double t = 0.0;
						ptM = segmentM.NearestPoint(pt, ref t);
						maxValue = distanceToPoint;
						tuple = element;
					}
				}
				if (!(maxValue < (double)systemFaceLenTerm))
				{
					continue;
				}
				Tuple<int, int> tuple2 = null;
				if (RASD2FlowArea.EnableTestMagnetizationChanges)
				{
					double maxValue2 = double.MaxValue;
					Tuple<int, int> tuple3 = null;
					Tuple<int, int> tuple4 = null;
					List<PointIntersect> elements2 = spatialIndex2.GetElements(extent);
					foreach (PointIntersect elements22 in elements2)
					{
						lines.GetPartialSegmentIndex(elements22.ASegmentIndex, ref partIndex, ref localSegIdx);
						lines.GetPartialSegmentIndex(elements22.BSegmentIndex, ref partIndex2, ref localSegIdx2);
						if (partIndex == tuple.Item1 || partIndex2 == tuple.Item1)
						{
							double distanceTo = elements22.Point.DistanceTo(ptM);
							if (distanceTo < maxValue2)
							{
								maxValue2 = distanceTo;
								tuple3 = new Tuple<int, int>(partIndex, localSegIdx);
								tuple4 = new Tuple<int, int>(partIndex2, localSegIdx2);
								point = elements22.Point;
							}
						}
					}
					if (maxValue2 < (double)systemFaceLenTerm)
					{
						ptM = point;
						maxValue = maxValue2;
						tuple = tuple3;
						tuple2 = tuple4;
					}
				}
				Point2Double point2Double = new Point2Double(ptM);
				if (useBackoffMethod)
				{
					SegmentM segmentM2 = new SegmentM(point2Double, facePoint.Point);
					float computed = 0f;
					do
					{
						Point2Double point2Double2 = new Point2Double(segmentM2.PointAtFraction(computed));
						if (ValidateFacepointMove(l, point2Double2))
						{
							facePoint.Point = point2Double2;
							FacePoints[l] = facePoint;
							if (computed == 0f)
							{
								valueTupleByInt.Add(l, new Tuple<int, int>(tuple.Item1, tuple.Item2));
							}
							break;
						}
						computed += 0.1f;
					}
					while (computed <= 0.9f);
				}
				else if (ValidateFacepointMove(l, point2Double))
				{
					facePoint.Point = point2Double;
					FacePoints[l] = facePoint;
					valueTupleByInt.Add(l, new Tuple<int, int>(tuple.Item1, tuple.Item2));
					if (tuple2 != null)
					{
						intersectionMagnetization.Add(l, new Tuple<int, int>(tuple2.Item1, tuple2.Item2));
					}
				}
				else if (RASD2FlowArea.EnableTestMagnetizationChanges && ValidateFacepointMoveWithFaceCollapse(l, point2Double, minLenFaceIdx))
				{
					facePoint.Point = point2Double;
					FacePoints[l] = facePoint;
					int newFpIdx = 0;
					CollapseFace(minLenFaceIdx, point2Double, ref newFpIdx);
					collapsedFaceCount++;
					valueTupleByInt[newFpIdx] = new Tuple<int, int>(tuple.Item1, tuple.Item2);
					if (tuple2 != null)
					{
						intersectionMagnetization[newFpIdx] = new Tuple<int, int>(tuple2.Item1, tuple2.Item2);
					}
				}
			}
			return valueTupleByInt;
		}
	}

	private bool ValidateFacepointMove(int fpIdx, Point2Double candidateNewPt)
	{
		List<int> cellsAroundFacepoint = GetCellsAroundFacepoint(fpIdx);
		using (List<int>.Enumerator cellsAroundFacepointList = cellsAroundFacepoint.GetEnumerator())
		{
			_Closure_0024__144_002D0 capturedLocals = default;
			while (cellsAroundFacepointList.MoveNext())
			{
				capturedLocals = new _Closure_0024__144_002D0(capturedLocals);
				capturedLocals.meshFV2D = this;
				capturedLocals.cIdx = cellsAroundFacepointList.Current;
				Cell cell = _cells[capturedLocals.cIdx];
				List<Point2Double> point2Doubles = new List<Point2Double>();
				foreach (int ordinal in cell.Faces.Select(capturedLocals._Lambda_0024__0))
				{
					if (ordinal == fpIdx)
					{
						point2Doubles.Add(candidateNewPt);
					}
					else
					{
						point2Doubles.Add(FacePoints[ordinal].Point);
					}
				}
				if (!ArePointsConvexEnough(point2Doubles))
				{
					return false;
				}
			}
		}
		return true;
	}

	private bool ValidateFacepointMoveWithFaceCollapse(int fpMoveIdx, Point2Double candidateNewPt, int collapseFaceIdx)
	{
		_ = FacePoints[fpMoveIdx];
		Face face = Faces[collapseFaceIdx];
		int otherFacepoint = face.OtherFacepoint(fpMoveIdx);
		_ = FacePoints[otherFacepoint];
		List<int> cellsAroundFacepoint = GetCellsAroundFacepoint(fpMoveIdx);
		List<int> cellsAroundFacepoint2 = GetCellsAroundFacepoint(otherFacepoint);
		cellsAroundFacepoint.AddRange(cellsAroundFacepoint2);
		cellsAroundFacepoint = cellsAroundFacepoint.Distinct().ToList();
		foreach (int cellsAroundFacepoint3 in cellsAroundFacepoint)
		{
			if (cellsAroundFacepoint3 == -1)
			{
				return false;
			}
			Cell cell = _cells[cellsAroundFacepoint3];
			List<Point2Double> point2Doubles = new List<Point2Double>();
			foreach (int face2 in cell.Faces)
			{
				if (face2 != collapseFaceIdx)
				{
					int fPNext = Faces[face2].GetFPNext(cellsAroundFacepoint3);
					if (fPNext == fpMoveIdx || fPNext == otherFacepoint)
					{
						point2Doubles.Add(candidateNewPt);
					}
					else
					{
						point2Doubles.Add(FacePoints[fPNext].Point);
					}
				}
			}
			if (!ArePointsConvexEnough(point2Doubles))
			{
				return false;
			}
		}
		int cellA = face.cellA;
		int cellB = face.cellB;
		if (cellA == -1 || cellB == -1)
		{
			return false;
		}
		Point2Double point = _cells[cellA].Point;
		Point2Double point2 = _cells[cellB].Point;
		if (FaceSimpleLength(collapseFaceIdx) < point.DistanceTo(point2) * (double)RASD2FlowArea.FaceCollapseBreaklineRatio)
		{
			return true;
		}
		return false;
	}

	private bool ArePointsStrictlyConvex(List<Point2Double> pts)
	{
		if (pts == null || pts.Count <= 2)
		{
			throw new ArgumentException("This isn't a polygon");
		}
		Func<Point2Double, Point2Double, Point2Double, bool> func = [SpecialName] (Point2Double pt1, Point2Double pt2, Point2Double pt3) => !((float)Vector.CrossProduct(pt1, pt2, pt3) <= 0.0001f);
		int count = pts.Count;
		checked
		{
			int count2 = count - 3;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				if (func(pts[intervalIndex], pts[intervalIndex + 1], pts[intervalIndex + 2]))
				{
					return false;
				}
			}
			if (func(pts[count - 2], pts[count - 1], pts[0]))
			{
				return false;
			}
			if (func(pts[count - 1], pts[count], pts[1]))
			{
				return false;
			}
			return true;
		}
	}

	private bool ArePointsConvexEnough(List<Point2Double> pts)
	{
		if (pts == null || pts.Count <= 2)
		{
			return false;
		}
		Func<Point2Double, Point2Double, Point2Double, bool> func = [SpecialName] (Point2Double pt1, Point2Double pt2, Point2Double pt3) =>
		{
			if ((float)Vector.CrossProduct(pt1, pt2, pt3) <= 1E-08f)
			{
				return true;
			}
			return (float)Vector.AngleBetween(pt2, pt3, pt1) >= 2.9670596f;
		};
		int count = pts.Count;
		checked
		{
			int count2 = count - 3;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				if (func(pts[intervalIndex], pts[intervalIndex + 1], pts[intervalIndex + 2]))
				{
					return false;
				}
			}
			if (func(pts[count - 2], pts[count - 1], pts[0]))
			{
				return false;
			}
			if (func(pts[count - 1], pts[0], pts[1]))
			{
				return false;
			}
			return true;
		}
	}

	private List<int> GetAllCollapsibleFaces(ref bool needsAnotherPass, float heuristicFactor = 1f)
	{
		_Closure_0024__148_002D0 capturedLocals = default;
		_Closure_0024__148_002D0 capturedLocals2 = new _Closure_0024__148_002D0(capturedLocals);
		capturedLocals2.collapse = new List<int>();
		needsAnotherPass = false;
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (FaceIsPerimeter(i))
				{
					continue;
				}
				Face face = Faces[i];
				FacePoint facePoint = FacePoints[face.fpA];
				FacePoint facePoint2 = FacePoints[face.fpB];
				Cell cell = _cells[face.cellA];
				Cell cell2 = _cells[face.cellB];
				float distanceTo = (float)facePoint.Point.DistanceTo(facePoint2.Point);
				float distanceTo2 = (float)cell.Point.DistanceTo(cell2.Point);
				if ((double)distanceTo < (double)distanceTo2 * ActualMinFaceLengthRatio * (double)heuristicFactor && cell.Faces.Count > 3 && cell2.Faces.Count > 3)
				{
					if (cell.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)) || cell2.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)))
					{
						needsAnotherPass = true;
					}
					else if (facePoint.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)) || facePoint2.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)))
					{
						needsAnotherPass = true;
					}
					else
					{
						capturedLocals2.collapse.Add(i);
					}
				}
			}
			return capturedLocals2.collapse;
		}
	}

	private List<int> GetCollapsibleFaces(List<int> connectedToFacepoints, float heuristicFactor = 1f)
	{
		_Closure_0024__149_002D0 capturedLocals = default;
		_Closure_0024__149_002D0 capturedLocals2 = new _Closure_0024__149_002D0(capturedLocals);
		List<int> distinct = connectedToFacepoints.SelectMany([SpecialName] (int fp) => FacePoints[fp].Faces).Distinct().ToList();
		capturedLocals2.collapse = new List<int>();
		foreach (int distinct2 in distinct)
		{
			if (!FaceIsPerimeter(distinct2))
			{
				Face face = Faces[distinct2];
				FacePoint facePoint = FacePoints[face.fpA];
				FacePoint facePoint2 = FacePoints[face.fpB];
				Cell cell = _cells[face.cellA];
				Cell cell2 = _cells[face.cellB];
				float distanceTo = (float)facePoint.Point.DistanceTo(facePoint2.Point);
				float distanceTo2 = (float)cell.Point.DistanceTo(cell2.Point);
				if ((double)distanceTo < (double)distanceTo2 * ActualMinFaceLengthRatio * (double)heuristicFactor && cell.Faces.Count > 3 && cell2.Faces.Count > 3 && !cell.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)) && !cell2.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)) && !facePoint.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)) && !facePoint2.Faces.Any([SpecialName] (int ff) => capturedLocals2.collapse.Contains(ff)))
				{
					capturedLocals2.collapse.Add(distinct2);
				}
			}
		}
		return capturedLocals2.collapse;
	}

	private bool CollapseFaces(List<int> faceList)
	{
		foreach (int face in faceList)
		{
			int newFpIdx = 0;
			CollapseFace(face, null, ref newFpIdx);
		}
		return SqueezeNullFFP();
	}

	private void CollapseFace(int fCollapseIdx, Point2Double? collapseToPoint, [Optional][DefaultParameterValue(0)] ref int newFpIdx)
	{
		Face face = Faces[fCollapseIdx];
		Cell cell = _cells[face.cellA];
		bool cellBFaceTerm = face.cellB == -1;
		Cell cell2 = default;
		if (!cellBFaceTerm)
		{
			cell2 = _cells[face.cellB];
		}
		int fpA = face.fpA;
		int fpB = face.fpB;
		FacePoint facePoint = FacePoints[fpA];
		FacePoint facePoint2 = FacePoints[fpB];
		bool facePointIsPerimeterOrFacePointIsPerimeter = FacePointIsPerimeter(fpA) || FacePointIsPerimeter(fpB);
		if (cell.Faces is int[])
		{
			cell.Faces = cell.Faces.ToList();
			_cells[face.cellA] = cell;
		}
		if (!cellBFaceTerm && cell2.Faces is int[])
		{
			cell2.Faces = cell2.Faces.ToList();
			_cells[face.cellB] = cell2;
		}
		cell.Faces.Remove(fCollapseIdx);
		if (!cellBFaceTerm)
		{
			cell2.Faces.Remove(fCollapseIdx);
		}
		FacePoint facePoints;
		if (collapseToPoint.HasValue && collapseToPoint.HasValue)
		{
			facePoints = new FacePoint(collapseToPoint.Value);
		}
		else if (FacePointIsPerimeter(fpA))
		{
			facePoints = new FacePoint(facePoint.Point);
		}
		else
		{
			facePoints = ((!FacePointIsPerimeter(fpB)) ? new FacePoint(facePoint.Point.MidPoint(facePoint2.Point)) : new FacePoint(facePoint2.Point));
		}
		List<int> faces = new List<int>();
		checked
		{
			foreach (int face3 in facePoint.Faces)
			{
				if (face3 == fCollapseIdx)
				{
					int fi = facePoint2.Faces.IndexOf(fCollapseIdx);
					int count = facePoint2.Faces.Count - 2;
					for (int i = 0; i <= count; i++)
					{
						int fi2 = facePoint2.CCWFaceByLocalIndex(fi);
						faces.Add(facePoint2.Faces[fi2]);
						fi = fi2;
					}
				}
				else
				{
					faces.Add(face3);
				}
			}
			facePoints.Faces = faces;
			FacePoints[fpA] = facePoints;
			FacePoints[fpB] = FacePoint.NullFacePoint;
			newFpIdx = fpA;
			foreach (int face4 in facePoints.Faces)
			{
				Face face2 = Faces[face4];
				if (face2.fpA == fpB)
				{
					face2.fpA = fpA;
				}
				else if (face2.fpB == fpB)
				{
					face2.fpB = fpA;
				}
				Faces[face4] = face2;
			}
			if (facePointIsPerimeterOrFacePointIsPerimeter)
			{
				Func<int, bool> func = [SpecialName] (int f) =>
				{
					int cellB = Faces[f].cellB;
					if (cellB == -1)
					{
						return true;
					}
					return CellIsVirtual(cellB) ? true : false;
				};
				int ordinal = 0;
				while ((!func(faces.First()) || !func(faces.Last())) && ordinal != faces.Count)
				{
					faces.Add(faces.First());
					faces.RemoveAt(0);
					ordinal++;
				}
			}
			Faces[fCollapseIdx] = Face.NullFace();
		}
	}

	private bool SqueezeNullFFP()
	{
		List<int> intList = new List<int>();
		int facesSpecialNameTerm = Faces.Where([SpecialName] (Face f) => f.IsNull()).Count();
		int facePointsSpecialNameTerm = FacePoints.Where([SpecialName] (FacePoint fp) => fp.IsNull()).Count();
		checked
		{
			Face[] faces = new Face[Faces.Count - facesSpecialNameTerm - 1 + 1];
			FacePoint[] facePoints = new FacePoint[FacePoints.Count - facePointsSpecialNameTerm - 1 + 1];
			int ordinal = 0;
			int count = Faces.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				Face face = Faces[intervalIndex];
				if (face.IsNull())
				{
					ordinal++;
					continue;
				}
				if (ordinal == 0)
				{
					faces[intervalIndex] = face;
					continue;
				}
				int faces2 = intervalIndex - ordinal;
				int cellA = face.cellA;
				Cell cell = _cells[cellA];
				int indexOf = cell.Faces.IndexOf(intervalIndex);
				if (indexOf == -1)
				{
					intList.Add(cellA);
					break;
				}
				cell.Faces[indexOf] = faces2;
				int cellB = face.cellB;
				if (cellB != -1)
				{
					Cell cell2 = _cells[cellB];
					int indexOf2 = cell2.Faces.IndexOf(intervalIndex);
					if (indexOf2 == -1)
					{
						intList.Add(cellB);
						break;
					}
					cell2.Faces[indexOf2] = faces2;
				}
				int fpA = face.fpA;
				int fpB = face.fpB;
				FacePoint facePoint = FacePoints[fpA];
				FacePoint facePoint2 = FacePoints[fpB];
				if (!facePoint.IsNull())
				{
					int indexOf3 = facePoint.Faces.IndexOf(intervalIndex);
					if (indexOf3 == -1)
					{
						intList.Add(cellA);
						intList.Add(cellB);
						break;
					}
					facePoint.Faces[indexOf3] = faces2;
				}
				if (!facePoint2.IsNull())
				{
					int indexOf4 = facePoint2.Faces.IndexOf(intervalIndex);
					if (indexOf4 == -1)
					{
						intList.Add(cellA);
						intList.Add(cellB);
						break;
					}
					facePoint2.Faces[indexOf4] = faces2;
				}
				faces[faces2] = face;
			}
			if (!ValidateSqueezeErrors(intList))
			{
				return false;
			}
			int ordinal2 = 0;
			int count2 = FacePoints.Count - 1;
			for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
			{
				FacePoint facePoint3 = FacePoints[intervalIndex2];
				if (facePoint3.IsNull())
				{
					ordinal2++;
					continue;
				}
				if (ordinal2 == 0)
				{
					facePoints[intervalIndex2] = facePoint3;
					continue;
				}
				int ordinal3 = intervalIndex2 - ordinal2;
				foreach (int face3 in facePoint3.Faces)
				{
					Face face2 = faces[face3];
					if (face2.fpA == intervalIndex2)
					{
						face2.fpA = ordinal3;
						continue;
					}
					if (face2.fpB == intervalIndex2)
					{
						face2.fpB = ordinal3;
						continue;
					}
					throw new Exception("Bad Face/FacePoint pointer agreement");
				}
				facePoints[ordinal3] = facePoint3;
			}
			if (!ValidateSqueezeErrors(intList))
			{
				return false;
			}
			Faces = faces.ToList();
			FacePoints = facePoints.ToList();
			return true;
		}
	}

	private void EnforceInternalPoints(Polyline lines, Dictionary<int, Tuple<int, int>> fpsMoved, Dictionary<int, Tuple<int, int>> fpsMovedSecondPass = null)
	{
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				if (face.IsNull() || FaceIsPerimeter(i) || !fpsMoved.ContainsKey(face.fpA) || !fpsMoved.ContainsKey(face.fpB))
				{
					continue;
				}
				Tuple<int, int> tuple = fpsMoved[face.fpA];
				Tuple<int, int> tuple2 = fpsMoved[face.fpB];
				if (tuple.Item1 != tuple2.Item1)
				{
					if (fpsMovedSecondPass == null)
					{
						continue;
					}
					if (fpsMovedSecondPass.ContainsKey(face.fpA))
					{
						Tuple<int, int> tuple3 = fpsMovedSecondPass[face.fpA];
						if (tuple3.Item1 != tuple2.Item1)
						{
							continue;
						}
						tuple = tuple3;
					}
					else
					{
						if (!fpsMovedSecondPass.ContainsKey(face.fpB))
						{
							continue;
						}
						Tuple<int, int> tuple4 = fpsMovedSecondPass[face.fpB];
						if (tuple4.Item1 != tuple.Item1)
						{
							continue;
						}
						tuple2 = tuple4;
					}
				}
				if (tuple.Item2 == tuple2.Item2)
				{
					continue;
				}
				if (tuple.Item2 < tuple2.Item2)
				{
					List<PointM> pointMs = new List<PointM>();
					int tupleTerm = tuple.Item2;
					int count2 = tuple2.Item2 - 1;
					for (int j = tupleTerm; j <= count2; j++)
					{
						SegmentM segmentM = lines.PartSegment(tuple.Item1, j);
						if (!(segmentM.A.DistanceTo(segmentM.B) < 1E-06))
						{
							pointMs.Add(segmentM.B);
						}
					}
					face.InternalPoints = new PointMs();
					face.InternalPoints.AddRange(pointMs);
				}
				else
				{
					List<PointM> pointMsList = new List<PointM>();
					int tuple2Term = tuple2.Item2;
					int count3 = tuple.Item2 - 1;
					for (int k = tuple2Term; k <= count3; k++)
					{
						SegmentM segmentM2 = lines.PartSegment(tuple.Item1, k);
						if (!(segmentM2.A.DistanceTo(segmentM2.B) < 1E-06))
						{
							pointMsList.Add(segmentM2.B);
						}
					}
					face.InternalPoints = new PointMs();
					face.InternalPoints.AddRangeReverse(pointMsList);
				}
				if (face.InternalPoints.Count > 0)
				{
					if (face.InternalPoints.First().DistanceTo(FacePoints[face.fpA].Point) < 1E-06)
					{
						face.InternalPoints.RemoveAt(0);
					}
					if (face.InternalPoints.Count > 0 && face.InternalPoints.Last().DistanceTo(FacePoints[face.fpB].Point) < 1E-06)
					{
						face.InternalPoints.RemoveAt(face.InternalPoints.Count - 1);
					}
				}
				if (!ValidateCell(face.cellA) || !ValidateCell(face.cellB))
				{
					face.InternalPoints = null;
				}
			}
		}
	}

	public Cell Cell(int index)
	{
		return _cells[index];
	}

	public void SetCellMinElevation(int index, float minElev)
	{
		Cell cell = _cells[index];
		cell.MinimumElevation = minElev;
		_cells[index] = cell;
	}

	public bool CellIsVirtual(int cIdx)
	{
		if (cIdx < 0)
		{
			return false;
		}
		if (_cells[cIdx].Faces.Count == 1)
		{
			return true;
		}
		return false;
	}

	public bool CellIsPerimeter(int cIdx)
	{
		Cell cell = _cells[cIdx];
		if (cell.Faces.Count == 1)
		{
			return true;
		}
		foreach (int face in cell.Faces)
		{
			if (FaceIsPerimeter(face))
			{
				return true;
			}
		}
		return false;
	}

	public int CellFacesCount(int c)
	{
		return _cells[c].Faces.Count;
	}

	public int[] CellFaces(int c)
	{
		return _cells[c].Faces.ToArray();
	}

	public Extent CellExtent(int c)
	{
		return CellPolygonPoints(c)?.ComputeExtent();
	}

	public float CellMinimumElevation(int c)
	{
		if (_cellMinimumElevationSet)
		{
			return _cells[c].MinimumElevation;
		}
		return float.NaN;
	}

	public void CellMinimumElevationSet(bool cellMinimumElevationSet)
	{
		_cellMinimumElevationSet = cellMinimumElevationSet;
	}

	public bool IsCellMinimumElevationSet()
	{
		return _cellMinimumElevationSet;
	}

	public Polygon CellPolygon(int c)
	{
		return new Polygon(CellPolygonPoints(c));
	}

	public Polyline CellPolyline(int c)
	{
		return new Polyline(CellPolygonPoints(c));
	}

	public double CellPolygonArea(int c)
	{
		PointMs pointMs = CellPolygonPoints(c);
		if (pointMs == null)
		{
			return 0.0;
		}
		return 0.0 - pointMs.ComputeArea();
	}

	private PointMs CellPolygonPoints(int c)
	{
		if (c >= _cells.Length)
		{
			return null;
		}
		Cell cell = _cells[c];
		PointMs pointMs = new PointMs();
		if (cell.Faces == null || cell.Faces.Count == 0)
		{
			return null;
		}
		if (cell.Faces.Count == 1)
		{
			Face face = Faces[cell.Faces[0]];
			pointMs.Add(new PointM(FacePoints[face.fpA].Point));
			if (face.InternalPoints != null)
			{
				pointMs.AddRange(face.InternalPoints);
			}
			pointMs.Add(new PointM(FacePoints[face.fpB].Point));
			if (face.InternalPoints != null)
			{
				pointMs.AddRangeReverse(face.InternalPoints);
			}
			return pointMs;
		}
		checked
		{
			int count = cell.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face2 = Faces[cell.Faces[i]];
				if (c == face2.cellA)
				{
					if (i == 0)
					{
						pointMs.Add(new PointM(FacePoints[face2.fpA].Point));
					}
					if (face2.InternalPoints != null)
					{
						pointMs.AddRange(face2.InternalPoints);
					}
					pointMs.Add(new PointM(FacePoints[face2.fpB].Point));
					continue;
				}
				if (i == 0)
				{
					if (face2.fpB == -1)
					{
						continue;
					}
					pointMs.Add(new PointM(FacePoints[face2.fpB].Point));
				}
				if (face2.InternalPoints != null)
				{
					pointMs.AddRangeReverse(face2.InternalPoints);
				}
				if (face2.fpA != -1)
				{
					pointMs.Add(new PointM(FacePoints[face2.fpA].Point));
				}
			}
			pointMs.Reverse();
			return pointMs;
		}
	}

	public IEnumerable<SegmentM> CellSegments(int c)
	{
		foreach (int face in _cells[c].Faces)
		{
			foreach (SegmentM faceSegment in FaceSegments(face))
			{
				yield return faceSegment;
			}
		}
	}

	public List<SegmentM> CellSegmentList(int c)
	{
		List<SegmentM> segmentMs = new List<SegmentM>();
		Cell cell = _cells[c];
		checked
		{
			int count = cell.Faces.Count - 1;
			SegmentM segmentM = default;
			for (int i = 0; i <= count; i++)
			{
				int index = cell.Faces[i];
				Face face = Faces[index];
				segmentM.A = FacePoints[face.fpA].PointM();
				if (face.InternalPoints == null || face.InternalPoints.Count == 0)
				{
					segmentM.B = FacePoints[face.fpB].PointM();
					segmentMs.Add(segmentM);
					continue;
				}
				segmentM.B = face.InternalPoints[0];
				segmentMs.Add(segmentM);
				int count2 = face.InternalPoints.Count - 2;
				for (int j = 0; j <= count2; j++)
				{
					segmentM.A = face.InternalPoints[j];
					segmentM.B = face.InternalPoints[j + 1];
					segmentMs.Add(segmentM);
				}
				segmentM.A = face.InternalPoints.Last();
				segmentM.B = FacePoints[face.fpB].PointM();
				segmentMs.Add(segmentM);
			}
			return segmentMs;
		}
	}

	public List<Segment2Double> CellSegment2DoubleList(int c)
	{
		List<Segment2Double> segment2Doubles = new List<Segment2Double>();
		Cell cell = _cells[c];
		checked
		{
			int count = cell.Faces.Count - 1;
			Segment2Double segment2Double = default;
			for (int i = 0; i <= count; i++)
			{
				int index = cell.Faces[i];
				Face face = Faces[index];
				segment2Double.A = FacePoints[face.fpA].Point;
				if (face.InternalPoints == null || face.InternalPoints.Count == 0)
				{
					segment2Double.B = FacePoints[face.fpB].Point;
					segment2Doubles.Add(segment2Double);
					continue;
				}
				segment2Double.B = new Point2Double(face.InternalPoints[0]);
				segment2Doubles.Add(segment2Double);
				int count2 = face.InternalPoints.Count - 2;
				for (int j = 0; j <= count2; j++)
				{
					segment2Double.A = new Point2Double(face.InternalPoints[j]);
					segment2Double.B = new Point2Double(face.InternalPoints[j + 1]);
					segment2Doubles.Add(segment2Double);
				}
				segment2Double.A = new Point2Double(face.InternalPoints.Last());
				segment2Double.B = FacePoints[face.fpB].Point;
				segment2Doubles.Add(segment2Double);
			}
			return segment2Doubles;
		}
	}

	public PointMs CellPoints(int c)
	{
		Cell cell = _cells[c];
		PointMs pointMs = new PointMs();
		if (cell.Faces.Count == 1)
		{
			Face face = Faces[cell.Faces[0]];
			pointMs.Add(new PointM(FacePoints[face.fpA].Point));
			if (face.InternalPoints != null)
			{
				pointMs.AddRange(face.InternalPoints);
			}
			pointMs.Add(new PointM(FacePoints[face.fpB].Point));
			if (face.InternalPoints != null)
			{
				pointMs.AddRangeReverse(face.InternalPoints);
			}
			return pointMs;
		}
		checked
		{
			int count = cell.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face2 = Faces[cell.Faces[i]];
				if (c == face2.cellA)
				{
					if (i == 0)
					{
						pointMs.Add(new PointM(FacePoints[face2.fpA].Point));
					}
					if (face2.InternalPoints != null)
					{
						pointMs.AddRange(face2.InternalPoints);
					}
					pointMs.Add(new PointM(FacePoints[face2.fpB].Point));
				}
				else
				{
					if (i == 0)
					{
						pointMs.Add(new PointM(FacePoints[face2.fpB].Point));
					}
					pointMs.Add(new PointM(FacePoints[face2.fpA].Point));
				}
			}
			pointMs.Reverse();
			return pointMs;
		}
	}

	public int CellPolygonTotalPoints()
	{
		int cellPolygonCountSum = 0;
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				cellPolygonCountSum += CellPolygon(i).Count;
			}
			return cellPolygonCountSum;
		}
	}

	public int NonVirtualCellPolygonTotalPoints()
	{
		int cellPolygonCountSum = 0;
		checked
		{
			int count = NonVirtualCellCount - 1;
			for (int i = 0; i <= count; i++)
			{
				cellPolygonCountSum += CellPolygon(i).Count;
			}
			return cellPolygonCountSum;
		}
	}

	public List<int> ConnectedCells(int c)
	{
		if (CellIsVirtual(c))
		{
			throw new NotSupportedException("Cell cannot be virtual");
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (int cellFacePoint in CellFacePoints(c))
		{
			FacePoint facePoint = FacePoints[cellFacePoint];
			intSet.UnionWith(facePoint.Faces.Select([SpecialName] (int f) => Faces[f].cellA));
			intSet.UnionWith(facePoint.Faces.Select([SpecialName] (int f) => Faces[f].cellB));
		}
		return intSet.ToList();
	}

	public int CellCCWOfFace(int fpIdx, int fIdx)
	{
		_ = FacePoints[fpIdx];
		Face face = Faces[fIdx];
		if (face.fpA == fpIdx)
		{
			return face.cellA;
		}
		if (face.fpB == fpIdx)
		{
			return face.cellB;
		}
		throw new Exception("Face/Facepoint disagreement");
	}

	public int FaceCCWOfCell(int fpIdx, int cIdx)
	{
		FacePoint facePoint = FacePoints[fpIdx];
		_ = ref _cells[cIdx];
		foreach (int face2 in facePoint.Faces)
		{
			Face face = Faces[face2];
			if (face.fpA == fpIdx)
			{
				if (face.cellB == cIdx)
				{
					return face2;
				}
			}
			else if (face.fpB == fpIdx && face.cellA == cIdx)
			{
				return face2;
			}
		}
		return -1;
	}

	public void FacePointXY(int fp, ref double x, ref double y, ref bool isPerimeter)
	{
		FacePointXY(fp, ref x, ref y);
		isPerimeter = FacePointIsPerimeter(fp);
	}

	public void FacePointXY(int fp, ref double x, ref double y)
	{
		x = FacePoints[fp].Point.X;
		y = FacePoints[fp].Point.Y;
	}

	public PointMs FacePointLocations()
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = FacePoints.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(FacePoints[i].PointM());
			}
			return pointMs;
		}
	}

	public int NearestFacePointInCell(int c, PointM point, [Optional][DefaultParameterValue(double.NaN)] ref double fpDistance)
	{
		Cell cell = _cells[c];
		int unity = -1;
		fpDistance = double.MaxValue;
		foreach (int face in cell.Faces)
		{
			double fpDistance2;
			if (unity == -1)
			{
				unity = Faces[face].fpA;
				fpDistance = point.DistanceTo(FacePoints[Faces[face].fpA].Point);
			}
			else
			{
				fpDistance2 = point.DistanceTo(FacePoints[Faces[face].fpA].Point);
				if (fpDistance > fpDistance2)
				{
					unity = Faces[face].fpA;
					fpDistance = fpDistance2;
				}
			}
			fpDistance2 = point.DistanceTo(FacePoints[Faces[face].fpB].Point);
			if (fpDistance > fpDistance2)
			{
				unity = Faces[face].fpB;
				fpDistance = fpDistance2;
			}
		}
		return unity;
	}

	public List<int> CellFacePoints(int cIdx)
	{
		List<int> intList = new List<int>();
		Cell cell = _cells[cIdx];
		checked
		{
			int count = cell.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[cell.Faces[i]];
				intList.Add(face.GetFPPrev(cIdx));
			}
			return intList;
		}
	}

	public void SetFaceMinElevation(int f, float faceMinElevation)
	{
		Faces[f].MinimumElevation = faceMinElevation;
	}

	public bool FaceIsPerimeter(int f)
	{
		int cellB = Faces[f].cellB;
		return cellB switch
		{
			-1 => true, 
			int.MinValue => false, 
			_ => _cells[cellB].Faces.Count == 1, 
		};
	}

	public SegmentM FaceSegment(int f)
	{
		if (f == -1)
		{
			return default;
		}
		SegmentM result = default;
		result.A = new PointM(FacePoints[Faces[f].fpA].Point);
		result.B = new PointM(FacePoints[Faces[f].fpB].Point);
		return result;
	}

	public double FaceSimpleLength(int f)
	{
		Face face = Faces[f];
		return FacePoints[face.fpA].Point.DistanceTo(FacePoints[face.fpB].Point);
	}

	public double FacePolylineLength(int f)
	{
		return PointsOnFace(f).ComputeLength();
	}

	public IEnumerable<SegmentM> FaceSegments(int f)
	{
		Face face = Faces[f];
		SegmentM segmentM = default;
		segmentM.A = FacePoints[face.fpA].PointM();
		if (face.InternalPoints == null || face.InternalPoints.Count == 0)
		{
			segmentM.B = FacePoints[face.fpB].PointM();
			yield return segmentM;
			yield break;
		}
		segmentM.B = face.InternalPoints[0];
		yield return segmentM;
		checked
		{
			int count = face.InternalPoints.Count - 2;
			for (int i = 0; i <= count; i++)
			{
				segmentM.A = face.InternalPoints[i];
				segmentM.B = face.InternalPoints[i + 1];
				yield return segmentM;
			}
			segmentM.A = face.InternalPoints.Last();
			segmentM.B = FacePoints[face.fpB].PointM();
			yield return segmentM;
		}
	}

	public SegmentM FaceSegmentReversed(int f)
	{
		SegmentM result = default;
		result.A = new PointM(FacePoints[Faces[f].fpB].Point);
		result.B = new PointM(FacePoints[Faces[f].fpA].Point);
		return result;
	}

	public Polyline FacePolyline(int f)
	{
		return new Polyline(PointsOnFace(f));
	}

	public List<Tuple<int, bool>> FacesAlongPolyline(Polyline pline)
	{
		List<Tuple<int, bool>> valueTupleList = new List<Tuple<int, bool>>();
		if (pline.PointMCount() == 0)
		{
			return valueTupleList;
		}
		PointM ptM = pline.FirstPointM();
		PointM ptM2 = pline.LastPointM();
		double maxValue = double.MaxValue;
		double maxValue2 = double.MaxValue;
		checked
		{
			int count = FacePointCount - 1;
			int ordinal = default;
			int ordinal2 = default;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = FacePoints[i].PointM();
				double distanceTo = pointM.DistanceTo(ptM);
				if (distanceTo < maxValue)
				{
					ordinal = i;
					maxValue = distanceTo;
				}
				distanceTo = pointM.DistanceTo(ptM2);
				if (distanceTo < maxValue2)
				{
					ordinal2 = i;
					maxValue2 = distanceTo;
				}
			}
			int otherFacepointTerm = ordinal;
			int segmentIndex2 = 0;
			double fraction2 = 0.0;
			int segmentIndex = default;
			double fraction = default;
			int otherFacepoint = default;
			int faceIndex = default;
			do
			{
				Face face = null;
				double maxValue3 = double.MaxValue;
				FacePoint facePoint = FacePoints[otherFacepointTerm];
				int count2 = facePoint.Faces.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int faceIndex2 = facePoint.Faces[j];
					Face face2 = Faces[faceIndex2];
					int otherFacepoint2 = face2.OtherFacepoint(otherFacepointTerm);
					PointM ptM3 = FacePoints[otherFacepoint2].PointM();
					double actualDistance = 0.0;
					PointOnPolyline pointOnPolyline = pline.NearestPointOnPolyline(ptM3, ref actualDistance);
					if (pointOnPolyline.SegmentIndex > segmentIndex2 || ((pointOnPolyline.SegmentIndex == segmentIndex2) & (pointOnPolyline.Fraction > fraction2)))
					{
						double distanceTo2 = pointOnPolyline.PointM.DistanceTo(ptM3);
						if (distanceTo2 < maxValue3)
						{
							maxValue3 = distanceTo2;
							segmentIndex = pointOnPolyline.SegmentIndex;
							fraction = pointOnPolyline.Fraction;
							otherFacepoint = otherFacepoint2;
							faceIndex = faceIndex2;
							face = face2;
						}
					}
				}
				if (maxValue3 == double.MaxValue)
				{
					break;
				}
				if (face.fpA == otherFacepointTerm)
				{
					valueTupleList.Add(new Tuple<int, bool>(faceIndex, item2: true));
				}
				else
				{
					valueTupleList.Add(new Tuple<int, bool>(faceIndex, item2: false));
				}
				otherFacepointTerm = otherFacepoint;
				segmentIndex2 = segmentIndex;
				fraction2 = fraction;
			}
			while (otherFacepointTerm != ordinal2);
			return valueTupleList;
		}
	}

	public List<Tuple<int, bool>> FacesAlongPolyline_Clipped(Polyline pline)
	{
		List<Tuple<int, bool>> valueTupleList = new List<Tuple<int, bool>>();
		if (Perimeter == null || !Perimeter.ContainsOrIntersects(pline))
		{
			return valueTupleList;
		}
		List<Polyline> clipPolyline = Perimeter.ClipPolyline(pline);
		if (clipPolyline.IsNullOrEmpty())
		{
			return null;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (Polyline clipPolyline2 in clipPolyline)
		{
			List<Tuple<int, bool>> facesAlongPolyline = FacesAlongPolyline(clipPolyline2);
			foreach (Tuple<int, bool> facesAlongPolyline2 in facesAlongPolyline)
			{
				if (!intSet.Contains(facesAlongPolyline2.Item1))
				{
					valueTupleList.Add(facesAlongPolyline2);
					intSet.Add(facesAlongPolyline2.Item1);
				}
			}
		}
		return valueTupleList;
	}

	public List<ProfileFaceRange> ConvertFaceTuplesToProfileFaceRange(IList<Tuple<int, bool>> faces)
	{
		List<ProfileFaceRange> profileFaceRanges = new List<ProfileFaceRange>();
		checked
		{
			int count = faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Tuple<int, bool> tuple = faces[i];
				int tupleTerm = tuple.Item1;
				Face face = Faces[tupleTerm];
				int fpStart;
				int fpEnd;
				if (tuple.Item2)
				{
					fpStart = face.fpA;
					fpEnd = face.fpB;
				}
				else
				{
					fpStart = face.fpB;
					fpEnd = face.fpA;
				}
				ProfileFaceRange profileFaceRange = new ProfileFaceRange(tupleTerm, fpStart, fpEnd, double.NaN, double.NaN);
				profileFaceRanges.Add(profileFaceRange);
			}
			return profileFaceRanges;
		}
	}

	public List<ProfileFaceRange> PerimeterFacesAlongPolyline(Polyline pline, ref Polyline magnetizedPline)
	{
		PointM pt = pline.FirstPointM();
		PointOnPolyline ptOnPoly = null;
		int nearestPerimeterFace = NearestPerimeterFace(pt, ref ptOnPoly);
		PointM pointM = pline.Midpoint().PointM;
		PointOnPolyline ptOnPoly2 = null;
		int nearestPerimeterFace2 = NearestPerimeterFace(pointM, ref ptOnPoly2);
		PointM pt2 = pline.LastPointM();
		PointOnPolyline ptOnPoly3 = null;
		int nearestPerimeterFace3 = NearestPerimeterFace(pt2, ref ptOnPoly3);
		bool notIsPointBetweenCCW = !Perimeter.IsPointBetweenCCW(ptOnPoly, ptOnPoly2, ptOnPoly3);
		if (notIsPointBetweenCCW)
		{
			magnetizedPline = Perimeter.PolylineSubsection(ptOnPoly3, ptOnPoly).Reverse();
		}
		else
		{
			magnetizedPline = Perimeter.PolylineSubsection(ptOnPoly, ptOnPoly3);
		}
		List<ProfileFaceRange> profileFaceRanges = new List<ProfileFaceRange>();
		Face face = Faces[nearestPerimeterFace];
		int intervalIndex;
		int fpStart;
		int intervalIndex2;
		if (notIsPointBetweenCCW)
		{
			fpStart = face.fpB;
			intervalIndex2 = face.fpA;
			intervalIndex = FacePoints[intervalIndex2].Faces.Last();
		}
		else
		{
			fpStart = face.fpA;
			intervalIndex2 = face.fpB;
			intervalIndex = FacePoints[intervalIndex2].Faces.First();
		}
		double actualDistance;
		if (nearestPerimeterFace == nearestPerimeterFace3 && nearestPerimeterFace == nearestPerimeterFace2)
		{
			Polyline polyline = FacePolyline(nearestPerimeterFace);
			if (notIsPointBetweenCCW)
			{
				polyline = polyline.Reverse();
			}
			Polyline polyline2 = polyline;
			PointM ptM = magnetizedPline.FirstPointM();
			actualDistance = 0.0;
			PointOnPolyline ptOnPoly4 = polyline2.NearestPointOnPolyline(ptM, ref actualDistance);
			double sStart = -1.0 * polyline.DistanceAlong(ptOnPoly4);
			double length = polyline.Length;
			profileFaceRanges.Add(new ProfileFaceRange(nearestPerimeterFace, fpStart, intervalIndex2, sStart, length));
			return profileFaceRanges;
		}
		Polyline magnetizedPline2 = magnetizedPline;
		PointM ptM2 = FacePoints[intervalIndex2].PointM();
		actualDistance = 0.0;
		PointOnPolyline ptOnPoly5 = magnetizedPline2.NearestPointOnPolyline(ptM2, ref actualDistance);
		double distanceAlong = magnetizedPline.DistanceAlong(ptOnPoly5);
		double sStart2 = distanceAlong - FacePolylineLength(nearestPerimeterFace);
		profileFaceRanges.Add(new ProfileFaceRange(nearestPerimeterFace, fpStart, intervalIndex2, sStart2, distanceAlong));
		int ordinal = 0;
		while (true)
		{
			Face face2 = Faces[intervalIndex];
			int unity = -1;
			if (notIsPointBetweenCCW)
			{
				fpStart = face2.fpB;
				intervalIndex2 = face2.fpA;
				unity = FacePoints[intervalIndex2].Faces.Last();
			}
			else
			{
				fpStart = face2.fpA;
				intervalIndex2 = face2.fpB;
				unity = FacePoints[intervalIndex2].Faces.First();
			}
			Polyline magnetizedPline3 = magnetizedPline;
			PointM ptM3 = FacePoints[fpStart].PointM();
			actualDistance = 0.0;
			PointOnPolyline ptOnPoly6 = magnetizedPline3.NearestPointOnPolyline(ptM3, ref actualDistance);
			Polyline magnetizedPline4 = magnetizedPline;
			PointM ptM4 = FacePoints[intervalIndex2].PointM();
			actualDistance = 0.0;
			PointOnPolyline ptOnPoly7 = magnetizedPline4.NearestPointOnPolyline(ptM4, ref actualDistance);
			profileFaceRanges.Add(new ProfileFaceRange(intervalIndex, fpStart, intervalIndex2, magnetizedPline.DistanceAlong(ptOnPoly6), magnetizedPline.DistanceAlong(ptOnPoly7)));
			intervalIndex = unity;
			if (intervalIndex == nearestPerimeterFace3)
			{
				break;
			}
			ordinal = checked(ordinal + 1);
			if (ordinal > FaceCount)
			{
				return null;
			}
		}
		Face face3 = Faces[nearestPerimeterFace3];
		int fpBFace3Term;
		int fpEnd;
		if (notIsPointBetweenCCW)
		{
			fpBFace3Term = face3.fpB;
			fpEnd = face3.fpA;
		}
		else
		{
			fpBFace3Term = face3.fpA;
			fpEnd = face3.fpB;
		}
		Polyline magnetizedPline5 = magnetizedPline;
		PointM ptM5 = FacePoints[fpBFace3Term].PointM();
		actualDistance = 0.0;
		PointOnPolyline ptOnPoly8 = magnetizedPline5.NearestPointOnPolyline(ptM5, ref actualDistance);
		double distanceAlong2 = magnetizedPline.DistanceAlong(ptOnPoly8);
		double sEnd = FacePolylineLength(nearestPerimeterFace3) + distanceAlong2;
		profileFaceRanges.Add(new ProfileFaceRange(nearestPerimeterFace3, fpBFace3Term, fpEnd, distanceAlong2, sEnd));
		return profileFaceRanges;
	}

	private int NearestPerimeterFace(PointM pt, ref PointOnPolyline ptOnPoly)
	{
		Polygon perimeter = Perimeter;
		double actualDistance = 0.0;
		ptOnPoly = perimeter.NearestPointOnPolyline(pt, ref actualDistance);
		DistinctSpatialIndex meshFaceSpatialIndex = SharedData.GetMeshFaceSpatialIndex(this);
		int result = -1;
		double maxValue = double.MaxValue;
		List<int> elements = meshFaceSpatialIndex.GetElements(ptOnPoly.PointM);
		foreach (int element in elements)
		{
			if (!FaceIsPerimeter(element))
			{
				continue;
			}
			Polyline polyline = FacePolyline(element);
			double actualDistance2 = 0.0;
			polyline.NearestPointOnPolyline(ptOnPoly.PointM, ref actualDistance2);
			if (actualDistance2 < maxValue)
			{
				maxValue = actualDistance2;
				result = element;
				if (actualDistance2 < 9.999999974752427E-07)
				{
					break;
				}
			}
		}
		return result;
	}

	public List<ProfileCellRange> CellsUnderPolyline(Polyline polyline, bool mergeSplitSegments = true)
	{
		List<ProfileCellRange> profileCellRanges = new List<ProfileCellRange>();
		double segmentLengthSum = 0.0;
		checked
		{
			int count = polyline.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segment = polyline.Segment(i);
				List<ProfileCellRange> cellsUnderSegment = CellsUnderSegment(segment);
				foreach (ProfileCellRange cellsUnderSegment2 in cellsUnderSegment)
				{
					ProfileCellRange current = cellsUnderSegment2;
					int unity = -1;
					int count2 = profileCellRanges.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						if (profileCellRanges[j].Cell == current.Cell)
						{
							unity = -1;
							break;
						}
					}
					current.StationStart += segmentLengthSum;
					current.StationEnd += segmentLengthSum;
					if (unity == -1)
					{
						profileCellRanges.Add(current);
						continue;
					}
					ProfileCellRange profileCellRange = profileCellRanges[unity];
					profileCellRange.StationStart = System.Math.Min(profileCellRange.StationStart, current.StationStart);
					profileCellRange.StationStart = System.Math.Max(profileCellRange.StationEnd, current.StationEnd);
					profileCellRanges[unity] = profileCellRange;
				}
				segmentLengthSum += segment.Length;
			}
			profileCellRanges.Sort(ProfileCellRange.CompareStationStart);
			if (mergeSplitSegments)
			{
				List<ProfileCellRange> profileCellRangesList = new List<ProfileCellRange>();
				double naN = double.NaN;
				int cellProfileCellRange2Term = -1;
				int count3 = profileCellRanges.Count - 1;
				for (int k = 0; k <= count3; k++)
				{
					ProfileCellRange profileCellRange2 = profileCellRanges[k];
					if (profileCellRange2.Cell == cellProfileCellRange2Term)
					{
						if (double.IsNaN(naN))
						{
							naN = profileCellRanges[k - 1].StationStart;
						}
						continue;
					}
					if (!double.IsNaN(naN))
					{
						double stationEnd = profileCellRanges[k - 1].StationEnd;
						profileCellRangesList[profileCellRangesList.Count - 1] = new ProfileCellRange(cellProfileCellRange2Term, naN, stationEnd);
						naN = double.NaN;
					}
					profileCellRangesList.Add(profileCellRange2);
					cellProfileCellRange2Term = profileCellRange2.Cell;
				}
				if (!double.IsNaN(naN))
				{
					profileCellRangesList[profileCellRangesList.Count - 1] = new ProfileCellRange(cellProfileCellRange2Term, naN, profileCellRanges.Last().StationEnd);
				}
				profileCellRanges = profileCellRangesList;
			}
			return profileCellRanges;
		}
	}

	public int GetCell(PointM pt)
	{
		List<int> elements = SharedData.GetMeshCellSpatialIndex(this).GetElements(pt.X, pt.Y);
		foreach (int element in elements)
		{
			if (CellPolygon(element).Contains(pt))
			{
				return element;
			}
		}
		return -1;
	}

	public List<ProfileCellRange> CellsUnderSegment(SegmentM segment)
	{
		List<int> elements = SharedData.GetMeshCellSpatialIndex(this).GetElements(segment);
		List<ProfileCellRange> profileCellRanges = new List<ProfileCellRange>();
		checked
		{
			foreach (int element in elements)
			{
				Polygon polygon = CellPolygon(element);
				bool polygonSegmentTerm = polygon.Contains(segment.A);
				bool polygonSegmentTerm2 = polygon.Contains(segment.B);
				if (polygonSegmentTerm && polygonSegmentTerm2)
				{
					profileCellRanges.Add(new ProfileCellRange(element, 0.0, segment.Length));
					continue;
				}
				List<PointIntersect> lineIntersectionMulti = polygon.GetLineIntersectionMulti(segment);
				if (lineIntersectionMulti != null && lineIntersectionMulti.Count > 0)
				{
					double bFraction = lineIntersectionMulti.First().BFraction;
					double bFraction2 = lineIntersectionMulti.First().BFraction;
					int count = lineIntersectionMulti.Count - 1;
					for (int i = 1; i <= count; i++)
					{
						bFraction = System.Math.Min(bFraction, lineIntersectionMulti[i].BFraction);
						bFraction2 = System.Math.Max(bFraction2, lineIntersectionMulti[i].BFraction);
					}
					double length = segment.Length;
					if (polygonSegmentTerm)
					{
						profileCellRanges.Add(new ProfileCellRange(element, 0.0, length * bFraction2));
					}
					else if (polygonSegmentTerm2)
					{
						profileCellRanges.Add(new ProfileCellRange(element, length * bFraction, length));
					}
					else
					{
						profileCellRanges.Add(new ProfileCellRange(element, length * bFraction, length * bFraction2));
					}
				}
			}
			profileCellRanges.Sort(ProfileCellRange.CompareStationStart);
			return profileCellRanges;
		}
	}

	public List<int> UniqueCellsAroundFaces(List<int> uniqueFaces)
	{
		IEnumerable<int> first = uniqueFaces.Select([SpecialName] (int f) => Faces[f].cellA);
		IEnumerable<int> second = uniqueFaces.Select([SpecialName] (int f) => Faces[f].cellB);
		return first.Concat(second).Distinct().ToList();
	}

	public void PointsOnFace(int f, PointMs pts)
	{
		pts.Clear();
		Face face = Faces[f];
		pts.Add(new PointM(FacePoints[face.fpA].Point));
		if (face.InternalPoints != null)
		{
			pts.AddRange(face.InternalPoints);
		}
		pts.Add(new PointM(FacePoints[face.fpB].Point));
	}

	public PointMs PointsOnFace(int f)
	{
		PointMs pointMs = new PointMs();
		PointsOnFace(f, pointMs);
		return pointMs;
	}

	public void PointsOnFaceBeforeMidside(Face face, FaceMidside mPt, PointMs pts)
	{
		pts.Clear();
		pts.Add(new PointM(FacePoints[face.fpA].Point));
		checked
		{
			if (face.InternalPoints != null)
			{
				int count = mPt.SegmentIndex - 1;
				for (int i = 0; i <= count; i++)
				{
					pts.Add(face.InternalPoints[i]);
				}
			}
			pts.Add(new PointM(mPt.Point.X, mPt.Point.Y));
		}
	}

	public void PointsOnFaceAfterMidside(Face face, FaceMidside mPt, PointMs pts)
	{
		pts.Clear();
		pts.Add(new PointM(mPt.Point.X, mPt.Point.Y));
		checked
		{
			if (face.InternalPoints != null)
			{
				int segmentIndex = mPt.SegmentIndex;
				int count = face.InternalPoints.Count - 1;
				for (int i = segmentIndex; i <= count; i++)
				{
					pts.Add(face.InternalPoints[i]);
				}
			}
			pts.Add(new PointM(FacePoints[face.fpB].Point));
		}
	}

	public int NearestFaceInCell(int c, PointM point, [Optional][DefaultParameterValue(double.NaN)] ref double fDistance)
	{
		Cell cell = _cells[c];
		int faceTerm = -1;
		fDistance = double.MaxValue;
		foreach (int face in cell.Faces)
		{
			double fDistance2 = FacePolyline(face).DistanceToPoint(point);
			if (faceTerm == -1 || fDistance2 < fDistance)
			{
				faceTerm = face;
				fDistance = fDistance2;
			}
		}
		return faceTerm;
	}

	public DistinctSpatialIndex ComputePerimeterSpatialIndex()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		DistinctSpatialIndex result = DistinctSpatialIndex.CreateSegmentIndex(_perimeter);
		stopwatch.Stop();
		_progressReporter.ReportMessage("Perimeter Spatial Index computed in " + stopwatch.ElapsedMilliseconds + " ms");
		return result;
	}

	public void EnsureFaceNormals()
	{
		checked
		{
			if (_faceNormal == null)
			{
				_faceNormal = new Vector2D[Faces.Count - 1 + 1];
				int count = Faces.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM pointM = FaceSegment(i).PerpendicularUnitVector();
					_faceNormal[i].X = (float)(0.0 - pointM.X);
					_faceNormal[i].Y = (float)(0.0 - pointM.Y);
				}
			}
		}
	}

	public Vector2D FaceNormal(int f)
	{
		return _faceNormal[f];
	}

	public float GetCellValueFromFaces(int cIdx, float[] faceValues, Func<int, Point2Double> GetFaceApplicationPoint)
	{
		Cell cell = _cells[cIdx];
		PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(cell.Point);
		foreach (int face in cell.Faces)
		{
			float faceValue = faceValues[face];
			if (faceValue != -9999f)
			{
				planarRegressionZ.Add(GetFaceApplicationPoint(face), faceValue);
			}
		}
		if (planarRegressionZ.Count() == 0)
		{
			return -9999f;
		}
		return (float)planarRegressionZ.SolveZ();
	}

	public float GetCellValueFromFaces(int cidx, FaceValues[] faceValues, Func<int, Point2Double> GetFaceApplicationPoint)
	{
		Cell cell = _cells[cidx];
		PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(cell.Point);
		foreach (int face2 in cell.Faces)
		{
			Face face = Faces[face2];
			float valueB = ((cidx != face.cellA) ? faceValues[face2].ValueB : faceValues[face2].ValueA);
			if (valueB != -9999f)
			{
				planarRegressionZ.Add(GetFaceApplicationPoint(face2), valueB);
			}
		}
		if (planarRegressionZ.Count() == 0)
		{
			return -9999f;
		}
		return (float)planarRegressionZ.SolveZ();
	}

	public float GetFacepointValueFromFaces(int fpIdx, float[] faceValues, Func<int, Point2Double> GetFaceApplicationPoint)
	{
		FacePoint facePoint = FacePoints[fpIdx];
		PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(facePoint.Point);
		foreach (int face in facePoint.Faces)
		{
			float faceValue = faceValues[face];
			if (faceValue != -9999f)
			{
				planarRegressionZ.Add(GetFaceApplicationPoint(face), faceValue);
			}
		}
		if (planarRegressionZ.Count() == 0)
		{
			return -9999f;
		}
		return (float)planarRegressionZ.SolveZ();
	}

	public float GetCellValueFromFacePoints(int cidx, float[] fpValues)
	{
		Cell cell = _cells[cidx];
		PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(cell.Point);
		foreach (int face in cell.Faces)
		{
			int fPNext = Faces[face].GetFPNext(cidx);
			float fpValue = fpValues[face];
			if (fpValue != -9999f)
			{
				planarRegressionZ.Add(FacePoints[fPNext].Point, fpValue);
			}
		}
		if (planarRegressionZ.Count() == 0)
		{
			return -9999f;
		}
		return (float)planarRegressionZ.SolveZ();
	}

	public float GetFaceValueFromCells(int fIdx, float[] cellValues)
	{
		Face face = Faces[fIdx];
		int cellA = face.cellA;
		int cellB = face.cellB;
		float cellValue = cellValues[cellA];
		float cellValue2 = cellValues[cellB];
		if (cellValue == -9999f)
		{
			return cellValue2;
		}
		if (cellValue2 == -9999f)
		{
			return cellValue;
		}
		return (cellValue + cellValue2) / 2f;
	}

	public float[] GetFPLocalValues(int cIdx, float[] fpGlobalValues)
	{
		Cell cell = _cells[cIdx];
		checked
		{
			float[] fpLocalValues = new float[cell.Faces.Count - 1 + 1];
			int count = fpLocalValues.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				int ordinal = i;
				int index = cell.Faces[i];
				int fPPrev = Faces[index].GetFPPrev(cIdx);
				fpLocalValues[ordinal] = fpGlobalValues[fPPrev];
			}
			return fpLocalValues;
		}
	}

	public Vector2D[] GetFPLocalValues(int cIdx, Vector2D[] fpGlobalValues)
	{
		Cell cell = _cells[cIdx];
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[cell.Faces.Count - 1 + 1];
			int count = vector2Ds.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				int ordinal = i;
				int index = cell.Faces[i];
				int fPPrev = Faces[index].GetFPPrev(cIdx);
				vector2Ds[ordinal] = fpGlobalValues[fPPrev];
			}
			return vector2Ds;
		}
	}

	public float[] GetFPLocalValues(int cIdx, float[][] fpValuesPerCell)
	{
		Cell cell = _cells[cIdx];
		checked
		{
			float[] fpLocalValues = new float[cell.Faces.Count - 1 + 1];
			int count = fpLocalValues.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				int ordinal = i;
				int faceIndex = cell.Faces[i];
				int fPPrev = Faces[faceIndex].GetFPPrev(cIdx);
				fpLocalValues[ordinal] = fpValuesPerCell[fPPrev][FacePoints[fPPrev].Faces.IndexOf(faceIndex)];
			}
			return fpLocalValues;
		}
	}

	public void DebugExportAll()
	{
		if (SharedData.RasMapper == null)
		{
			return;
		}
		string textValue = "Graph Nodes (No virtual)";
		RASMapper rasMapper = SharedData.RasMapper;
		FeatureLayer featureLayer = ExportNodeLayer(textValue);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer);
		featureLayer.Checked = true;
		string textValue2 = "Graph Edges";
		FeatureLayer featureLayer2 = ExportEdgeLayer(textValue2);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue2))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue2);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer2);
		featureLayer2.Checked = true;
		string textValue3 = "Mesh Perimeter";
		FeatureLayer featureLayer3 = ExportPerimeterLayer(textValue3);
		if (featureLayer3 != null)
		{
			if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue3))
			{
				rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue3);
			}
			rasMapper.MapLayersGroup.Nodes.Add(featureLayer3);
			featureLayer3.Checked = true;
		}
		string textValue4 = "Mesh FacePoints";
		FeatureLayer featureLayer4 = ExportFacePointLayer(textValue4);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue4))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue4);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer4);
		featureLayer4.Checked = true;
		string textValue5 = "Mesh Faces";
		FeatureLayer featureLayer5 = ExportFaceLayer(textValue5);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue5))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue5);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer5);
		featureLayer5.Checked = true;
		string textValue6 = "Mesh TIN";
		FeatureLayer featureLayer6 = ExportMeshTINLayer(textValue6);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue6))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue6);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer6);
		featureLayer6.Checked = false;
		string textValue7 = "Perimeter TIN";
		FeatureLayer featureLayer7 = ExportPerimTINLayer(textValue7);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue7))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue7);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer7);
		featureLayer7.Checked = false;
		string textValue8 = "Merged TIN (experimental)";
		FeatureLayer featureLayer8 = ExportMergedTINLayer(textValue8);
		if (rasMapper.MapLayersGroup.Nodes.ContainsKey(textValue8))
		{
			rasMapper.MapLayersGroup.Nodes.RemoveByKey(textValue8);
		}
		rasMapper.MapLayersGroup.Nodes.Add(featureLayer8);
		featureLayer8.Checked = false;
	}

	public FeatureLayer ExportPerimTINLayer(string name)
	{
		DevelopmentTin perimeterTin = GetPerimeterTin(_perimeter);
		return ExportTINLayer(name, perimeterTin);
	}

	public FeatureLayer ExportMergedTINLayer(string name)
	{
		double nearPointTolerance = 1E-06;
		DelaunayTINFactory delaunayTINFactory = new DelaunayTINFactory(nearPointTolerance);
		PointMs pointMs = new PointMs();
		Cell[] cells = _cells;
		for (int i = 0; i < cells.Length; i = checked(i + 1))
		{
			Cell cell = cells[i];
			if (cell.Faces != null && cell.Faces.Count > 1)
			{
				pointMs.Add(new PointM(cell.Point));
			}
		}
		new Extent((IExtent)_perimeter);
		DevelopmentTin tin = delaunayTINFactory.ComputeTIN(_perimeter, null, pointMs, null, removeDeletedTriangles: false);
		return ExportTINLayer(name, tin);
	}

	public FeatureLayer ExportMeshTINLayer(string name)
	{
		PointMs pointMs = new PointMs();
		Cell[] cells = _cells;
		for (int i = 0; i < cells.Length; i = checked(i + 1))
		{
			Cell cell = cells[i];
			if (cell.Faces != null && cell.Faces.Count > 1)
			{
				pointMs.Add(new PointM(cell.Point));
			}
		}
		new Extent((IExtent)_perimeter);
		DevelopmentTin meshTin = null;
		ProgressReporter reporter = ProgressReporter.None();
		GetValidateMeshTIN(pointMs, ref meshTin, _extent, reporter);
		return ExportTINLayer(name, meshTin);
	}

	private FeatureLayer ExportTINLayer(string name, DevelopmentTin tin)
	{
		TinFeatureLayer tinFeatureLayer = new TinFeatureLayer(name);
		tinFeatureLayer.AddFeature(tin.MakeTinWithDeletedTris());
		return tinFeatureLayer;
	}

	public FeatureLayer ExportNodeLayer(string name)
	{
		PointFeatureLayer pointFeatureLayer = new PointFeatureLayer(name);
		string columnName = "Index";
		pointFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				Cell cell = _cells[i];
				if (cell.Faces != null && cell.Faces.Count != 1)
				{
					Point feature = new Point(cell.Point);
					pointFeatureLayer.AddFeature(feature)[columnName] = i;
				}
			}
			return pointFeatureLayer;
		}
	}

	public FeatureLayer ExportFacePointLayer(string name)
	{
		PointFeatureLayer pointFeatureLayer = new PointFeatureLayer(name);
		string columnName = "Index";
		pointFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		checked
		{
			int count = FacePoints.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point feature = new Point(FacePoints[i].Point);
				pointFeatureLayer.AddFeature(feature)[columnName] = i;
			}
			return pointFeatureLayer;
		}
	}

	public FeatureLayer ExportFaceLayer(string name)
	{
		PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer(name);
		string columnName = "Index";
		polylineFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				PointMs pointMs = new PointMs();
				int fpA = face.fpA;
				int fpB = face.fpB;
				if (fpA < 0 || fpB < 0)
				{
					continue;
				}
				FacePoint facePoint = FacePoints[fpA];
				FacePoint facePoint2 = FacePoints[fpB];
				pointMs.Add(new PointM(facePoint.Point.X, facePoint.Point.Y));
				if (face.InternalPoints != null)
				{
					int count2 = face.InternalPoints.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						pointMs.Add(face.InternalPoints[j]);
					}
				}
				pointMs.Add(new PointM(facePoint2.Point.X, facePoint2.Point.Y));
				polylineFeatureLayer.AddFeature(new Polyline(pointMs))[columnName] = i;
			}
			return polylineFeatureLayer;
		}
	}

	public FeatureLayer ExportEdgeLayer(string name)
	{
		PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer(name);
		string columnName = "Cell A";
		string columnName2 = "Cell B";
		polylineFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		polylineFeatureLayer.AddAttributeColumn(columnName2, typeof(int));
		checked
		{
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				if (face.cellA != -1 && face.cellB != -1)
				{
					PointMs pointMs = new PointMs();
					pointMs.Add(new PointM(_cells[face.cellA].Point));
					pointMs.Add(new PointM(_cells[face.cellB].Point));
					DataRow dataRow = polylineFeatureLayer.AddFeature(new Polyline(pointMs));
					dataRow[columnName] = face.cellA;
					dataRow[columnName2] = face.cellB;
				}
			}
			return polylineFeatureLayer;
		}
	}

	public FeatureLayer ExportPerimeterLayer(string name)
	{
		if (!_perimeter.IsValid())
		{
			return null;
		}
		PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer(name);
		checked
		{
			int count = _perimeter.Count - 2;
			PointMs pointMs;
			for (int i = 0; i <= count; i++)
			{
				pointMs = new PointMs();
				pointMs.Add(_perimeter.PointM(i));
				pointMs.Add(_perimeter.PointM(i + 1));
				polylineFeatureLayer.AddFeature(new Polyline(pointMs));
			}
			pointMs = new PointMs();
			pointMs.Add(_perimeter.PointM(_perimeter.Count - 2));
			pointMs.Add(_perimeter.PointM(_perimeter.Count - 1));
			polylineFeatureLayer.AddFeature(new Polyline(pointMs));
			return polylineFeatureLayer;
		}
	}

	public static bool ExistsAndIsCurrent(H5Reader hr, string meshname, string meshDate)
	{
		string meshGroupName = MakeMeshGroupName(meshname);
		if (hr.ExistsAsGroup(meshGroupName))
		{
			if (Operators.CompareString(hr.TryReadStringAttributeScalar(meshGroupName, "Version"), "1.0", TextCompare: false) != 0)
			{
				return false;
			}
			if (!string.Equals(hr.TryReadStringAttributeScalar(meshGroupName, "Data Date", "No Date"), meshDate, StringComparison.CurrentCultureIgnoreCase))
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public void SaveMesh(H5Writer hw, string meshName, string meshDate, ProgressReporter prog = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (!IsComplete())
		{
			return;
		}
		string meshGroupName = MakeMeshGroupName(meshName);
		if (hw.ExistsAsGroup(meshGroupName))
		{
			hw.Delete(meshGroupName);
		}
		hw.CreateGroup(meshGroupName);
		hw.WriteAttribute(meshGroupName, "Version", "1.0");
		hw.WriteAttribute(meshGroupName, "Data Date", meshDate);
		hw.WriteAttribute(meshGroupName, "Extents", _extent.MinXMaxXMinYMaxY());
		hw.WriteAttribute(meshGroupName, "Cell Maximum Index", _cellMaximumAreaIndex);
		hw.WriteAttribute(meshGroupName, "Cell Maximum Size", _cellMaximumArea);
		hw.WriteAttribute(meshGroupName, "Cell Minimum Size", _cellMinimumArea);
		hw.WriteAttribute(meshGroupName, "Cell Average Size", _cellAverageArea);
		checked
		{
			using (HDFBufferedWriter<double> hDFBufferedWriter = new HDFBufferedWriter<double>(hw, meshGroupName + "/Perimeter", _perimeter.Count, 2, "Points", "X", "Y"))
			{
				int count2 = _perimeter.Count - 1;
				for (int i = 0; i <= count2; i++)
				{
					PointM pointM = _perimeter.PointM(i);
					hDFBufferedWriter.AddRow(pointM.X, pointM.Y);
				}
			}
			using (HDFBufferedWriter<double> hDFBufferedWriter2 = new HDFBufferedWriter<double>(hw, meshGroupName + "/Cells Center Coordinate", _cells.Count(), 2, "Cell", "X", "Y"))
			{
				Cell[] cells = _cells;
				for (int j = 0; j < cells.Length; j++)
				{
					Cell cell = cells[j];
					hDFBufferedWriter2.AddRow(cell.Point.X, cell.Point.Y);
				}
			}
			prog.ReportProgress(5);
			using (HDFBufferedWriter<int> hDFBufferedWriter3 = new HDFBufferedWriter<int>(hw, meshGroupName + "/Cells Face and Orientation Info", _cells.Count(), 2, "Cell", "Starting Index", "Count"))
			{
				using HDFBufferedWriter<int> hDFBufferedWriter4 = new HDFBufferedWriter<int>(hw, meshGroupName + "/Cells Face and Orientation Values", 0, 2, "row", "Face Index", "Orientation");
				int count3 = _cells.Count() - 1;
				for (int k = 0; k <= count3; k++)
				{
					Cell cell2 = _cells[k];
					if (cell2.Faces == null)
					{
						continue;
					}
					hDFBufferedWriter3.AddRow(hDFBufferedWriter4.RowsWritten, cell2.Faces.Count);
					int count4 = cell2.Faces.Count - 1;
					for (int l = 0; l <= count4; l++)
					{
						int faceIndex = cell2.Faces[l];
						Face face = Faces[faceIndex];
						if (cell2.Faces.Count == 1)
						{
							hDFBufferedWriter4.AddRow(faceIndex, -1);
						}
						else if (face.cellA == k)
						{
							hDFBufferedWriter4.AddRow(faceIndex, 1);
						}
						else
						{
							hDFBufferedWriter4.AddRow(faceIndex, -1);
						}
					}
				}
			}
			prog.ReportProgress(30);
			int facesCell3Term = 3;
			Cell[] cells2 = _cells;
			for (int m = 0; m < cells2.Length; m++)
			{
				Cell cell3 = cells2[m];
				if (cell3.Faces != null && cell3.Faces.Count > facesCell3Term)
				{
					facesCell3Term = cell3.Faces.Count;
				}
			}
			using (HDFBufferedWriter<int> hDFBufferedWriter5 = new HDFBufferedWriter<int>(hw, meshGroupName + "/Cells FacePoint Indexes", _cells.Count(), facesCell3Term, "Cell", "Face Point Indexes"))
			{
				int[] intValues = new int[facesCell3Term - 1 + 1];
				int count5 = _cells.Count() - 1;
				for (int n = 0; n <= count5; n++)
				{
					Cell cell4 = _cells[n];
					Buffers.ArraySetAllVB(intValues, -1);
					if (cell4.Faces != null)
					{
						if (cell4.Faces.Count == 1)
						{
							int index = cell4.Faces[0];
							intValues[0] = Faces[index].fpA;
							intValues[1] = Faces[index].fpB;
						}
						else
						{
							int count6 = cell4.Faces.Count - 1;
							for (int intervalIndex = 0; intervalIndex <= count6; intervalIndex++)
							{
								Face face2 = Faces[cell4.Faces[intervalIndex]];
								intValues[intervalIndex] = face2.GetFPPrev(n);
							}
						}
					}
					hDFBufferedWriter5.AddRow(intValues);
				}
			}
			prog.ReportProgress(55);
			using (HDFBufferedWriter<int> hDFBufferedWriter6 = new HDFBufferedWriter<int>(hw, meshGroupName + "/Faces FacePoint Indexes", Faces.Count, 2, "Face", "Face Point A", "Face Point B"))
			{
				using HDFBufferedWriter<int> hDFBufferedWriter7 = new HDFBufferedWriter<int>(hw, meshGroupName + "/Faces Cell Indexes", Faces.Count, 2, "Face", "Cell 0", "Cell 1");
				using HDFBufferedWriter<float> hDFBufferedWriter8 = new HDFBufferedWriter<float>(hw, meshGroupName + "/Faces NormalUnitVector and Length", Faces.Count, 3, "Face", "X Component", "Y Component", "Face Length");
				int count7 = Faces.Count - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count7; intervalIndex2++)
				{
					Face face3 = Faces[intervalIndex2];
					hDFBufferedWriter6.AddRow(face3.fpA, face3.fpB);
					hDFBufferedWriter7.AddRow(face3.cellA, face3.cellB);
					SegmentM segmentM = FaceSegment(intervalIndex2);
					PointM pointM2 = segmentM.PerpendicularUnitVector();
					hDFBufferedWriter8.AddRow((float)(0.0 - pointM2.X), (float)(0.0 - pointM2.Y), (float)segmentM.Length);
				}
			}
			prog.ReportProgress(63);
			using (HDFBufferedWriter<int> hDFBufferedWriter9 = new HDFBufferedWriter<int>(hw, meshGroupName + "/Faces Perimeter Info", Faces.Count, 2, "Face", "Start Index", "Count"))
			{
				using HDFBufferedWriter<double> hDFBufferedWriter10 = new HDFBufferedWriter<double>(hw, meshGroupName + "/Faces Perimeter Values", 0, 2, "row", "X", "Y");
				int count8 = Faces.Count - 1;
				for (int intervalIndex3 = 0; intervalIndex3 <= count8; intervalIndex3++)
				{
					Face face4 = Faces[intervalIndex3];
					if (face4.InternalPoints == null || face4.InternalPoints.Count == 0)
					{
						hDFBufferedWriter9.AddRow(hDFBufferedWriter10.RowsWritten, 0);
						continue;
					}
					hDFBufferedWriter9.AddRow(hDFBufferedWriter10.RowsWritten, face4.InternalPoints.Count);
					int count9 = face4.InternalPoints.Count - 1;
					for (int intervalIndex4 = 0; intervalIndex4 <= count9; intervalIndex4++)
					{
						hDFBufferedWriter10.AddRow(face4.InternalPoints[intervalIndex4].X, face4.InternalPoints[intervalIndex4].Y);
					}
				}
			}
			using (HDFBufferedWriter<double> hDFBufferedWriter11 = new HDFBufferedWriter<double>(hw, meshGroupName + "/FacePoints Coordinate", FacePoints.Count, 2, "Face Point", "X", "Y"))
			{
				foreach (FacePoint facePoint in FacePoints)
				{
					hDFBufferedWriter11.AddRow(facePoint.Point.X, facePoint.Point.Y);
				}
			}
			prog.ReportProgress(80);
			using (HDFBufferedWriter<int> hDFBufferedWriter12 = new HDFBufferedWriter<int>(hw, meshGroupName + "/FacePoints Cell Info", FacePoints.Count, 2, "Face Point", "Starting Index", "Count"))
			{
				using HDFBufferedWriter<int> hDFBufferedWriter13 = new HDFBufferedWriter<int>(hw, meshGroupName + "/FacePoints Cell Index Values", 0, 1, "row", "Cell Index");
				int count10 = FacePoints.Count - 1;
				for (int intervalIndex5 = 0; intervalIndex5 <= count10; intervalIndex5++)
				{
					_ = FacePoints[intervalIndex5];
					List<int> cellsAroundFacepoint = GetCellsAroundFacepoint(intervalIndex5);
					hDFBufferedWriter12.AddRow(hDFBufferedWriter13.RowsWritten, cellsAroundFacepoint.Count);
					int count11 = cellsAroundFacepoint.Count - 1;
					for (int intervalIndex6 = 0; intervalIndex6 <= count11; intervalIndex6++)
					{
						hDFBufferedWriter13.AddRow(cellsAroundFacepoint[intervalIndex6]);
					}
				}
			}
			using (HDFBufferedWriter<int> hDFBufferedWriter14 = new HDFBufferedWriter<int>(hw, meshGroupName + "/FacePoints Is Perimeter", FacePoints.Count, 1, "Face Point", "Is On Perimeter"))
			{
				int count12 = FacePoints.Count - 1;
				for (int ordinal = 0; ordinal <= count12; ordinal++)
				{
					hDFBufferedWriter14.AddRow(unchecked(0 - (FacePointIsPerimeter(ordinal) ? 1 : 0)));
				}
			}
			List<FaceAroundFacePoint>[] faceAroundFacePointListArray = new List<FaceAroundFacePoint>[FacePoints.Count - 1 + 1];
			int count13 = Faces.Count - 1;
			for (int intervalIndex7 = 0; intervalIndex7 <= count13; intervalIndex7++)
			{
				Face face5 = Faces[intervalIndex7];
				int fpA = face5.fpA;
				int fpB = face5.fpB;
				if (faceAroundFacePointListArray[fpA] == null)
				{
					faceAroundFacePointListArray[fpA] = new List<FaceAroundFacePoint>();
				}
				if (faceAroundFacePointListArray[fpB] == null)
				{
					faceAroundFacePointListArray[fpB] = new List<FaceAroundFacePoint>();
				}
				faceAroundFacePointListArray[fpA].Add(new FaceAroundFacePoint(intervalIndex7, -1, FaceSegment(intervalIndex7).Bearing(), FaceIsPerimeter(intervalIndex7)));
				faceAroundFacePointListArray[fpB].Add(new FaceAroundFacePoint(intervalIndex7, 1, FaceSegmentReversed(intervalIndex7).Bearing(), FaceIsPerimeter(intervalIndex7)));
			}
			int count14 = FacePoints.Count - 1;
			for (int intervalIndex8 = 0; intervalIndex8 <= count14; intervalIndex8++)
			{
				faceAroundFacePointListArray[intervalIndex8].Sort(FaceAroundFacePoint.CompareBearings);
				int ordinal2 = 0;
				foreach (FaceAroundFacePoint faceAroundFacePoint in faceAroundFacePointListArray[intervalIndex8])
				{
					if (faceAroundFacePoint.isPerimeter)
					{
						ordinal2++;
					}
				}
				if (ordinal2 != 2)
				{
					continue;
				}
				if (faceAroundFacePointListArray[intervalIndex8].Count == 2)
				{
					int faceIndex2 = FacePoints[intervalIndex8].Faces[0];
					if (faceAroundFacePointListArray[intervalIndex8][0].faceIndex != faceIndex2)
					{
						FaceAroundFacePoint faceAroundFacePoint2 = faceAroundFacePointListArray[intervalIndex8][0];
						faceAroundFacePointListArray[intervalIndex8][0] = faceAroundFacePointListArray[intervalIndex8][1];
						faceAroundFacePointListArray[intervalIndex8][1] = faceAroundFacePoint2;
					}
				}
				else
				{
					int ordinal3 = 0;
					for (int count = faceAroundFacePointListArray[intervalIndex8].Count; !((faceAroundFacePointListArray[intervalIndex8].First().isPerimeter && faceAroundFacePointListArray[intervalIndex8].Last().isPerimeter) | (ordinal3 == count)); ordinal3++)
					{
						faceAroundFacePointListArray[intervalIndex8].Add(faceAroundFacePointListArray[intervalIndex8].First());
						faceAroundFacePointListArray[intervalIndex8].RemoveAt(0);
					}
				}
			}
			using (HDFBufferedWriter<int> hDFBufferedWriter15 = new HDFBufferedWriter<int>(hw, meshGroupName + "/FacePoints Face and Orientation Info", FacePoints.Count, 2, "Face Point", "Start Index", "Count"))
			{
				using HDFBufferedWriter<int> hDFBufferedWriter16 = new HDFBufferedWriter<int>(hw, meshGroupName + "/FacePoints Face and Orientation Values", 0, 2, "row", "Face Index", "Orientation: '1' means face points toward this facepoint, '-1' means face points away from this facepoint");
				int count15 = FacePoints.Count - 1;
				_Closure_0024__260_002D0 capturedLocals = default;
				for (int intervalIndex9 = 0; intervalIndex9 <= count15; intervalIndex9++)
				{
					List<FaceAroundFacePoint> faceAroundFacePoints = faceAroundFacePointListArray[intervalIndex9];
					hDFBufferedWriter15.AddRow(hDFBufferedWriter16.RowsWritten, faceAroundFacePoints.Count);
					List<int> cellsAroundFacepoint2 = GetCellsAroundFacepoint(intervalIndex9);
					foreach (int cellsAroundFacepoint22 in cellsAroundFacepoint2)
					{
						capturedLocals = new _Closure_0024__260_002D0(capturedLocals);
						capturedLocals.ccwFaceIdx = FaceCCWOfCell(intervalIndex9, cellsAroundFacepoint22);
						if (capturedLocals.ccwFaceIdx == -1)
						{
							break;
						}
						int index2 = faceAroundFacePointListArray[intervalIndex9].FirstIndexOf(capturedLocals._Lambda_0024__0);
						hDFBufferedWriter16.AddRow(capturedLocals.ccwFaceIdx, faceAroundFacePoints[index2].faceOrientation);
					}
					FacePoint facePoint2 = FacePoints[intervalIndex9];
					int[] faces = new int[facePoint2.Faces.Count - 1 + 1];
					facePoint2.Faces = faces;
					int count16 = faceAroundFacePoints.Count - 1;
					for (int intervalIndex10 = 0; intervalIndex10 <= count16; intervalIndex10++)
					{
						facePoint2.Faces[intervalIndex10] = faceAroundFacePoints[intervalIndex10].faceIndex;
					}
					FacePoints[intervalIndex9] = facePoint2;
					faceAroundFacePoints = null;
				}
			}
			prog.ReportProgress(100);
			stopwatch.Stop();
		}
	}

	public static string MakeMeshGroupName(string meshname)
	{
		return "Geometry/2D Flow Areas/" + meshname;
	}

	public Tin ExportAsTIN(TerrainLayer trLayer)
	{
		checked
		{
			PointMs pointMs = new PointMs(NonVirtualCellCount + FacePointCount);
			int count = NonVirtualCellCount - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2Double point = Cell(i).Point;
				pointMs.Add(new PointM(point.X, point.Y, 0.0));
			}
			int count2 = FacePointCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				Point2Double point2 = FacePoints[j].Point;
				pointMs.Add(new PointM(point2.X, point2.Y, 0.0));
			}
			float[] pointElevations = trLayer.ComputePointElevations(pointMs);
			int count3 = pointMs.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				PointM pointM = pointMs[k];
				pointM.Z = pointElevations[k];
				pointMs[k] = pointM;
			}
			List<TriangleStruct> triangleStructs = new List<TriangleStruct>();
			int nonVirtualCellCount = NonVirtualCellCount;
			int count4 = NonVirtualCellCount - 1;
			for (int l = 0; l <= count4; l++)
			{
				Cell(l);
				List<int> cellFacePoints = CellFacePoints(l);
				int count5 = cellFacePoints.Count - 1;
				for (int m = 0; m <= count5; m++)
				{
					int cellFacePoint = cellFacePoints[m];
					int cellFacePoint2 = ((m != cellFacePoints.Count - 1) ? cellFacePoints[m + 1] : cellFacePoints[0]);
					TriangleStruct triangleStruct = default;
					triangleStruct.AssignPoints(l, nonVirtualCellCount + cellFacePoint, nonVirtualCellCount + cellFacePoint2);
					triangleStructs.Add(triangleStruct);
				}
			}
			Tin tin = new Tin(pointMs, triangleStructs.ToArray());
			tin.ComputeEdgesAndAdjacentTriangles();
			return tin;
		}
	}

	public Profile GetSingleFaceProfile(TerrainLayer terrain, int faceIdx, float faceProfileFilterTol)
	{
		return ComputeFaceProfiles(new int[1] { faceIdx }, terrain, faceProfileFilterTol).First();
	}

	public Profile[] ComputeFaceProfiles(TerrainLayer terrain, float faceProfileFilterTol, ProgressReporter reporter = null)
	{
		return ComputeFaceProfiles(Enumerable.Range(0, Faces.Count).ToArray(), terrain, faceProfileFilterTol, reporter);
	}

	public Profile[] ComputeFaceProfiles(int[] faceIndexes, TerrainLayer terrain, float faceProfileFilterTol, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		ProgressReporter progressReporter = reporter.WithProgressModifier(0, 0.2f);
		List<Polyline> polylines = new List<Polyline>(faceIndexes.Length);
		int progressMod = RasMapperLib.Utilities.Math.ProgressMod(faceIndexes.Count());
		checked
		{
			int count = faceIndexes.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (unchecked(i % progressMod) == 0)
				{
					progressReporter.ReportProgress((int)System.Math.Round((double)(100 * i) / (double)faceIndexes.Count()));
				}
				int f = faceIndexes[i];
				polylines.Add(FacePolyline(f));
			}
			progressReporter.ReportProgress(100);
			progressReporter = reporter.WithProgressModifier(20, 0.8f);
			return terrain.ComputeProfiles(polylines, faceProfileFilterTol, progressReporter);
		}
	}

	public Profile[] ComputeAllFaceProfiles(TerrainLayer terrain, float faceProfileFilterTol, Action<float> reportProgress = null)
	{
		ComputeFaceSpatialIndex();
		throw new NotImplementedException();
	}

	public static float ManningsN(H5Reader hr, string d2FlowNameGroup)
	{
		float computed = 0.06f;
		if (hr.GetAttributeNames(d2FlowNameGroup)?.Contains("Manning's n") ?? false)
		{
			hr.ReadAttributeScalar(d2FlowNameGroup, "Manning's n", ref computed);
		}
		return computed;
	}

	public void ComputeFaceAreaAndTopWidth(float ws, float faceLength, Point2DCollection areaTable, ref float area, ref float topWidth)
	{
		checked
		{
			int count = areaTable.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				if (areaTable[i].Y >= ws)
				{
					topWidth = areaTable.InverseSlope(i);
					area = areaTable[i - 1].X + topWidth * (ws - areaTable[i - 1].Y);
					return;
				}
			}
			topWidth = faceLength;
			area = areaTable.Last().X + topWidth * (ws - areaTable.Last().Y);
		}
	}

	public float ComputeFacePerimeter(float ws, Point2DCollection perimeterTable)
	{
		checked
		{
			int count = perimeterTable.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				if (perimeterTable[i].Y >= ws)
				{
					float submergenceRatio = (ws - perimeterTable[i - 1].Y) / (perimeterTable[i].Y - perimeterTable[i - 1].Y);
					return perimeterTable[i - 1].X * submergenceRatio + perimeterTable[i].X * (1f - submergenceRatio);
				}
			}
			return perimeterTable.Last().X;
		}
	}

	public float[] ComputeInitialElevations(RASGeometry geometry, List<PointCellElev> FixedPoints)
	{
		checked
		{
			_ = new bool[_cells.Count() - 1 + 1];
			float[] initialElevations = new float[_cells.Count() - 1 + 1];
			Buffers.ArraySetAllVB(initialElevations, float.MaxValue);
			PointMs pointMs = new PointMs();
			foreach (PointCellElev FixedPoint in FixedPoints)
			{
				pointMs.Add(FixedPoint.Point.X, FixedPoint.Point.Y, FixedPoint.Elevation);
				initialElevations[FixedPoint.CellIndex] = FixedPoint.Elevation;
			}
			float[] floatValues = new float[Faces.Count - 1 + 1];
			int count = Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				floatValues[i] = Faces[i].MinimumElevation;
			}
			SA2DStructureLayer sA2DStructures = geometry.SA2DStructures;
			foreach (int filteredFIDSIndex in sA2DStructures.FilteredFIDS())
			{
				if (!sA2DStructures.GetIsInternal(filteredFIDSIndex) || sA2DStructures.GetIsBridge(filteredFIDSIndex))
				{
					continue;
				}
				Polyline pline = sA2DStructures.Polyline(filteredFIDSIndex);
				List<Tuple<int, bool>> facesAlongPolyline = FacesAlongPolyline(pline);
				foreach (Tuple<int, bool> facesAlongPolyline2 in facesAlongPolyline)
				{
					floatValues[facesAlongPolyline2.Item1] = float.MaxValue;
				}
			}
			double nearPointTolerance = 1E-06;
			DevelopmentTin developmentTin = new DelaunayTINFactory(nearPointTolerance).ComputeTIN(null, null, pointMs, null, removeDeletedTriangles: false);
			int count2 = developmentTin.Triangles.Count - 1;
			double t = default;
			for (int j = 0; j <= count2; j++)
			{
				int ordinal = 0;
				do
				{
					int adjacentPointIdx = developmentTin.Triangles[j].AdjacentPointIdx(ordinal);
					int trianglePointAfterPoint = developmentTin.TrianglePointAfterPoint(j, adjacentPointIdx);
					if (adjacentPointIdx < trianglePointAfterPoint)
					{
						SegmentM segment = developmentTin.Edge(j, ordinal);
						if (segment.HasZ())
						{
							PointMs pointMs2 = new PointMs(segment);
							Polyline polyline = new Polyline(pointMs2);
							List<ProfileCellRange> cellsUnderPolyline = CellsUnderPolyline(polyline);
							if (cellsUnderPolyline.Count > 2)
							{
								bool condition = true;
								float[] floatValuesArray = new float[cellsUnderPolyline.Count - 2 + 1];
								int count3 = cellsUnderPolyline.Count - 2;
								for (int k = 0; k <= count3; k++)
								{
									int cell = cellsUnderPolyline[k].Cell;
									int cell2 = cellsUnderPolyline[k + 1].Cell;
									int faceBetweenCells = GetFaceBetweenCells(cell, cell2);
									if (faceBetweenCells == -1)
									{
										if (GetCommonFacePoint(cell, cell2) == -1)
										{
											condition = false;
											break;
										}
										continue;
									}
									segment.NearestPoint(_cells[cell].Point.ToPointM(), ref t);
									floatValuesArray[k] = (float)segment.ZAtFraction(t);
									if (floatValuesArray[k] <= Faces[faceBetweenCells].MinimumElevation)
									{
										condition = false;
										break;
									}
								}
								if (condition)
								{
									int count4 = cellsUnderPolyline.Count - 2;
									for (int l = 1; l <= count4; l++)
									{
										int cell3 = cellsUnderPolyline[l].Cell;
										if (initialElevations[cell3] == float.MaxValue)
										{
											initialElevations[cell3] = floatValuesArray[l];
										}
									}
								}
							}
						}
					}
					ordinal++;
				}
				while (ordinal <= 2);
			}
			int m = default;
			for (bool[] setCell = new bool[_cells.Count() - 1 + 1]; !((ComputeInitialElevations_WaveFill(initialElevations, floatValues, setCell) == 0) | (m == _cells.Count())); m++)
			{
			}
			int count5 = _cells.Count() - 1;
			for (int n = 0; n <= count5; n++)
			{
				if (initialElevations[n] == float.MaxValue)
				{
					initialElevations[n] = float.NaN;
				}
			}
			return initialElevations;
		}
	}

	private int ComputeInitialElevations_WaveFill(float[] Elev, float[] FaceWithStructuresMinimum, bool[] setCell)
	{
		Buffers.ArraySetAllVB(setCell, v: false);
		checked
		{
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Elev[i] != float.MaxValue)
				{
					continue;
				}
				foreach (int face in _cells[i].Faces)
				{
					int otherCell = Faces[face].OtherCell(i);
					if (Elev[otherCell] < float.MaxValue && Elev[otherCell] > FaceWithStructuresMinimum[face])
					{
						setCell[i] = true;
					}
				}
			}
			int initialElevations_WaveFill = 0;
			int count2 = _cells.Count() - 1;
			for (int j = 0; j <= count2; j++)
			{
				if (!setCell[j])
				{
					continue;
				}
				int ordinal = 0;
				float elevSum = 0f;
				foreach (int face2 in _cells[j].Faces)
				{
					int otherCell2 = Faces[face2].OtherCell(j);
					if (Elev[otherCell2] < float.MaxValue && Elev[otherCell2] > FaceWithStructuresMinimum[face2])
					{
						ordinal++;
						elevSum += Elev[otherCell2];
					}
				}
				Elev[j] = elevSum / (float)ordinal;
				initialElevations_WaveFill++;
			}
			return initialElevations_WaveFill;
		}
	}

	public void EnsureFaceInvLengths()
	{
		checked
		{
			if (FaceInvLengths == null)
			{
				FaceInvLengths = new float[Faces.Count - 1 + 1];
				int count = Faces.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					FaceInvLengths[i] = 1f / (float)FacePolyline(i).Length;
				}
			}
		}
	}

	private FaceVelocityCoef GetCellVelocityCoef(int cidx)
	{
		FaceVelocityCoef result = default;
		foreach (int face in _cells[cidx].Faces)
		{
			result.AddFaceNormal(FaceNormal(face));
		}
		result.Complete();
		return result;
	}

	public void EnsureVelocityComputesReady()
	{
		EnsureFaceVelocityParameters();
	}

	public Vector2D ComputeCellValue_FacePerpLeastSquares(int c, float[] perpendicularFaceVelocities)
	{
		FaceVelocityCoef cellVelocityCoef = GetCellVelocityCoef(c);
		float computed = 0f;
		float computed2 = 0f;
		foreach (int face in _cells[c].Faces)
		{
			Vector2D vector2D = FaceNormal(face);
			float perpendicularFaceVelocity = perpendicularFaceVelocities[face];
			computed += vector2D.X * perpendicularFaceVelocity;
			computed2 += vector2D.Y * perpendicularFaceVelocity;
		}
		float x = cellVelocityCoef.A22 * computed - cellVelocityCoef.A12 * computed2;
		float y = (0f - cellVelocityCoef.A12) * computed + cellVelocityCoef.A11 * computed2;
		return new Vector2D(x, y);
	}

	public float ComputeCellValue_FacePlanarZ(int c, float[] faceValues, Func<int, Point2Double> FaceLocations)
	{
		Cell cell = _cells[c];
		PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(cell.Point);
		checked
		{
			int count = cell.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int faceIndex = cell.Faces[i];
				float faceValue = faceValues[faceIndex];
				planarRegressionZ.Add(FaceLocations(faceIndex), faceValue);
			}
			return (float)planarRegressionZ.SolveZ();
		}
	}

	public Vector2D ComputeCellValue_FacePerpLeastSquares_Local(int c, float[] localFaceVelocities)
	{
		FaceVelocityCoef cellVelocityCoef = GetCellVelocityCoef(c);
		float computed = 0f;
		float computed2 = 0f;
		Cell cell = _cells[c];
		checked
		{
			int count = cell.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int f = cell.Faces[i];
				Vector2D vector2D = FaceNormal(f);
				float localFaceVelocity = localFaceVelocities[c];
				computed += vector2D.X * localFaceVelocity;
				computed2 += vector2D.Y * localFaceVelocity;
			}
			float x = cellVelocityCoef.A22 * computed - cellVelocityCoef.A12 * computed2;
			float y = (0f - cellVelocityCoef.A12) * computed + cellVelocityCoef.A11 * computed2;
			return new Vector2D(x, y);
		}
	}

	public Vector2D[] ComputeCellVelocityTimeSeries(int cIdx, Dictionary<int, float[]> faceVelocities)
	{
		int elementAtFaceVelocitiesTerm = faceVelocities.ElementAt(0).Value.Length;
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[elementAtFaceVelocitiesTerm - 1 + 1];
			FaceVelocityCoef cellVelocityCoef = GetCellVelocityCoef(cIdx);
			int count = elementAtFaceVelocitiesTerm - 1;
			Vector2D vector2D2 = default;
			for (int i = 0; i <= count; i++)
			{
				float computed = 0f;
				float computed2 = 0f;
				foreach (int face in _cells[cIdx].Faces)
				{
					Vector2D vector2D = FaceNormal(face);
					float faceVelocity = faceVelocities[face][i];
					computed += vector2D.X * faceVelocity;
					computed2 += vector2D.Y * faceVelocity;
				}
				vector2D2.X = cellVelocityCoef.A22 * computed - cellVelocityCoef.A12 * computed2;
				vector2D2.Y = (0f - cellVelocityCoef.A12) * computed + cellVelocityCoef.A11 * computed2;
				vector2Ds[i] = vector2D2;
			}
			return vector2Ds;
		}
	}

	public float ComputeCellValueFromFaces(int c, float[] faceValues)
	{
		float cellValueFromFaces = -9999f;
		foreach (int face in _cells[c].Faces)
		{
			cellValueFromFaces = System.Math.Max(cellValueFromFaces, System.Math.Abs(faceValues[face]));
		}
		return cellValueFromFaces;
	}

	private void EnsureFaceVelocityParameters()
	{
		EnsureFaceInvLengths();
		EnsureFaceNormals();
	}

	private void ComputeFacePointVelocityCoefficients_Weighted()
	{
		_Closure_0024__283_002D0 capturedLocals = default;
		_Closure_0024__283_002D0 capturedLocals2 = new _Closure_0024__283_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		checked
		{
			capturedLocals2.facePointVelocityCoef = new FaceVelocityCoef[FacePoints.Count - 1 + 1];
			Utility.Parallel.SmartFor(FacePoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					foreach (int face in capturedLocals2.meshFV2D.FacePoints[i].Faces)
					{
						capturedLocals2.facePointVelocityCoef[i].AddWeightedFaceNormal(capturedLocals2.meshFV2D.FaceNormal(face), capturedLocals2.meshFV2D.FaceInvLengths[face]);
					}
					capturedLocals2.facePointVelocityCoef[i].Complete();
				}
			});
		}
	}

	private FacePointVels ComputeFacePointVelocity_FacePerpLeastSquares_Weighted(int fp, float[] perpendicularFaceVelocities, FaceValues[] faceWSs)
	{
		FacePoint facePoint = FacePoints[fp];
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[facePoint.Faces.Count - 1 + 1];
			bool condition = true;
			int count = facePoint.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int faceIndex = facePoint.Faces[i];
				if (faceWSs[faceIndex].ValueA != -9999f)
				{
					condition = false;
					break;
				}
				if (faceWSs[faceIndex].ValueB != -9999f)
				{
					condition = false;
					break;
				}
			}
			FacePointVels result;
			if (condition)
			{
				result = new FacePointVels(vector2Ds);
			}
			else
			{
				int count2 = facePoint.Faces.Count - 1;
				int startLocalFaceIdx = default;
				int endLocalFaceIdx = default;
				Vector2D vector2D = default;
				for (int j = 0; j <= count2; j++)
				{
					FaceVelocityCoef faceVelocityCoef = default;
					float computed = 0f;
					float computed2 = 0f;
					GetHydraulicallyConnectedArc(fp, j, faceWSs, ref startLocalFaceIdx, ref endLocalFaceIdx);
					int startLocalFaceIdx2 = startLocalFaceIdx;
					do
					{
						int faceIndex2 = facePoint.Faces[startLocalFaceIdx2];
						Vector2D norm = FaceNormal(faceIndex2);
						float perpendicularFaceVelocity = perpendicularFaceVelocities[faceIndex2];
						computed += norm.X * perpendicularFaceVelocity * FaceInvLengths[faceIndex2];
						computed2 += norm.Y * perpendicularFaceVelocity * FaceInvLengths[faceIndex2];
						faceVelocityCoef.AddWeightedFaceNormal(norm, FaceInvLengths[faceIndex2]);
						startLocalFaceIdx2 = facePoint.CCWFaceByLocalIndex(startLocalFaceIdx2);
					}
					while (startLocalFaceIdx2 != endLocalFaceIdx);
					int faceIndex3 = facePoint.Faces[endLocalFaceIdx];
					if (!faceWSs[faceIndex3].IsHydraulicallyConnected)
					{
						Vector2D norm2 = FaceNormal(faceIndex3);
						float perpendicularFaceVelocity2 = perpendicularFaceVelocities[faceIndex3];
						computed += norm2.X * perpendicularFaceVelocity2 * FaceInvLengths[faceIndex3];
						computed2 += norm2.Y * perpendicularFaceVelocity2 * FaceInvLengths[faceIndex3];
						faceVelocityCoef.AddWeightedFaceNormal(norm2, FaceInvLengths[faceIndex3]);
					}
					faceVelocityCoef.Complete();
					vector2D.X = faceVelocityCoef.A22 * computed - faceVelocityCoef.A12 * computed2;
					vector2D.Y = (0f - faceVelocityCoef.A12) * computed + faceVelocityCoef.A11 * computed2;
					vector2Ds[j] = vector2D;
				}
				result = new FacePointVels(vector2Ds);
			}
			return result;
		}
	}

	public FacePointVels ComputeFacePointVelocity_FaceTrueVelAvg_Weighted(int fp, Tuple<Vector2D, Vector2D>[] faceTrueVelocities, FaceValues[] faceWSs)
	{
		FacePoint facePoint = FacePoints[fp];
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[facePoint.Faces.Count - 1 + 1];
			bool condition = true;
			int count = facePoint.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int faceIndex = facePoint.Faces[i];
				if (faceWSs[faceIndex].ValueA != -9999f)
				{
					condition = false;
					break;
				}
				if (faceWSs[faceIndex].ValueB != -9999f)
				{
					condition = false;
					break;
				}
			}
			FacePointVels result;
			if (condition)
			{
				result = new FacePointVels(vector2Ds);
			}
			else
			{
				int count2 = facePoint.Faces.Count - 1;
				int startLocalFaceIdx = default;
				int endLocalFaceIdx = default;
				Vector2D vector2D = default;
				for (int j = 0; j <= count2; j++)
				{
					GetHydraulicallyConnectedArc(fp, j, faceWSs, ref startLocalFaceIdx, ref endLocalFaceIdx);
					int startLocalFaceIdx2 = startLocalFaceIdx;
					float computed = 0f;
					float computed2 = 0f;
					float faceInvLengthSum = 0f;
					do
					{
						int faceIndex2 = facePoint.Faces[startLocalFaceIdx2];
						Tuple<Vector2D, Vector2D> tuple = faceTrueVelocities[faceIndex2];
						float faceInvLength = FaceInvLengths[faceIndex2];
						if (startLocalFaceIdx2 == startLocalFaceIdx)
						{
							if (Faces[facePoint.Faces[startLocalFaceIdx2]].fpA == fp)
							{
								computed += tuple.Item1.X * faceInvLength;
								computed2 += tuple.Item1.Y * faceInvLength;
							}
							else
							{
								computed += tuple.Item2.X * faceInvLength;
								computed2 += tuple.Item2.Y * faceInvLength;
							}
						}
						else
						{
							computed += tuple.Item1.X * faceInvLength;
							computed2 += tuple.Item1.Y * faceInvLength;
						}
						faceInvLengthSum += faceInvLength;
						startLocalFaceIdx2 = facePoint.CCWFaceByLocalIndex(startLocalFaceIdx2);
					}
					while (startLocalFaceIdx2 != endLocalFaceIdx);
					int faceIndex3 = facePoint.Faces[endLocalFaceIdx];
					if (!faceWSs[faceIndex3].IsHydraulicallyConnected)
					{
						Tuple<Vector2D, Vector2D> tuple2 = faceTrueVelocities[faceIndex3];
						float faceInvLength2 = FaceInvLengths[faceIndex3];
						if (Faces[faceIndex3].fpA == fp)
						{
							computed += tuple2.Item2.X * faceInvLength2;
							computed2 += tuple2.Item2.Y * faceInvLength2;
						}
						else
						{
							computed += tuple2.Item1.X * faceInvLength2;
							computed2 += tuple2.Item1.Y * faceInvLength2;
						}
						faceInvLengthSum += faceInvLength2;
					}
					vector2D.X = computed / faceInvLengthSum;
					vector2D.Y = computed2 / faceInvLengthSum;
					vector2Ds[j] = vector2D;
				}
				result = new FacePointVels(vector2Ds);
			}
			return result;
		}
	}

	public Vector2D ComputeFacePointVelocity_FacePerpLeastSquares_Weighted_Local(int fpIdx, float[] localFaceVelocities)
	{
		FacePoint facePoint = FacePoints[fpIdx];
		float computed = 0f;
		float computed2 = 0f;
		checked
		{
			int count = facePoint.Faces.Count - 1;
			FaceVelocityCoef faceVelocityCoef = default;
			for (int i = 0; i <= count; i++)
			{
				int faceIndex = facePoint.Faces[i];
				Vector2D norm = FaceNormal(faceIndex);
				float localFaceVelocity = localFaceVelocities[i];
				computed += norm.X * localFaceVelocity * FaceInvLengths[faceIndex];
				computed2 += norm.Y * localFaceVelocity * FaceInvLengths[faceIndex];
				faceVelocityCoef.AddWeightedFaceNormal(norm, FaceInvLengths[faceIndex]);
			}
			faceVelocityCoef.Complete();
			return faceVelocityCoef.Compute(computed, computed2);
		}
	}

	public float[] ComputeFacePointDV_FacePerpLeastSquares_Weighted(int fp, float[] faceDVs, FaceValues[] faceWSs, Func<int, Point2Double> getFaceApplicationPoint)
	{
		FacePoint facePoint = FacePoints[fp];
		checked
		{
			float[] facePointDV_FacePerpLeastSquares_Weighted = new float[facePoint.Faces.Count - 1 + 1];
			bool condition = true;
			int count = facePoint.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int faceIndex = facePoint.Faces[i];
				if (faceWSs[faceIndex].ValueA != -9999f)
				{
					condition = false;
				}
				if (faceWSs[faceIndex].ValueB != -9999f)
				{
					condition = false;
				}
			}
			if (condition)
			{
				return facePointDV_FacePerpLeastSquares_Weighted;
			}
			PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(facePoint.Point);
			int count2 = facePoint.Faces.Count - 1;
			int startLocalFaceIdx = default;
			int endLocalFaceIdx = default;
			for (int j = 0; j <= count2; j++)
			{
				GetHydraulicallyConnectedArc(fp, j, faceWSs, ref startLocalFaceIdx, ref endLocalFaceIdx);
				int startLocalFaceIdx2 = startLocalFaceIdx;
				do
				{
					int faceIndex2 = facePoint.Faces[startLocalFaceIdx2];
					float faceDV = faceDVs[faceIndex2];
					planarRegressionZ.Add(getFaceApplicationPoint(faceIndex2), faceDV);
					startLocalFaceIdx2 = facePoint.CCWFaceByLocalIndex(startLocalFaceIdx2);
				}
				while (startLocalFaceIdx2 != endLocalFaceIdx);
				int faceIndex3 = facePoint.Faces[endLocalFaceIdx];
				if (!faceWSs[faceIndex3].IsHydraulicallyConnected)
				{
					FaceNormal(faceIndex3);
					float faceDV2 = faceDVs[faceIndex3];
					planarRegressionZ.Add(getFaceApplicationPoint(faceIndex3), faceDV2);
				}
				facePointDV_FacePerpLeastSquares_Weighted[j] = (float)planarRegressionZ.SolveZ();
			}
			return facePointDV_FacePerpLeastSquares_Weighted;
		}
	}

	private void GetHydraulicallyConnectedArc(int fpIdx, int localFaceIdx, FaceValues[] faceWSs, ref int startLocalFaceIdx, ref int endLocalFaceIdx)
	{
		FacePoint facePoint = FacePoints[fpIdx];
		endLocalFaceIdx = localFaceIdx;
		do
		{
			endLocalFaceIdx = facePoint.CCWFaceByLocalIndex(endLocalFaceIdx);
		}
		while (faceWSs[facePoint.Faces[endLocalFaceIdx]].IsHydraulicallyConnected && endLocalFaceIdx != localFaceIdx);
		startLocalFaceIdx = localFaceIdx;
		if (startLocalFaceIdx != endLocalFaceIdx)
		{
			while (faceWSs[facePoint.Faces[startLocalFaceIdx]].IsHydraulicallyConnected)
			{
				startLocalFaceIdx = facePoint.CWFaceByLocalIndex(startLocalFaceIdx);
			}
		}
	}

	public FacePointVels[] ComputeSlopingFacepointVelocities_LeastSquares(float[] perpendicularFaceVelocities, FaceValues[] faceWSs)
	{
		FacePointVels[] fpVelocities = null;
		ComputeSlopingFacepointVelocities_LeastSquares(perpendicularFaceVelocities, faceWSs, ref fpVelocities);
		return fpVelocities;
	}

	public FacePointVels[] ComputeSlopingFacepointVelocities_LeastSquares(float[] perpendicularFaceVelocities, FaceValues[] faceWSs, List<int> uniqueFaceList)
	{
		FacePointVels[] fpVelocities = null;
		ComputeSlopingFacepointVelocities_LeastSquares(perpendicularFaceVelocities, faceWSs, ref fpVelocities, uniqueFaceList);
		return fpVelocities;
	}

	public FacePointVels[] ComputeSlopingFacepointVelocities_LeastSquares(float[] perpendicularFaceVelocities, FaceValues[] faceWSs, SlopingMeshMap slopedArea)
	{
		FacePointVels[] fpVelocities = null;
		List<int> uniqueFacePts = null;
		if (slopedArea != null && !slopedArea.UniqueFacePoints.IsNullOrEmpty())
		{
			uniqueFacePts = slopedArea.UniqueFacePoints;
		}
		ComputeSlopingFacepointVelocities_LeastSquares(perpendicularFaceVelocities, faceWSs, ref fpVelocities, uniqueFacePts);
		return fpVelocities;
	}

	private void ComputeSlopingFacepointVelocities_LeastSquares(float[] perpendicularFaceVelocities, FaceValues[] faceWSs, ref FacePointVels[] fpVelocities, List<int> uniqueFacePts = null)
	{
		_Closure_0024__292_002D0 capturedLocals = default;
		_Closure_0024__292_002D0 capturedLocals2 = new _Closure_0024__292_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.perpendicularFaceVelocities = perpendicularFaceVelocities;
		capturedLocals2.faceWSs = faceWSs;
		capturedLocals2.uniqueFacePts = uniqueFacePts;
		checked
		{
			if (fpVelocities == null || fpVelocities.Count() != FacePoints.Count)
			{
				fpVelocities = new FacePointVels[FacePoints.Count - 1 + 1];
			}
			capturedLocals2.lFPVels = fpVelocities;
			EnsureFaceVelocityParameters();
			if (!capturedLocals2.uniqueFacePts.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.uniqueFacePts.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFacePt = capturedLocals2.uniqueFacePts[i];
						capturedLocals2.lFPVels[uniqueFacePt] = capturedLocals2.meshFV2D.ComputeFacePointVelocity_FacePerpLeastSquares_Weighted(uniqueFacePt, capturedLocals2.perpendicularFaceVelocities, capturedLocals2.faceWSs);
					}
				});
				return;
			}
			Utility.Parallel.SmartFor(FacePoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.lFPVels[i] = capturedLocals2.meshFV2D.ComputeFacePointVelocity_FacePerpLeastSquares_Weighted(i, capturedLocals2.perpendicularFaceVelocities, capturedLocals2.faceWSs);
				}
			});
		}
	}

	public FacePointVels[] ComputeSlopingFacepointVelocities_AvgTrueVels(Tuple<Vector2D, Vector2D>[] faceTrueVels, FaceValues[] faceWSs, SlopingMeshMap slopedArea = null)
	{
		FacePointVels[] fpVelocities = null;
		List<int> uniqueFacePts = null;
		if (slopedArea != null && !slopedArea.UniqueFacePoints.IsNullOrEmpty())
		{
			uniqueFacePts = slopedArea.UniqueFacePoints;
		}
		ComputeSlopingFacepointVelocities_AvgTrueVels(faceTrueVels, faceWSs, ref fpVelocities, uniqueFacePts);
		return fpVelocities;
	}

	private void ComputeSlopingFacepointVelocities_AvgTrueVels(Tuple<Vector2D, Vector2D>[] faceTrueVels, FaceValues[] faceWSs, ref FacePointVels[] fpVelocities, List<int> uniqueFacePts = null)
	{
		_Closure_0024__294_002D0 capturedLocals = default;
		_Closure_0024__294_002D0 capturedLocals2 = new _Closure_0024__294_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.faceTrueVels = faceTrueVels;
		capturedLocals2.faceWSs = faceWSs;
		capturedLocals2.uniqueFacePts = uniqueFacePts;
		checked
		{
			if (fpVelocities == null || fpVelocities.Count() != FacePoints.Count)
			{
				fpVelocities = new FacePointVels[FacePoints.Count - 1 + 1];
			}
			capturedLocals2.lFPVels = fpVelocities;
			EnsureFaceVelocityParameters();
			if (!capturedLocals2.uniqueFacePts.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.uniqueFacePts.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFacePt = capturedLocals2.uniqueFacePts[i];
						capturedLocals2.lFPVels[uniqueFacePt] = capturedLocals2.meshFV2D.ComputeFacePointVelocity_FaceTrueVelAvg_Weighted(uniqueFacePt, capturedLocals2.faceTrueVels, capturedLocals2.faceWSs);
					}
				});
				return;
			}
			Utility.Parallel.SmartFor(FacePoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.lFPVels[i] = capturedLocals2.meshFV2D.ComputeFacePointVelocity_FaceTrueVelAvg_Weighted(i, capturedLocals2.faceTrueVels, capturedLocals2.faceWSs);
				}
			});
		}
	}

	public Tuple<Vector2D, Vector2D>[] ComputeFaceTrueVelocities_CStencilAveraged(FaceValues[] faceWSValues, float[] facePerpVelocities, MeshMap mapped2DArea = null)
	{
		_Closure_0024__295_002D0 capturedLocals = default;
		_Closure_0024__295_002D0 capturedLocals2 = new _Closure_0024__295_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.faceWSValues = faceWSValues;
		capturedLocals2.facePerpVelocities = facePerpVelocities;
		capturedLocals2.mapped2DArea = mapped2DArea;
		checked
		{
			capturedLocals2.trueVels = new Tuple<Vector2D, Vector2D>[FaceCount - 1 + 1];
			EnsureFaceNormals();
			if (capturedLocals2.mapped2DArea != null && !capturedLocals2.mapped2DArea.UniqueFaces.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.mapped2DArea.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.mapped2DArea.UniqueFaces[i];
						capturedLocals2.trueVels[uniqueFaceIndex] = capturedLocals2.meshFV2D.ComputeFaceVelocity_CAveragedStencil(uniqueFaceIndex, capturedLocals2.facePerpVelocities, capturedLocals2.faceWSValues);
					}
				});
			}
			else
			{
				Utility.Parallel.SmartFor(FaceCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						capturedLocals2.trueVels[i] = capturedLocals2.meshFV2D.ComputeFaceVelocity_CAveragedStencil(i, capturedLocals2.facePerpVelocities, capturedLocals2.faceWSValues);
					}
				});
			}
			return capturedLocals2.trueVels;
		}
	}

	private void ComputeStencils(int fidx, ref FaceVelocityCoef stencilA, ref FaceVelocityCoef stencilB)
	{
		stencilA = default;
		stencilB = default;
		Face face = Faces[fidx];
		Cell cell = _cells[face.cellA];
		Cell cell2 = _cells[face.cellB];
		int f = cell.NextCWFaceByGlobalIndex(fidx);
		int f2 = cell.NextCCWFaceByGlobalIndex(fidx);
		int f3 = cell2.NextCWFaceByGlobalIndex(fidx);
		int f4 = cell2.NextCCWFaceByGlobalIndex(fidx);
		stencilA.AddFaceNormal(FaceNormal(f));
		stencilA.AddFaceNormal(FaceNormal(f2));
		stencilA.AddFaceNormal(FaceNormal(fidx));
		stencilA.Complete();
		stencilB.AddFaceNormal(FaceNormal(f3));
		stencilB.AddFaceNormal(FaceNormal(f4));
		stencilB.AddFaceNormal(FaceNormal(fidx));
		stencilB.Complete();
	}

	public Tuple<Vector2D, Vector2D> ComputeFaceVelocity_CAveragedStencil(int fIdx, float[] perpendicularFaceVelocities, FaceValues[] faceWSs)
	{
		Face face = Faces[fIdx];
		Vector2D faceNormal = FaceNormal(fIdx);
		float faceVelocity = perpendicularFaceVelocities[fIdx];
		FaceVelocityCoef stencilA = default;
		FaceVelocityCoef stencilB = default;
		ComputeStencils(fIdx, ref stencilA, ref stencilB);
		Cell cell = _cells[face.cellA];
		int nextCWFaceByGlobalIndex = cell.NextCWFaceByGlobalIndex(fIdx);
		int nextCCWFaceByGlobalIndex = cell.NextCCWFaceByGlobalIndex(fIdx);
		Vector2D cwFaceNormal = FaceNormal(nextCWFaceByGlobalIndex);
		Vector2D ccwFaceNormal = FaceNormal(nextCCWFaceByGlobalIndex);
		float cwFaceVelocity = perpendicularFaceVelocities[nextCWFaceByGlobalIndex];
		float ccwFaceVelocity = perpendicularFaceVelocities[nextCCWFaceByGlobalIndex];
		bool isHydraulicallyConnected = faceWSs[nextCWFaceByGlobalIndex].IsHydraulicallyConnected;
		bool isHydraulicallyConnected2 = faceWSs[nextCCWFaceByGlobalIndex].IsHydraulicallyConnected;
		Vector2D solveFaceVectorC = SolveFaceVectorC(stencilA, faceNormal, faceVelocity, cwFaceNormal, cwFaceVelocity, isHydraulicallyConnected, ccwFaceNormal, ccwFaceVelocity, isHydraulicallyConnected2);
		Cell cell2 = _cells[face.cellB];
		int nextCWFaceByGlobalIndex2 = cell2.NextCWFaceByGlobalIndex(fIdx);
		int nextCCWFaceByGlobalIndex2 = cell2.NextCCWFaceByGlobalIndex(fIdx);
		Vector2D cwFaceNormal2 = FaceNormal(nextCWFaceByGlobalIndex2);
		Vector2D ccwFaceNormal2 = FaceNormal(nextCCWFaceByGlobalIndex2);
		float cwFaceVelocity2 = perpendicularFaceVelocities[nextCWFaceByGlobalIndex2];
		float ccwFaceVelocity2 = perpendicularFaceVelocities[nextCCWFaceByGlobalIndex2];
		bool isHydraulicallyConnected3 = faceWSs[nextCWFaceByGlobalIndex2].IsHydraulicallyConnected;
		bool isHydraulicallyConnected4 = faceWSs[nextCCWFaceByGlobalIndex2].IsHydraulicallyConnected;
		Vector2D solveFaceVectorC2 = SolveFaceVectorC(stencilB, faceNormal, faceVelocity, cwFaceNormal2, cwFaceVelocity2, isHydraulicallyConnected3, ccwFaceNormal2, ccwFaceVelocity2, isHydraulicallyConnected4);
		if (faceWSs[fIdx].IsHydraulicallyConnected)
		{
			Vector2D vector2D = new Vector2D((solveFaceVectorC.X + solveFaceVectorC2.X) / 2f, (solveFaceVectorC.Y + solveFaceVectorC2.Y) / 2f);
			return new Tuple<Vector2D, Vector2D>(vector2D, vector2D);
		}
		return new Tuple<Vector2D, Vector2D>(solveFaceVectorC, solveFaceVectorC2);
	}

	private Vector2D SolveFaceVectorC(FaceVelocityCoef coefMatrix, Vector2D faceNormal, float faceVelocity, Vector2D cwFaceNormal, float cwFaceVelocity, bool cwFaceConnected, Vector2D ccwFaceNormal, float ccwFaceVelocity, bool ccwFaceConnected)
	{
		Vector2D vector2D = new Vector2D(faceNormal.Y, 0f - faceNormal.X);
		float dotProduct = 0f;
		if (cwFaceConnected || ccwFaceConnected)
		{
			float cwFaceNormalCwFaceVelocityTerm = cwFaceNormal.X * cwFaceVelocity;
			float cwFaceNormalCwFaceVelocityTerm2 = cwFaceNormal.Y * cwFaceVelocity;
			cwFaceNormalCwFaceVelocityTerm += ccwFaceNormal.X * ccwFaceVelocity;
			cwFaceNormalCwFaceVelocityTerm2 += ccwFaceNormal.Y * ccwFaceVelocity;
			cwFaceNormalCwFaceVelocityTerm += faceNormal.X * faceVelocity;
			cwFaceNormalCwFaceVelocityTerm2 += faceNormal.Y * faceVelocity;
			Vector2D vector2D2 = default;
			vector2D2.X = coefMatrix.A22 * cwFaceNormalCwFaceVelocityTerm - coefMatrix.A12 * cwFaceNormalCwFaceVelocityTerm2;
			vector2D2.Y = (0f - coefMatrix.A12) * cwFaceNormalCwFaceVelocityTerm + coefMatrix.A11 * cwFaceNormalCwFaceVelocityTerm2;
			dotProduct = vector2D2.X * vector2D.X + vector2D2.Y * vector2D.Y;
		}
		Vector2D vector2D3 = new Vector2D(dotProduct * vector2D.X, dotProduct * vector2D.Y);
		Vector2D vector2D4 = new Vector2D(faceVelocity * faceNormal.X, faceVelocity * faceNormal.Y);
		return new Vector2D(vector2D4.X + vector2D3.X, vector2D4.Y + vector2D3.Y);
	}

	public Vector2D[] ComputeFaceTrueVelocities(FaceValues[] faceWSValues, FacePointVels[] fpVels, SlopingMeshMap slopedArea = null)
	{
		_Closure_0024__302_002D0 capturedLocals = default;
		_Closure_0024__302_002D0 capturedLocals2 = new _Closure_0024__302_002D0(capturedLocals);
		capturedLocals2.meshFV2D = this;
		capturedLocals2.faceWSValues = faceWSValues;
		capturedLocals2.fpVels = fpVels;
		capturedLocals2.slopedArea = slopedArea;
		checked
		{
			capturedLocals2.trueVels = new Vector2D[FaceCount - 1 + 1];
			if (capturedLocals2.slopedArea != null && !capturedLocals2.slopedArea.UniqueFaces.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.slopedArea.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.slopedArea.UniqueFaces[i];
						FaceValues faceValues = capturedLocals2.faceWSValues[uniqueFaceIndex];
						if (faceValues.IsHydraulicallyConnected)
						{
							Face face = capturedLocals2.meshFV2D.Faces[uniqueFaceIndex];
							_ = ref capturedLocals2.meshFV2D._cells[face.cellA];
							FacePoint facePoint = capturedLocals2.meshFV2D.FacePoints[face.fpA];
							FacePoint facePoint2 = capturedLocals2.meshFV2D.FacePoints[face.fpB];
							Vector2D vector2D = capturedLocals2.fpVels[face.fpA].Velocity[facePoint.Faces.IndexOf(uniqueFaceIndex)];
							Vector2D vector2D2 = capturedLocals2.fpVels[face.fpB].Velocity[facePoint2.Faces.IndexOf(uniqueFaceIndex)];
							Vector2D vector2D3 = new Vector2D((vector2D.X + vector2D2.X) / 2f, (vector2D.Y + vector2D2.Y) / 2f);
							capturedLocals2.trueVels[uniqueFaceIndex] = vector2D3;
						}
					}
				});
			}
			else
			{
				Utility.Parallel.SmartFor(FaceCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						FaceValues faceValues = capturedLocals2.faceWSValues[i];
						if (faceValues.IsHydraulicallyConnected)
						{
							Face face = capturedLocals2.meshFV2D.Faces[i];
							_ = ref capturedLocals2.meshFV2D._cells[face.cellA];
							FacePoint facePoint = capturedLocals2.meshFV2D.FacePoints[face.fpA];
							FacePoint facePoint2 = capturedLocals2.meshFV2D.FacePoints[face.fpB];
							Vector2D vector2D = capturedLocals2.fpVels[face.fpA].Velocity[facePoint.Faces.IndexOf(i)];
							Vector2D vector2D2 = capturedLocals2.fpVels[face.fpB].Velocity[facePoint2.Faces.IndexOf(i)];
							Vector2D vector2D3 = new Vector2D((vector2D.X + vector2D2.X) / 2f, (vector2D.Y + vector2D2.Y) / 2f);
							capturedLocals2.trueVels[i] = vector2D3;
						}
					}
				});
			}
			return capturedLocals2.trueVels;
		}
	}

	public Vector2D[] ComputeFaceTrueShear(Vector2D[] faceTrueVels, float[] faceMann, float[] faceHydrRad, SlopingMeshMap slopedArea = null)
	{
		_Closure_0024__303_002D0 capturedLocals = default;
		_Closure_0024__303_002D0 capturedLocals2 = new _Closure_0024__303_002D0(capturedLocals);
		capturedLocals2.faceTrueVels = faceTrueVels;
		capturedLocals2.faceMann = faceMann;
		capturedLocals2.faceHydrRad = faceHydrRad;
		capturedLocals2.slopedArea = slopedArea;
		checked
		{
			capturedLocals2.shears = new Vector2D[FaceCount - 1 + 1];
			capturedLocals2.factor = 0f;
			if (SharedData.Units == SharedData.EnumUnits.SIUnits)
			{
				capturedLocals2.factor = 9810f;
			}
			else
			{
				if (SharedData.Units != SharedData.EnumUnits.USCustomary)
				{
					throw new NotImplementedException();
				}
				capturedLocals2.factor = 28.258362f;
			}
			if (capturedLocals2.slopedArea != null && !capturedLocals2.slopedArea.UniqueFaces.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.slopedArea.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.slopedArea.UniqueFaces[i];
						float x = capturedLocals2.faceTrueVels[uniqueFaceIndex].X;
						float y = capturedLocals2.faceTrueVels[uniqueFaceIndex].Y;
						float magnitude = capturedLocals2.faceTrueVels[uniqueFaceIndex].Magnitude;
						float faceMann2 = capturedLocals2.faceMann[uniqueFaceIndex];
						float faceHydrRad2 = capturedLocals2.faceHydrRad[uniqueFaceIndex];
						if ((x == 0f && y == 0f) || faceHydrRad2 == 0f)
						{
							capturedLocals2.shears[uniqueFaceIndex] = default;
						}
						else
						{
							float x2 = capturedLocals2.factor * magnitude * x * faceMann2 * faceMann2 / (float)System.Math.Pow(faceHydrRad2, 0.33333333333333337);
							float y2 = capturedLocals2.factor * magnitude * y * faceMann2 * faceMann2 / (float)System.Math.Pow(faceHydrRad2, 0.33333333333333337);
							capturedLocals2.shears[uniqueFaceIndex] = new Vector2D(x2, y2);
						}
					}
				});
			}
			else
			{
				Utility.Parallel.SmartFor(FaceCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						float x = capturedLocals2.faceTrueVels[i].X;
						float y = capturedLocals2.faceTrueVels[i].Y;
						float magnitude = capturedLocals2.faceTrueVels[i].Magnitude;
						float faceMann3 = capturedLocals2.faceMann[i];
						float faceHydrRad3 = capturedLocals2.faceHydrRad[i];
						if ((x == 0f && y == 0f) || faceHydrRad3 == 0f)
						{
							capturedLocals2.shears[i] = default;
						}
						else
						{
							float x2 = capturedLocals2.factor * magnitude * x * faceMann3 * faceMann3 / (float)System.Math.Pow(faceHydrRad3, 0.33333333333333337);
							float y2 = capturedLocals2.factor * magnitude * y * faceMann3 * faceMann3 / (float)System.Math.Pow(faceHydrRad3, 0.33333333333333337);
							capturedLocals2.shears[i] = new Vector2D(x2, y2);
						}
					}
				});
			}
			return capturedLocals2.shears;
		}
	}

	public float[] ComputeAllFacepointHydrDepths(float[] faceHydrDepths, Func<int, Point2Double> GetFaceLoc)
	{
		checked
		{
			float[] allFacepointHydrDepths = new float[FacePointCount - 1 + 1];
			int count = FacePointCount - 1;
			for (int i = 0; i <= count; i++)
			{
				FacePoint facePoint = FacePoints[i];
				PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(facePoint.Point);
				int count2 = facePoint.Faces.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int faceIndex = facePoint.Faces[j];
					float faceHydrDepth = faceHydrDepths[faceIndex];
					if (faceHydrDepth != 0f)
					{
						planarRegressionZ.Add(GetFaceLoc(faceIndex), faceHydrDepth);
					}
				}
				if (planarRegressionZ.Count() == 0)
				{
					allFacepointHydrDepths[i] = 0f;
				}
				else
				{
					allFacepointHydrDepths[i] = (float)planarRegressionZ.SolveZ();
				}
			}
			return allFacepointHydrDepths;
		}
	}

	public FaceValues ComputeFaceWSsBENPrev(int f, float cellWSA, float cellWSB)
	{
		Face face = Faces[f];
		int cellA = face.cellA;
		int cellB = face.cellB;
		Cell cell = _cells[cellA];
		Cell cell2 = _cells[cellB];
		float minimumElevation3 = cell.MinimumElevation;
		float minimumElevation4 = cell2.MinimumElevation;
		float minimumElevation = face.MinimumElevation;
		bool cellIsVirtual = CellIsVirtual(cellA);
		bool cellIsVirtual2 = CellIsVirtual(cellB);
		if (cellIsVirtual)
		{
			minimumElevation3 = minimumElevation;
		}
		else if (cellIsVirtual2)
		{
			minimumElevation4 = minimumElevation;
		}
		bool cellWSAMinimumElevation3Term = cellWSA <= minimumElevation3 + RASResults.MinWSPlotTolerance;
		bool cellWSBMinimumElevation4Term = cellWSB <= minimumElevation4 + RASResults.MinWSPlotTolerance;
		FaceValues result = default;
		if (cellWSAMinimumElevation3Term || cellWSBMinimumElevation4Term || FaceIsPerimeter(f))
		{
			if (cellWSAMinimumElevation3Term)
			{
				result.ValueA = -9999f;
			}
			else
			{
				result.ValueA = cellWSA;
			}
			if (cellWSBMinimumElevation4Term)
			{
				result.ValueB = -9999f;
			}
			else
			{
				result.ValueB = cellWSB;
			}
			result.HydraulicConnection = HydraulicConnection.None;
		}
		else if (cellWSA <= minimumElevation && cellWSB <= minimumElevation)
		{
			result.ValueA = cellWSA;
			result.ValueB = cellWSB;
			result.HydraulicConnection = HydraulicConnection.None;
		}
		else
		{
			int neighborCell = 0;
			float cellWSATerm = 0f;
			if (cellWSA > cellWSB)
			{
				neighborCell = cellA;
				cellWSATerm = cellWSA;
			}
			else
			{
				neighborCell = cellB;
				cellWSATerm = cellWSB;
			}
			float minimumElevation4AboveMinimumElevation3 = minimumElevation4 - minimumElevation3;
			if ((cellWSB - cellWSA) * minimumElevation4AboveMinimumElevation3 <= 0f)
			{
				result.ValueA = cellWSATerm;
				result.ValueB = cellWSATerm;
				result.HydraulicConnection = HydraulicConnection.Backfill;
			}
			else
			{
				minimumElevation4AboveMinimumElevation3 = System.Math.Abs(minimumElevation4AboveMinimumElevation3);
				float minimumElevation2 = _cells[neighborCell].MinimumElevation;
				float minimumElevation2Term = cellWSATerm - minimumElevation2;
				if (minimumElevation2Term >= 2f * minimumElevation4AboveMinimumElevation3)
				{
					result.ValueA = cellWSATerm;
					result.ValueB = cellWSATerm;
					result.HydraulicConnection = HydraulicConnection.DownhillDeep;
				}
				else
				{
					float systemCellWSATerm = System.Math.Min(cellWSA, cellWSB);
					float systemMinimumElevationTerm = System.Math.Max(minimumElevation, systemCellWSATerm);
					float minimumElevation2Term2 = systemMinimumElevationTerm - minimumElevation2;
					float computed = systemMinimumElevationTerm + (minimumElevation2Term * minimumElevation2Term - minimumElevation2Term2 * minimumElevation2Term2) / (2f * minimumElevation4AboveMinimumElevation3);
					if (minimumElevation2Term > minimumElevation4AboveMinimumElevation3)
					{
						computed = ((2f * minimumElevation4AboveMinimumElevation3 - minimumElevation2Term) * computed + (minimumElevation2Term - minimumElevation4AboveMinimumElevation3) * cellWSATerm) / minimumElevation4AboveMinimumElevation3;
						result.HydraulicConnection = HydraulicConnection.DownhillIntermediate;
					}
					else
					{
						result.HydraulicConnection = HydraulicConnection.DownhillShallow;
					}
					result.ValueA = computed;
					result.ValueB = computed;
				}
			}
		}
		return result;
	}

	public FaceValues ComputeFaceWSsNew(int f, float cellWSA, float cellWSB)
	{
		Face face = Faces[f];
		int cellA = face.cellA;
		int cellB = face.cellB;
		Cell cell = _cells[cellA];
		Cell cell2 = _cells[cellB];
		float minimumElevation = cell.MinimumElevation;
		float minimumElevation2 = cell2.MinimumElevation;
		float minimumElevation3 = face.MinimumElevation;
		bool cellWSAMinimumElevationTerm = cellWSA <= minimumElevation + RASResults.MinWSPlotTolerance;
		bool cellWSBMinimumElevation2Term = cellWSB <= minimumElevation2 + RASResults.MinWSPlotTolerance;
		FaceValues result = default;
		if (cellWSAMinimumElevationTerm || cellWSBMinimumElevation2Term || FaceIsPerimeter(f))
		{
			if (cellWSAMinimumElevationTerm || CellIsVirtual(cellA))
			{
				result.ValueA = -9999f;
			}
			else
			{
				result.ValueA = cellWSA;
			}
			if (cellWSBMinimumElevation2Term || CellIsVirtual(cellB))
			{
				result.ValueB = -9999f;
			}
			else
			{
				result.ValueB = cellWSB;
			}
			result.HydraulicConnection = HydraulicConnection.None;
		}
		else if (cellWSA <= minimumElevation3 && cellWSB <= minimumElevation3)
		{
			result.ValueA = cellWSA;
			result.ValueB = cellWSB;
			result.HydraulicConnection = HydraulicConnection.None;
		}
		else
		{
			int neighborCell;
			float cellWSATerm;
			float cellWSBTerm;
			float minimumElevation4;
			if (cellWSA > cellWSB)
			{
				neighborCell = cellA;
				cellWSATerm = cellWSA;
				cellWSBTerm = cellWSB;
				minimumElevation4 = _cells[neighborCell].MinimumElevation;
			}
			else
			{
				neighborCell = cellB;
				cellWSATerm = cellWSB;
				cellWSBTerm = cellWSA;
				minimumElevation4 = _cells[neighborCell].MinimumElevation;
			}
			float minimumElevation2AboveMinimumElevation = minimumElevation2 - minimumElevation;
			bool cellWSBCellWSATerm = (cellWSB - cellWSA) * minimumElevation2AboveMinimumElevation <= 0f;
			minimumElevation2AboveMinimumElevation = System.Math.Abs(minimumElevation2AboveMinimumElevation);
			float minimumElevation4Term = cellWSATerm - minimumElevation4;
			float systemMinimumElevation3Term = System.Math.Max(minimumElevation3, cellWSBTerm);
			float minimumElevation4Term2 = systemMinimumElevation3Term - minimumElevation4;
			float minimumElevation3Term = cellWSATerm - minimumElevation3;
			bool wasCritCapUsed = default;
			AvgWaterSurfaceWithCritCheck(cellWSA, cellWSB, cellWSATerm, minimumElevation3, ref wasCritCapUsed);
			float computed = cellWSATerm;
			bool wasCritCapUsed2 = wasCritCapUsed && minimumElevation4Term / minimumElevation3Term > 2f;
			if (wasCritCapUsed2 || cellWSBCellWSATerm)
			{
				if (wasCritCapUsed2)
				{
					if (neighborCell == cellA)
					{
						result.ValueA = computed;
						result.ValueB = cellWSB;
					}
					else
					{
						result.ValueA = cellWSA;
						result.ValueB = computed;
					}
					result.HydraulicConnection = HydraulicConnection.Levee;
				}
				else
				{
					result.ValueA = computed;
					result.ValueB = computed;
					result.HydraulicConnection = HydraulicConnection.Backfill;
				}
			}
			else if (minimumElevation4Term >= 2f * minimumElevation2AboveMinimumElevation)
			{
				result.ValueA = computed;
				result.ValueB = computed;
				result.HydraulicConnection = HydraulicConnection.DownhillDeep;
			}
			else
			{
				float computed2 = systemMinimumElevation3Term + (minimumElevation4Term * minimumElevation4Term - minimumElevation4Term2 * minimumElevation4Term2) / (2f * minimumElevation2AboveMinimumElevation);
				if (minimumElevation4Term > minimumElevation2AboveMinimumElevation)
				{
					computed2 = ((2f * minimumElevation2AboveMinimumElevation - minimumElevation4Term) * computed2 + (minimumElevation4Term - minimumElevation2AboveMinimumElevation) * computed) / minimumElevation2AboveMinimumElevation;
					result.HydraulicConnection = HydraulicConnection.DownhillIntermediate;
				}
				else
				{
					result.HydraulicConnection = HydraulicConnection.DownhillShallow;
				}
				result.ValueA = computed2;
				result.ValueB = computed2;
			}
		}
		return result;
	}

	public FaceValues ComputeFaceWaterSurfaces(int f, float[] cellWS)
	{
		Face face = Faces[f];
		return ComputeFaceWaterSurfaces(f, cellWS[face.cellA], cellWS[face.cellB]);
	}

	public FaceValues ComputeFaceWaterSurfaces(int f, Dictionary<int, float> cellWS)
	{
		Face face = Faces[f];
		return ComputeFaceWaterSurfaces(f, cellWS[face.cellA], cellWS[face.cellB]);
	}

	private FaceValues ComputeFaceWaterSurfaces(int f, float cellWSA, float cellWSB)
	{
		if (SharedData.FaceWSMode == SharedData.FaceWSMethod.Adjusted)
		{
			return ComputeFaceWSsNew(f, cellWSA, cellWSB);
		}
		return ComputeFaceWSsBENPrev(f, cellWSA, cellWSB);
	}

	private float AvgWaterSurfaceWithCritCheck(float cellWSA, float cellWSB, float maxWSEL, float minZFace, ref bool wasCritCapUsed)
	{
		float cellWSACellWSBTerm = (cellWSA + cellWSB) * 0.5f;
		float maxWSELMinZFaceTerm = (maxWSEL - minZFace) * (2f / 3f) + minZFace;
		if (cellWSACellWSBTerm > maxWSELMinZFaceTerm)
		{
			wasCritCapUsed = false;
			return cellWSACellWSBTerm;
		}
		wasCritCapUsed = true;
		return maxWSELMinZFaceTerm;
	}

	public float[] ComputeFacePointWSs(int fp, FaceValues[] faceWSs, Func<int, Point2Double> GetFaceApplicationPoint)
	{
		FacePoint facePoint = FacePoints[fp];
		checked
		{
			float[] facePointWSs = new float[facePoint.Faces.Count - 1 + 1];
			bool condition = true;
			int count = facePoint.Faces.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				facePointWSs[i] = -9999f;
				int faceIndex = facePoint.Faces[i];
				if (faceWSs[faceIndex].ValueA != -9999f)
				{
					condition = false;
				}
				if (faceWSs[faceIndex].ValueB != -9999f)
				{
					condition = false;
				}
			}
			if (condition)
			{
				return facePointWSs;
			}
			PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(facePoint.Point);
			new List<Tuple<Point2Double, float, float>>();
			int count2 = facePoint.Faces.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				if (facePointWSs[j] != -9999f)
				{
					continue;
				}
				planarRegressionZ.Clear();
				int ccwFaceByLocalIndexFacePointTerm = j;
				do
				{
					ccwFaceByLocalIndexFacePointTerm = facePoint.CCWFaceByLocalIndex(ccwFaceByLocalIndexFacePointTerm);
				}
				while (!(!faceWSs[facePoint.Faces[ccwFaceByLocalIndexFacePointTerm]].IsHydraulicallyConnected | (ccwFaceByLocalIndexFacePointTerm == j)));
				int cwFaceByLocalIndexFacePointTerm = j;
				if (cwFaceByLocalIndexFacePointTerm != ccwFaceByLocalIndexFacePointTerm)
				{
					while (faceWSs[facePoint.Faces[cwFaceByLocalIndexFacePointTerm]].IsHydraulicallyConnected)
					{
						cwFaceByLocalIndexFacePointTerm = facePoint.CWFaceByLocalIndex(cwFaceByLocalIndexFacePointTerm);
					}
				}
				int ordinal = cwFaceByLocalIndexFacePointTerm;
				do
				{
					int faceIndex2 = facePoint.Faces[ordinal];
					double valueB = ((Faces[faceIndex2].fpA != fp) ? ((double)faceWSs[faceIndex2].ValueB) : ((double)faceWSs[faceIndex2].ValueA));
					if (valueB != -9999.0)
					{
						planarRegressionZ.Add(GetFaceApplicationPoint(faceIndex2), valueB);
					}
					facePointWSs[ordinal] = (float)valueB;
					ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
				}
				while (ordinal != ccwFaceByLocalIndexFacePointTerm);
				int faceIndex3 = facePoint.Faces[ccwFaceByLocalIndexFacePointTerm];
				if (!faceWSs[faceIndex3].IsHydraulicallyConnected)
				{
					double valueA = ((Faces[faceIndex3].fpA != fp) ? ((double)faceWSs[faceIndex3].ValueA) : ((double)faceWSs[faceIndex3].ValueB));
					if (valueA != -9999.0)
					{
						planarRegressionZ.Add(GetFaceApplicationPoint(faceIndex3), valueA);
					}
				}
				if (planarRegressionZ.Count() > 0)
				{
					float solveZ = (float)planarRegressionZ.SolveZ();
					ordinal = cwFaceByLocalIndexFacePointTerm;
					do
					{
						facePointWSs[ordinal] = solveZ;
						ordinal = facePoint.CCWFaceByLocalIndex(ordinal);
					}
					while (ordinal != ccwFaceByLocalIndexFacePointTerm);
				}
				if (cwFaceByLocalIndexFacePointTerm == ccwFaceByLocalIndexFacePointTerm)
				{
					break;
				}
			}
			return facePointWSs;
		}
	}

	public void EnsureFaceMidsideCached()
	{
		if (!USE_FACE_MIDSIDE_CACHING || _faceMidside != null)
		{
			return;
		}
		checked
		{
			_faceMidside = new FaceMidside[Faces.Count - 1 + 1];
			int count = Faces.Count - 1;
			FaceMidside faceMidside = default;
			for (int i = 0; i <= count; i++)
			{
				Face face = Faces[i];
				Polyline polyline = FacePolyline(i);
				PointOnPolyline pointOnPolyline = null;
				if (face.cellA != -1 && face.cellB != -1)
				{
					SegmentM segment = new SegmentM(_cells[face.cellA].Point, _cells[face.cellB].Point);
					PointIntersect lineIntersection = polyline.GetLineIntersection(segment);
					if (lineIntersection != null)
					{
						pointOnPolyline = lineIntersection.APointOnPolyline;
					}
				}
				if (pointOnPolyline == null)
				{
					pointOnPolyline = polyline.Midpoint();
				}
				faceMidside.Point.X = pointOnPolyline.PointM.X;
				faceMidside.Point.Y = pointOnPolyline.PointM.Y;
				faceMidside.SegmentIndex = pointOnPolyline.SegmentIndex;
				_faceMidside[i] = faceMidside;
			}
		}
	}

	public bool EnsureFaceCentroidCached(RASD2FlowArea d2FlowArea, string meshName)
	{
		return EnsureFaceCentroidCached(d2FlowArea.SourceFilename, meshName);
	}

	public bool EnsureFaceCentroidCached(string hdfFilename, string meshName)
	{
		if (_faceCentroidStation != null)
		{
			return true;
		}
		string meshGroupName = MakeMeshGroupName(meshName);
		using (H5Reader h5Reader = new H5Reader(hdfFilename))
		{
			string meshGroupNameTerm = meshGroupName + "/Faces Low Elevation Centroid";
			if (!h5Reader.ExistsAsDataset(meshGroupNameTerm))
			{
				return false;
			}
			float[] values = null;
			h5Reader.ReadDataset(meshGroupNameTerm, ref values);
			_faceCentroidStation = values;
		}
		return true;
	}

	public float FaceCentroidDistance(int fidx)
	{
		return _faceCentroidStation[fidx];
	}

	public PointM FaceCentroidPoint(int fidx)
	{
		FaceSegment(fidx);
		FaceCentroidDistance(fidx);
		return FaceSegment(fidx).MidPoint();
	}

	public FaceMidside GetFaceMidSide(int fIdx)
	{
		if (USE_FACE_MIDSIDE_CACHING)
		{
			if (_faceMidside == null)
			{
				EnsureFaceMidsideCached();
			}
			return _faceMidside[fIdx];
		}
		Face face = Faces[fIdx];
		Polyline polyline = FacePolyline(fIdx);
		PointOnPolyline pointOnPolyline = null;
		if (face.cellA != -1 && face.cellB != -1)
		{
			SegmentM segment = new SegmentM(_cells[face.cellA].Point, _cells[face.cellB].Point);
			PointIntersect lineIntersection = polyline.GetLineIntersection(segment);
			if (lineIntersection != null)
			{
				double length = polyline.Length;
				double distanceAlongOverLength = polyline.DistanceAlong(lineIntersection.APointOnPolyline) / length;
				if ((distanceAlongOverLength > 0.05) & (distanceAlongOverLength < 0.95))
				{
					pointOnPolyline = lineIntersection.APointOnPolyline;
				}
			}
		}
		if (pointOnPolyline == null)
		{
			pointOnPolyline = polyline.Midpoint();
		}
		FaceMidside result = default;
		result.Point.X = pointOnPolyline.PointM.X;
		result.Point.Y = pointOnPolyline.PointM.Y;
		result.SegmentIndex = pointOnPolyline.SegmentIndex;
		return result;
	}

	public string GetMeshErrorMessage()
	{
		if (MeshCompletionState == MeshStatus.Complete)
		{
			return "";
		}
		if (MeshCompletionState == MeshStatus.PointsOutsidePerimeter)
		{
			return BadIndexes.Count + " Error(s) - Points Outside of Perimeter";
		}
		if (MeshCompletionState == MeshStatus.DuplicatePoints)
		{
			return BadIndexes.Count + " Error(s) - Duplicate Cell Points";
		}
		if (MeshCompletionState == MeshStatus.DisconnectedGraphs)
		{
			return "Error - Disconnected Mesh Sections";
		}
		if (MeshCompletionState == MeshStatus.FacePerimeterConnectionError)
		{
			return BadIndexes.Count + " Error(s) - Face Perimeter Connection Error";
		}
		if (MeshCompletionState == MeshStatus.MaxFacesPerCellExceeded)
		{
			return BadIndexes.Count + " Error(s) - Maximum 8 Faces per Cell";
		}
		if (MeshCompletionState == MeshStatus.MaxFacesPerFacepointExceeded)
		{
			return BadIndexes.Count + " Error(s) - Maximum 8 Faces per FacePoint";
		}
		if (MeshCompletionState == MeshStatus.FewerThanMinPerimeterPoints)
		{
			return "Error - Needs More Perimeter Points";
		}
		if (MeshCompletionState == MeshStatus.FewerThanMinNumCells)
		{
			return "Error - Needs > 1 Cell";
		}
		if (MeshCompletionState == MeshStatus.CellAreaTotalError)
		{
			return "Cell Area Error";
		}
		if (MeshCompletionState == MeshStatus.MinFaceLenError)
		{
			return BadIndexes.Count + " Error(s) - Minimum Face Length Exceeded";
		}
		if (MeshCompletionState == MeshStatus.PerimeterPolygonError)
		{
			return _statusMessage;
		}
		return "Unknown Error";
	}

	public override bool Intersects(Extent extent)
	{
		return false;
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return false;
	}

	public override string ToString()
	{
		if (!IsComplete())
		{
			return "Mesh: " + GetMeshErrorMessage();
		}
		return "Mesh 2DFV";
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		if (_perimeter != null)
		{
			return _perimeter.FirstPointM();
		}
		return new PointM(_cells[0].Point);
	}

	public override bool EqualXY(Feature feature)
	{
		return false;
	}

	public CellFlowPathStruct[] ComputeFlowPaths()
	{
		checked
		{
			CellFlowPathStruct[] cellFlowPathStructs = new CellFlowPathStruct[CellCount - 1 + 1];
			float[] floatValues = new float[CellCount - 1 + 1];
			int count = _cells.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				floatValues[i] = CellMinimumFaceMinimum(i);
			}
			int count2 = _cells.Count() - 1;
			for (int j = 0; j <= count2; j++)
			{
				Cell cell = _cells[j];
				cellFlowPathStructs[j].DownstreamCell = -1;
				foreach (int face4 in cell.Faces)
				{
					float minimumElevation = Faces[face4].MinimumElevation;
					int downstreamCell = Faces[face4].OtherCell(j);
					if (!(minimumElevation > floatValues[j]) && !(floatValues[downstreamCell] >= floatValues[j]) && (cellFlowPathStructs[j].DownstreamCell == -1 || floatValues[downstreamCell] < floatValues[cellFlowPathStructs[j].DownstreamCell]))
					{
						cellFlowPathStructs[j].DownstreamCell = downstreamCell;
						cellFlowPathStructs[j].DownstreamFace = face4;
					}
				}
			}
			List<int> intList = new List<int>();
			List<int> intListList = new List<int>();
			BitArray bitArray = new BitArray(CellCount);
			int count3 = CellCount - 1;
			for (int k = 0; k <= count3; k++)
			{
				if (cellFlowPathStructs[k].DownstreamCell != -1)
				{
					continue;
				}
				foreach (int face5 in _cells[k].Faces)
				{
					int otherCell = Faces[face5].OtherCell(k);
					if (floatValues[otherCell] == floatValues[k] && cellFlowPathStructs[otherCell].DownstreamCell != -1)
					{
						intList.Add(k);
					}
				}
			}
			while (intList.Count != 0)
			{
				foreach (int ordinal in intList)
				{
					foreach (int face6 in _cells[ordinal].Faces)
					{
						int downstreamCell2 = Faces[face6].OtherCell(ordinal);
						if (floatValues[downstreamCell2] == floatValues[ordinal] && cellFlowPathStructs[downstreamCell2].DownstreamCell != -1 && (cellFlowPathStructs[ordinal].DownstreamCell == -1 || Faces[cellFlowPathStructs[downstreamCell2].DownstreamFace].MinimumElevation < Faces[cellFlowPathStructs[ordinal].DownstreamFace].MinimumElevation))
						{
							cellFlowPathStructs[ordinal].DownstreamCell = downstreamCell2;
							cellFlowPathStructs[ordinal].DownstreamFace = face6;
						}
					}
				}
				intListList.Clear();
				foreach (int ordinal2 in intList)
				{
					foreach (int face7 in _cells[ordinal2].Faces)
					{
						int otherCell2 = Faces[face7].OtherCell(ordinal2);
						if (floatValues[otherCell2] == floatValues[ordinal2] && cellFlowPathStructs[otherCell2].DownstreamCell == -1 && !bitArray[otherCell2])
						{
							intListList.Add(otherCell2);
							bitArray[otherCell2] = true;
						}
					}
				}
				List<int> intList2 = intList;
				intList = intListList;
				intListList = intList2;
			}
			Stack<int> intStack = new Stack<int>();
			int count4 = CellCount - 1;
			for (int l = 0; l <= count4; l++)
			{
				if (cellFlowPathStructs[l].DownstreamCell != -1)
				{
					continue;
				}
				bitArray.SetAll(value: false);
				intStack.Clear();
				intStack.Push(l);
				int otherCell3Term = l;
				while (intStack.Count != 0)
				{
					int pop = intStack.Pop();
					foreach (int face8 in _cells[pop].Faces)
					{
						int otherCell3 = Faces[face8].OtherCell(pop);
						if (!bitArray[otherCell3] && floatValues[otherCell3] == floatValues[l] && cellFlowPathStructs[otherCell3].DownstreamCell == -1)
						{
							bitArray[otherCell3] = true;
							intStack.Push(otherCell3);
							if (_cells[otherCell3].MinimumElevation < _cells[otherCell3Term].MinimumElevation)
							{
								otherCell3Term = otherCell3;
							}
						}
					}
				}
				intList.Clear();
				intList.Add(otherCell3Term);
				while (intList.Count != 0)
				{
					intListList.Clear();
					foreach (int downstreamCell3 in intList)
					{
						foreach (int face9 in _cells[downstreamCell3].Faces)
						{
							int otherCell4 = Faces[face9].OtherCell(downstreamCell3);
							if (floatValues[otherCell4] == floatValues[downstreamCell3] && cellFlowPathStructs[otherCell4].DownstreamCell == -1 && otherCell4 != otherCell3Term)
							{
								intListList.Add(otherCell4);
								cellFlowPathStructs[otherCell4].DownstreamCell = downstreamCell3;
								cellFlowPathStructs[otherCell4].DownstreamFace = face9;
							}
						}
					}
					List<int> intList3 = intList;
					intList = intListList;
					intListList = intList3;
				}
			}
			List<SinkStruct> sinkStructs = new List<SinkStruct>();
			int count5 = CellCount - 1;
			for (int m = 0; m <= count5; m++)
			{
				if (cellFlowPathStructs[m].DownstreamCell != -1)
				{
					continue;
				}
				SinkStruct sinkStruct = new SinkStruct(m);
				cellFlowPathStructs[m].SinkCell = m;
				sinkStruct.AddCell(CellPolygonArea(m), floatValues[m]);
				intStack.Clear();
				intStack.Push(m);
				while (intStack.Count != 0)
				{
					int pop2 = intStack.Pop();
					foreach (int face10 in _cells[pop2].Faces)
					{
						int otherCell5 = Faces[face10].OtherCell(pop2);
						if (cellFlowPathStructs[otherCell5].DownstreamCell == pop2)
						{
							cellFlowPathStructs[otherCell5].SinkCell = m;
							intStack.Push(otherCell5);
							sinkStruct.AddCell(CellPolygonArea(m), floatValues[m]);
						}
					}
				}
				sinkStructs.Add(sinkStruct);
			}
			if (SharedData.DebugMode)
			{
				int contributingCellCountSum = 0;
				foreach (SinkStruct sinkStruct2 in sinkStructs)
				{
					contributingCellCountSum += sinkStruct2.ContributingCellCount;
				}
			}
			Dictionary<int, int> intByInt = new Dictionary<int, int>();
			int count6 = sinkStructs.Count - 1;
			for (int n = 0; n <= count6; n++)
			{
				intByInt.Add(sinkStructs[n].Cell, n);
			}
			int count7 = FaceCount - 1;
			for (int ridgeFaces2 = 0; ridgeFaces2 <= count7; ridgeFaces2++)
			{
				Face face = Faces[ridgeFaces2];
				int sinkCell = cellFlowPathStructs[face.cellA].SinkCell;
				int sinkCell2 = cellFlowPathStructs[face.cellB].SinkCell;
				if (sinkCell == sinkCell2)
				{
					continue;
				}
				int unity = 1;
				do
				{
					List<int> ridgeFaces = ((unity != 1) ? sinkStructs[intByInt[sinkCell2]].RidgeFaces : sinkStructs[intByInt[sinkCell]].RidgeFaces);
					if (ridgeFaces.Count == 0)
					{
						ridgeFaces.Add(ridgeFaces2);
					}
					else
					{
						bool condition = false;
						int count8 = ridgeFaces.Count - 1;
						for (int intervalIndex = 0; intervalIndex <= count8; intervalIndex++)
						{
							Face face2 = Faces[ridgeFaces[intervalIndex]];
							if ((cellFlowPathStructs[face2.cellA].SinkCell == sinkCell && cellFlowPathStructs[face2.cellB].SinkCell == sinkCell2) || (cellFlowPathStructs[face2.cellA].SinkCell == sinkCell2 && cellFlowPathStructs[face2.cellB].SinkCell == sinkCell))
							{
								condition = true;
								if (face.MinimumElevation < face2.MinimumElevation)
								{
									ridgeFaces[intervalIndex] = ridgeFaces2;
								}
							}
						}
						if (!condition)
						{
							ridgeFaces.Add(ridgeFaces2);
						}
					}
					unity++;
				}
				while (unity <= 2);
			}
			PolylineFeatureLayer polylineFeatureLayer = new PolylineFeatureLayer("Connectivity");
			polylineFeatureLayer.IsDebugLayer = true;
			foreach (SinkStruct sinkStruct3 in sinkStructs)
			{
				foreach (int ridgeFace in sinkStruct3.RidgeFaces)
				{
					polylineFeatureLayer.AddFeature(new Polyline(new SegmentM(Cell(sinkStruct3.Cell).Point.ToPointM(), FaceSegment(ridgeFace).MidPoint()).CreateArcArrow()));
				}
			}
			SharedData.RasMapper.MapLayersGroup.Nodes.Add(polylineFeatureLayer);
			PolylineFeatureLayer polylineFeatureLayer2 = new PolylineFeatureLayer("Sink Regions");
			polylineFeatureLayer2.IsDebugLayer = true;
			int count9 = FaceCount - 1;
			for (int intervalIndex2 = 0; intervalIndex2 <= count9; intervalIndex2++)
			{
				Face face3 = Faces[intervalIndex2];
				int sinkCell3 = cellFlowPathStructs[face3.cellA].SinkCell;
				int sinkCell4 = cellFlowPathStructs[face3.cellB].SinkCell;
				if (sinkCell3 != sinkCell4)
				{
					polylineFeatureLayer2.AddFeature(FacePolyline(intervalIndex2));
				}
			}
			SharedData.RasMapper.MapLayersGroup.Nodes.Add(polylineFeatureLayer2);
			return cellFlowPathStructs;
		}
	}

	public float CellMaximumFaceMinimum(int c)
	{
		float minValue = float.MinValue;
		foreach (int face in _cells[c].Faces)
		{
			minValue = System.Math.Max(minValue, Faces[face].MinimumElevation);
		}
		return minValue;
	}

	public float CellMinimumFaceMinimum(int c)
	{
		float maxValue = float.MaxValue;
		foreach (int face in _cells[c].Faces)
		{
			maxValue = System.Math.Min(maxValue, Faces[face].MinimumElevation);
		}
		return maxValue;
	}

	public float[] SolveInitialConditions(List<PointM> pts, List<Polygon> boundingPolys, float approachFactor, int numIterations)
	{
		_Closure_0024__370_002D0 capturedLocals = new _Closure_0024__370_002D0(null);
		capturedLocals.meshFV2D = this;
		capturedLocals.approachFactor = approachFactor;
		if (boundingPolys == null)
		{
			boundingPolys = new List<Polygon>();
		}
		checked
		{
			if (prevPts != null && prevPts.Count == pts.Count && prevPolys != null && prevPolys.Count == boundingPolys.Count)
			{
				bool condition = true;
				int count = pts.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (!prevPts[i].EqualXYZMWithNaN(pts[i]))
					{
						condition = false;
						break;
					}
				}
				if (condition)
				{
					int count2 = boundingPolys.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						if (!prevPolys[j].EqualXY(boundingPolys[j]))
						{
							condition = false;
							break;
						}
					}
				}
				if (prevApprFactor != capturedLocals.approachFactor)
				{
					condition = false;
				}
				if (prevIterCt != numIterations)
				{
					condition = false;
				}
				if (condition)
				{
					return prevICSln;
				}
			}
			capturedLocals.possibleWet = new bool[CellCount - 1 + 1];
			float[] prevICSlnArray = new float[CellCount - 1 + 1];
			float[] prevICSlnArrayArray = new float[CellCount - 1 + 1];
			capturedLocals.sourceVals = new float[CellCount - 1 + 1];
			int count3 = CellCount - 1;
			for (int k = 0; k <= count3; k++)
			{
				capturedLocals.sourceVals[k] = -9999f;
				prevICSlnArray[k] = CellMinimumElevation(k);
				capturedLocals.possibleWet[k] = false;
			}
			DistinctSpatialIndex distinctSpatialIndex = ComputeCellSpatialIndex(skipVirtualCells: true);
			int count4 = pts.Count - 1;
			for (int l = 0; l <= count4; l++)
			{
				PointM pointM = pts[l];
				List<int> distinctElements = distinctSpatialIndex.GetDistinctElements(pointM.X, pointM.X, pointM.Y, pointM.Y);
				foreach (int distinctElement in distinctElements)
				{
					if (!CellPolygon(distinctElement).Contains(pointM))
					{
						continue;
					}
					capturedLocals.sourceVals[distinctElement] = (float)pointM.Z;
					prevICSlnArray[distinctElement] = (float)pointM.Z;
					capturedLocals.possibleWet[l] = true;
					foreach (int face2 in _cells[distinctElement].Faces)
					{
						capturedLocals.possibleWet[Faces[face2].OtherCell(distinctElement)] = true;
					}
					if (!pointM.HasM())
					{
						break;
					}
					float pointMTerm = (float)pointM.M;
					if (pointMTerm > 1f || pointMTerm < 0f)
					{
						throw new ArgumentException();
					}
					List<int> intList = new List<int>();
					intList.Add(distinctElement);
					while (intList.Count > 0)
					{
						int ordinal = intList.Last();
						Cell cell = _cells[ordinal];
						float prevICSlnArray2 = prevICSlnArray[ordinal];
						intList.RemoveAt(intList.Count - 1);
						foreach (int face3 in _cells[ordinal].Faces)
						{
							Face face = Faces[face3];
							int otherCell = face.OtherCell(ordinal);
							capturedLocals.possibleWet[otherCell] = true;
							double distanceTo = cell.Point.DistanceTo(_cells[otherCell].Point);
							if ((double)prevICSlnArray2 - 0.5 * distanceTo * (double)pointMTerm > (double)face.MinimumElevation)
							{
								float prevICSlnArray3 = (float)((double)prevICSlnArray2 - distanceTo * (double)pointMTerm);
								if (prevICSlnArray3 > prevICSlnArray[otherCell])
								{
									prevICSlnArray[otherCell] = prevICSlnArray3;
									intList.Add(otherCell);
								}
							}
						}
					}
					break;
				}
			}
			int count5 = numIterations - 1;
			_Closure_0024__370_002D1 capturedLocals2 = default;
			for (int m = 0; m <= count5; m++)
			{
				capturedLocals2 = new _Closure_0024__370_002D1(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.tn = prevICSlnArrayArray;
				capturedLocals2.tc = prevICSlnArray;
				Utility.Parallel.SmartFor(CellCount, capturedLocals2._Lambda_0024__0);
				float[] prevICSlnArray4 = prevICSlnArray;
				prevICSlnArray = prevICSlnArrayArray;
				prevICSlnArrayArray = prevICSlnArray4;
			}
			prevPts = pts;
			prevPolys = boundingPolys;
			prevICSln = prevICSlnArray;
			prevApprFactor = capturedLocals.approachFactor;
			prevIterCt = numIterations;
			return prevICSlnArray;
		}
	}

	public override IEditableFeature GetMutableView()
	{
		throw new NotImplementedException();
	}

	public override Feature SetZAll(double z)
	{
		throw new NotImplementedException();
	}

	public override Feature SetMAll(double m)
	{
		throw new NotImplementedException();
	}

	public override IList<double> GetZPerVertex()
	{
		throw new NotImplementedException();
	}

	public override IList<double> GetMPerVertex()
	{
		throw new NotImplementedException();
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		throw new NotImplementedException();
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		throw new NotImplementedException();
	}

	public string SerializeMesh()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CELLS: " + CellCount);
		stringBuilder.AppendLine("FACES: " + FaceCount);
		stringBuilder.AppendLine("FACEPOINTS: " + FacePointCount);
		checked
		{
			int count = CellCount - 1;
			for (int i = 0; i <= count; i++)
			{
				Cell cell = _cells[i];
				stringBuilder.AppendLine(string.Format("C[{0}]: F={1}", i, string.Join(",", cell.Faces.ToArray())));
			}
			int count2 = FaceCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				Face face = Faces[j];
				PointMs internalPoints = face.InternalPoints;
				string internalPointsTerm = "(NONE)";
				if (!internalPoints.IsNullOrEmpty())
				{
					internalPointsTerm = string.Join("|", internalPoints);
				}
				stringBuilder.AppendLine(string.Format("F[{0}]: CA={1} CB={2} FPA={3} FPB={4}, INTERNAL:{5}", new object[6] { j, face.cellA, face.cellB, face.fpA, face.fpB, internalPointsTerm }));
			}
			int count3 = FacePointCount - 1;
			for (int k = 0; k <= count3; k++)
			{
				FacePoint facePoint = FacePoints[k];
				stringBuilder.AppendLine(string.Format("FP[{0}]: F={1}, PT={2}", k, string.Join(",", facePoint.Faces), facePoint.Point));
			}
			return stringBuilder.ToString();
		}
	}
}
