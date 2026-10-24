using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Utility.DataStructures;

namespace RasMapperLib;

public class SelectedFeature
{
	private enum SelectionType
	{
		Individual,
		All,
		Range,
		None
	}

	private const string FT_UNOPENED = "Feature isn't opened";

	public readonly int FID;

	private readonly HashSet<int> _selectedPoints;

	public readonly ReadOnlyHashSet<int> SelectedPoints;

	private bool _isOpen;

	public bool IsOpen => _isOpen;

	public int Count => _selectedPoints.Count;

	public SelectedFeature(int fID)
	{
		_isOpen = false;
		FID = fID;
		SelectedPoints = new ReadOnlyHashSet<int>();
		_selectedPoints = SelectedPoints;
	}

	public SelectedFeature(int fID, bool fake)
	{
		_isOpen = false;
		FID = fID;
	}

	public SelectedFeature Clone()
	{
		SelectedFeature selectedFeature = new SelectedFeature(FID)
		{
			_isOpen = _isOpen
		};
		HashSetExtensionMethods.AddRange(selectedFeature._selectedPoints, _selectedPoints);
		return selectedFeature;
	}

	public void ClearSelection()
	{
		_selectedPoints.Clear();
	}

	public void CloseFeature()
	{
		ClearSelection();
		_isOpen = false;
	}

	public void OpenFeature()
	{
		_isOpen = true;
	}

	public void SelectPoint(int ptIdx, bool forceAdd = false)
	{
		if (!_isOpen && !forceAdd)
		{
			throw new Exception("Feature isn't opened");
		}
		_selectedPoints.Add(ptIdx);
	}

	public void SelectPoints(IList<int> pts)
	{
		if (!_isOpen)
		{
			throw new Exception("Feature isn't opened");
		}
		foreach (int pt in pts)
		{
			_selectedPoints.Add(pt);
		}
	}

	public void SelectAllPoints(int pointCount)
	{
		SelectRange(0, pointCount);
	}

	public void SelectRange(int start, int count)
	{
		if (!_isOpen)
		{
			throw new Exception("Feature isn't opened");
		}
		checked
		{
			int count2 = start + count - 1;
			for (int i = start; i <= count2; i++)
			{
				_selectedPoints.Add(i);
			}
		}
	}

	public void RemovePoint(int ptIdx)
	{
		if (!_isOpen)
		{
			throw new Exception("Feature isn't opened");
		}
		_selectedPoints.Remove(ptIdx);
	}

	public void TogglePoint(int ptIdx)
	{
		if (!_isOpen)
		{
			throw new Exception("Feature isn't opened");
		}
		if (_selectedPoints.Contains(ptIdx))
		{
			_selectedPoints.Remove(ptIdx);
		}
		else
		{
			_selectedPoints.Add(ptIdx);
		}
	}

	public bool Contains(int ptIdx)
	{
		return _selectedPoints.Contains(ptIdx);
	}

	public int[] GetSortedPoints()
	{
		return _selectedPoints.OrderBy([SpecialName] (int f) => f).ToArray();
	}
}
