using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class XSLSIntersectionData
{
	private List<PolylineIntersection>[] _xsIntersections;

	private long _xsChangeCountCross;

	private long _lsChangeCountCross;

	private object _xsLock;

	private List<PolylineIntersection>[] _lsIntersections;

	private long _xsChangeCountLongitudinal;

	private long _lsChangeCountLongitudinal;

	private object _lsLock;

	private PolylineFeatureLayer _xs;

	private PolylineFeatureLayer _ls;

	public XSLSIntersectionData(PolylineFeatureLayer xs, PolylineFeatureLayer longitudinalLayer)
	{
		_xsChangeCountCross = -1L;
		_lsChangeCountCross = -1L;
		_xsLock = RuntimeHelpers.GetObjectValue(new object());
		_xsChangeCountLongitudinal = -1L;
		_lsChangeCountLongitudinal = -1L;
		_lsLock = RuntimeHelpers.GetObjectValue(new object());
		_ls = longitudinalLayer;
		_xs = xs;
	}

	public List<PolylineIntersection> XSIntersections(int xsIdx)
	{
		EnsureXSIntersections();
		return _xsIntersections[xsIdx];
	}

	public List<PolylineIntersection>[] GetAllXSIntersections()
	{
		EnsureXSIntersections();
		return _xsIntersections.Select([SpecialName] (List<PolylineIntersection> lst) => lst.ToList()).ToArray();
	}

	private void EnsureXSIntersections()
	{
		object xsLock = _xsLock;
		ObjectFlowControl.CheckForSyncLockOnValueType(xsLock);
		bool lockTaken = false;
		try
		{
			Monitor.Enter(xsLock, ref lockTaken);
			if (_xsIntersections == null || _xs.FeatureChangedCount() != _xsChangeCountCross || _ls.FeatureChangedCount() != _lsChangeCountCross)
			{
				RecomputeXSIntersectionList();
			}
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(xsLock);
			}
		}
	}

	private void RecomputeXSIntersectionList()
	{
		_xsChangeCountCross = _xs.FeatureChangedCount();
		_lsChangeCountCross = _ls.FeatureChangedCount();
		_ls.GetSegmentSpatialIndex();
		_xs.FeatureTable();
		_ls.FeatureTable();
		Array.Resize(ref _xsIntersections, _xs.FeatureTable().Rows.Count);
		Parallel.For(0, _xs.FeatureCount(), [SpecialName] (int xsLoopIdx) =>
		{
			Polyline polyline = _xs.Polyline(xsLoopIdx);
			_xsIntersections[xsLoopIdx] = _ls.SpatialIntersect(xsLoopIdx, polyline);
		});
	}

	public List<PolylineIntersection> LSIntersections(int lsid)
	{
		EnsureLSIntersections();
		return _lsIntersections[lsid];
	}

	private void EnsureLSIntersections()
	{
		object lsLock = _lsLock;
		ObjectFlowControl.CheckForSyncLockOnValueType(lsLock);
		bool lockTaken = false;
		try
		{
			Monitor.Enter(lsLock, ref lockTaken);
			if (_lsIntersections == null || _xs.FeatureChangedCount() != _xsChangeCountLongitudinal || _ls.FeatureChangedCount() != _lsChangeCountLongitudinal)
			{
				RecomputeLSIntersectionList();
			}
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(lsLock);
			}
		}
	}

	private void RecomputeLSIntersectionList()
	{
		RecomputeXSIntersectionList();
		_xsChangeCountLongitudinal = _xs.FeatureChangedCount();
		_lsChangeCountLongitudinal = _ls.FeatureChangedCount();
		Array.Resize(ref _lsIntersections, _ls.FeatureCount());
		checked
		{
			int count = _ls.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				_lsIntersections[i] = new List<PolylineIntersection>();
			}
			int count2 = _xs.FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				List<PolylineIntersection> xsIntersection = _xsIntersections[j];
				foreach (PolylineIntersection xsIntersection2 in xsIntersection)
				{
					_lsIntersections[xsIntersection2.APolylineIndex].Add(xsIntersection2);
				}
			}
			int count3 = _ls.FeatureTable().Rows.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				_lsIntersections[k].Sort(PolylineIntersection.CompareA);
			}
		}
	}
}
