using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Utility;
using Utility.Extensions;

namespace RasMapperLib;

public class TerrainQueryProfileInfo : IProfileInfo
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__26_002D0
	{
		public List<TerrainQueryProfileInfo> pfInfos;

		public List<int> needIdxs;

		public List<Profile> retn;

		public _Closure_0024__26_002D0(_Closure_0024__26_002D0 other)
		{
			if (other != null)
			{
				pfInfos = other.pfInfos;
				needIdxs = other.needIdxs;
				retn = other.retn;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__26_002D1
	{
		public Profile[] pfs;

		public _Closure_0024__26_002D0 outerClosure;

		public _Closure_0024__26_002D1(_Closure_0024__26_002D1 other)
		{
			if (other != null)
			{
				pfs = other.pfs;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			for (int i = start; i <= fin; i = checked(i + 1))
			{
				outerClosure.pfInfos[outerClosure.needIdxs[i]].SetAndFilterProfile(pfs[i]);
				outerClosure.retn[outerClosure.needIdxs[i]] = outerClosure.pfInfos[outerClosure.needIdxs[i]].GetProfile();
			}
		}
	}

	private float _stationOffset;

	private RASGeometry _geomLayer;

	private TerrainLayer _trLayer;

	private Polyline _pline;

	private Profile _profile;

	private double _filterTolerance;

	private int _pointCountFilter;

	private int _horizontalDecimalPlaces;

	private int _verticalDecimalPlaces;

	public int Count
	{
		get
		{
			ComputeProfileIfNeeded();
			if (_profile == null)
			{
				return 0;
			}
			return _profile.Count;
		}
	}

	public float FirstStation => 0f;

	public float LastStation => (float)_pline.Length;

	public bool ShouldAdjustToZeroStationing => true;

	public TerrainQueryProfileInfo(Polyline pline, TerrainLayer tlayer, double filterTolerance = 0.01)
	{
		_stationOffset = 0f;
		_pointCountFilter = 0;
		_horizontalDecimalPlaces = -1;
		_verticalDecimalPlaces = -1;
		_pline = pline;
		_trLayer = tlayer;
		_filterTolerance = filterTolerance;
	}

	public TerrainQueryProfileInfo(Polyline pline, RASGeometry geomLayer)
	{
		_stationOffset = 0f;
		_pointCountFilter = 0;
		_horizontalDecimalPlaces = -1;
		_verticalDecimalPlaces = -1;
		_pline = pline;
		_geomLayer = geomLayer;
		_filterTolerance = geomLayer.FilterToleranceZ;
	}

	public void AttachFilterer(int pointCount = 0)
	{
		if (pointCount > 0)
		{
			_pointCountFilter = pointCount;
		}
		else
		{
			_pointCountFilter = SharedData.MaxNumberOfPointsOnXS;
		}
	}

	public void AttachDecimalPlaces(int horizontalDP, int verticalDP)
	{
		_horizontalDecimalPlaces = horizontalDP;
		_verticalDecimalPlaces = verticalDP;
	}

	public Profile GetProfile()
	{
		ComputeProfileIfNeeded();
		return _profile;
	}

	Profile IProfileInfo.GetProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile();
	}

	public bool ProfileNeedsUpdate()
	{
		if (_profile == null || GetCurrentAssociatedTerrain() != GetTerrainFromProfile())
		{
			return true;
		}
		return false;
	}

	private void ComputeProfileIfNeeded()
	{
		if (ProfileNeedsUpdate())
		{
			ComputeProfile();
		}
	}

	private void ComputeProfile()
	{
		TerrainLayer currentAssociatedTerrain = GetCurrentAssociatedTerrain();
		Profile profile = null;
		profile = ((currentAssociatedTerrain == null || !currentAssociatedTerrain.AllSourceFilesExist()) ? new Profile(SharedData.LengthUnits, SharedData.LengthUnits) : currentAssociatedTerrain.ComputeProfile(_pline, (float)_filterTolerance));
		SetAndFilterProfile(profile);
	}

	public void SetAndFilterProfile(Profile pf)
	{
		_profile = pf;
		if (_horizontalDecimalPlaces >= 0 && _horizontalDecimalPlaces >= 0)
		{
			_profile = _profile.RoundData(_horizontalDecimalPlaces, _verticalDecimalPlaces);
		}
		if (_pointCountFilter > 0)
		{
			_profile = _profile.FilterXYIterative(10, _pointCountFilter);
		}
		MarkProfileUpdated();
	}

	public static List<Profile> GetProfiles(List<TerrainQueryProfileInfo> pfInfos, TerrainLayer trLayer = null)
	{
		_Closure_0024__26_002D0 capturedLocals = new _Closure_0024__26_002D0(null);
		capturedLocals.pfInfos = pfInfos;
		capturedLocals.retn = new List<Profile>();
		capturedLocals.needIdxs = new List<int>();
		List<Polyline> polylines = new List<Polyline>();
		checked
		{
			int count = capturedLocals.pfInfos.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				TerrainQueryProfileInfo terrainQueryProfileInfo = capturedLocals.pfInfos[i];
				if (terrainQueryProfileInfo.ProfileNeedsUpdate())
				{
					capturedLocals.needIdxs.Add(i);
					polylines.Add(terrainQueryProfileInfo._pline);
					capturedLocals.retn.Add(null);
				}
				else
				{
					capturedLocals.retn.Add(terrainQueryProfileInfo._profile);
				}
			}
			if (capturedLocals.needIdxs.Count > 0)
			{
				_Closure_0024__26_002D1 capturedLocals2 = default;
				_Closure_0024__26_002D1 capturedLocals3 = new _Closure_0024__26_002D1(capturedLocals2);
				capturedLocals3.outerClosure = capturedLocals;
				if (trLayer == null)
				{
					trLayer = capturedLocals3.outerClosure.pfInfos.First()._trLayer;
				}
				if (trLayer == null)
				{
					return capturedLocals3.outerClosure.retn;
				}
				capturedLocals3.pfs = trLayer.ComputeProfiles(polylines, (float)capturedLocals3.outerClosure.pfInfos.First()._filterTolerance);
				Parallel.SmartFor(capturedLocals3.pfs.Count(), [SpecialName] (int start, int fin) =>
				{
					for (int j = start; j <= fin; j++)
					{
						capturedLocals3.outerClosure.pfInfos[capturedLocals3.outerClosure.needIdxs[j]].SetAndFilterProfile(capturedLocals3.pfs[j]);
						capturedLocals3.outerClosure.retn[capturedLocals3.outerClosure.needIdxs[j]] = capturedLocals3.outerClosure.pfInfos[capturedLocals3.outerClosure.needIdxs[j]].GetProfile();
					}
				}, 100);
			}
			return capturedLocals.retn;
		}
	}

	private void MarkProfileUpdated()
	{
		_trLayer = GetCurrentAssociatedTerrain();
	}

	private TerrainLayer GetCurrentAssociatedTerrain()
	{
		if (_geomLayer == null)
		{
			return _trLayer;
		}
		return _geomLayer.Terrain;
	}

	private TerrainLayer GetTerrainFromProfile()
	{
		if (_profile == null)
		{
			return null;
		}
		return _trLayer;
	}

	public override string ToString()
	{
		if (_profile == null || GetCurrentAssociatedTerrain() != GetTerrainFromProfile())
		{
			return "(Will cut from Terrain as needed)";
		}
		return _profile.Count.ToString().InParentheses();
	}

	public void SetStationingOffset(float offsetValue)
	{
		_stationOffset = offsetValue;
	}

	void IProfileInfo.SetStationingOffset(float offsetValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetStationingOffset
		this.SetStationingOffset(offsetValue);
	}
}
