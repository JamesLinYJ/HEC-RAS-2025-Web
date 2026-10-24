using System;
using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

public class PolyPath64 : PolyPathBase
{
	public Path64? Polygon { get; private set; }

	public PolyPath64 this[int index]
	{
		get
		{
			if (index < 0 || index >= _childs.Count)
			{
				throw new InvalidOperationException();
			}
			return (PolyPath64)_childs[index];
		}
	}

	public PolyPath64(PolyPathBase? parent = null)
		: base(parent)
	{
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override PolyPathBase AddChild(Path64 p)
	{
		PolyPathBase polyPathBase = new PolyPath64(this);
		(polyPathBase as PolyPath64).Polygon = p;
		_childs.Add(polyPathBase);
		return polyPathBase;
	}

	public PolyPath64 Child(int index)
	{
		if (index < 0 || index >= _childs.Count)
		{
			throw new InvalidOperationException();
		}
		return (PolyPath64)_childs[index];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double Area()
	{
		double num = ((Polygon == null) ? 0.0 : Clipper.Area(Polygon));
		foreach (PolyPath64 child in _childs)
		{
			num += child.Area();
		}
		return num;
	}
}
