using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Mesh;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class SlopingFactors
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__1_002D0
	{
		public FaceValues[] faceWS;

		public MeshFV2D activeMesh;

		public float[] cellWaterSurface;

		public MeshMap meshMap;

		public _Closure_0024__1_002D0(_Closure_0024__1_002D0 other)
		{
			if (other != null)
			{
				faceWS = other.faceWS;
				activeMesh = other.activeMesh;
				cellWaterSurface = other.cellWaterSurface;
				meshMap = other.meshMap;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				faceWS[i] = activeMesh.ComputeFaceWaterSurfaces(i, cellWaterSurface);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = meshMap.UniqueFaces[i];
				faceWS[uniqueFaceIndex] = activeMesh.ComputeFaceWaterSurfaces(uniqueFaceIndex, cellWaterSurface);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__2_002D0
	{
		public FaceValues[] faceWS;

		public MeshFV2D mesh;

		public float[] cellWaterSurface;

		public _Closure_0024__2_002D0(_Closure_0024__2_002D0 other)
		{
			if (other != null)
			{
				faceWS = other.faceWS;
				mesh = other.mesh;
				cellWaterSurface = other.cellWaterSurface;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				faceWS[i] = mesh.ComputeFaceWaterSurfaces(i, cellWaterSurface);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__4_002D0
	{
		public MeshMap meshMap;

		public FaceValues[] lFaceWS;

		public MeshFV2D activeMesh;

		public float[] cellWaterSurface;

		public float[][] lFacePointWS;

		public Func<int, Point2Double> faceApplicationPoint;

		public _Closure_0024__4_002D0(_Closure_0024__4_002D0 other)
		{
			if (other != null)
			{
				meshMap = other.meshMap;
				lFaceWS = other.lFaceWS;
				activeMesh = other.activeMesh;
				cellWaterSurface = other.cellWaterSurface;
				lFacePointWS = other.lFacePointWS;
				faceApplicationPoint = other.faceApplicationPoint;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = meshMap.UniqueFaces[i];
				lFaceWS[uniqueFaceIndex] = activeMesh.ComputeFaceWaterSurfaces(uniqueFaceIndex, cellWaterSurface);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFacePoint = meshMap.UniqueFacePoints[i];
				lFacePointWS[uniqueFacePoint] = activeMesh.ComputeFacePointWSs(uniqueFacePoint, lFaceWS, faceApplicationPoint);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__6_002D0
	{
		public FaceValues[] lFaceWS;

		public MeshFV2D activeMesh;

		public float[] cellWaterSurface;

		public float[][] lFacePointWS;

		public Func<int, Point2Double> faceApplicationPoint;

		public _Closure_0024__6_002D0(_Closure_0024__6_002D0 other)
		{
			if (other != null)
			{
				lFaceWS = other.lFaceWS;
				activeMesh = other.activeMesh;
				cellWaterSurface = other.cellWaterSurface;
				lFacePointWS = other.lFacePointWS;
				faceApplicationPoint = other.faceApplicationPoint;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				lFaceWS[i] = activeMesh.ComputeFaceWaterSurfaces(i, cellWaterSurface);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				lFacePointWS[i] = activeMesh.ComputeFacePointWSs(i, lFaceWS, faceApplicationPoint);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public SlopingMeshMap area;

		public FaceValues[] faceWS;

		public MeshFV2D activeMesh;

		public float[] cellWaterSurface;

		public float[] cellVariable;

		public FaceValues[] faceVar;

		public float[][] lFacepointValue;

		public Func<int, Point2Double> faceApplicationPoint;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				area = other.area;
				faceWS = other.faceWS;
				activeMesh = other.activeMesh;
				cellWaterSurface = other.cellWaterSurface;
				cellVariable = other.cellVariable;
				faceVar = other.faceVar;
				lFacepointValue = other.lFacepointValue;
				faceApplicationPoint = other.faceApplicationPoint;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = area.UniqueFaces[i];
				faceWS[uniqueFaceIndex] = activeMesh.ComputeFaceWaterSurfaces(uniqueFaceIndex, cellWaterSurface);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			FaceValues faceValues2 = default;
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = area.UniqueFaces[i];
				Face face = activeMesh.Faces[uniqueFaceIndex];
				FaceValues faceValues = faceWS[uniqueFaceIndex];
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
				faceVar[uniqueFaceIndex] = faceValues2;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFacePoint = area.UniqueFacePoints[i];
				lFacepointValue[uniqueFacePoint] = activeMesh.ComputeFacePointWSs(uniqueFacePoint, faceVar, faceApplicationPoint);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				faceWS[i] = activeMesh.ComputeFaceWaterSurfaces(i, cellWaterSurface);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4(int start, int finish)
		{
			FaceValues faceValues2 = default;
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				Face face = activeMesh.Faces[i];
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
		}

		[SpecialName]
		internal void _Lambda_0024__5(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				lFacepointValue[i] = activeMesh.ComputeFacePointWSs(i, faceVar, faceApplicationPoint);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__8_002D0
	{
		public SlopingMeshMap area;

		public MeshFV2D activeMesh;

		public float[] faceVar;

		public float[] cellVariable;

		public float[] lFacepointValue;

		public _Closure_0024__8_002D0(_Closure_0024__8_002D0 other)
		{
			if (other != null)
			{
				area = other.area;
				activeMesh = other.activeMesh;
				faceVar = other.faceVar;
				cellVariable = other.cellVariable;
				lFacepointValue = other.lFacepointValue;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFaceIndex = area.UniqueFaces[i];
				Face face = activeMesh.Faces[uniqueFaceIndex];
				faceVar[uniqueFaceIndex] = (cellVariable[face.cellA] + cellVariable[face.cellB]) / 2f;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				int uniqueFacePoint = area.UniqueFacePoints[i];
				FacePoint facePoint = activeMesh.FacePoints[uniqueFacePoint];
				float faceVarSum = 0f;
				foreach (int face in facePoint.Faces)
				{
					faceVarSum += faceVar[face];
				}
				lFacepointValue[uniqueFacePoint] = faceVarSum / (float)facePoint.Faces.Count;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				Face face = activeMesh.Faces[i];
				faceVar[i] = (cellVariable[face.cellA] + cellVariable[face.cellB]) / 2f;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__3(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				FacePoint facePoint = activeMesh.FacePoints[i];
				float faceVarSum = 0f;
				foreach (int face in facePoint.Faces)
				{
					faceVarSum += faceVar[face];
				}
				lFacepointValue[i] = faceVarSum / (float)facePoint.Faces.Count;
			}
		}
	}

	public static FaceValues[] ComputeSlopingWSFaceFactors(RASD2FlowArea d2FlowArea, float[] cellWaterSurface, MeshMap meshMap)
	{
		_Closure_0024__1_002D0 capturedLocals = default;
		_Closure_0024__1_002D0 capturedLocals2 = new _Closure_0024__1_002D0(capturedLocals);
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		capturedLocals2.meshMap = meshMap;
		capturedLocals2.activeMesh = d2FlowArea.Meshes().ElementAtOrDefault(capturedLocals2.meshMap.MeshIndex);
		checked
		{
			capturedLocals2.faceWS = new FaceValues[capturedLocals2.activeMesh.FaceCount - 1 + 1];
			if (capturedLocals2.meshMap.UniqueFaces.IsNullOrEmpty())
			{
				Parallel.SmartFor(capturedLocals2.activeMesh.FaceCount, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						capturedLocals2.faceWS[i] = capturedLocals2.activeMesh.ComputeFaceWaterSurfaces(i, capturedLocals2.cellWaterSurface);
					}
				}, 1000);
			}
			else
			{
				Parallel.SmartFor(capturedLocals2.meshMap.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.meshMap.UniqueFaces[i];
						capturedLocals2.faceWS[uniqueFaceIndex] = capturedLocals2.activeMesh.ComputeFaceWaterSurfaces(uniqueFaceIndex, capturedLocals2.cellWaterSurface);
					}
				}, 1000);
			}
			return capturedLocals2.faceWS;
		}
	}

	public static FaceValues[] ComputeSlopingWSFaceFactors(MeshFV2D mesh, float[] cellWaterSurface)
	{
		_Closure_0024__2_002D0 capturedLocals = default;
		_Closure_0024__2_002D0 capturedLocals2 = new _Closure_0024__2_002D0(capturedLocals);
		capturedLocals2.mesh = mesh;
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		checked
		{
			capturedLocals2.faceWS = new FaceValues[capturedLocals2.mesh.FaceCount - 1 + 1];
			Parallel.SmartFor(capturedLocals2.mesh.FaceCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.faceWS[i] = capturedLocals2.mesh.ComputeFaceWaterSurfaces(i, capturedLocals2.cellWaterSurface);
				}
			}, 1000);
			return capturedLocals2.faceWS;
		}
	}

	public static List<FaceValues> ComputeSlopingWSFaceFactors(MeshFV2D mesh, Dictionary<int, float> cellWaterSurface, List<int> facesToCompute)
	{
		List<FaceValues> faceValues = new List<FaceValues>();
		foreach (int facesToCompute2 in facesToCompute)
		{
			faceValues.Add(mesh.ComputeFaceWaterSurfaces(facesToCompute2, cellWaterSurface));
		}
		return faceValues;
	}

	public static void ComputeSlopingWSFPValues(RASD2FlowArea d2FlowArea, MeshMap meshMap, float[] cellWaterSurface, ref FaceValues[] faceWS, ref float[][] facePointValues)
	{
		_Closure_0024__4_002D0 capturedLocals = default;
		_Closure_0024__4_002D0 capturedLocals2 = new _Closure_0024__4_002D0(capturedLocals);
		capturedLocals2.meshMap = meshMap;
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		int meshIndex = capturedLocals2.meshMap.MeshIndex;
		capturedLocals2.activeMesh = d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		capturedLocals2.faceApplicationPoint = d2FlowArea.GetFaceLocationFunc(meshIndex);
		checked
		{
			if (faceWS == null || faceWS.Length != capturedLocals2.activeMesh.FaceCount)
			{
				faceWS = new FaceValues[capturedLocals2.activeMesh.FaceCount - 1 + 1];
			}
			if (facePointValues == null || facePointValues.Length != capturedLocals2.activeMesh.FacePointCount)
			{
				facePointValues = new float[capturedLocals2.activeMesh.FacePointCount - 1 + 1][];
			}
			capturedLocals2.lFaceWS = faceWS;
			capturedLocals2.lFacePointWS = facePointValues;
			if (!capturedLocals2.meshMap.UniqueFacePoints.IsNullOrEmpty() && !capturedLocals2.meshMap.UniqueFaces.IsNullOrEmpty())
			{
				Parallel.SmartFor(capturedLocals2.meshMap.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.meshMap.UniqueFaces[i];
						capturedLocals2.lFaceWS[uniqueFaceIndex] = capturedLocals2.activeMesh.ComputeFaceWaterSurfaces(uniqueFaceIndex, capturedLocals2.cellWaterSurface);
					}
				}, 100);
				Parallel.SmartFor(capturedLocals2.meshMap.UniqueFacePoints.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFacePoint = capturedLocals2.meshMap.UniqueFacePoints[i];
						capturedLocals2.lFacePointWS[uniqueFacePoint] = capturedLocals2.activeMesh.ComputeFacePointWSs(uniqueFacePoint, capturedLocals2.lFaceWS, capturedLocals2.faceApplicationPoint);
					}
				}, 100);
			}
			else
			{
				ComputeSlopingWSFacePointValues(d2FlowArea, capturedLocals2.meshMap.MeshIndex, capturedLocals2.cellWaterSurface, ref faceWS, ref facePointValues);
			}
		}
	}

	public static void ComputeAllSlopingWSFacePointValues(RASD2FlowArea d2FlowArea, float[][] meshCellWaterSurfaces, ref FaceValues[][] meshFaceWS, ref float[][][] meshFacePointValues)
	{
		checked
		{
			meshFaceWS = new FaceValues[d2FlowArea.MeshCount - 1 + 1][];
			meshFacePointValues = new float[d2FlowArea.MeshCount - 1 + 1][][];
			int count = d2FlowArea.MeshCount - 1;
			for (int i = 0; i <= count; i++)
			{
				FaceValues[] faceWS = null;
				float[][] facePointValues = null;
				ComputeSlopingWSFacePointValues(d2FlowArea, i, meshCellWaterSurfaces[i], ref faceWS, ref facePointValues);
				meshFaceWS[i] = faceWS;
				meshFacePointValues[i] = facePointValues;
			}
		}
	}

	public static void ComputeSlopingWSFacePointValues(RASD2FlowArea d2FlowArea, int meshIdx, float[] cellWaterSurface, ref FaceValues[] faceWS, ref float[][] facePointValues)
	{
		_Closure_0024__6_002D0 capturedLocals = default;
		_Closure_0024__6_002D0 capturedLocals2 = new _Closure_0024__6_002D0(capturedLocals);
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		capturedLocals2.activeMesh = d2FlowArea.Meshes().ElementAtOrDefault(meshIdx);
		capturedLocals2.faceApplicationPoint = d2FlowArea.GetFaceLocationFunc(meshIdx);
		checked
		{
			if (faceWS == null || faceWS.Length != capturedLocals2.activeMesh.FaceCount)
			{
				faceWS = new FaceValues[capturedLocals2.activeMesh.FaceCount - 1 + 1];
			}
			if (facePointValues == null || facePointValues.Length != capturedLocals2.activeMesh.FacePointCount)
			{
				facePointValues = new float[capturedLocals2.activeMesh.FacePointCount - 1 + 1][];
			}
			capturedLocals2.lFaceWS = faceWS;
			capturedLocals2.lFacePointWS = facePointValues;
			Parallel.SmartFor(capturedLocals2.activeMesh.FaceCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.lFaceWS[i] = capturedLocals2.activeMesh.ComputeFaceWaterSurfaces(i, capturedLocals2.cellWaterSurface);
				}
			}, 1000);
			Parallel.SmartFor(capturedLocals2.activeMesh.FacePointCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.lFacePointWS[i] = capturedLocals2.activeMesh.ComputeFacePointWSs(i, capturedLocals2.lFaceWS, capturedLocals2.faceApplicationPoint);
				}
			}, 1000);
		}
	}

	public static void ComputeFacepointValues_SimpleHydraulicConnectivity(RASD2FlowArea d2FlowArea, SlopingMeshMap area, float[] cellWaterSurface, float[] cellVariable, ref float[][] facePointValues)
	{
		_Closure_0024__7_002D0 capturedLocals = default;
		_Closure_0024__7_002D0 capturedLocals2 = new _Closure_0024__7_002D0(capturedLocals);
		capturedLocals2.area = area;
		capturedLocals2.cellWaterSurface = cellWaterSurface;
		capturedLocals2.cellVariable = cellVariable;
		int meshIndex = capturedLocals2.area.MeshIndex;
		capturedLocals2.activeMesh = d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		capturedLocals2.faceApplicationPoint = d2FlowArea.GetFaceLocationFunc(meshIndex);
		checked
		{
			capturedLocals2.faceVar = new FaceValues[capturedLocals2.activeMesh.FaceCount - 1 + 1];
			capturedLocals2.faceWS = new FaceValues[capturedLocals2.activeMesh.FaceCount - 1 + 1];
			if (facePointValues == null || facePointValues.Length != capturedLocals2.activeMesh.FacePointCount)
			{
				facePointValues = new float[capturedLocals2.activeMesh.FacePointCount - 1 + 1][];
			}
			capturedLocals2.lFacepointValue = facePointValues;
			if (!capturedLocals2.area.UniqueFacePoints.IsNullOrEmpty() && !capturedLocals2.area.UniqueFaces.IsNullOrEmpty())
			{
				Parallel.SmartFor(capturedLocals2.area.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.area.UniqueFaces[i];
						capturedLocals2.faceWS[uniqueFaceIndex] = capturedLocals2.activeMesh.ComputeFaceWaterSurfaces(uniqueFaceIndex, capturedLocals2.cellWaterSurface);
					}
				}, 100);
				Parallel.SmartFor(capturedLocals2.area.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					FaceValues faceValues2 = default;
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex2 = capturedLocals2.area.UniqueFaces[i];
						Face face = capturedLocals2.activeMesh.Faces[uniqueFaceIndex2];
						FaceValues faceValues = capturedLocals2.faceWS[uniqueFaceIndex2];
						if (faceValues.IsHydraulicallyConnected)
						{
							faceValues2.HydraulicConnection = HydraulicConnection.DownhillDeep;
							faceValues2.ValueB = (faceValues2.ValueA = (capturedLocals2.cellVariable[face.cellA] + capturedLocals2.cellVariable[face.cellB]) / 2f);
						}
						else
						{
							faceValues2.HydraulicConnection = HydraulicConnection.None;
							faceValues2.ValueA = capturedLocals2.cellVariable[face.cellA];
							faceValues2.ValueB = capturedLocals2.cellVariable[face.cellB];
						}
						capturedLocals2.faceVar[uniqueFaceIndex2] = faceValues2;
					}
				}, 100);
				Parallel.SmartFor(capturedLocals2.area.UniqueFacePoints.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFacePoint = capturedLocals2.area.UniqueFacePoints[i];
						capturedLocals2.lFacepointValue[uniqueFacePoint] = capturedLocals2.activeMesh.ComputeFacePointWSs(uniqueFacePoint, capturedLocals2.faceVar, capturedLocals2.faceApplicationPoint);
					}
				}, 100);
				return;
			}
			Parallel.SmartFor(capturedLocals2.activeMesh.FaceCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.faceWS[i] = capturedLocals2.activeMesh.ComputeFaceWaterSurfaces(i, capturedLocals2.cellWaterSurface);
				}
			}, 100);
			Parallel.SmartFor(capturedLocals2.activeMesh.FaceCount, [SpecialName] (int start, int finish) =>
			{
				FaceValues faceValues2 = default;
				for (int i = start; i <= finish; i++)
				{
					Face face = capturedLocals2.activeMesh.Faces[i];
					FaceValues faceValues = capturedLocals2.faceWS[i];
					if (faceValues.IsHydraulicallyConnected)
					{
						faceValues2.HydraulicConnection = HydraulicConnection.DownhillDeep;
						faceValues2.ValueB = (faceValues2.ValueA = (capturedLocals2.cellVariable[face.cellA] + capturedLocals2.cellVariable[face.cellB]) / 2f);
					}
					else
					{
						faceValues2.HydraulicConnection = HydraulicConnection.None;
						faceValues2.ValueA = capturedLocals2.cellVariable[face.cellA];
						faceValues2.ValueB = capturedLocals2.cellVariable[face.cellB];
					}
					capturedLocals2.faceVar[i] = faceValues2;
				}
			}, 100);
			Parallel.SmartFor(capturedLocals2.activeMesh.FacePointCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					capturedLocals2.lFacepointValue[i] = capturedLocals2.activeMesh.ComputeFacePointWSs(i, capturedLocals2.faceVar, capturedLocals2.faceApplicationPoint);
				}
			}, 100);
		}
	}

	public static void ComputeFacepointValues_NoHydraulicConnectivity(RASD2FlowArea d2FlowArea, SlopingMeshMap area, float[] cellVariable, ref float[] facePointValues)
	{
		_Closure_0024__8_002D0 capturedLocals = default;
		_Closure_0024__8_002D0 capturedLocals2 = new _Closure_0024__8_002D0(capturedLocals);
		capturedLocals2.area = area;
		capturedLocals2.cellVariable = cellVariable;
		int meshIndex = capturedLocals2.area.MeshIndex;
		capturedLocals2.activeMesh = d2FlowArea.Meshes().ElementAtOrDefault(meshIndex);
		d2FlowArea.GetFaceLocationFunc(meshIndex);
		checked
		{
			capturedLocals2.faceVar = new float[capturedLocals2.activeMesh.FaceCount - 1 + 1];
			if (facePointValues == null || facePointValues.Length != capturedLocals2.activeMesh.FacePointCount)
			{
				facePointValues = new float[capturedLocals2.activeMesh.FacePointCount - 1 + 1];
			}
			capturedLocals2.lFacepointValue = facePointValues;
			if (!capturedLocals2.area.UniqueFacePoints.IsNullOrEmpty() && !capturedLocals2.area.UniqueFaces.IsNullOrEmpty())
			{
				Parallel.SmartFor(capturedLocals2.area.UniqueFaces.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFaceIndex = capturedLocals2.area.UniqueFaces[i];
						Face face = capturedLocals2.activeMesh.Faces[uniqueFaceIndex];
						capturedLocals2.faceVar[uniqueFaceIndex] = (capturedLocals2.cellVariable[face.cellA] + capturedLocals2.cellVariable[face.cellB]) / 2f;
					}
				}, 100);
				Parallel.SmartFor(capturedLocals2.area.UniqueFacePoints.Count, [SpecialName] (int start, int finish) =>
				{
					for (int i = start; i <= finish; i++)
					{
						int uniqueFacePoint = capturedLocals2.area.UniqueFacePoints[i];
						FacePoint facePoint = capturedLocals2.activeMesh.FacePoints[uniqueFacePoint];
						float computed = 0f;
						foreach (int face2 in facePoint.Faces)
						{
							computed += capturedLocals2.faceVar[face2];
						}
						capturedLocals2.lFacepointValue[uniqueFacePoint] = computed / (float)facePoint.Faces.Count;
					}
				}, 100);
				return;
			}
			Parallel.SmartFor(capturedLocals2.activeMesh.FaceCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					Face face = capturedLocals2.activeMesh.Faces[i];
					capturedLocals2.faceVar[i] = (capturedLocals2.cellVariable[face.cellA] + capturedLocals2.cellVariable[face.cellB]) / 2f;
				}
			}, 100);
			Parallel.SmartFor(capturedLocals2.activeMesh.FacePointCount, [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					FacePoint facePoint = capturedLocals2.activeMesh.FacePoints[i];
					float computed2 = 0f;
					foreach (int face3 in facePoint.Faces)
					{
						computed2 += capturedLocals2.faceVar[face3];
					}
					capturedLocals2.lFacepointValue[i] = computed2 / (float)facePoint.Faces.Count;
				}
			}, 100);
		}
	}
}
