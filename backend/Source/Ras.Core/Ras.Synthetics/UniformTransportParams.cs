using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public abstract class UniformTransportParams : BaseParams
{
	public abstract int CellsX { get; }

	public abstract int CellsY { get; }

	public abstract double DomainLengthX { get; }

	public abstract double DomainLengthY { get; }

	public virtual double FlowDepth => 1.0;

	public virtual double VelocityX => 0.0;

	public virtual double VelocityY => 0.0;

	public override Extent SystemExtent => new Extent(0.0, DomainLengthX, 0.0, DomainLengthY);

	public Vector Velocity => new Vector(VelocityX, VelocityY);

	public override float TerrainElevation(Point point)
	{
		return 0f;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point _) => (float)FlowDepth;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point _, double _) => (float)FlowDepth;
	}

	public override Func<Point, Vector> InitialVelocityFunction()
	{
		return (Point _) => Velocity;
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point _, double _) => Velocity;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => (float)VelocityX;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => (float)VelocityY;
	}

	public override Func<Point, double, float> AnalyticalVelocityXFunction()
	{
		return (Point _, double _) => (float)VelocityX;
	}

	public override Func<Point, double, float> AnalyticalVelocityYFunction()
	{
		return (Point _, double _) => (float)VelocityY;
	}

	public override Mesh CreateMesh()
	{
		return CreateMesh(CellsX, CellsY);
	}

	public Mesh CreateMesh(int cellsX, int cellsY)
	{
		return MeshGenerator.FromExtent(SystemExtent, cellsX, cellsY);
	}

	public TransportFlowField CreateTransportFlowField(Mesh mesh)
	{
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		double[] doubleValues = new double[mesh.FaceCount];
		double[] doubleValuesArray = new double[mesh.FaceCount];
		double[] doubleValues2 = new double[mesh.CellCount];
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			double flowDepthFaceSimpleLengthTerm = (doubleValuesArray[i] = FlowDepth * mesh.FaceSimpleLength(i));
			Vector vector = metadataCache.FaceNormal[i];
			doubleValues[i] = (VelocityX * vector.DeltaX + VelocityY * vector.DeltaY) * flowDepthFaceSimpleLengthTerm;
		}
		for (int j = 0; j < mesh.CellCount; j++)
		{
			doubleValues2[j] = FlowDepth * metadataCache.CellAreas[j];
		}
		return TransportFlowField.Steady(doubleValues, doubleValuesArray, doubleValues2);
	}

	public int[] InflowFaces(Mesh mesh)
	{
		HydraulicMeshComputer metadataCache = mesh.MetadataCache;
		List<int> intList = new List<int>();
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			if (mesh.Faces[i].cellB < 0)
			{
				Vector vector = metadataCache.FaceNormal[i];
				if (VelocityX * vector.DeltaX + VelocityY * vector.DeltaY < 0.0)
				{
					intList.Add(i);
				}
			}
		}
		return intList.ToArray();
	}

	public Vector[] CreateCellVelocity(Mesh mesh)
	{
		Vector[] vectors = new Vector[mesh.CellCount];
		for (int i = 0; i < vectors.Length; i++)
		{
			vectors[i] = Velocity;
		}
		return vectors;
	}
}
