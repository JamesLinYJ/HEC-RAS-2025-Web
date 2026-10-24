using System.Collections.Generic;
using H5Assist.Caching.VB;

namespace RasMapperLib.Terrain;

public class StitchCache
{
	private H5RowCache<double> _rowCache;

	private int _nRows;

	private int _dsetIdx;

	private List<SegmentM> _stitches;

	public int Count => _nRows;

	public StitchCache(string filename, string stitchDataset)
	{
		_rowCache = new H5RowCache<double>(filename);
		_rowCache.EnsureDataset(stitchDataset);
		_dsetIdx = _rowCache.GetIndex(stitchDataset);
		_nRows = checked((int)_rowCache.DatasetDimensions[_dsetIdx][0]);
	}

	public StitchCache(List<SegmentM> stitches)
	{
		_stitches = stitches;
		if (_stitches == null)
		{
			_nRows = 0;
		}
		else
		{
			_nRows = _stitches.Count;
		}
	}

	public IEnumerable<SegmentM> Stitches()
	{
		if (_stitches != null)
		{
			foreach (SegmentM stitch in _stitches)
			{
				yield return stitch;
			}
			yield break;
		}
		checked
		{
			int count = _nRows - 1;
			for (int i = 0; i <= count; i++)
			{
				double[] doubleValues = _rowCache.Item(_dsetIdx, i);
				double px = doubleValues[0];
				double py = doubleValues[1];
				double pz = doubleValues[2];
				double px2 = doubleValues[3];
				double py2 = doubleValues[4];
				double pz2 = doubleValues[5];
				double pm = doubleValues[6];
				yield return new SegmentM(new PointM(px, py, pz, pm), new PointM(px2, py2, pz2, pm));
			}
		}
	}
}
