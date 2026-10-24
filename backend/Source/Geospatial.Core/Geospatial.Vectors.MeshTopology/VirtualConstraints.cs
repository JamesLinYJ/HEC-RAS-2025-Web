using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Geospatial.Vectors.MeshTopology;

public class VirtualConstraints
{
	public enum ConstraintMode
	{
		NULL = -1,
		FIXED,
		SLIDE_ADD_REMOVE
	}

	public struct Constraint
	{
		public Polyline Polyline { get; private set; }

		public int ArcIdx { get; private set; }

		public ConstraintMode mode { get; private set; }

		public Constraint(Polyline polyline, int arcIdx, ConstraintMode mode = ConstraintMode.FIXED)
		{
			this.mode = ConstraintMode.FIXED;
			Polyline = polyline;
			ArcIdx = arcIdx;
			this.mode = mode;
		}

		public Constraint(Constraint c)
		{
			mode = ConstraintMode.FIXED;
			Polyline = c.Polyline;
			ArcIdx = c.ArcIdx;
			mode = c.mode;
		}

		public Constraint WithMode(ConstraintMode mode)
		{
			Constraint result = new Constraint(this);
			result.mode = mode;
			return result;
		}

		public Constraint WithArcIdx(int arcIdx)
		{
			Constraint result = new Constraint(this);
			result.ArcIdx = arcIdx;
			return result;
		}
	}

	private int cachedConstraint = -1;

	private double[] cachedSegLengths;

	protected List<Constraint> constraints;

	public int Count => constraints.Count;

	public Constraint this[int n] => constraints[n];

	public VirtualConstraints()
	{
		constraints = new List<Constraint>();
	}

	public void Clear()
	{
		constraints.Clear();
	}

	public int Add(Polyline pline, int arcIdx = -1, ConstraintMode mode = ConstraintMode.FIXED)
	{
		return Add(new Constraint(pline, arcIdx, mode));
	}

	public int Add(Constraint constraint)
	{
		constraints.Add(constraint);
		return constraints.Count - 1;
	}

	public int FindOrAdd(Polyline pline, int arcIdx)
	{
		for (int i = 0; i < constraints.Count; i++)
		{
			if (arcIdx == constraints[i].ArcIdx)
			{
				return i;
			}
		}
		return Add(pline, arcIdx);
	}

	public void SetMode(int n, ConstraintMode mode)
	{
		constraints[n] = constraints[n].WithMode(mode);
	}

	public void RemapArcs(List<int> arcMap)
	{
		for (int i = 0; i < constraints.Count; i++)
		{
			Constraint constraint = constraints[i];
			constraints[i] = constraint.WithArcIdx(arcMap[constraint.ArcIdx]);
		}
	}

	public void ReplaceConstraint(int i, Constraint constraint)
	{
		constraints[i] = constraint;
		if (cachedConstraint == i)
		{
			cachedConstraint = -1;
			cachedSegLengths = null;
		}
	}

	public double DistanceAlong(int i, PointOnPolyline pop)
	{
		if (cachedConstraint != i)
		{
			cachedSegLengths = null;
		}
		double result = constraints[i].Polyline.DistanceAlongFast(pop, ref cachedSegLengths);
		cachedConstraint = i;
		return result;
	}

	public double DistanceAlong(int i, PointOnPolyline pop, ref double[] segLengths)
	{
		double result = DistanceAlong(i, pop);
		segLengths = cachedSegLengths;
		return result;
	}

	public List<List<object>> SimpleRepr()
	{
		return constraints.Select((Constraint con) => new List<object>
		{
			DynamicMesh.SerializePolyline(con.Polyline),
			con.ArcIdx
		}).ToList();
	}

	public void UpdateFromJson(JsonElement elt)
	{
		foreach (JsonElement enumerateArray in elt.EnumerateArray())
		{
			Polyline polyline = DynamicMesh.DeserializePolyline(enumerateArray[0]);
			int arcIdx = (int)enumerateArray[1].GetInt64();
			constraints.Add(new Constraint(polyline, arcIdx));
		}
	}
}
