using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class SelectedFeatureCollection : IEnumerable<SelectedFeature>
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__12_002D0
	{
		public int fid;

		public _Closure_0024__12_002D0(_Closure_0024__12_002D0 other)
		{
			if (other != null)
			{
				fid = other.fid;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(int f)
		{
			return f > fid;
		}
	}

	private readonly Dictionary<int, SelectedFeature> _selectedFeatures;

	public int Count => _selectedFeatures.Count;

	public event EventHandler<SelectionChangedEventArgs> SelectionChanged;

	public SelectedFeatureCollection()
	{
		_selectedFeatures = new Dictionary<int, SelectedFeature>();
	}

	public SelectedFeature Add(int ftIdx)
	{
		SelectedFeature selectedFeature = new SelectedFeature(ftIdx);
		_selectedFeatures[ftIdx] = selectedFeature;
		SelectionChangedInternal(SelectionType.Selected);
		return selectedFeature;
	}

	public void Add(SelectedFeature selectedFeature)
	{
		_selectedFeatures[selectedFeature.FID] = selectedFeature;
		SelectionChangedInternal(SelectionType.Selected);
	}

	public void AddRange(IEnumerable<int> ftIdxs)
	{
		foreach (int ftIdx in ftIdxs)
		{
			_selectedFeatures[ftIdx] = new SelectedFeature(ftIdx);
		}
		SelectionChangedInternal(SelectionType.Selected);
	}

	public void AddRange(int start, int count)
	{
		checked
		{
			int count2 = start + count - 1;
			for (int i = start; i <= count2; i++)
			{
				_selectedFeatures[i] = new SelectedFeature(i);
			}
			SelectionChangedInternal(SelectionType.Selected);
		}
	}

	public void Remove(int ftIdx)
	{
		_selectedFeatures.Remove(ftIdx);
		SelectionChangedInternal(SelectionType.Deselected);
	}

	public void ClearSelection()
	{
		_selectedFeatures.Clear();
		SelectionChangedInternal(SelectionType.Deselected);
	}

	public bool RemoveIfGreaterThan(int fid)
	{
		_Closure_0024__12_002D0 capturedLocals = default;
		_Closure_0024__12_002D0 capturedLocals2 = new _Closure_0024__12_002D0(capturedLocals);
		capturedLocals2.fid = fid;
		List<int> intList = (from f in CopyFIDs()
			where f > capturedLocals2.fid
			select f).ToList();
		if (intList.Count == 0)
		{
			return false;
		}
		foreach (int ordinal in intList)
		{
			Remove(ordinal);
		}
		return true;
	}

	public bool Contains(int ftIdx)
	{
		return _selectedFeatures.ContainsKey(ftIdx);
	}

	public int SelectedFID(int index)
	{
		return _selectedFeatures[index].FID;
	}

	public SelectedFeature Key(int ftIdx)
	{
		return _selectedFeatures[ftIdx];
	}

	public bool HasOpenedFeature()
	{
		foreach (KeyValuePair<int, SelectedFeature> selectedFeature in _selectedFeatures)
		{
			if (selectedFeature.Value.IsOpen)
			{
				return true;
			}
		}
		return false;
	}

	public SelectedFeature GetFirstOpenFeature()
	{
		foreach (KeyValuePair<int, SelectedFeature> selectedFeature in _selectedFeatures)
		{
			if (selectedFeature.Value.IsOpen)
			{
				return selectedFeature.Value;
			}
		}
		return null;
	}

	public IEnumerable<int> FIDs()
	{
		foreach (KeyValuePair<int, SelectedFeature> selectedFeature in _selectedFeatures)
		{
			yield return selectedFeature.Key;
		}
	}

	public List<int> CopyFIDs()
	{
		return _selectedFeatures.Keys.ToList();
	}

	public int GetFirstOpenFID()
	{
		foreach (KeyValuePair<int, SelectedFeature> selectedFeature in _selectedFeatures)
		{
			if (selectedFeature.Value.IsOpen)
			{
				return selectedFeature.Key;
			}
		}
		return -1;
	}

	public SelectedFeatureCollection Clone()
	{
		SelectedFeatureCollection selectedFeatureCollection = new SelectedFeatureCollection();
		foreach (KeyValuePair<int, SelectedFeature> selectedFeature in _selectedFeatures)
		{
			selectedFeatureCollection.Add(selectedFeature.Value.Clone());
		}
		return selectedFeatureCollection;
	}

	public IEnumerator<SelectedFeature> GetEnumerator()
	{
		foreach (SelectedFeature selectedFeature in _selectedFeatures.Values)
		{
			yield return selectedFeature;
		}
	}

	IEnumerator<SelectedFeature> IEnumerable<SelectedFeature>.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetEnumerator
		return this.GetEnumerator();
	}

	private IEnumerator IEnumerable_GetEnumerator()
	{
		return GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in IEnumerable_GetEnumerator
		return this.IEnumerable_GetEnumerator();
	}

	private void SelectionChangedInternal(SelectionType st)
	{
		SelectionChangedInternal(new SelectionChangedEventArgs(st));
	}

	private void SelectionChangedInternal(SelectionChangedEventArgs args)
	{
		OnSelectionChanged(args);
		SelectionChanged?.Invoke(this, args);
	}

	protected virtual void OnSelectionChanged(SelectionChangedEventArgs args)
	{
	}
}
