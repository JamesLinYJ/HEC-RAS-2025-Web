using System;
using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

public class PolyPathD : PolyPathBase
{
	internal double Scale { get; set; }

	public PathD? Polygon { get; private set; }

	[IndexerName("Child")]
	public PolyPathD this[int index]
	{
		get
		{
			if (index < 0 || index >= _childs.Count)
			{
				throw new InvalidOperationException();
			}
			return (PolyPathD)_childs[index];
		}
	}

	public PolyPathD(PolyPathBase? parent = null)
		: base(parent)
	{
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public override PolyPathBase AddChild(Path64 p)
	{
		PolyPathBase polyPathBase = new PolyPathD(this);
		(polyPathBase as PolyPathD).Scale = Scale;
		(polyPathBase as PolyPathD).Polygon = Clipper.ScalePathD(p, 1.0 / Scale);
		_childs.Add(polyPathBase);
		return polyPathBase;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public PolyPathBase AddChild(PathD p)
	{
		PolyPathBase polyPathBase = new PolyPathD(this);
		(polyPathBase as PolyPathD).Scale = Scale;
		(polyPathBase as PolyPathD).Polygon = p;
		_childs.Add(polyPathBase);
		return polyPathBase;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double Area()
	{
		double num = ((Polygon == null) ? 0.0 : Clipper.Area(Polygon));
		foreach (PolyPathD child in _childs)
		{
			num += child.Area();
		}
		return num;
	}
}
