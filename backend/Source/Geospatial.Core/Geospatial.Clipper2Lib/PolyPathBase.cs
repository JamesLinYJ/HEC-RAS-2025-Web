using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

public abstract class PolyPathBase : IEnumerable
{
	private class NodeEnumerator : IEnumerator
	{
		private int position = -1;

		private readonly List<PolyPathBase> _nodes;

		public object Current
		{
			get
			{
				if (position < 0 || position >= _nodes.Count)
				{
					throw new InvalidOperationException();
				}
				return _nodes[position];
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public NodeEnumerator(List<PolyPathBase> nodes)
		{
			_nodes = new List<PolyPathBase>(nodes);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool MoveNext()
		{
			position++;
			return position < _nodes.Count;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Reset()
		{
			position = -1;
		}
	}

	internal PolyPathBase? _parent;

	internal List<PolyPathBase> _childs = new List<PolyPathBase>();

	public bool IsHole => GetIsHole();

	public int Level => GetLevel();

	public int Count => _childs.Count;

	public IEnumerator GetEnumerator()
	{
		return new NodeEnumerator(_childs);
	}

	public PolyPathBase(PolyPathBase? parent = null)
	{
		_parent = parent;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int GetLevel()
	{
		int num = 0;
		for (PolyPathBase parent = _parent; parent != null; parent = parent._parent)
		{
			num++;
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool GetIsHole()
	{
		int level = GetLevel();
		if (level != 0)
		{
			return (level & 1) == 0;
		}
		return false;
	}

	public abstract PolyPathBase AddChild(Path64 p);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear()
	{
		_childs.Clear();
	}

	internal string ToStringInternal(int idx, int level)
	{
		string text = "";
		string text2 = "";
		string value = "s";
		if (_childs.Count == 1)
		{
			value = "";
		}
		text2 = text2.PadLeft(level * 2);
		text = (((level & 1) != 0) ? (text + $"{text2}+- polygon ({idx}) contains {_childs.Count} hole{value}.\n") : (text + $"{text2}+- hole ({idx}) contains {_childs.Count} nested polygon{value}.\n"));
		for (int i = 0; i < Count; i++)
		{
			if (_childs[i].Count > 0)
			{
				text += _childs[i].ToStringInternal(i, level + 1);
			}
		}
		return text;
	}

	public override string ToString()
	{
		if (Level > 0)
		{
			return "";
		}
		string value = "s";
		if (_childs.Count == 1)
		{
			value = "";
		}
		string text = $"Polytree with {_childs.Count} polygon{value}.\n";
		for (int i = 0; i < Count; i++)
		{
			if (_childs[i].Count > 0)
			{
				text += _childs[i].ToStringInternal(i, 1);
			}
		}
		return text + "\n";
	}
}
