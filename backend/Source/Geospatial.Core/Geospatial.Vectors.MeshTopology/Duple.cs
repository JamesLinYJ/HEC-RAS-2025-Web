using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Geospatial.Vectors.MeshTopology;

[DebuggerDisplay("{DebugString()}")]
public struct Duple<T> : IEnumerable<T>, IEnumerable where T : IEquatable<T>
{
	[JsonInclude]
	public T A;

	[JsonInclude]
	public T B;

	public T this[int idx]
	{
		get
		{
			return idx switch
			{
				0 => A, 
				1 => B, 
				_ => throw new IndexOutOfRangeException(), 
			};
		}
		set
		{
			switch (idx)
			{
			case 0:
				A = value;
				break;
			case 1:
				B = value;
				break;
			default:
				throw new IndexOutOfRangeException();
			}
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		yield return A;
		yield return B;
	}

	public Duple(Duple<T> other)
	{
		A = other.A;
		B = other.B;
	}

	public Duple<T> Reverse()
	{
		return new Duple<T>(B, A);
	}

	public Duple(T A, T B)
	{
		this.A = A;
		this.B = B;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public bool Contains(T localValue)
	{
		if (!A.Equals(localValue))
		{
			return B.Equals(localValue);
		}
		return true;
	}

	public static implicit operator Duple<T>(List<T> items)
	{
		return new Duple<T>
		{
			A = items[0],
			B = items[1]
		};
	}

	public string DebugString()
	{
		return "[" + A.ToString() + ", " + B.ToString() + "]";
	}
}
