using System;
using System.Collections.Generic;

namespace Geospatial.Rendering.Systems.Vector;

public struct VectorID : IEquatable<VectorID>
{
	public class Comparer : IEqualityComparer<VectorID>
	{
		public bool Equals(VectorID x, VectorID y)
		{
			return x.ID == y.ID;
		}

		public int GetHashCode(VectorID obj)
		{
			return obj.GetHashCode();
		}
	}

	public const long NullVal = 0L;

	private long _id;

	public long ID => _id;

	public VectorID()
	{
		_id = 0L;
	}

	public VectorID(long id)
	{
		_id = id;
	}

	public bool IsNull()
	{
		return _id == 0;
	}

	public static implicit operator VectorID(long id)
	{
		return new VectorID(id);
	}

	public static implicit operator long(VectorID id)
	{
		return id._id;
	}

	public bool Equals(VectorID other)
	{
		return _id == other._id;
	}

	public override bool Equals(object obj)
	{
		if (obj is VectorID vectorID)
		{
			return _id == vectorID._id;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return _id.GetHashCode();
	}
}
