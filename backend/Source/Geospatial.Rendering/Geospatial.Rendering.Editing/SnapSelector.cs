using System;
using System.Collections.Generic;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class SnapSelector
{
	public readonly struct CandidateSlice(IList<FeatureSegmentSubset> candidates, Func<int, Polyline> getLine, object sourceKey = null)
	{
		public readonly IList<FeatureSegmentSubset> Candidates = candidates;

		public readonly Func<int, Polyline> GetLine = getLine;

		public readonly object SourceKey = sourceKey;
	}

	public readonly struct SnapAnchor(object sourceKey, int fid, PointOnPolyline position)
	{
		public readonly object SourceKey = sourceKey;

		public readonly int Fid = fid;

		public readonly PointOnPolyline Position = position;

		public readonly bool IsValid = true;
	}

	public delegate IReadOnlyList<CandidateSlice> CandidateSupplier(HitTestArgs hit, double toleranceWorld);

	private readonly IVectorManager _vectorManager;

	private readonly CandidateSupplier _supplier;

	private VectorID _glyphId;

	private SnapResult _last;

	private SnapAnchor _currentAnchor;

	private Func<int, Polyline> _currentGetLine;

	private SnapAnchor _committedAnchor;

	public bool HasSnap { get; private set; }

	public SnapResult LastResult => _last;

	public Point SnapPoint => _last.SnapPoint;

	public SnapAnchor CommittedAnchor => _committedAnchor;

	public SnapSelector(IVectorManager vectorManager, CandidateSupplier supplier)
	{
		_vectorManager = vectorManager ?? throw new ArgumentNullException("vectorManager");
		_supplier = supplier ?? throw new ArgumentNullException("supplier");
	}

	public bool TrySnap(HitTestArgs hit, double tolerancePixels)
	{
		HasSnap = false;
		_currentAnchor = default;
		_currentGetLine = null;
		if (hit == null)
		{
			return false;
		}
		double tolerancePixelsTimesCellSize = tolerancePixels * hit.Window.CellSize;
		if (!(tolerancePixelsTimesCellSize > 0.0))
		{
			return false;
		}
		IReadOnlyList<CandidateSlice> readOnlyList = _supplier(hit, tolerancePixelsTimesCellSize);
		if (readOnlyList == null || readOnlyList.Count == 0)
		{
			return false;
		}
		Point queryPoint = hit.QueryPoint;
		SnapResult last = SnapResult.NoSnap;
		double maxValue = double.MaxValue;
		object sourceKey = null;
		Func<int, Polyline> currentGetLine = null;
		for (int i = 0; i < readOnlyList.Count; i++)
		{
			CandidateSlice candidateSlice = readOnlyList[i];
			if (candidateSlice.Candidates.Count == 0)
			{
				continue;
			}
			SnapResult snapResult = SnapHelpers.TrySnap(hit, candidateSlice.Candidates, candidateSlice.GetLine);
			if (snapResult.Success)
			{
				double distanceSquaredTo = snapResult.SnapPoint.DistanceSquaredTo(queryPoint);
				if (distanceSquaredTo < maxValue)
				{
					maxValue = distanceSquaredTo;
					last = snapResult;
					sourceKey = candidateSlice.SourceKey;
					currentGetLine = candidateSlice.GetLine;
				}
			}
		}
		if (!last.Success)
		{
			return false;
		}
		if (maxValue > tolerancePixelsTimesCellSize * tolerancePixelsTimesCellSize)
		{
			return false;
		}
		_last = last;
		HasSnap = true;
		if (last.SplitLines.Count == 1)
		{
			SplitLineResult splitLineResult = last.SplitLines[0];
			_currentAnchor = new SnapAnchor(sourceKey, splitLineResult.FID, splitLineResult.Point);
			_currentGetLine = currentGetLine;
		}
		return true;
	}

	public bool TryGetCurrentAnchor(out SnapAnchor anchor, out Func<int, Polyline> getLine)
	{
		anchor = _currentAnchor;
		getLine = _currentGetLine;
		if (HasSnap && _currentAnchor.IsValid)
		{
			return _currentGetLine != null;
		}
		return false;
	}

	public void PromoteCommitted()
	{
		_committedAnchor = ((HasSnap && _currentAnchor.IsValid) ? _currentAnchor : default(SnapAnchor));
	}

	public void ResetTrace()
	{
		_committedAnchor = default;
	}

	public void Render()
	{
		if (HasSnap)
		{
			_vectorManager.AddOrUpdateVector(ref _glyphId, _last.SnapPoint, DefaultEditSymbology.SelectPointSymbology);
		}
	}

	public void Clear()
	{
		_vectorManager.TryDeleteVector(ref _glyphId);
		HasSnap = false;
	}
}
