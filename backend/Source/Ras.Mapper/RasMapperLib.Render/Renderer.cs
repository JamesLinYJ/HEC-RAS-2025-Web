using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using RasMapperLib.Names;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class Renderer
{
	[Serializable]
	[CompilerGenerated]
	internal sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<FlatCellMap, int> _0024I81_002D0;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal int _Lambda_0024__81_002D0(FlatCellMap mappedCell)
		{
			return mappedCell.Index;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D0
	{
		public float[] clipPixelDepths;

		public float[] output;

		public Renderer renderer;

		public _Closure_0024__15_002D0(_Closure_0024__15_002D0 other)
		{
			if (other != null)
			{
				clipPixelDepths = other.clipPixelDepths;
				output = other.output;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D1
	{
		public float[] invertedDepths;

		public _Closure_0024__15_002D0 outerClosure;

		public _Closure_0024__15_002D1(_Closure_0024__15_002D1 other)
		{
			if (other != null)
			{
				invertedDepths = other.invertedDepths;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float clipPixelDepth = outerClosure.clipPixelDepths[i];
				if (clipPixelDepth == -9999f)
				{
					invertedDepths[i] = -9999f;
				}
				else
				{
					invertedDepths[i] = -1f * clipPixelDepth;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D2
	{
		public FlatMeshMap flatArea;

		public _Closure_0024__15_002D0 outerClosure;

		public _Closure_0024__15_002D2(_Closure_0024__15_002D2 other)
		{
			if (other != null)
			{
				flatArea = other.flatArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D3
	{
		public float[] cellInputValues;

		public float[] cellWaterSurfaces;

		public _Closure_0024__15_002D2 outerClosure;

		public _Closure_0024__15_002D3(_Closure_0024__15_002D3 other)
		{
			if (other != null)
			{
				cellInputValues = other.cellInputValues;
				cellWaterSurfaces = other.cellWaterSurfaces;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			outerClosure.outerClosure.renderer.ComputeCellValuesFlat(outerClosure.flatArea, cellInputValues, outerClosure.outerClosure.output);
		}

		[SpecialName]
		internal void _Lambda_0024__2()
		{
			outerClosure.outerClosure.renderer.ComputeCellValuesFlat(outerClosure.flatArea, cellInputValues, outerClosure.outerClosure.output, outerClosure.outerClosure.clipPixelDepths);
		}

		[SpecialName]
		internal void _Lambda_0024__3()
		{
			outerClosure.outerClosure.renderer.ComputeCellValuesFlat(outerClosure.flatArea, cellInputValues, outerClosure.outerClosure.output, outerClosure.outerClosure.clipPixelDepths, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D4
	{
		public SlopingMeshMap slopedArea;

		public _Closure_0024__15_002D0 outerClosure;

		public _Closure_0024__15_002D4(_Closure_0024__15_002D4 other)
		{
			if (other != null)
			{
				slopedArea = other.slopedArea;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__15_002D5
	{
		public float[] cellInputValues;

		public float[] cellWaterSurfaces;

		public _Closure_0024__15_002D4 outerClosure;

		public _Closure_0024__15_002D5(_Closure_0024__15_002D5 other)
		{
			if (other != null)
			{
				cellInputValues = other.cellInputValues;
				cellWaterSurfaces = other.cellWaterSurfaces;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			outerClosure.outerClosure.renderer.ComputeFromCellValues_NoHydraulicConnectivity(outerClosure.slopedArea, cellInputValues, outerClosure.outerClosure.output);
		}

		[SpecialName]
		internal void _Lambda_0024__5()
		{
			outerClosure.outerClosure.renderer.ComputeFromCellValues_NoHydraulicConnectivity(outerClosure.slopedArea, cellInputValues, outerClosure.outerClosure.output, outerClosure.outerClosure.clipPixelDepths);
		}

		[SpecialName]
		internal void _Lambda_0024__6()
		{
			outerClosure.outerClosure.renderer.ComputeFromCellValues_SimpleHydraulicConnectivity(outerClosure.slopedArea, cellInputValues, cellWaterSurfaces, outerClosure.outerClosure.output, outerClosure.outerClosure.clipPixelDepths);
		}

		[SpecialName]
		internal void _Lambda_0024__7()
		{
			outerClosure.outerClosure.renderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellInputValues, outerClosure.outerClosure.output);
		}

		[SpecialName]
		internal void _Lambda_0024__8()
		{
			outerClosure.outerClosure.renderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellInputValues, outerClosure.outerClosure.output, outerClosure.outerClosure.clipPixelDepths);
		}

		[SpecialName]
		internal void _Lambda_0024__9()
		{
			outerClosure.outerClosure.renderer.ComputeCellValuesFlat(outerClosure.slopedArea, cellInputValues, outerClosure.outerClosure.output, outerClosure.outerClosure.clipPixelDepths, cellWaterSurfaces);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__21_002D0
	{
		public float[][] cellElevs;

		public float[][] initialCellElevs;

		public _Closure_0024__21_002D1 outerClosure;

		public _Closure_0024__21_002D0(_Closure_0024__21_002D0 other)
		{
			if (other != null)
			{
				cellElevs = other.cellElevs;
				initialCellElevs = other.initialCellElevs;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(SlopingCellMap cell)
		{
			int index = cell.Index;
			checked
			{
				if (index > cellElevs.Length - 1)
				{
					return;
				}
				float[] initialCellElev = initialCellElevs[index];
				float[] cellElev = cellElevs[index];
				foreach (SlopingCellPoint mapPoint in cell.MapPoints)
				{
					float terrain = outerClosure.terrain[mapPoint.Index];
					bool condition = false;
					int count = cellElev.Count() - 2;
					for (int i = 0; i <= count; i++)
					{
						if (terrain >= initialCellElev[i] && terrain < initialCellElev[i + 1])
						{
							float terrain2 = 0f;
							if (outerClosure.outputAsChange)
							{
								terrain2 = terrain;
							}
							outerClosure.output[mapPoint.Index] = cellElev[i] + terrain2;
							condition = true;
							break;
						}
					}
					if (!condition && cellElev.Length > 0)
					{
						float terrain3 = 0f;
						if (outerClosure.outputAsChange)
						{
							terrain3 = terrain;
						}
						if (terrain > initialCellElev.Last())
						{
							outerClosure.output[mapPoint.Index] = cellElev[initialCellElev.Length - 1] + terrain3;
						}
						else
						{
							outerClosure.output[mapPoint.Index] = cellElev[0] + terrain3;
						}
					}
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(FlatCellMap cell)
		{
			int index = cell.Index;
			checked
			{
				if (index > cellElevs.Length - 1)
				{
					return;
				}
				float[] initialCellElev = initialCellElevs[index];
				float[] cellElev = cellElevs[index];
				foreach (int mapPoint in cell.MapPoints)
				{
					float terrain = outerClosure.terrain[mapPoint];
					bool condition = false;
					int count = cellElev.Count() - 2;
					for (int i = 0; i <= count; i++)
					{
						if (terrain >= initialCellElev[i] && terrain < initialCellElev[i + 1])
						{
							float terrain2 = 0f;
							if (outerClosure.outputAsChange)
							{
								terrain2 = terrain;
							}
							outerClosure.output[mapPoint] = cellElev[i] + terrain2;
							condition = true;
							break;
						}
					}
					if (!condition && cellElev.Length > 0)
					{
						float terrain3 = 0f;
						if (outerClosure.outputAsChange)
						{
							terrain3 = terrain;
						}
						if (terrain > initialCellElev.Last())
						{
							outerClosure.output[mapPoint] = cellElev[initialCellElev.Length - 1] + terrain3;
						}
						else
						{
							outerClosure.output[mapPoint] = cellElev[0] + terrain3;
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__21_002D1
	{
		public float[] terrain;

		public bool outputAsChange;

		public float[] output;

		public _Closure_0024__21_002D1(_Closure_0024__21_002D1 other)
		{
			if (other != null)
			{
				terrain = other.terrain;
				outputAsChange = other.outputAsChange;
				output = other.output;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__26_002D0
	{
		public float[] clipCellWSEL;

		public MeshFV2D mesh;

		public FaceValues[] faceValuesArray;

		public float[] output;

		public float[][] fpValuesPerCell;

		public Renderer renderer;

		public _Closure_0024__26_002D0(_Closure_0024__26_002D0 other)
		{
			if (other != null)
			{
				clipCellWSEL = other.clipCellWSEL;
				mesh = other.mesh;
				faceValuesArray = other.faceValuesArray;
				output = other.output;
				fpValuesPerCell = other.fpValuesPerCell;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(SlopingCellMap mapCell)
		{
			checked
			{
				if (clipCellWSEL != null)
				{
					Cell cell = mesh.Cell(mapCell.Index);
					float clipCellWSEL = this.clipCellWSEL[mapCell.Index];
					if (clipCellWSEL <= cell.MinimumElevation + renderer.MinWSPlotTolerance)
					{
						return;
					}
					bool condition = true;
					int countOffset = cell.Faces.Count - 1;
					for (int i = 0; i <= countOffset; i++)
					{
						int faceIndex = cell.Faces[i];
						FaceValues faceValues = faceValuesArray[faceIndex];
						if (faceValues.IsHydraulicallyConnected & (faceValues.HydraulicConnection != HydraulicConnection.DownhillShallow))
						{
							condition = false;
						}
					}
					if (SharedData.ShallowBehaviorMode == SharedData.ShallowBehavior.ReduceToHorizontal && condition)
					{
						foreach (SlopingCellPoint mapPoint in mapCell.MapPoints)
						{
							output[mapPoint.Index] = clipCellWSEL;
						}
						return;
					}
				}
				float[] fPLocalValues = mesh.GetFPLocalValues(mapCell.Index, fpValuesPerCell);
				if (clipCellWSEL != null && SharedData.FacepointAdjustmentMode == SharedData.FacepointAdjustmentMethod.DragAverage)
				{
					DownwardAdjustFPValues(clipCellWSEL[mapCell.Index], fPLocalValues);
				}
				FacepointWeightedPaint(mapCell, fPLocalValues, output);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__28_002D0
	{
		public MeshFV2D mesh;

		public float[] faceScalarValues;

		public float[] fpVals;

		public List<int> fpList;

		public _Closure_0024__28_002D0(_Closure_0024__28_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				faceScalarValues = other.faceScalarValues;
				fpVals = other.fpVals;
				fpList = other.fpList;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					FacePoint facePoint = mesh.FacePoints[i];
					float faceScalarValueSum = 0f;
					int countOffset = facePoint.Faces.Count - 1;
					for (int j = 0; j <= countOffset; j++)
					{
						int faceIndex = facePoint.Faces[j];
						faceScalarValueSum += faceScalarValues[faceIndex];
					}
					fpVals[i] = faceScalarValueSum / (float)facePoint.Faces.Count;
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					int fpList = this.fpList[i];
					FacePoint facePoint = mesh.FacePoints[fpList];
					float faceScalarValueSum = 0f;
					int countOffset = facePoint.Faces.Count - 1;
					for (int j = 0; j <= countOffset; j++)
					{
						int faceIndex = facePoint.Faces[j];
						faceScalarValueSum += faceScalarValues[faceIndex];
					}
					fpVals[fpList] = faceScalarValueSum / (float)facePoint.Faces.Count;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__29_002D0
	{
		public MeshFV2D mesh;

		public Vector2D[] faceVectors;

		public Vector2D[] fpVals;

		public List<int> fpList;

		public _Closure_0024__29_002D0(_Closure_0024__29_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				faceVectors = other.faceVectors;
				fpVals = other.fpVals;
				fpList = other.fpList;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					FacePoint facePoint = mesh.FacePoints[i];
					int count = facePoint.Faces.Count;
					float computed = 0f;
					float computed2 = 0f;
					int countOffset = count - 1;
					for (int j = 0; j <= countOffset; j++)
					{
						int faceIndex = facePoint.Faces[j];
						computed += faceVectors[faceIndex].X;
						computed2 += faceVectors[faceIndex].Y;
					}
					fpVals[i] = new Vector2D(computed / (float)count, computed2 / (float)count);
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					int fpList = this.fpList[i];
					FacePoint facePoint = mesh.FacePoints[fpList];
					int count = facePoint.Faces.Count;
					float computed = 0f;
					float computed2 = 0f;
					int countOffset = count - 1;
					for (int j = 0; j <= countOffset; j++)
					{
						int faceIndex = facePoint.Faces[j];
						computed += faceVectors[faceIndex].X;
						computed2 += faceVectors[faceIndex].Y;
					}
					fpVals[fpList] = new Vector2D(computed / (float)count, computed2 / (float)count);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__30_002D0
	{
		public MeshFV2D mesh;

		public Func<int, Point2Double> getFaceApplicationPoint;

		public float[] faceScalarValues;

		public float[] fpVals;

		public SlopingMeshMap slopedArea;

		public _Closure_0024__30_002D0(_Closure_0024__30_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				getFaceApplicationPoint = other.getFaceApplicationPoint;
				faceScalarValues = other.faceScalarValues;
				fpVals = other.fpVals;
				slopedArea = other.slopedArea;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				FacePoint facePoint = mesh.FacePoints[i];
				Point2Double point = facePoint.Point;
				PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(point);
				foreach (int face in facePoint.Faces)
				{
					planarRegressionZ.Add(getFaceApplicationPoint(face), faceScalarValues[face]);
				}
				float solveZ = (float)planarRegressionZ.SolveZ();
				fpVals[i] = solveZ;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFacePoint = slopedArea.UniqueFacePoints[i];
				FacePoint facePoint = mesh.FacePoints[uniqueFacePoint];
				Point2Double point = facePoint.Point;
				PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(point);
				foreach (int face in facePoint.Faces)
				{
					planarRegressionZ.Add(getFaceApplicationPoint(face), faceScalarValues[face]);
				}
				float solveZ = (float)planarRegressionZ.SolveZ();
				fpVals[uniqueFacePoint] = solveZ;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__33_002D0
	{
		public MeshFV2D mesh;

		public float[] faceScalarValues;

		public float[] outCellVals;

		public _Closure_0024__33_002D0(_Closure_0024__33_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				faceScalarValues = other.faceScalarValues;
				outCellVals = other.outCellVals;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int cIdx)
		{
			Cell cell = mesh.Cell(cIdx);
			int count = cell.Faces.Count;
			float faceScalarValueSum = 0f;
			checked
			{
				int countOffset = count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					int faceIndex = cell.Faces[i];
					faceScalarValueSum += faceScalarValues[faceIndex];
				}
				outCellVals[cIdx] = faceScalarValueSum / (float)count;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__34_002D0
	{
		public MeshFV2D mesh;

		public Vector2D[] faceVectors;

		public Vector2D[] outCellVals;

		public _Closure_0024__34_002D0(_Closure_0024__34_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				faceVectors = other.faceVectors;
				outCellVals = other.outCellVals;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int cIdx)
		{
			Cell cell = mesh.Cell(cIdx);
			int count = cell.Faces.Count;
			Vector2D vector2D = default;
			checked
			{
				int countOffset = count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					int faceIndex = cell.Faces[i];
					vector2D += faceVectors[faceIndex];
				}
				outCellVals[cIdx] = vector2D / count;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__36_002D0
	{
		public MeshFV2D mesh;

		public float[] faceScalarValues;

		public float[] outCellVals;

		public _Closure_0024__36_002D0(_Closure_0024__36_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				faceScalarValues = other.faceScalarValues;
				outCellVals = other.outCellVals;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int cIdx)
		{
			Cell cell = mesh.Cell(cIdx);
			int count = cell.Faces.Count;
			float absoluteFaceScalarValue = 0f;
			checked
			{
				int countOffset = count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					int faceIndex = cell.Faces[i];
					float absoluteFaceScalarValue2 = Math.Abs(faceScalarValues[faceIndex]);
					if (absoluteFaceScalarValue < absoluteFaceScalarValue2)
					{
						absoluteFaceScalarValue = absoluteFaceScalarValue2;
					}
				}
				outCellVals[cIdx] = absoluteFaceScalarValue;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__38_002D0
	{
		public List<int> uniqueCells;

		public Action<int> computeCellValue;

		public _Closure_0024__38_002D0(_Closure_0024__38_002D0 other)
		{
			if (other != null)
			{
				uniqueCells = other.uniqueCells;
				computeCellValue = other.computeCellValue;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueCellIndex = uniqueCells[i];
				computeCellValue(uniqueCellIndex);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__39_002D0
	{
		public Action<int> computeCellValue;

		public _Closure_0024__39_002D0(_Closure_0024__39_002D0 other)
		{
			if (other != null)
			{
				computeCellValue = other.computeCellValue;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				computeCellValue(i);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__40_002D0
	{
		public float[] clipPixelDepths;

		public _Closure_0024__40_002D0(_Closure_0024__40_002D0 other)
		{
			if (other != null)
			{
				clipPixelDepths = other.clipPixelDepths;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__40_002D1
	{
		public float[] invertedDepths;

		public _Closure_0024__40_002D0 outerClosure;

		public _Closure_0024__40_002D1(_Closure_0024__40_002D1 other)
		{
			if (other != null)
			{
				invertedDepths = other.invertedDepths;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float clipPixelDepth = outerClosure.clipPixelDepths[i];
				if (clipPixelDepth == -9999f)
				{
					invertedDepths[i] = -9999f;
				}
				else
				{
					invertedDepths[i] = -1f * clipPixelDepth;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__41_002D0
	{
		public float[] clipPixelDepths;

		public _Closure_0024__41_002D0(_Closure_0024__41_002D0 other)
		{
			if (other != null)
			{
				clipPixelDepths = other.clipPixelDepths;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__41_002D1
	{
		public float[] invertedDepths;

		public _Closure_0024__41_002D0 outerClosure;

		public _Closure_0024__41_002D1(_Closure_0024__41_002D1 other)
		{
			if (other != null)
			{
				invertedDepths = other.invertedDepths;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				float clipPixelDepth = outerClosure.clipPixelDepths[i];
				if (clipPixelDepth == -9999f)
				{
					invertedDepths[i] = -9999f;
				}
				else
				{
					invertedDepths[i] = -1f * clipPixelDepth;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__47_002D0
	{
		public MeshFV2D mesh;

		public float[] cellWaterSurface;

		public FaceValues[] faceWSValues;

		public float[] output;

		public float[][] facePointValues;

		public Renderer renderer;

		public _Closure_0024__47_002D0(_Closure_0024__47_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				cellWaterSurface = other.cellWaterSurface;
				faceWSValues = other.faceWSValues;
				output = other.output;
				facePointValues = other.facePointValues;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(SlopingCellMap slopedCell)
		{
			int index = slopedCell.Index;
			Cell cell = mesh.Cell(index);
			float cellWaterSurface = this.cellWaterSurface[index];
			if (cellWaterSurface <= cell.MinimumElevation + renderer.MinWSPlotTolerance)
			{
				return;
			}
			int count = cell.Faces.Count;
			bool condition = true;
			checked
			{
				float[] floatValues = new float[count - 1 + 1];
				int countOffset = count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					int faceIndex = cell.Faces[i];
					FaceValues faceValues = faceWSValues[faceIndex];
					if (index == mesh.Faces[faceIndex].cellA)
					{
						floatValues[i] = faceValues.ValueA;
					}
					else
					{
						floatValues[i] = faceValues.ValueB;
					}
					if (faceValues.IsHydraulicallyConnected & (faceValues.HydraulicConnection != HydraulicConnection.DownhillShallow))
					{
						condition = false;
					}
				}
				if (SharedData.ShallowBehaviorMode == SharedData.ShallowBehavior.ReduceToHorizontal && condition)
				{
					foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
					{
						output[mapPoint.Index] = cellWaterSurface;
					}
					return;
				}
				float[] floatValuesArray = new float[count - 1 + 1];
				int countOffset2 = count - 1;
				for (int j = 0; j <= countOffset2; j++)
				{
					int ordinal = j;
					int faceIndex2 = cell.Faces[j];
					int fPPrev = mesh.Faces[faceIndex2].GetFPPrev(index);
					floatValuesArray[ordinal] = facePointValues[fPPrev][mesh.FacePoints[fPPrev].Faces.IndexOf(faceIndex2)];
				}
				if (SharedData.FacepointAdjustmentMode == SharedData.FacepointAdjustmentMethod.DragAverage)
				{
					DownwardAdjustFPValues(cellWaterSurface, floatValuesArray);
				}
				PaintCell_8Stencil(slopedCell, floatValuesArray, floatValues, output);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__48_002D0
	{
		public MeshFV2D mesh;

		public float[] cellWaterSurface;

		public FaceValues[] faceWSValues;

		public float[] output;

		public float[][] facePointValues;

		public float[][] cellRebalanceWeights;

		public Renderer renderer;

		public _Closure_0024__48_002D0(_Closure_0024__48_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				cellWaterSurface = other.cellWaterSurface;
				faceWSValues = other.faceWSValues;
				output = other.output;
				facePointValues = other.facePointValues;
				cellRebalanceWeights = other.cellRebalanceWeights;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(SlopingCellMap slopedCell)
		{
			int index = slopedCell.Index;
			Cell cell = mesh.Cell(index);
			float cellWaterSurface = this.cellWaterSurface[index];
			if (cellWaterSurface <= cell.MinimumElevation + renderer.MinWSPlotTolerance)
			{
				return;
			}
			int count = cell.Faces.Count;
			bool condition = true;
			checked
			{
				float[] floatValues = new float[count - 1 + 1];
				int countOffset = count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					int faceIndex = cell.Faces[i];
					FaceValues faceValues = faceWSValues[faceIndex];
					if (index == mesh.Faces[faceIndex].cellA)
					{
						floatValues[i] = faceValues.ValueA;
					}
					else
					{
						floatValues[i] = faceValues.ValueB;
					}
					if (faceValues.IsHydraulicallyConnected & (faceValues.HydraulicConnection != HydraulicConnection.DownhillShallow))
					{
						condition = false;
					}
				}
				if (SharedData.ShallowBehaviorMode == SharedData.ShallowBehavior.ReduceToHorizontal && condition)
				{
					foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
					{
						output[mapPoint.Index] = cellWaterSurface;
					}
					return;
				}
				float[] floatValuesArray = new float[count - 1 + 1];
				int countOffset2 = count - 1;
				for (int j = 0; j <= countOffset2; j++)
				{
					int ordinal = j;
					int faceIndex2 = cell.Faces[j];
					int fPPrev = mesh.Faces[faceIndex2].GetFPPrev(index);
					floatValuesArray[ordinal] = facePointValues[fPPrev][mesh.FacePoints[fPPrev].Faces.IndexOf(faceIndex2)];
				}
				if (SharedData.FacepointAdjustmentMode == SharedData.FacepointAdjustmentMethod.DragAverage)
				{
					DownwardAdjustFPValues(cellWaterSurface, floatValuesArray);
				}
				if (cellRebalanceWeights == null)
				{
					PaintCell_8Stencil(slopedCell, floatValuesArray, floatValues, output);
				}
				else
				{
					PaintCell_8Stencil_RebalanceWeights(slopedCell, floatValuesArray, floatValues, output, cellRebalanceWeights[index]);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__58_002D0
	{
		public SlopingCellMap slopedCell;

		public float[] clipPixelDepth;

		public float[] fpLocalValues;

		public float[] output;

		public _Closure_0024__58_002D0(_Closure_0024__58_002D0 other)
		{
			if (other != null)
			{
				slopedCell = other.slopedCell;
				clipPixelDepth = other.clipPixelDepth;
				fpLocalValues = other.fpLocalValues;
				output = other.output;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			checked
			{
				for (int i = start; i <= finish; i++)
				{
					SlopingCellPoint slopingCellPoint = slopedCell.MapPoints[i];
					if (clipPixelDepth != null && clipPixelDepth[slopingCellPoint.Index] <= 0f)
					{
						continue;
					}
					float computed = 0f;
					int ordinal = 0;
					int count = fpLocalValues.Count() - 1;
					for (int j = 0; j <= count; j++)
					{
						float fpLocalValue = fpLocalValues[j];
						if (fpLocalValue != -9999f)
						{
							ordinal++;
							computed += slopingCellPoint.FPPrevWeights[j] * fpLocalValue;
						}
					}
					if (ordinal > 0)
					{
						output[slopingCellPoint.Index] = computed;
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__83_002D0
	{
		public Dictionary<int, float[]> xsSubsectionValues;

		public float[] clipDepths;

		public float[] output;

		public Renderer renderer;

		public _Closure_0024__83_002D0(_Closure_0024__83_002D0 other)
		{
			if (other != null)
			{
				xsSubsectionValues = other.xsSubsectionValues;
				clipDepths = other.clipDepths;
				output = other.output;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(MappedXSIS interpSurface)
		{
			_Closure_0024__83_002D1 localValue = default;
			_Closure_0024__83_002D1 capturedLocals = new _Closure_0024__83_002D1(localValue);
			capturedLocals.outerClosure = this;
			capturedLocals.interpSurface = interpSurface;
			capturedLocals.usXS = renderer._XSIS.GetUpstreamXSID(capturedLocals.interpSurface.ID);
			capturedLocals.dsXS = renderer._XSIS.GetDownstreamXSID(capturedLocals.interpSurface.ID);
			capturedLocals.usXSVels = xsSubsectionValues[capturedLocals.usXS];
			capturedLocals.dsXSVels = xsSubsectionValues[capturedLocals.dsXS];
			capturedLocals.usStationComputer = renderer._XS.GetStationComputer(capturedLocals.usXS);
			capturedLocals.dsStationComputer = renderer._XS.GetStationComputer(capturedLocals.dsXS);
			Utility.Parallel.SmartFor(capturedLocals.interpSurface.MapPoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					XSISMapPoint xSISMapPoint = capturedLocals.interpSurface.MapPoints[i];
					if (!(capturedLocals.outerClosure.clipDepths[xSISMapPoint.Index] <= 0f))
					{
						float averagedMValue = capturedLocals.outerClosure.renderer._flowDistribution.AveragedMValue(capturedLocals.usXS, capturedLocals.usStationComputer(xSISMapPoint.M), capturedLocals.usXSVels);
						float averagedMValue2 = capturedLocals.outerClosure.renderer._flowDistribution.AveragedMValue(capturedLocals.dsXS, capturedLocals.dsStationComputer(xSISMapPoint.M), capturedLocals.dsXSVels);
						capturedLocals.outerClosure.output[xSISMapPoint.Index] = averagedMValue * xSISMapPoint.Z + averagedMValue2 * (1f - xSISMapPoint.Z);
					}
				}
			});
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__83_002D1
	{
		public MappedXSIS interpSurface;

		public int usXS;

		public Func<float, float> usStationComputer;

		public float[] usXSVels;

		public int dsXS;

		public Func<float, float> dsStationComputer;

		public float[] dsXSVels;

		public _Closure_0024__83_002D0 outerClosure;

		public _Closure_0024__83_002D1(_Closure_0024__83_002D1 other)
		{
			if (other != null)
			{
				interpSurface = other.interpSurface;
				usXS = other.usXS;
				usStationComputer = other.usStationComputer;
				usXSVels = other.usXSVels;
				dsXS = other.dsXS;
				dsStationComputer = other.dsStationComputer;
				dsXSVels = other.dsXSVels;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				XSISMapPoint xSISMapPoint = interpSurface.MapPoints[i];
				if (!(outerClosure.clipDepths[xSISMapPoint.Index] <= 0f))
				{
					float averagedMValue = outerClosure.renderer._flowDistribution.AveragedMValue(usXS, usStationComputer(xSISMapPoint.M), usXSVels);
					float averagedMValue2 = outerClosure.renderer._flowDistribution.AveragedMValue(dsXS, dsStationComputer(xSISMapPoint.M), dsXSVels);
					outerClosure.output[xSISMapPoint.Index] = averagedMValue * xSISMapPoint.Z + averagedMValue2 * (1f - xSISMapPoint.Z);
				}
			}
		}
	}

	protected CacheCollection _cache;

	protected RASResults _result;

	protected RASGeometry _geometry;

	protected RASD2FlowArea _d2FlowArea;

	protected RASXSInterpolationSurface _XSIS;

	protected RASXS _XS;

	protected FlowDistributionData _flowDistribution;

	protected string[] _d2FlowAreaNames;

	protected string _hdfFilename;

	protected string _postProcessFilename;

	protected float MinWSPlotTolerance;

	protected PostProcessor _postProcessor;

	protected Renderer(RASResults res, CacheCollection cache, bool needsVelocityFlowData, bool needsPostProcessFile)
	{
		if (cache == null)
		{
			_cache = new CacheCollection();
		}
		else
		{
			_cache = cache;
		}
		H5Reader.SetLogging(log: false);
		_result = res;
		_geometry = _result.Geometry;
		_d2FlowArea = _geometry.D2FlowArea;
		_XSIS = _geometry.XSInterpolationSurface;
		_XS = _geometry.XS;
		_geometry.XS.FeatureTable();
		_geometry.XSInterpolationSurface.FeatureTable();
		_geometry.D2FlowArea.FeatureTable();
		_geometry.BankLines.FeatureTable();
		_geometry.River.FeatureTable();
		_geometry.StorageArea.FeatureTable();
		_geometry.Junctions.FeatureTable();
		checked
		{
			_d2FlowAreaNames = new string[_result.Geometry.D2FlowArea.FeatureCount() - 1 + 1];
			int count = _d2FlowAreaNames.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				_d2FlowAreaNames[i] = _result.Geometry.D2FlowArea.GetFeatureName(i);
			}
			_hdfFilename = _result.SourceFilename;
			_postProcessor = _result.GetPostProcessor();
			_postProcessFilename = _postProcessor.Filename;
			MinWSPlotTolerance = RASResults.MinWSPlotTolerance;
			if (!File.Exists(_hdfFilename))
			{
				throw new Exception("Results file " + Path.GetFileName(_hdfFilename) + " not found. Please rerun results.");
			}
			LoadGeometryData();
			if (needsVelocityFlowData)
			{
				_flowDistribution = _result.FlowDistribution;
				foreach (MeshFV2D meshe in _d2FlowArea.Meshes())
				{
					meshe.EnsureVelocityComputesReady();
				}
			}
			if (needsPostProcessFile)
			{
				_postProcessor.EnsurePostProcessPopup();
			}
		}
	}

	public static Func<RASGeometryMapPoints, float[], float[]> GetSimplifiedComputer(CacheCollection cache, int pfIndex, MapTypes mapType, RASResults results, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer)
	{
		if (mapType == MapTypes.Elevation)
		{
			return WaterSurfaceRenderer.GetComputer(results, pfIndex, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.Depth)
		{
			return DepthRenderer.GetComputer(results, pfIndex, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.Velocity)
		{
			return VelocityRenderer.GetComputer(results, pfIndex, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.DepthTimesVelocity)
		{
			return DepthAndVelocityRenderer.GetComputer(results, pfIndex, DepthVelocityMethod.DepthTimesVelocity, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.DepthTimesVelocitySquared)
		{
			return DepthAndVelocityRenderer.GetComputer(results, pfIndex, DepthVelocityMethod.DepthTimesVelocitySquared, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.EnergyDepth)
		{
			return DepthAndVelocityRenderer.GetComputer(results, pfIndex, DepthVelocityMethod.EnergyDepth, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.EnergyElevation)
		{
			return DepthAndVelocityRenderer.GetComputer(results, pfIndex, DepthVelocityMethod.EnergyElevation, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		if (mapType == MapTypes.Froude)
		{
			return DepthAndVelocityRenderer.GetComputer(results, pfIndex, DepthVelocityMethod.Froude, cache, GetReusableBuffer, ReturnReusableBuffer);
		}
		return null;
	}

	public static Func<RASGeometryMapPoints, float[], float[][]> GetSimplifiedComputerAllValues(CacheCollection cache, int pfIndex, MapTypes mapType, RASResults results, Func<float[]> GetReusableBuffer, Action<float[]> ReturnReusableBuffer, bool TrueFaceVelocitiesPreprocessed = false)
	{
		if (mapType == MapTypes.Velocity)
		{
			return VelocityRenderer.GetComputerAllValues(results, pfIndex, cache, GetReusableBuffer, ReturnReusableBuffer, TrueFaceVelocitiesPreprocessed);
		}
		return null;
	}

	protected void ComputeFromCellValues(RASGeometryMapPoints mps, float[][] meshCellInputValues, float[][] meshCellWaterSurfaces, float[] output, Coverage cov, bool interpolate, float[] clipPixelDepths = null)
	{
		_Closure_0024__15_002D0 capturedLocals = new _Closure_0024__15_002D0(null);
		capturedLocals.renderer = this;
		capturedLocals.output = output;
		capturedLocals.clipPixelDepths = clipPixelDepths;
		if (meshCellWaterSurfaces == null || meshCellInputValues == null)
		{
			return;
		}
		List<Task> tasks = new List<Task>();
		if (cov == Coverage.Unknown)
		{
			cov = Coverage.Wet;
		}
		checked
		{
			if (cov == Coverage.Dry)
			{
				_Closure_0024__15_002D1 capturedLocals2 = default;
				_Closure_0024__15_002D1 capturedLocals3 = new _Closure_0024__15_002D1(capturedLocals2);
				capturedLocals3.outerClosure = capturedLocals;
				capturedLocals3.invertedDepths = new float[capturedLocals3.outerClosure.clipPixelDepths.Count() - 1 + 1];
				Utility.Parallel.SmartFor(capturedLocals3.outerClosure.clipPixelDepths.Count() - 1, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float clipPixelDepth = capturedLocals3.outerClosure.clipPixelDepths[i];
						if (clipPixelDepth == -9999f)
						{
							capturedLocals3.invertedDepths[i] = -9999f;
						}
						else
						{
							capturedLocals3.invertedDepths[i] = -1f * clipPixelDepth;
						}
					}
				});
				capturedLocals3.outerClosure.clipPixelDepths = capturedLocals3.invertedDepths;
			}
			using (List<FlatMeshMap>.Enumerator flatMapped2DAreas = mps.FlatMapped2DAreas().GetEnumerator())
			{
				_Closure_0024__15_002D2 capturedLocals4 = default;
				_Closure_0024__15_002D3 capturedLocals5 = default;
				while (flatMapped2DAreas.MoveNext())
				{
					capturedLocals4 = new _Closure_0024__15_002D2(capturedLocals4);
					capturedLocals4.outerClosure = capturedLocals;
					capturedLocals4.flatArea = flatMapped2DAreas.Current;
					capturedLocals5 = new _Closure_0024__15_002D3(capturedLocals5);
					capturedLocals5.outerClosure = capturedLocals4;
					capturedLocals5.cellWaterSurfaces = meshCellWaterSurfaces[capturedLocals5.outerClosure.flatArea.MeshIndex];
					capturedLocals5.cellInputValues = meshCellInputValues[capturedLocals5.outerClosure.flatArea.MeshIndex];
					switch (cov)
					{
					case Coverage.Average:
						tasks.Add(Task.Factory.StartNew(capturedLocals5._Lambda_0024__1));
						break;
					case Coverage.Dry:
						tasks.Add(Task.Factory.StartNew(capturedLocals5._Lambda_0024__2));
						break;
					case Coverage.Wet:
						tasks.Add(Task.Factory.StartNew(capturedLocals5._Lambda_0024__3));
						break;
					default:
						throw new ArgumentException();
					}
				}
			}
			using (List<SlopingMeshMap>.Enumerator slopingMapped2DAreas = mps.SlopingMapped2DAreas().GetEnumerator())
			{
				_Closure_0024__15_002D4 capturedLocals6 = default;
				_Closure_0024__15_002D5 capturedLocals7 = default;
				while (slopingMapped2DAreas.MoveNext())
				{
					capturedLocals6 = new _Closure_0024__15_002D4(capturedLocals6);
					capturedLocals6.outerClosure = capturedLocals;
					capturedLocals6.slopedArea = slopingMapped2DAreas.Current;
					capturedLocals7 = new _Closure_0024__15_002D5(capturedLocals7);
					capturedLocals7.outerClosure = capturedLocals6;
					capturedLocals7.cellWaterSurfaces = meshCellWaterSurfaces[capturedLocals7.outerClosure.slopedArea.MeshIndex];
					capturedLocals7.cellInputValues = meshCellInputValues[capturedLocals7.outerClosure.slopedArea.MeshIndex];
					if (interpolate)
					{
						switch (cov)
						{
						case Coverage.Average:
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__4));
							break;
						case Coverage.Dry:
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__5));
							break;
						case Coverage.Wet:
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__6));
							break;
						default:
							throw new ArgumentException();
						}
					}
					else
					{
						switch (cov)
						{
						case Coverage.Average:
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__7));
							break;
						case Coverage.Dry:
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__8));
							break;
						case Coverage.Wet:
							tasks.Add(Task.Factory.StartNew(capturedLocals7._Lambda_0024__9));
							break;
						default:
							throw new ArgumentException();
						}
					}
				}
			}
			Task.WaitAll(tasks.ToArray());
		}
	}

	private void ComputeFromCellValues_SimpleHydraulicConnectivity(SlopingMeshMap slopedArea, float[] cellInputValues, float[] cellWaterSurfaces, float[] output, float[] clipPixelDepths = null)
	{
		if (slopedArea != null && slopedArea.Cells.Count != 0)
		{
			float[][] facePointValues = null;
			SlopingFactors.ComputeFacepointValues_SimpleHydraulicConnectivity(_d2FlowArea, slopedArea, cellWaterSurfaces, cellInputValues, ref facePointValues);
			ComputeFromFacepointValues(slopedArea, facePointValues, output, clipPixelDepths, cellWaterSurfaces);
		}
	}

	private void ComputeFromCellValues_NoHydraulicConnectivity(SlopingMeshMap slopedArea, float[] cellInputValues, float[] output, float[] clipIfLTEZero = null)
	{
		if (slopedArea != null && slopedArea.Cells.Count != 0)
		{
			float[] facePointValues = null;
			SlopingFactors.ComputeFacepointValues_NoHydraulicConnectivity(_d2FlowArea, slopedArea, cellInputValues, ref facePointValues);
			ComputeFromFacepointValues(slopedArea, facePointValues, output, clipIfLTEZero);
		}
	}

	protected void ComputeCellValuesFlat(FlatMeshMap flatArea, float[] cellInputValues, float[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (flatArea == null || flatArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = flatArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(flatArea.Cells, [SpecialName] (FlatCellMap flatCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(flatCell.Index);
				if (clipCellWSEL[flatCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			SimplePaint(flatCell, cellInputValues[flatCell.Index], output, clipPixelDepth);
		});
	}

	protected void ComputeCellValuesFlat(FlatMeshMap flatArea, Vector2D[] cellInputValues, Vector2D[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (flatArea == null || flatArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = flatArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(flatArea.Cells, [SpecialName] (FlatCellMap flatCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(flatCell.Index);
				if (clipCellWSEL[flatCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			SimplePaint(flatCell, cellInputValues[flatCell.Index], output, clipPixelDepth);
		});
	}

	protected void ComputeCellValuesFlat(SlopingMeshMap slopedArea, float[] cellInputValues, float[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, [SpecialName] (SlopingCellMap flatCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(flatCell.Index);
				if (clipCellWSEL[flatCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			SimplePaint(flatCell, cellInputValues[flatCell.Index], output, clipPixelDepth);
		});
	}

	protected void ComputeFromSubCellValues(RASGeometryMapPoints mps, float[] terrain, float[][][] meshInitialCellElevs, float[][][] meshCellElevs, float[] output, bool outputAsChange)
	{
		_Closure_0024__21_002D1 capturedLocals = new _Closure_0024__21_002D1(null);
		capturedLocals.terrain = terrain;
		capturedLocals.output = output;
		capturedLocals.outputAsChange = outputAsChange;
		_Closure_0024__21_002D0 capturedLocals2 = default;
		foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
		{
			capturedLocals2 = new _Closure_0024__21_002D0(capturedLocals2);
			capturedLocals2.outerClosure = capturedLocals;
			capturedLocals2.cellElevs = meshCellElevs[joinedMappedMeshe.Key];
			capturedLocals2.initialCellElevs = meshInitialCellElevs[joinedMappedMeshe.Key];
			Action<SlopingCellMap> body = capturedLocals2._Lambda_0024__0;
			Action<FlatCellMap> body2 = capturedLocals2._Lambda_0024__1;
			System.Threading.Tasks.Parallel.ForEach(joinedMappedMeshe.Value.Item1.Cells, body2);
			System.Threading.Tasks.Parallel.ForEach(joinedMappedMeshe.Value.Item2.Cells, body);
		}
	}

	protected void ComputeFromSubFaceValues(RASGeometryMapPoints mps, float[] terrain, float[][][] meshInitialFaceElevs, float[][][] meshFaceElevs, float[] output, bool outputAsChange)
	{
	}

	protected void ComputeFromFacepointValues(SlopingMeshMap slopedArea, float[] fpValues, float[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, [SpecialName] (SlopingCellMap mapCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(mapCell.Index);
				if (clipCellWSEL[mapCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			float[] fPLocalValues = meshFV2D.GetFPLocalValues(mapCell.Index, fpValues);
			FacepointWeightedPaint(mapCell, fPLocalValues, output, clipPixelDepth);
		});
	}

	protected void ComputeFromFacepointValues(SlopingMeshMap slopedArea, Vector2D[] fpValues, Vector2D[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, [SpecialName] (SlopingCellMap mapCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(mapCell.Index);
				if (clipCellWSEL[mapCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			Vector2D[] fPLocalValues = meshFV2D.GetFPLocalValues(mapCell.Index, fpValues);
			FacepointWeightedPaint(mapCell, fPLocalValues, output, clipPixelDepth);
		});
	}

	protected void ComputeFromFacepointValues(SlopingMeshMap slopedArea, float[][] fpValuesPerCell, float[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, [SpecialName] (SlopingCellMap mapCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(mapCell.Index);
				if (clipCellWSEL[mapCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			float[] fPLocalValues = meshFV2D.GetFPLocalValues(mapCell.Index, fpValuesPerCell);
			FacepointWeightedPaint(mapCell, fPLocalValues, output, clipPixelDepth);
		});
	}

	protected void ComputeFromFacepointValues_SpecialWaterSurfaceMode(SlopingMeshMap slopedArea, float[] clipCellWSEL, FaceValues[] faceValues, float[][] fpValuesPerCell, float[] output)
	{
		_Closure_0024__26_002D0 capturedLocals = default;
		_Closure_0024__26_002D0 capturedLocals2 = new _Closure_0024__26_002D0(capturedLocals);
		capturedLocals2.renderer = this;
		capturedLocals2.clipCellWSEL = clipCellWSEL;
		capturedLocals2.faceValuesArray = faceValues;
		capturedLocals2.fpValuesPerCell = fpValuesPerCell;
		capturedLocals2.output = output;
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		capturedLocals2.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, checked([SpecialName] (SlopingCellMap mapCell) =>
		{
			if (capturedLocals2.clipCellWSEL != null)
			{
				Cell cell = capturedLocals2.mesh.Cell(mapCell.Index);
				float clipCellWSEL2 = capturedLocals2.clipCellWSEL[mapCell.Index];
				if (clipCellWSEL2 <= cell.MinimumElevation + capturedLocals2.renderer.MinWSPlotTolerance)
				{
					return;
				}
				bool condition = true;
				int count = cell.Faces.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					int faceIndex = cell.Faces[i];
					FaceValues faceValues2 = capturedLocals2.faceValuesArray[faceIndex];
					if (faceValues2.IsHydraulicallyConnected & (faceValues2.HydraulicConnection != HydraulicConnection.DownhillShallow))
					{
						condition = false;
					}
				}
				if (SharedData.ShallowBehaviorMode == SharedData.ShallowBehavior.ReduceToHorizontal && condition)
				{
					foreach (SlopingCellPoint mapPoint in mapCell.MapPoints)
					{
						capturedLocals2.output[mapPoint.Index] = clipCellWSEL2;
					}
					return;
				}
			}
			float[] fPLocalValues = capturedLocals2.mesh.GetFPLocalValues(mapCell.Index, capturedLocals2.fpValuesPerCell);
			if (capturedLocals2.clipCellWSEL != null && SharedData.FacepointAdjustmentMode == SharedData.FacepointAdjustmentMethod.DragAverage)
			{
				DownwardAdjustFPValues(capturedLocals2.clipCellWSEL[mapCell.Index], fPLocalValues);
			}
			FacepointWeightedPaint(mapCell, fPLocalValues, capturedLocals2.output);
		}));
	}

	protected void ComputeFromFacepointValuesPixelParallelCellLocal(SlopingMeshMap slopedArea, float[][] cellFPLocalValues, float[] output, float[] clipPixelDepth = null, float[] clipCellWSEL = null)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, [SpecialName] (SlopingCellMap mapCell) =>
		{
			if (clipCellWSEL != null)
			{
				Cell cell = meshFV2D.Cell(mapCell.Index);
				if (clipCellWSEL[mapCell.Index] <= cell.MinimumElevation + MinWSPlotTolerance)
				{
					return;
				}
			}
			float[] fpLocalValues = cellFPLocalValues[mapCell.Index];
			FacepointWeightedPaintParallel(mapCell, fpLocalValues, output, clipPixelDepth);
		});
	}

	protected float[] AverageFaceScalarsToFacepointValuesSA(MeshFV2D mesh, float[] faceScalarValues, List<int> fpList = null)
	{
		_Closure_0024__28_002D0 capturedLocals = default;
		_Closure_0024__28_002D0 capturedLocals2 = new _Closure_0024__28_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		capturedLocals2.faceScalarValues = faceScalarValues;
		capturedLocals2.fpList = fpList;
		checked
		{
			capturedLocals2.fpVals = new float[capturedLocals2.mesh.FacePointCount - 1 + 1];
			Buffers.ArraySetAllVB(capturedLocals2.fpVals, -9999f);
			if (capturedLocals2.fpList.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.mesh.FacePointCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						FacePoint facePoint = capturedLocals2.mesh.FacePoints[i];
						float computed = 0f;
						int count = facePoint.Faces.Count - 1;
						for (int j = 0; j <= count; j++)
						{
							int faceIndex = facePoint.Faces[j];
							computed += capturedLocals2.faceScalarValues[faceIndex];
						}
						capturedLocals2.fpVals[i] = computed / (float)facePoint.Faces.Count;
					}
				});
			}
			else
			{
				Utility.Parallel.SmartFor(capturedLocals2.fpList.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int fpList2 = capturedLocals2.fpList[i];
						FacePoint facePoint = capturedLocals2.mesh.FacePoints[fpList2];
						float computed2 = 0f;
						int count2 = facePoint.Faces.Count - 1;
						for (int j = 0; j <= count2; j++)
						{
							int faceIndex2 = facePoint.Faces[j];
							computed2 += capturedLocals2.faceScalarValues[faceIndex2];
						}
						capturedLocals2.fpVals[fpList2] = computed2 / (float)facePoint.Faces.Count;
					}
				});
			}
			return capturedLocals2.fpVals;
		}
	}

	protected Vector2D[] AverageFaceVectorsToFacepointValuesSA(MeshFV2D mesh, Vector2D[] faceVectors, List<int> fpList = null)
	{
		_Closure_0024__29_002D0 capturedLocals = default;
		_Closure_0024__29_002D0 capturedLocals2 = new _Closure_0024__29_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		capturedLocals2.faceVectors = faceVectors;
		capturedLocals2.fpList = fpList;
		checked
		{
			capturedLocals2.fpVals = new Vector2D[capturedLocals2.mesh.FacePointCount - 1 + 1];
			if (capturedLocals2.fpList.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.mesh.FacePointCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						FacePoint facePoint = capturedLocals2.mesh.FacePoints[i];
						int count = facePoint.Faces.Count;
						float computed = 0f;
						float computed2 = 0f;
						int count2 = count - 1;
						for (int j = 0; j <= count2; j++)
						{
							int faceIndex = facePoint.Faces[j];
							computed += capturedLocals2.faceVectors[faceIndex].X;
							computed2 += capturedLocals2.faceVectors[faceIndex].Y;
						}
						capturedLocals2.fpVals[i] = new Vector2D(computed / (float)count, computed2 / (float)count);
					}
				});
			}
			else
			{
				Utility.Parallel.SmartFor(capturedLocals2.fpList.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int fpList2 = capturedLocals2.fpList[i];
						FacePoint facePoint = capturedLocals2.mesh.FacePoints[fpList2];
						int count = facePoint.Faces.Count;
						float computed3 = 0f;
						float computed4 = 0f;
						int count3 = count - 1;
						for (int j = 0; j <= count3; j++)
						{
							int faceIndex2 = facePoint.Faces[j];
							computed3 += capturedLocals2.faceVectors[faceIndex2].X;
							computed4 += capturedLocals2.faceVectors[faceIndex2].Y;
						}
						capturedLocals2.fpVals[fpList2] = new Vector2D(computed3 / (float)count, computed4 / (float)count);
					}
				});
			}
			return capturedLocals2.fpVals;
		}
	}

	protected float[] AverageFaceScalarsToFacepointValuesPZ(int meshIdx, float[] faceScalarValues, SlopingMeshMap slopedArea = null)
	{
		_Closure_0024__30_002D0 capturedLocals = default;
		_Closure_0024__30_002D0 capturedLocals2 = new _Closure_0024__30_002D0(capturedLocals);
		capturedLocals2.faceScalarValues = faceScalarValues;
		capturedLocals2.slopedArea = slopedArea;
		capturedLocals2.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(meshIdx);
		checked
		{
			capturedLocals2.fpVals = new float[capturedLocals2.mesh.FacePointCount - 1 + 1];
			Buffers.ArraySetAllVB(capturedLocals2.fpVals, -9999f);
			capturedLocals2.getFaceApplicationPoint = _d2FlowArea.GetFaceLocationFunc(meshIdx);
			if (capturedLocals2.slopedArea == null || capturedLocals2.slopedArea.UniqueFacePoints.IsNullOrEmpty())
			{
				Utility.Parallel.SmartFor(capturedLocals2.mesh.FacePointCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						FacePoint facePoint = capturedLocals2.mesh.FacePoints[i];
						Point2Double point = facePoint.Point;
						PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(point);
						foreach (int face in facePoint.Faces)
						{
							planarRegressionZ.Add(capturedLocals2.getFaceApplicationPoint(face), capturedLocals2.faceScalarValues[face]);
						}
						float solveZ = (float)planarRegressionZ.SolveZ();
						capturedLocals2.fpVals[i] = solveZ;
					}
				});
			}
			else
			{
				Utility.Parallel.SmartFor(capturedLocals2.slopedArea.UniqueFacePoints.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFacePoint = capturedLocals2.slopedArea.UniqueFacePoints[i];
						FacePoint facePoint = capturedLocals2.mesh.FacePoints[uniqueFacePoint];
						Point2Double point = facePoint.Point;
						PlanarRegressionZ planarRegressionZ = new PlanarRegressionZ(point);
						foreach (int face2 in facePoint.Faces)
						{
							planarRegressionZ.Add(capturedLocals2.getFaceApplicationPoint(face2), capturedLocals2.faceScalarValues[face2]);
						}
						float solveZ2 = (float)planarRegressionZ.SolveZ();
						capturedLocals2.fpVals[uniqueFacePoint] = solveZ2;
					}
				});
			}
			return capturedLocals2.fpVals;
		}
	}

	protected float[] AverageFaceScalarsToCellValues(MeshFV2D mesh, float[] faceScalarValues, List<int> uniqueCells = null)
	{
		float[] floatValues = new float[checked(mesh.NonVirtualCellCount - 1 + 1)];
		Buffers.ArraySetAllVB(floatValues, -9999f);
		AverageFaceScalarsToCellValues(mesh, faceScalarValues, floatValues, uniqueCells);
		return floatValues;
	}

	protected Vector2D[] AverageFaceVectorsToCellValues(MeshFV2D mesh, Vector2D[] faceVectors, List<int> uniqueCells = null)
	{
		Vector2D[] vector2Ds = new Vector2D[checked(mesh.NonVirtualCellCount - 1 + 1)];
		AverageFaceScalarsToCellValues(mesh, faceVectors, vector2Ds, uniqueCells);
		return vector2Ds;
	}

	protected void AverageFaceScalarsToCellValues(MeshFV2D mesh, float[] faceScalarValues, float[] outCellVals, List<int> uniqueCells = null)
	{
		_Closure_0024__33_002D0 capturedLocals = default;
		_Closure_0024__33_002D0 capturedLocals2 = new _Closure_0024__33_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		capturedLocals2.faceScalarValues = faceScalarValues;
		capturedLocals2.outCellVals = outCellVals;
		ComputeCellValuesInternal(capturedLocals2.outCellVals.Count(), uniqueCells, checked([SpecialName] (int cIdx) =>
		{
			Cell cell = capturedLocals2.mesh.Cell(cIdx);
			int count = cell.Faces.Count;
			float computed = 0f;
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int faceIndex = cell.Faces[i];
				computed += capturedLocals2.faceScalarValues[faceIndex];
			}
			capturedLocals2.outCellVals[cIdx] = computed / (float)count;
		}));
	}

	protected void AverageFaceScalarsToCellValues(MeshFV2D mesh, Vector2D[] faceVectors, Vector2D[] outCellVals, List<int> uniqueCells = null)
	{
		_Closure_0024__34_002D0 capturedLocals = default;
		_Closure_0024__34_002D0 capturedLocals2 = new _Closure_0024__34_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		capturedLocals2.faceVectors = faceVectors;
		capturedLocals2.outCellVals = outCellVals;
		ComputeCellValuesInternal(capturedLocals2.outCellVals.Count(), uniqueCells, checked([SpecialName] (int cIdx) =>
		{
			Cell cell = capturedLocals2.mesh.Cell(cIdx);
			int count = cell.Faces.Count;
			Vector2D vector2D = default;
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int faceIndex = cell.Faces[i];
				vector2D += capturedLocals2.faceVectors[faceIndex];
			}
			capturedLocals2.outCellVals[cIdx] = vector2D / count;
		}));
	}

	protected float[] MaximumABSFaceScalarsToCellValues(MeshFV2D mesh, float[] faceScalarValues, List<int> uniqueCells = null)
	{
		float[] floatValues = new float[checked(mesh.NonVirtualCellCount - 1 + 1)];
		Buffers.ArraySetAllVB(floatValues, -9999f);
		MaximumABSFaceScalarsToCellValues(mesh, faceScalarValues, floatValues, uniqueCells);
		return floatValues;
	}

	protected void MaximumABSFaceScalarsToCellValues(MeshFV2D mesh, float[] faceScalarValues, float[] outCellVals, List<int> uniqueCells = null)
	{
		_Closure_0024__36_002D0 capturedLocals = default;
		_Closure_0024__36_002D0 capturedLocals2 = new _Closure_0024__36_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		capturedLocals2.faceScalarValues = faceScalarValues;
		capturedLocals2.outCellVals = outCellVals;
		ComputeCellValuesInternal(capturedLocals2.outCellVals.Count(), uniqueCells, checked([SpecialName] (int cIdx) =>
		{
			Cell cell = capturedLocals2.mesh.Cell(cIdx);
			int count = cell.Faces.Count;
			float computed = 0f;
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int faceIndex = cell.Faces[i];
				float computed2 = Math.Abs(capturedLocals2.faceScalarValues[faceIndex]);
				if (computed < computed2)
				{
					computed = computed2;
				}
			}
			capturedLocals2.outCellVals[cIdx] = computed;
		}));
	}

	private void ComputeCellValuesInternal(int cellCount, List<int> uniqueCells, Action<int> ComputeCellValue)
	{
		if (uniqueCells == null)
		{
			ComputeCellValuesInternal(cellCount, ComputeCellValue);
		}
		else
		{
			ComputeCellValuesInternal(uniqueCells, ComputeCellValue);
		}
	}

	private void ComputeCellValuesInternal(List<int> uniqueCells, Action<int> ComputeCellValue)
	{
		_Closure_0024__38_002D0 capturedLocals = default;
		_Closure_0024__38_002D0 capturedLocals2 = new _Closure_0024__38_002D0(capturedLocals);
		capturedLocals2.uniqueCells = uniqueCells;
		capturedLocals2.computeCellValue = ComputeCellValue;
		Utility.Parallel.SmartFor(capturedLocals2.uniqueCells.Count, [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueCellIndex = capturedLocals2.uniqueCells[i];
				capturedLocals2.computeCellValue(uniqueCellIndex);
			}
		});
	}

	private void ComputeCellValuesInternal(int cellCount, Action<int> ComputeCellValue)
	{
		_Closure_0024__39_002D0 capturedLocals = default;
		_Closure_0024__39_002D0 capturedLocals2 = new _Closure_0024__39_002D0(capturedLocals);
		capturedLocals2.computeCellValue = ComputeCellValue;
		Utility.Parallel.SmartFor(cellCount, [SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				capturedLocals2.computeCellValue(i);
			}
		});
	}

	protected void ComputeFromFaceScalarValues(RASGeometryMapPoints mps, float[][] meshFaceScalarValues, float[][] meshCellWaterSurfaces, Coverage coverage, float[] output, float[] clipPixelDepths)
	{
		_Closure_0024__40_002D0 capturedLocals = new _Closure_0024__40_002D0(null);
		capturedLocals.clipPixelDepths = clipPixelDepths;
		if (coverage == Coverage.Unknown)
		{
			coverage = Coverage.Wet;
		}
		checked
		{
			if (coverage == Coverage.Dry)
			{
				_Closure_0024__40_002D1 capturedLocals2 = default;
				_Closure_0024__40_002D1 capturedLocals3 = new _Closure_0024__40_002D1(capturedLocals2);
				capturedLocals3.outerClosure = capturedLocals;
				capturedLocals3.invertedDepths = new float[capturedLocals3.outerClosure.clipPixelDepths.Count() - 1 + 1];
				Utility.Parallel.SmartFor(capturedLocals3.outerClosure.clipPixelDepths.Count() - 1, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float clipPixelDepth = capturedLocals3.outerClosure.clipPixelDepths[i];
						if (clipPixelDepth == -9999f)
						{
							capturedLocals3.invertedDepths[i] = -9999f;
						}
						else
						{
							capturedLocals3.invertedDepths[i] = -1f * clipPixelDepth;
						}
					}
				});
				capturedLocals3.outerClosure.clipPixelDepths = capturedLocals3.invertedDepths;
			}
			foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
			{
				int key = joinedMappedMeshe.Key;
				_d2FlowArea.Meshes().ElementAtOrDefault(key);
				FlatMeshMap flatMeshMap = joinedMappedMeshe.Value.Item1;
				SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
				float[] cellWaterSurface = meshCellWaterSurfaces[key];
				float[] faceScalarValues = meshFaceScalarValues[key];
				switch (coverage)
				{
				case Coverage.Average:
					ComputeFromFaceScalarValues(flatMeshMap, faceScalarValues, output, null, null);
					ComputeFromFaceScalarValues(slopingMeshMap, faceScalarValues, output, null, null);
					break;
				case Coverage.Dry:
					ComputeFromFaceScalarValues(flatMeshMap, faceScalarValues, output, null, capturedLocals.clipPixelDepths);
					ComputeFromFaceScalarValues(slopingMeshMap, faceScalarValues, output, null, capturedLocals.clipPixelDepths);
					break;
				case Coverage.Wet:
					ComputeFromFaceScalarValues(flatMeshMap, faceScalarValues, output, cellWaterSurface, capturedLocals.clipPixelDepths);
					ComputeFromFaceScalarValues(slopingMeshMap, faceScalarValues, output, cellWaterSurface, capturedLocals.clipPixelDepths);
					break;
				}
			}
		}
	}

	protected void ComputeFromFaceNormalValues(RASGeometryMapPoints mps, float[][] meshFaceVectors, float[][] meshCellWaterSurfaces, Coverage coverage, float[] clipPixelDepths, float[] valueX, float[] valueY, float[] valueMagnitude)
	{
		_Closure_0024__41_002D0 capturedLocals = new _Closure_0024__41_002D0(null);
		capturedLocals.clipPixelDepths = clipPixelDepths;
		checked
		{
			if (coverage == Coverage.Dry)
			{
				_Closure_0024__41_002D1 capturedLocals2 = default;
				_Closure_0024__41_002D1 capturedLocals3 = new _Closure_0024__41_002D1(capturedLocals2);
				capturedLocals3.outerClosure = capturedLocals;
				capturedLocals3.invertedDepths = new float[capturedLocals3.outerClosure.clipPixelDepths.Count() - 1 + 1];
				Utility.Parallel.SmartFor(capturedLocals3.outerClosure.clipPixelDepths.Count() - 1, [SpecialName] (int start, int fin) =>
				{
					for (int i = start; i <= fin; i++)
					{
						float clipPixelDepth = capturedLocals3.outerClosure.clipPixelDepths[i];
						if (clipPixelDepth == -9999f)
						{
							capturedLocals3.invertedDepths[i] = -9999f;
						}
						else
						{
							capturedLocals3.invertedDepths[i] = -1f * clipPixelDepth;
						}
					}
				});
				capturedLocals3.outerClosure.clipPixelDepths = capturedLocals3.invertedDepths;
			}
			foreach (KeyValuePair<int, Tuple<FlatMeshMap, SlopingMeshMap>> joinedMappedMeshe in mps.JoinedMappedMeshes())
			{
				int key = joinedMappedMeshe.Key;
				FlatMeshMap flatMeshMap = joinedMappedMeshe.Value.Item1;
				SlopingMeshMap slopingMeshMap = joinedMappedMeshe.Value.Item2;
				float[] meshCellWaterSurface = meshCellWaterSurfaces[key];
				float[] facePerpendicularValues = meshFaceVectors[key];
				switch (coverage)
				{
				case Coverage.Average:
					ComputeFromFacePerpValues(flatMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, null, null);
					ComputeFromFacePerpValues(slopingMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, meshCellWaterSurface, null, clipWSE: false);
					break;
				case Coverage.Dry:
					ComputeFromFacePerpValues(flatMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, null, capturedLocals.clipPixelDepths);
					ComputeFromFacePerpValues(slopingMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, meshCellWaterSurface, capturedLocals.clipPixelDepths, clipWSE: false);
					break;
				case Coverage.Wet:
					ComputeFromFacePerpValues(flatMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, meshCellWaterSurface, capturedLocals.clipPixelDepths);
					ComputeFromFacePerpValues(slopingMeshMap, facePerpendicularValues, valueX, valueY, valueMagnitude, meshCellWaterSurface, capturedLocals.clipPixelDepths, clipWSE: true);
					break;
				}
			}
		}
	}

	private void ComputeFromFaceScalarValues(FlatMeshMap flatArea, float[] faceScalarValues, float[] output, float[] cellWaterSurface, float[] clipPixelDepths)
	{
		if (flatArea != null && flatArea.Cells.Count != 0)
		{
			int meshIndex = flatArea.MeshIndex;
			MeshFV2D mesh = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
			float[] cellInputValues = AverageFaceScalarsToCellValues(mesh, faceScalarValues, flatArea.UniqueCells());
			ComputeCellValuesFlat(flatArea, cellInputValues, output, clipPixelDepths, cellWaterSurface);
		}
	}

	private void ComputeFromFaceScalarValues(SlopingMeshMap slopedArea, float[] faceScalarValues, float[] output, float[] cellWaterSurface, float[] clipPixelDepths)
	{
		if (slopedArea != null && slopedArea.Cells.Count != 0)
		{
			float[] fpValues = AverageFaceScalarsToFacepointValuesSA(_d2FlowArea.Meshes().ElementAtOrDefault(slopedArea.MeshIndex), faceScalarValues, slopedArea.UniqueFacePoints);
			ComputeFromFacepointValues(slopedArea, fpValues, output, clipPixelDepths, cellWaterSurface);
		}
	}

	protected void ComputeFromFacePerpValues(FlatMeshMap flatArea, float[] facePerpendicularValues, float[] outputX, float[] outputY, float[] outputMagnitude, float[] clipCellWSEL, float[] clipPixelDepth)
	{
		if (flatArea == null || flatArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = flatArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		System.Threading.Tasks.Parallel.ForEach(flatArea.Cells, [SpecialName] (FlatCellMap flatCell) =>
		{
			int index = flatCell.Index;
			Cell cell = meshFV2D.Cell(index);
			if (clipCellWSEL == null || !(clipCellWSEL[index] <= cell.MinimumElevation + MinWSPlotTolerance))
			{
				Vector2D cellValue_FacePerpLeastSquares = meshFV2D.ComputeCellValue_FacePerpLeastSquares(index, facePerpendicularValues);
				SimplePaint(flatCell, cellValue_FacePerpLeastSquares, outputX, outputY, outputMagnitude, clipPixelDepth);
			}
		});
	}

	protected void ComputeFromFacePerpValues(SlopingMeshMap slopedArea, float[] facePerpendicularValues, float[] outputX, float[] outputY, float[] outputMagnitude, float[] cellWaterSurface, float[] clipPixelDepths, bool clipWSE)
	{
		if (slopedArea != null && slopedArea.Cells.Count != 0)
		{
			int meshIndex = slopedArea.MeshIndex;
			MeshFV2D? meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
			FaceValues[] faceWSs = SlopingFactors.ComputeSlopingWSFaceFactors(_d2FlowArea, cellWaterSurface, slopedArea);
			FacePointVels[] fpVels = meshFV2D.ComputeSlopingFacepointVelocities_LeastSquares(facePerpendicularValues, faceWSs, slopedArea);
			Render2D_8Stencil(slopedArea, facePerpendicularValues, outputX, outputY, outputMagnitude, cellWaterSurface, clipPixelDepths, null, null, fpVels, clipWSE);
		}
	}

	protected void Render2D_8Stencil(SlopingMeshMap slopedArea, float[] facePerpendicularValues, float[] outputX, float[] outputY, float[] outputMagnitude, float[] cellWaterSurface, float[] clipPixelDepths, FaceValues[] faceWSValues = null, Tuple<Vector2D, Vector2D>[] faceTrueVels = null, FacePointVels[] fpVels = null, bool skipDryCells = true)
	{
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		if (faceWSValues == null || faceWSValues.Count() != meshFV2D.FaceCount)
		{
			faceWSValues = SlopingFactors.ComputeSlopingWSFaceFactors(_d2FlowArea, cellWaterSurface, slopedArea);
		}
		if (faceTrueVels == null || faceTrueVels.Count() != meshFV2D.FaceCount)
		{
			faceTrueVels = meshFV2D.ComputeFaceTrueVelocities_CStencilAveraged(faceWSValues, facePerpendicularValues, slopedArea);
		}
		if (fpVels == null || fpVels.Count() != meshFV2D.FacePointCount)
		{
			fpVels = meshFV2D.ComputeSlopingFacepointVelocities_AvgTrueVels(faceTrueVels, faceWSValues, slopedArea);
		}
		System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, [SpecialName] (SlopingCellMap slopedCell) =>
		{
			int index = slopedCell.Index;
			Cell cell = meshFV2D.Cell(index);
			if (!skipDryCells || !(cellWaterSurface[index] <= cell.MinimumElevation + MinWSPlotTolerance))
			{
				Vector2D[] localFaceValues = GetLocalFaceValues(meshFV2D, index, faceTrueVels);
				Vector2D[] localFacepointValues = GetLocalFacepointValues(meshFV2D, index, fpVels);
				PaintCell_8Stencil(slopedCell, localFacepointValues, localFaceValues, clipPixelDepths, outputX, outputY, outputMagnitude);
			}
		});
	}

	protected void Render2D_NodeDonatedWeights_WaterSurface(SlopingMeshMap slopedArea, FaceValues[] faceWSValues, float[][] facePointValues, float[] cellWaterSurface, float[] output)
	{
		_Closure_0024__47_002D0 capturedLocals = default;
		_Closure_0024__47_002D0 capturedLocals2 = new _Closure_0024__47_002D0(capturedLocals);
		capturedLocals2.renderer = this;
		capturedLocals2.faceWSValues = faceWSValues;
		capturedLocals2.facePointValues = facePointValues;
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		capturedLocals2.output = output;
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		capturedLocals2.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		try
		{
			System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, checked([SpecialName] (SlopingCellMap slopedCell) =>
			{
				int index = slopedCell.Index;
				Cell cell = capturedLocals2.mesh.Cell(index);
				float cellWaterSurface2 = capturedLocals2.cellWaterSurface[index];
				if (!(cellWaterSurface2 <= cell.MinimumElevation + capturedLocals2.renderer.MinWSPlotTolerance))
				{
					int count = cell.Faces.Count;
					bool condition = true;
					float[] floatValues = new float[count - 1 + 1];
					int count2 = count - 1;
					for (int i = 0; i <= count2; i++)
					{
						int faceIndex = cell.Faces[i];
						FaceValues faceValues = capturedLocals2.faceWSValues[faceIndex];
						if (index == capturedLocals2.mesh.Faces[faceIndex].cellA)
						{
							floatValues[i] = faceValues.ValueA;
						}
						else
						{
							floatValues[i] = faceValues.ValueB;
						}
						if (faceValues.IsHydraulicallyConnected & (faceValues.HydraulicConnection != HydraulicConnection.DownhillShallow))
						{
							condition = false;
						}
					}
					if (SharedData.ShallowBehaviorMode == SharedData.ShallowBehavior.ReduceToHorizontal && condition)
					{
						foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
						{
							capturedLocals2.output[mapPoint.Index] = cellWaterSurface2;
						}
						return;
					}
					float[] floatValuesArray = new float[count - 1 + 1];
					int count3 = count - 1;
					for (int j = 0; j <= count3; j++)
					{
						int ordinal = j;
						int faceIndex2 = cell.Faces[j];
						int fPPrev = capturedLocals2.mesh.Faces[faceIndex2].GetFPPrev(index);
						floatValuesArray[ordinal] = capturedLocals2.facePointValues[fPPrev][capturedLocals2.mesh.FacePoints[fPPrev].Faces.IndexOf(faceIndex2)];
					}
					if (SharedData.FacepointAdjustmentMode == SharedData.FacepointAdjustmentMethod.DragAverage)
					{
						DownwardAdjustFPValues(cellWaterSurface2, floatValuesArray);
					}
					PaintCell_8Stencil(slopedCell, floatValuesArray, floatValues, capturedLocals2.output);
				}
			}));
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	protected void Render2D_NodeDonatedWeights_WaterSurface(SlopingMeshMap slopedArea, FaceValues[] faceWSValues, float[][] facePointValues, float[] cellWaterSurface, float[] output, float[][] cellRebalanceWeights = null)
	{
		_Closure_0024__48_002D0 capturedLocals = default;
		_Closure_0024__48_002D0 capturedLocals2 = new _Closure_0024__48_002D0(capturedLocals);
		capturedLocals2.renderer = this;
		capturedLocals2.faceWSValues = faceWSValues;
		capturedLocals2.facePointValues = facePointValues;
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		capturedLocals2.output = output;
		capturedLocals2.cellRebalanceWeights = cellRebalanceWeights;
		if (slopedArea == null || slopedArea.Cells.Count == 0)
		{
			return;
		}
		int meshIndex = slopedArea.MeshIndex;
		capturedLocals2.mesh = _d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		try
		{
			System.Threading.Tasks.Parallel.ForEach(slopedArea.Cells, checked([SpecialName] (SlopingCellMap slopedCell) =>
			{
				int index = slopedCell.Index;
				Cell cell = capturedLocals2.mesh.Cell(index);
				float cellWaterSurface2 = capturedLocals2.cellWaterSurface[index];
				if (!(cellWaterSurface2 <= cell.MinimumElevation + capturedLocals2.renderer.MinWSPlotTolerance))
				{
					int count = cell.Faces.Count;
					bool condition = true;
					float[] floatValues = new float[count - 1 + 1];
					int count2 = count - 1;
					for (int i = 0; i <= count2; i++)
					{
						int faceIndex = cell.Faces[i];
						FaceValues faceValues = capturedLocals2.faceWSValues[faceIndex];
						if (index == capturedLocals2.mesh.Faces[faceIndex].cellA)
						{
							floatValues[i] = faceValues.ValueA;
						}
						else
						{
							floatValues[i] = faceValues.ValueB;
						}
						if (faceValues.IsHydraulicallyConnected & (faceValues.HydraulicConnection != HydraulicConnection.DownhillShallow))
						{
							condition = false;
						}
					}
					if (SharedData.ShallowBehaviorMode == SharedData.ShallowBehavior.ReduceToHorizontal && condition)
					{
						foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
						{
							capturedLocals2.output[mapPoint.Index] = cellWaterSurface2;
						}
						return;
					}
					float[] floatValuesArray = new float[count - 1 + 1];
					int count3 = count - 1;
					for (int j = 0; j <= count3; j++)
					{
						int ordinal = j;
						int faceIndex2 = cell.Faces[j];
						int fPPrev = capturedLocals2.mesh.Faces[faceIndex2].GetFPPrev(index);
						floatValuesArray[ordinal] = capturedLocals2.facePointValues[fPPrev][capturedLocals2.mesh.FacePoints[fPPrev].Faces.IndexOf(faceIndex2)];
					}
					if (SharedData.FacepointAdjustmentMode == SharedData.FacepointAdjustmentMethod.DragAverage)
					{
						DownwardAdjustFPValues(cellWaterSurface2, floatValuesArray);
					}
					if (capturedLocals2.cellRebalanceWeights == null)
					{
						PaintCell_8Stencil(slopedCell, floatValuesArray, floatValues, capturedLocals2.output);
					}
					else
					{
						PaintCell_8Stencil_RebalanceWeights(slopedCell, floatValuesArray, floatValues, capturedLocals2.output, capturedLocals2.cellRebalanceWeights[index]);
					}
				}
			}));
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	private static void PaintCell_8Stencil_RebalanceWeights(SlopingCellMap cell, float[] fpLocalValues, float[] fLocalValues, float[] output, float[] extraWeights)
	{
		int fLocalValueLength = fLocalValues.Length;
		checked
		{
			foreach (SlopingCellPoint mapPoint in cell.MapPoints)
			{
				int index = mapPoint.Index;
				float[] floatValues = new float[mapPoint.VelocityWeights.Length - 1 + 1];
				double velocityWeightTimesExtraWeightSum = 0.0;
				int count = floatValues.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					float velocityWeight = mapPoint.VelocityWeights[i];
					float extraWeight = extraWeights[i];
					float velocityWeightTimesExtraWeight = velocityWeight * extraWeight;
					velocityWeightTimesExtraWeightSum += (double)velocityWeightTimesExtraWeight;
					floatValues[i] = velocityWeightTimesExtraWeight;
				}
				float computed = 0f;
				int count2 = fLocalValueLength - 1;
				for (int j = 0; j <= count2; j++)
				{
					float fpLocalValue = fpLocalValues[j];
					computed += fpLocalValue * floatValues[j];
				}
				int count3 = fLocalValueLength - 1;
				for (int k = 0; k <= count3; k++)
				{
					float fLocalValue = fLocalValues[k];
					computed += fLocalValue * floatValues[fLocalValueLength + k];
				}
				output[index] = (float)((double)computed / velocityWeightTimesExtraWeightSum);
			}
		}
	}

	private static void PaintCell_8Stencil(SlopingCellMap cell, float[] fpLocalValues, float[] fLocalValues, float[] output)
	{
		int fLocalValueLength = fLocalValues.Length;
		checked
		{
			foreach (SlopingCellPoint mapPoint in cell.MapPoints)
			{
				int index = mapPoint.Index;
				float output2 = 0f;
				int count = fLocalValueLength - 1;
				for (int i = 0; i <= count; i++)
				{
					float fpLocalValue = fpLocalValues[i];
					output2 += fpLocalValue * mapPoint.VelocityWeights[i];
				}
				int count2 = fLocalValueLength - 1;
				for (int j = 0; j <= count2; j++)
				{
					float fLocalValue = fLocalValues[j];
					output2 += fLocalValue * mapPoint.VelocityWeights[fLocalValueLength + j];
				}
				output[index] = output2;
			}
		}
	}

	private static void PaintCell_8Stencil(SlopingCellMap cell, Vector2D[] fpLocalValues, Vector2D[] fLocalValues, float[] clipPixelDepths, float[] outputX, float[] outputY, float[] outputMagnitude)
	{
		int fLocalValueLength = fLocalValues.Length;
		checked
		{
			foreach (SlopingCellPoint mapPoint in cell.MapPoints)
			{
				int index = mapPoint.Index;
				if (clipPixelDepths == null || !(clipPixelDepths[index] <= 0f))
				{
					float outputX2 = 0f;
					float outputY2 = 0f;
					int count = fLocalValueLength - 1;
					for (int i = 0; i <= count; i++)
					{
						Vector2D vector2D = fpLocalValues[i];
						outputX2 += vector2D.X * mapPoint.VelocityWeights[i];
						outputY2 += vector2D.Y * mapPoint.VelocityWeights[i];
					}
					int count2 = fLocalValueLength - 1;
					for (int j = 0; j <= count2; j++)
					{
						Vector2D vector2D2 = fLocalValues[j];
						outputX2 += vector2D2.X * mapPoint.VelocityWeights[fLocalValueLength + j];
						outputY2 += vector2D2.Y * mapPoint.VelocityWeights[fLocalValueLength + j];
					}
					outputX[index] = outputX2;
					outputY[index] = outputY2;
					outputMagnitude[index] = (float)Math.Sqrt(outputX2 * outputX2 + outputY2 * outputY2);
				}
			}
		}
	}

	private static void PaintCell_4Stencil(SlopingCellMap cell, Vector2D[] fpLocalValues, float[] clipPixelDepths, float[] outputX, float[] outputY, float[] outputMagnitude)
	{
		int fpLocalValueLength = fpLocalValues.Length;
		checked
		{
			foreach (SlopingCellPoint mapPoint in cell.MapPoints)
			{
				int index = mapPoint.Index;
				if (!(clipPixelDepths[index] <= 0f))
				{
					float outputX2 = 0f;
					float outputY2 = 0f;
					int count = fpLocalValueLength - 1;
					for (int i = 0; i <= count; i++)
					{
						Vector2D vector2D = fpLocalValues[i];
						outputX2 += vector2D.X * mapPoint.FPPrevWeights[i];
						outputY2 += vector2D.Y * mapPoint.FPPrevWeights[i];
					}
					outputX[mapPoint.Index] = outputX2;
					outputY[mapPoint.Index] = outputY2;
					outputMagnitude[mapPoint.Index] = (float)Math.Sqrt(outputX2 * outputX2 + outputY2 * outputY2);
				}
			}
		}
	}

	private static void DownwardAdjustFPValues(float cellWS, float[] fpLocalValues)
	{
		float fpLocalValueSum = 0f;
		int fpLocalValueLength = fpLocalValues.Length;
		checked
		{
			int count = fpLocalValueLength - 1;
			for (int i = 0; i <= count; i++)
			{
				fpLocalValueSum += fpLocalValues[i];
			}
			fpLocalValueSum /= (float)fpLocalValueLength;
			float cellWSAboveFpLocalValueSumOverFpLocalValueLength = (cellWS - fpLocalValueSum) / (float)fpLocalValueLength;
			int count2 = fpLocalValueLength - 1;
			for (int j = 0; j <= count2; j++)
			{
				fpLocalValues[j] += cellWSAboveFpLocalValueSumOverFpLocalValueLength;
			}
		}
	}

	private static Vector2D[] GetLocalFacepointValues(MeshFV2D mesh, int c, FacePointVels[] fpVelocityRing)
	{
		Cell cell = mesh.Cell(c);
		int count = cell.Faces.Count;
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[count - 1 + 1];
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int ordinal = i;
				int faceIndex = cell.Faces[i];
				int fPPrev = mesh.Faces[faceIndex].GetFPPrev(c);
				vector2Ds[ordinal] = fpVelocityRing[fPPrev].Velocity[mesh.FacePoints[fPPrev].Faces.IndexOf(faceIndex)];
			}
			return vector2Ds;
		}
	}

	private static Vector2D[] GetLocalFaceValues(MeshFV2D mesh, int c, Tuple<Vector2D, Vector2D>[] faceTrueVels)
	{
		Cell cell = mesh.Cell(c);
		int count = cell.Faces.Count;
		checked
		{
			Vector2D[] vector2Ds = new Vector2D[count - 1 + 1];
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				int faceIndex = cell.Faces[i];
				if (c == mesh.Faces[faceIndex].cellA)
				{
					vector2Ds[i] = faceTrueVels[faceIndex].Item1;
				}
				else
				{
					vector2Ds[i] = faceTrueVels[faceIndex].Item2;
				}
			}
			return vector2Ds;
		}
	}

	private static void FacepointWeightedPaint(SlopingCellMap slopedCell, float[] fpLocalValues, float[] output, float[] clipPixelDepth = null)
	{
		checked
		{
			foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
			{
				if (clipPixelDepth != null && clipPixelDepth[mapPoint.Index] <= 0f)
				{
					continue;
				}
				float output2 = 0f;
				int ordinal = 0;
				int count = fpLocalValues.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					float fpLocalValue = fpLocalValues[i];
					if (fpLocalValue != -9999f)
					{
						ordinal++;
						output2 += mapPoint.FPPrevWeights[i] * fpLocalValue;
					}
				}
				if (ordinal > 0)
				{
					output[mapPoint.Index] = output2;
				}
			}
		}
	}

	private static void FacepointWeightedPaint(SlopingCellMap slopedCell, Vector2D[] fpLocalValues, Vector2D[] output, float[] clipPixelDepth = null)
	{
		checked
		{
			foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
			{
				if (clipPixelDepth != null && clipPixelDepth[mapPoint.Index] <= 0f)
				{
					continue;
				}
				Vector2D vector2D = default;
				int ordinal = 0;
				int count = fpLocalValues.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					Vector2D vector2D2 = fpLocalValues[i];
					if (vector2D2 != Vector2D.NoData)
					{
						ordinal++;
						vector2D += mapPoint.FPPrevWeights[i] * vector2D2;
					}
				}
				if (ordinal > 0)
				{
					output[mapPoint.Index] = vector2D;
				}
			}
		}
	}

	private static void FacepointWeightedPaintParallel(SlopingCellMap slopedCell, float[] fpLocalValues, float[] output, float[] clipPixelDepth = null)
	{
		_Closure_0024__58_002D0 capturedLocals = default;
		_Closure_0024__58_002D0 capturedLocals2 = new _Closure_0024__58_002D0(capturedLocals);
		capturedLocals2.slopedCell = slopedCell;
		capturedLocals2.fpLocalValues = fpLocalValues;
		capturedLocals2.output = output;
		capturedLocals2.clipPixelDepth = clipPixelDepth;
		Utility.Parallel.SmartFor(capturedLocals2.slopedCell.MapPoints.Count, checked([SpecialName] (int start, int finish) =>
		{
			for (int i = start; i <= finish; i++)
			{
				SlopingCellPoint slopingCellPoint = capturedLocals2.slopedCell.MapPoints[i];
				if (capturedLocals2.clipPixelDepth == null || !(capturedLocals2.clipPixelDepth[slopingCellPoint.Index] <= 0f))
				{
					float computed = 0f;
					int ordinal = 0;
					int count = capturedLocals2.fpLocalValues.Count() - 1;
					for (int j = 0; j <= count; j++)
					{
						float fpLocalValue = capturedLocals2.fpLocalValues[j];
						if (fpLocalValue != -9999f)
						{
							ordinal++;
							computed += slopingCellPoint.FPPrevWeights[j] * fpLocalValue;
						}
					}
					if (ordinal > 0)
					{
						capturedLocals2.output[slopingCellPoint.Index] = computed;
					}
				}
			}
		}), 256);
	}

	private static void SimplePaint<T>(FlatCellMap flatCell, T output2, T[] output, float[] clipPixelDepth)
	{
		if (clipPixelDepth != null)
		{
			{
				foreach (int mapPoint in flatCell.MapPoints)
				{
					if (clipPixelDepth[mapPoint] > 0f)
					{
						output[mapPoint] = output2;
					}
				}
				return;
			}
		}
		foreach (int mapPoint2 in flatCell.MapPoints)
		{
			output[mapPoint2] = output2;
		}
	}

	private static void SimplePaint(SlopingCellMap slopedCell, float output2, float[] output, float[] clipPixelDepth)
	{
		if (clipPixelDepth != null)
		{
			{
				foreach (SlopingCellPoint mapPoint in slopedCell.MapPoints)
				{
					if (clipPixelDepth[mapPoint.Index] > 0f)
					{
						output[mapPoint.Index] = output2;
					}
				}
				return;
			}
		}
		foreach (SlopingCellPoint mapPoint2 in slopedCell.MapPoints)
		{
			output[mapPoint2.Index] = output2;
		}
	}

	private static void SimplePaint(FlatCellMap flatCell, Vector2D vector2D, float[] outputX, float[] outputY, float[] outputMagnitude, float[] clipPixelDepth = null)
	{
		foreach (int mapPoint in flatCell.MapPoints)
		{
			if (clipPixelDepth == null || clipPixelDepth[mapPoint] > 0f)
			{
				float x = vector2D.X;
				float y = vector2D.Y;
				outputX[mapPoint] = x;
				outputY[mapPoint] = y;
				outputMagnitude[mapPoint] = (float)Math.Sqrt(x * x + y * y);
			}
		}
	}

	private void LoadGeometryData()
	{
		_geometry.XSInterpolationSurface.FeatureTable();
	}

	protected Names GetWaterSurfaceDatasetNames()
	{
		Names names = new Names();
		if (_result.UnsteadyHydraulics)
		{
			names.Max1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Water Surface";
			names.Max1DRow = 0;
			names.MaxSA = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Maximum Water Surface";
			names.MaxSARow = 0;
			names.Max2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MaxWaterSurface.Name(_d2FlowAreaNames);
			names.Max2DRow = 0;
			names.Min1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Water Surface";
			names.Min1DRow = 0;
			names.MinSA = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Minimum Water Surface";
			names.MinSARow = 0;
			names.Min2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MinWaterSurface.Name(_d2FlowAreaNames);
			names.Min2DRow = 0;
			names.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
			names.ProfileSA = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface";
			names.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(_d2FlowAreaNames);
		}
		else if (_result.SteadyHydraulics)
		{
			names.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";
			names.ProfileSA = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Storage Areas/Water Surface";
			names.Profile2D = null;
		}
		return names;
	}

	protected void GetVelocityDatasetNames(ref Names velNames, ref Names flowNames, ref Names wsNames)
	{
		velNames = new Names();
		flowNames = new Names();
		wsNames = new Names();
		if (_result.UnsteadyHydraulics)
		{
			velNames.Max1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Velocity Flow Distribution";
			velNames.Max1DRow = 0;
			wsNames.Max1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Water Surface";
			wsNames.Max1DRow = 0;
			velNames.Max2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MaxFaceVelocity.Name(_d2FlowAreaNames);
			velNames.Max2DRow = 0;
			wsNames.Max2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MaxWaterSurface.Name(_d2FlowAreaNames);
			wsNames.Max2DRow = 0;
			velNames.Min1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Velocity Flow Distribution";
			velNames.Min1DRow = 0;
			wsNames.Min1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Channel Velocity";
			wsNames.Min1DRow = 1;
			velNames.Min2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MinFaceVelocity.Name(_d2FlowAreaNames);
			velNames.Min2DRow = 0;
			wsNames.Min2D = ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MinWaterSurface.Name(_d2FlowAreaNames);
			wsNames.Min2DRow = 0;
			flowNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";
			wsNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
			wsNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(_d2FlowAreaNames);
			velNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceVelocity(_d2FlowAreaNames);
		}
		else if (_result.SteadyHydraulics)
		{
			wsNames.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";
			flowNames.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow";
		}
	}

	protected void GetVelocity1DDatasetNames(ref Names velChanNames, ref Names velTotalNames)
	{
		velChanNames = new Names();
		velTotalNames = new Names();
		if (_result.UnsteadyHydraulics)
		{
			velChanNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Velocity Channel";
			velTotalNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Velocity Total";
		}
	}

	protected void GetFlowDatasetNames(ref Names flowNames, ref Names wsNames)
	{
		flowNames = new Names();
		wsNames = new Names();
		if (_result.UnsteadyHydraulics)
		{
			flowNames.Max1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Flow";
			flowNames.Max1DRow = 0;
			wsNames.Max1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Flow";
			wsNames.Max1DRow = 1;
			flowNames.Min1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Flow";
			flowNames.Min1DRow = 0;
			wsNames.Min1D = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Flow";
			wsNames.Min1DRow = 1;
			flowNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";
			wsNames.Profile1D = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
		}
		else if (_result.SteadyHydraulics)
		{
			flowNames.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow";
			wsNames.Profile1D = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";
		}
	}

	protected void GetLPSurfaceDatasetNames(ref Names wsNames, ref Names lpUNames, ref Names lpVNames)
	{
		wsNames = new Names();
		lpUNames = new Names();
		lpVNames = new Names();
		if (_result.UnsteadyHydraulics)
		{
			wsNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(_d2FlowAreaNames);
			lpUNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceLaplacianU(_d2FlowAreaNames);
			lpVNames.Profile2D = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceLaplacianV(_d2FlowAreaNames);
		}
	}

	protected List<int> Get1RingFPsAroundFaces(MeshFV2D mesh, SlopingMeshMap slopedArea)
	{
		if (slopedArea == null || slopedArea.UniqueFaces.IsNullOrEmpty())
		{
			return null;
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (int uniqueFace in slopedArea.UniqueFaces)
		{
			Face face = mesh.Faces[uniqueFace];
			intSet.Add(face.fpA);
			intSet.Add(face.fpB);
		}
		return intSet.ToList();
	}

	protected void TimeSeriesInitialize<T>(RASGeometryMapPoints mps, T initialValue, ref T[][] rows) where T : struct
	{
		checked
		{
			if (rows == null || rows.Count() != mps.InputCount)
			{
				rows = new T[mps.InputCount - 1 + 1][];
			}
			int count = _result.ProfileNames.Count();
			int count2 = mps.InputCount - 1;
			for (int i = 0; i <= count2; i++)
			{
				Buffers.ArraySetAllVB(ref rows[i], initialValue, count);
			}
		}
	}

	protected float[][] ReadMappedMeshProfile(List<string> meshDatasets, int readRow, H5RowCache<float> rowCache, RASGeometryMapPoints mps = null)
	{
		if (meshDatasets == null || meshDatasets.Count == 0)
		{
			return null;
		}
		float[][] floatValues = new float[checked(meshDatasets.Count - 1 + 1)][];
		List<int> range = ((mps == null) ? Enumerable.Range(0, _d2FlowAreaNames.Count()).ToList() : mps.AllMappedMeshIndexes().ToList());
		foreach (int range2 in range)
		{
			floatValues[range2] = rowCache.EnsureGetRow(meshDatasets[range2], readRow);
		}
		return floatValues;
	}

	protected Dictionary<int, float[]> ReadMappedXSProfile(RASGeometryMapPoints mps, string dataset, H5RowCache<float> rowCache)
	{
		Dictionary<int, float[]> mappedXSProfile = new Dictionary<int, float[]>();
		foreach (MappedXSIS mappedXSIS in mps.MappedXSISs)
		{
			int iD = mappedXSIS.ID;
			int upstreamXSID = _XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = _XSIS.GetDownstreamXSID(iD);
			if (!mappedXSProfile.ContainsKey(upstreamXSID))
			{
				mappedXSProfile[upstreamXSID] = rowCache.EnsureGetRow(dataset, upstreamXSID);
			}
			if (!mappedXSProfile.ContainsKey(downstreamXSID))
			{
				mappedXSProfile[downstreamXSID] = rowCache.EnsureGetRow(dataset, downstreamXSID);
			}
		}
		return mappedXSProfile;
	}

	protected float[][] ReadMappedXSProfile(string dataset, H5RowCache<float> rowCache)
	{
		checked
		{
			float[][] mappedXSProfile = new float[_XS.FeatureCount() - 1 + 1][];
			int count = _XS.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				mappedXSProfile[i] = rowCache.EnsureGetRow(dataset, i);
			}
			return mappedXSProfile;
		}
	}

	protected List<long> ReadMappedXSProfileDimensions(string dataset)
	{
		List<long> longs = new List<long>();
		checked
		{
			using H5Reader h5Reader = new H5Reader(_result.SourceFilename);
			int count = _XS.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				long[] datasetDimensions = h5Reader.GetDatasetDimensions(dataset);
				longs[i] = datasetDimensions[0];
			}
			return longs;
		}
	}

	protected Dictionary<int, float[]> ReadMappedXSTimeseries(RASGeometryMapPoints mps, string dataset, H5ColumnCache<float> colCache)
	{
		Dictionary<int, float[]> mappedXSTimeseries = new Dictionary<int, float[]>();
		foreach (MappedXSIS mappedXSIS in mps.MappedXSISs)
		{
			int iD = mappedXSIS.ID;
			int upstreamXSID = _XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = _XSIS.GetDownstreamXSID(iD);
			if (!mappedXSTimeseries.ContainsKey(upstreamXSID))
			{
				mappedXSTimeseries[upstreamXSID] = colCache.EnsureGetColumn(dataset, upstreamXSID);
			}
			if (!mappedXSTimeseries.ContainsKey(downstreamXSID))
			{
				mappedXSTimeseries[downstreamXSID] = colCache.EnsureGetColumn(dataset, downstreamXSID);
			}
		}
		return mappedXSTimeseries;
	}

	public Dictionary<int, T[]> ReadTimeSeries<T>(string dataset, IEnumerable<int> columns, H5ColumnCache<T> colCache = null) where T : struct
	{
		if (colCache == null)
		{
			colCache = new H5ColumnCache<T>(_postProcessFilename);
		}
		Dictionary<int, T[]> timeSeries = new Dictionary<int, T[]>();
		foreach (int column in columns)
		{
			timeSeries[column] = colCache.EnsureGetColumn(dataset, column);
		}
		return timeSeries;
	}

	public T[] ReadTimeSeries<T>(string dataset, int column, H5ColumnCache<T> colCache = null) where T : struct
	{
		if (colCache == null)
		{
			colCache = new H5ColumnCache<T>(_postProcessFilename);
		}
		return colCache.EnsureGetColumn(dataset, column);
	}

	protected Dictionary<int, float[]> ReadMappedSATimeseries(RASGeometryMapPoints mps, string dataset, H5ColumnCache<float> colCache)
	{
		Dictionary<int, float[]> mappedSATimeseries = new Dictionary<int, float[]>();
		foreach (SAMap mappedSA in mps.MappedSAs)
		{
			mappedSATimeseries[mappedSA.ID] = colCache.EnsureGetColumn(dataset, mappedSA.ID);
		}
		return mappedSATimeseries;
	}

	protected Dictionary<int, Dictionary<int, float[]>> ReadMapped2DCellTimeseries(RASGeometryMapPoints mps, List<string> meshDatasets, H5ColumnCache<float> colCache)
	{
		Dictionary<int, Dictionary<int, float[]>> mapped2DCellTimeseries = new Dictionary<int, Dictionary<int, float[]>>();
		foreach (int allMappedMeshIndex in mps.AllMappedMeshIndexes())
		{
			Dictionary<int, float[]> mapped2DCellTimeseriesArray = new Dictionary<int, float[]>();
			foreach (FlatCellMap allMappedCell in mps.AllMappedCells(allMappedMeshIndex))
			{
				mapped2DCellTimeseriesArray[allMappedCell.Index] = colCache.EnsureGetColumn(meshDatasets[allMappedMeshIndex], allMappedCell.Index);
			}
			mapped2DCellTimeseries[allMappedMeshIndex] = mapped2DCellTimeseriesArray;
		}
		return mapped2DCellTimeseries;
	}

	protected Dictionary<int, Dictionary<int, float[]>> ReadMappedAndConnected2DCellTimeseries(RASGeometryMapPoints mps, List<string> meshDatasets, H5ColumnCache<float> colCache)
	{
		Dictionary<int, Dictionary<int, float[]>> mappedAndConnected2DCellTimeseries = new Dictionary<int, Dictionary<int, float[]>>();
		foreach (int allMappedMeshIndex in mps.AllMappedMeshIndexes())
		{
			MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(allMappedMeshIndex);
			Dictionary<int, float[]> mappedAndConnected2DCellTimeseriesArray = new Dictionary<int, float[]>();
			foreach (FlatCellMap allMappedCell in mps.AllMappedCells(allMappedMeshIndex))
			{
				int index = allMappedCell.Index;
				if (!mappedAndConnected2DCellTimeseriesArray.ContainsKey(index))
				{
					mappedAndConnected2DCellTimeseriesArray[index] = colCache.EnsureGetColumn(meshDatasets[allMappedMeshIndex], index);
				}
				foreach (int cellFacePoint in meshFV2D.CellFacePoints(index))
				{
					foreach (int face2 in meshFV2D.FacePoints[cellFacePoint].Faces)
					{
						Face face = meshFV2D.Faces[face2];
						int fpAFaceTerm = -1;
						fpAFaceTerm = ((face.fpA != cellFacePoint) ? face.cellB : face.cellA);
						if (fpAFaceTerm != -1 && !mappedAndConnected2DCellTimeseriesArray.ContainsKey(fpAFaceTerm))
						{
							mappedAndConnected2DCellTimeseriesArray[fpAFaceTerm] = colCache.EnsureGetColumn(meshDatasets[allMappedMeshIndex], fpAFaceTerm);
						}
					}
				}
				mappedAndConnected2DCellTimeseries[allMappedMeshIndex] = mappedAndConnected2DCellTimeseriesArray;
			}
		}
		return mappedAndConnected2DCellTimeseries;
	}

	protected Dictionary<int, Dictionary<int, float[]>> ReadMappedAndConnected2DFaceTimeseries(RASGeometryMapPoints mps, List<string> meshDatasets, H5ColumnCache<float> colCache)
	{
		Dictionary<int, Dictionary<int, float[]>> mappedAndConnected2DFaceTimeseries = new Dictionary<int, Dictionary<int, float[]>>();
		foreach (int allMappedMeshIndex in mps.AllMappedMeshIndexes())
		{
			MeshFV2D meshFV2D = _d2FlowArea.Meshes().ElementAtOrDefault(allMappedMeshIndex);
			Dictionary<int, float[]> mappedAndConnected2DFaceTimeseriesArray = new Dictionary<int, float[]>();
			foreach (FlatCellMap allMappedCell in mps.AllMappedCells(allMappedMeshIndex))
			{
				int[] cellFaces = meshFV2D.CellFaces(allMappedCell.Index);
				foreach (int index in cellFaces)
				{
					int fPNext = meshFV2D.Faces[index].GetFPNext(allMappedCell.Index);
					foreach (int face in meshFV2D.FacePoints[fPNext].Faces)
					{
						if (!mappedAndConnected2DFaceTimeseriesArray.ContainsKey(face))
						{
							mappedAndConnected2DFaceTimeseriesArray[face] = colCache.EnsureGetColumn(meshDatasets[allMappedMeshIndex], face);
						}
					}
				}
			}
			mappedAndConnected2DFaceTimeseries[allMappedMeshIndex] = mappedAndConnected2DFaceTimeseriesArray;
		}
		return mappedAndConnected2DFaceTimeseries;
	}

	protected void ReadMappedMeshProfileSubValues(List<string> meshDatasets, bool isSubcell, int readRow, H5RowCache<float> rowCache, ref float[][][] meshInitialElevs, ref float[][][] meshElevs, RASGeometryMapPoints mps = null, List<string> infoDSetPerMesh = null, string lengthAttributeDataset = null)
	{
		if (infoDSetPerMesh == null && lengthAttributeDataset == null)
		{
			throw new ArgumentException("Must pass in an info dataset or a length attribute, but not both");
		}
		if (infoDSetPerMesh != null && lengthAttributeDataset != null)
		{
			throw new ArgumentException("Info dataset and length attribute should be mutually exclusive");
		}
		float[][] mappedMeshProfVals = ReadMappedMeshProfile(meshDatasets, readRow, rowCache, mps);
		List<int> range = ((mps == null) ? Enumerable.Range(0, _d2FlowAreaNames.Count()).ToList() : mps.AllMappedMeshIndexes().ToList());
		int[][] intValues = new int[checked(meshDatasets.Count - 1 + 1)][];
		foreach (int range2 in range)
		{
			List<FlatCellMap> source = mps.AllMappedCells(range2).ToList();
			intValues[range2] = source.Select((_Closure_0024__._0024I81_002D0 == null) ? (_Closure_0024__._0024I81_002D0 = [SpecialName] (FlatCellMap mappedCell) => mappedCell.Index) : _Closure_0024__._0024I81_002D0).ToArray();
		}
		meshElevs = AuxillaryRenderer.ReadSubcellElevations(meshDatasets.Count, isSubcell, intValues, _result, infoDSetPerMesh, lengthAttributeDataset, mappedMeshProfVals, range, ref meshInitialElevs);
	}

	protected Dictionary<int, float[]> Read2DColumns(List<int> columns, string dataset, H5ColumnCache<float> colCache)
	{
		Dictionary<int, float[]> floatValuesByInt = new Dictionary<int, float[]>();
		foreach (int column in columns)
		{
			if (!floatValuesByInt.ContainsKey(column))
			{
				floatValuesByInt[column] = colCache.EnsureGetColumn(dataset, column);
			}
		}
		return floatValuesByInt;
	}

	protected void Compute1DFromSubsections(Dictionary<int, float[]> xsSubsectionValues, RASGeometryMapPoints mps, float[] clipDepths, float[] output)
	{
		_Closure_0024__83_002D0 capturedLocals = default;
		_Closure_0024__83_002D0 capturedLocals2 = new _Closure_0024__83_002D0(capturedLocals);
		capturedLocals2.renderer = this;
		capturedLocals2.xsSubsectionValues = xsSubsectionValues;
		capturedLocals2.clipDepths = clipDepths;
		capturedLocals2.output = output;
		System.Threading.Tasks.Parallel.ForEach(mps.MappedXSISs, [SpecialName] (MappedXSIS interpSurface) =>
		{
			_Closure_0024__83_002D1 capturedLocals3 = default;
			_Closure_0024__83_002D1 capturedLocals4 = new _Closure_0024__83_002D1(capturedLocals3);
			capturedLocals4.outerClosure = capturedLocals2;
			capturedLocals4.interpSurface = interpSurface;
			capturedLocals4.usXS = capturedLocals2.renderer._XSIS.GetUpstreamXSID(capturedLocals4.interpSurface.ID);
			capturedLocals4.dsXS = capturedLocals2.renderer._XSIS.GetDownstreamXSID(capturedLocals4.interpSurface.ID);
			capturedLocals4.usXSVels = capturedLocals2.xsSubsectionValues[capturedLocals4.usXS];
			capturedLocals4.dsXSVels = capturedLocals2.xsSubsectionValues[capturedLocals4.dsXS];
			capturedLocals4.usStationComputer = capturedLocals2.renderer._XS.GetStationComputer(capturedLocals4.usXS);
			capturedLocals4.dsStationComputer = capturedLocals2.renderer._XS.GetStationComputer(capturedLocals4.dsXS);
			Utility.Parallel.SmartFor(capturedLocals4.interpSurface.MapPoints.Count, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i = checked(i + 1))
				{
					XSISMapPoint xSISMapPoint = capturedLocals4.interpSurface.MapPoints[i];
					if (!(capturedLocals4.outerClosure.clipDepths[xSISMapPoint.Index] <= 0f))
					{
						float averagedMValue = capturedLocals4.outerClosure.renderer._flowDistribution.AveragedMValue(capturedLocals4.usXS, capturedLocals4.usStationComputer(xSISMapPoint.M), capturedLocals4.usXSVels);
						float averagedMValue2 = capturedLocals4.outerClosure.renderer._flowDistribution.AveragedMValue(capturedLocals4.dsXS, capturedLocals4.dsStationComputer(xSISMapPoint.M), capturedLocals4.dsXSVels);
						capturedLocals4.outerClosure.output[xSISMapPoint.Index] = averagedMValue * xSISMapPoint.Z + averagedMValue2 * (1f - xSISMapPoint.Z);
					}
				}
			});
		});
	}
}
