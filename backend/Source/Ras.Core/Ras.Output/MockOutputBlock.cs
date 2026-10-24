using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using Geospatial.Vectors;
using Ras.Layers;

namespace Ras.Output;

public class MockOutputBlock : IHorizontalOutputBlock, IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps
{
	private readonly int _pfCount;

	private readonly List<string> _pfNames;

	private readonly float[][] _perMeshCellWs;

	private float[][] _perMeshFaceVel;

	public IList<string> ProfileNames => _pfNames;

	public string Name { get; set; }

	public Geometry Geometry { get; }

	public IReadOnlyList<DateTime> Times
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
		}
		remove
		{
		}
	}

	public static MockOutputBlock SingleMesh(Geometry geom, float[] cellws, float[] facevel)
	{
		float[][] floatValues = new float[1][];
		float[][] floatValuesArray = new float[1][];
		floatValues[0] = cellws;
		floatValuesArray[0] = facevel;
		return new MockOutputBlock(geom, floatValues, floatValuesArray);
	}

	public static MockOutputBlock FromVelocity(Geometry geom, float WSEL, Vector constVelocity)
	{
		int count = geom.FlowAreaLayer.Count;
		float[][] floatValues = new float[count][];
		float[][] floatValuesArray = new float[count][];
		for (int i = 0; i < count; i++)
		{
			Mesh mesh = geom.FlowAreaLayer[i].Mesh;
			floatValuesArray[i] = new float[mesh.FaceCount];
			floatValues[i] = Enumerable.Repeat(WSEL, mesh.CellCount).ToArray();
			for (int j = 0; j < mesh.FaceCount; j++)
			{
				double dot = mesh.FaceSegment(j).AsVector().Perpendicular(clockwise: true)
					.Dot(constVelocity);
				floatValuesArray[i][j] = (float)dot;
			}
		}
		return new MockOutputBlock(geom, floatValues, floatValuesArray);
	}

	public static MockOutputBlock FromVelocity(Geometry geom, float WSEL, Func<Point, Vector> getVelocity)
	{
		int count = geom.FlowAreaLayer.Count;
		float[][] floatValues = new float[count][];
		float[][] floatValuesArray = new float[count][];
		for (int i = 0; i < count; i++)
		{
			Mesh mesh = geom.FlowAreaLayer[i].Mesh;
			floatValuesArray[i] = new float[mesh.FaceCount];
			floatValues[i] = Enumerable.Repeat(WSEL, mesh.CellCount).ToArray();
			for (int j = 0; j < mesh.FaceCount; j++)
			{
				Segment segment = mesh.FaceSegment(j);
				Vector vector = segment.AsVector().Perpendicular(clockwise: true);
				Point midpoint = segment.Midpoint();
				Vector other = getVelocity(midpoint);
				double dot = vector.Dot(other);
				floatValuesArray[i][j] = (float)dot;
			}
		}
		return new MockOutputBlock(geom, floatValues, floatValuesArray);
	}

	public MockOutputBlock(Geometry geom, float[][] perMeshCellWs, float[][] perMeshFaceVel)
	{
		Geometry = geom;
		_pfCount = 1;
		_pfNames = (from c in Enumerable.Range(0, _pfCount)
			select c.ToString()).ToList();
		_perMeshCellWs = perMeshCellWs;
		_perMeshFaceVel = perMeshFaceVel;
	}

	public bool TryReadPerCrossSection(OutputVariable variable, int profile, out float[] values)
	{
		throw new NotImplementedException();
	}

	public bool TryReadPerStorageArea(OutputVariable variable, int profile, out float[,] values)
	{
		throw new NotImplementedException();
	}

	public bool TryReadPerMesh(OutputVariable variable, int profile, Memory<float>[] values)
	{
		if (variable == OutputVariables.Depth)
		{
			for (int i = 0; i < values.Length; i++)
			{
				if (!values[i].IsEmpty)
				{
					_perMeshCellWs[i].CopyTo(values[i]);
				}
			}
		}
		else
		{
			if (variable != OutputVariables.Velocity)
			{
				throw new NotImplementedException();
			}
			for (int j = 0; j < values.Length; j++)
			{
				if (!values[j].IsEmpty)
				{
					_perMeshFaceVel[j].CopyTo(values[j]);
				}
			}
		}
		return true;
	}
}
