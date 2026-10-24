using System;

namespace Geospatial.Vectors;

public struct ImmutableMultiPartPolygonBuilder(ImmutableMultiPartPolygon pl)
{
	private ImmutableMultiPartPolygon _pl = pl;

	private int _curPolygonIdx = 0;

	private int _curPtIdx = 0;

	private bool _isFinished = false;

	private void AssertNotFinished()
	{
		if (_isFinished)
		{
			throw new InvalidOperationException("Already finished building.");
		}
	}

	public void NextPolygon()
	{
		AssertNotFinished();
		if (_curPolygonIdx > _pl.PolygonCount)
		{
			throw new InvalidOperationException("Should already be finished, please call .Finish()");
		}
		if (_curPolygonIdx != 0 && _pl.PolygonStartIndex.Span[_curPolygonIdx - 1] == _curPtIdx)
		{
			throw new InvalidOperationException("Added 0 point polygon.");
		}
		_curPolygonIdx++;
		if (_curPolygonIdx < _pl.PolygonCount)
		{
			_pl.PolygonStartIndex.Span[_curPolygonIdx] = _curPtIdx;
		}
	}

	public void AddPoint(Point pt)
	{
		AssertNotFinished();
		_pl.Points.Span[_curPtIdx] = pt;
		_curPtIdx++;
	}

	public ImmutableMultiPartPolygon Finish()
	{
		if (_curPtIdx != _pl.Points.Length)
		{
			throw new InvalidOperationException("Did not use all available points.");
		}
		for (int i = 1; i < _pl.PolygonCount; i++)
		{
			if (_pl.PolygonStartIndex.Span[i - 1] == _pl.PolygonStartIndex.Span[i])
			{
				throw new InvalidOperationException("Invalid. Contains 0 point polygon.");
			}
		}
		_isFinished = true;
		return _pl;
	}
}
