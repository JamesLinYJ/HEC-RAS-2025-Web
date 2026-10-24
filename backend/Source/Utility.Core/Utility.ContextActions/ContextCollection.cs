using System.Collections;
using System.Collections.Generic;
using Utility.DataStructures;

namespace Utility.ContextActions;

public class ContextCollection : ContextBase, IContextCollection, IContextBase, IList<IContextBase>, ICollection<IContextBase>, IEnumerable<IContextBase>, IEnumerable
{
	public bool FlattenHierarchy { get; set; }

	public bool SeparateWhenFlattened { get; set; }

	public bool ShowHeaderWhenFlattened { get; set; }

	public AdvancedCollection<IContextBase> ContextElements { get; private set; }

	public int Count => ((ICollection<IContextBase>)ContextElements).Count;

	public bool IsReadOnly => ((ICollection<IContextBase>)ContextElements).IsReadOnly;

	public IContextBase this[int index]
	{
		get
		{
			return ((IList<IContextBase>)ContextElements)[index];
		}
		set
		{
			((IList<IContextBase>)ContextElements)[index] = value;
		}
	}

	public ContextCollection(string name, bool enabled, bool flatten, bool separateWhenFlattened = false, bool showHeaderWhenFlattened = true)
		: base(name, enabled)
	{
		ContextElements = new AdvancedCollection<IContextBase>();
		FlattenHierarchy = flatten;
		SeparateWhenFlattened = separateWhenFlattened;
		ShowHeaderWhenFlattened = showHeaderWhenFlattened;
	}

	public int IndexOf(IContextBase iContextBase)
	{
		return ((IList<IContextBase>)ContextElements).IndexOf(iContextBase);
	}

	public void Insert(int index, IContextBase iContextBase)
	{
		((IList<IContextBase>)ContextElements).Insert(index, iContextBase);
	}

	public void RemoveAt(int index)
	{
		((IList<IContextBase>)ContextElements).RemoveAt(index);
	}

	public void Add(IContextBase iContextBase)
	{
		((ICollection<IContextBase>)ContextElements).Add(iContextBase);
	}

	public void AddRange(IList<IContextBase> items)
	{
		foreach (IContextBase iContextBase in items)
		{
			Add(iContextBase);
		}
	}

	public void Clear()
	{
		((ICollection<IContextBase>)ContextElements).Clear();
	}

	public bool Contains(IContextBase iContextBase)
	{
		return ((ICollection<IContextBase>)ContextElements).Contains(iContextBase);
	}

	public void CopyTo(IContextBase[] contextElements, int arrayIndex)
	{
		((ICollection<IContextBase>)ContextElements).CopyTo(contextElements, arrayIndex);
	}

	public bool Remove(IContextBase iContextBase)
	{
		return ((ICollection<IContextBase>)ContextElements).Remove(iContextBase);
	}

	public IEnumerator<IContextBase> GetEnumerator()
	{
		return ((IEnumerable<IContextBase>)ContextElements).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable<IContextBase>)ContextElements).GetEnumerator();
	}
}
