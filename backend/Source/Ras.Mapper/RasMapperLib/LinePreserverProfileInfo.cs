using System;
using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class LinePreserverProfileInfo : IProfileInfo
{
	private Profile _profile;

	private RASXS _xsLayer;

	private int _xsid;

	private XSSubSet _xsSubset;

	private Profile _origXSProfile;

	private Polyline _origXSPolyline;

	private Tuple<float, float> _origXSBankPointStaElev;

	public XSSubSet XSSubset => _xsSubset;

	public int Count => _profile.Count;

	public float FirstStation
	{
		get
		{
			if (_profile != null)
			{
				return _profile.FirstStation();
			}
			return 0f;
		}
	}

	public float LastStation
	{
		get
		{
			if (_profile != null)
			{
				return _profile.LastStation();
			}
			return (float)_xsLayer.Polyline(_xsid).Length;
		}
	}

	public bool ShouldAdjustToZeroStationing
	{
		get
		{
			if (_xsSubset == XSSubSet.OverBanksOnly)
			{
				return false;
			}
			return true;
		}
	}

	public LinePreserverProfileInfo(RASXS xsLayer, int xsid, XSSubSet xsSubset)
	{
		_xsLayer = xsLayer;
		_xsid = xsid;
		_xsSubset = xsSubset;
		_origXSBankPointStaElev = new Tuple<float, float>(_xsLayer.GetLeftBankStation(xsid), _xsLayer.GetRightBankStation(xsid));
		_origXSPolyline = new Polyline(_xsLayer[xsid].CopyPoints());
		Profile profile = _xsLayer.GetProfile(_xsid);
		_origXSProfile = new Profile();
		foreach (Point2D profile2 in profile)
		{
			_origXSProfile.Add(profile2);
		}
	}

	public void SetStationingOffset(float offsetValue)
	{
	}

	void IProfileInfo.SetStationingOffset(float offsetValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetStationingOffset
		this.SetStationingOffset(offsetValue);
	}

	private bool XSSubsetDiffers()
	{
		_ = _xsSubset;
		_ = 1;
		return true;
	}

	public void InvalidateProfile()
	{
		_profile = null;
	}

	public bool ProfileNeedsUpdate()
	{
		if (_profile == null)
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
		Polyline polyline = _xsLayer.Polyline(_xsid);
		PointM leftBankPoint = _xsLayer.GetLeftBankPoint(_xsid);
		double actualDistance = 0.0;
		PointOnPolyline ptOnPoly = polyline.NearestPointOnPolyline(leftBankPoint, ref actualDistance);
		double distanceAlong = _xsLayer.Polyline(_xsid).DistanceAlong(ptOnPoly);
		float origXSBankPointStaElevTerm = _origXSBankPointStaElev.Item1;
		Polyline polyline2 = _xsLayer.Polyline(_xsid);
		PointM rightBankPoint = _xsLayer.GetRightBankPoint(_xsid);
		actualDistance = 0.0;
		PointOnPolyline ptOnPoly2 = polyline2.NearestPointOnPolyline(rightBankPoint, ref actualDistance);
		double distanceAlong2 = _xsLayer.Polyline(_xsid).DistanceAlong(ptOnPoly2);
		float origXSBankPointStaElevTerm2 = _origXSBankPointStaElev.Item2;
		bool condition = true;
		if (_xsSubset == XSSubSet.ChannelOnly)
		{
			float computed = origXSBankPointStaElevTerm2 - origXSBankPointStaElevTerm;
			double distanceAlong2AboveDistanceAlong = distanceAlong2 - distanceAlong;
			if (Math.Abs((double)computed - distanceAlong2AboveDistanceAlong) > (double)computed * 0.01)
			{
				condition = false;
			}
		}
		else if (_xsSubset == XSSubSet.OverBanksOnly)
		{
			double origXSPolylineTerm = (double)origXSBankPointStaElevTerm + (_origXSPolyline.Length - (double)origXSBankPointStaElevTerm2);
			double distanceAlongPolylineTerm = distanceAlong + (_xsLayer.Polyline(_xsid).Length - distanceAlong2);
			if (Math.Abs(origXSPolylineTerm - distanceAlongPolylineTerm) > origXSPolylineTerm * 0.01)
			{
				condition = false;
			}
		}
		if (condition)
		{
			Profile profile = new Profile();
			IProfileInfo profileProperties = _xsLayer.GetProfileProperties(_xsid);
			_xsLayer.SetProfileTrackTerrain(_xsid);
			Profile profile2 = _xsLayer.GetProfile(_xsid);
			_xsLayer.SetProfileProperties(_xsid, profileProperties);
			foreach (Point2D profile22 in profile2)
			{
				if (_xsSubset == XSSubSet.ChannelOnly)
				{
					if ((double)profile22.X < distanceAlong || (double)profile22.X > distanceAlong2)
					{
						profile.Add(profile22.X, profile22.Y);
					}
				}
				else if (_xsSubset == XSSubSet.OverBanksOnly && (double)profile22.X >= distanceAlong && (double)profile22.X <= distanceAlong2)
				{
					profile.Add(profile22.X, profile22.Y);
				}
			}
			double origXSProfileTerm = 0.0;
			bool condition2 = false;
			double distanceAlongTerm = distanceAlong - (double)origXSBankPointStaElevTerm;
			double origXSProfileTerm2 = 0.0;
			foreach (Point2D origXSProfile in _origXSProfile)
			{
				bool origXSProfileTerm3 = false;
				if (_xsSubset == XSSubSet.ChannelOnly)
				{
					origXSProfileTerm3 = origXSProfile.X >= origXSBankPointStaElevTerm && origXSProfile.X <= origXSBankPointStaElevTerm2;
				}
				else if (_xsSubset == XSSubSet.OverBanksOnly)
				{
					origXSProfileTerm3 = origXSProfile.X < origXSBankPointStaElevTerm || origXSProfile.X > origXSBankPointStaElevTerm2;
				}
				if (origXSProfileTerm3)
				{
					if (!condition2)
					{
						_ = (double)origXSProfile.X / _origXSPolyline.Length;
						origXSProfileTerm2 = distanceAlongTerm + (double)origXSProfile.X;
						condition2 = true;
						profile.Add(origXSProfileTerm2, origXSProfile.Y);
					}
					else
					{
						origXSProfileTerm2 += (double)origXSProfile.X - origXSProfileTerm;
						profile.Add(origXSProfileTerm2, origXSProfile.Y);
					}
				}
				else
				{
					origXSProfileTerm2 += (double)origXSProfile.X - origXSProfileTerm;
				}
				origXSProfileTerm = origXSProfile.X;
			}
			profile.SortByStation();
			_profile = profile;
			return;
		}
		ElevationControlPointLayer elevationControlPointLayer = new ElevationControlPointLayer();
		foreach (Point2D origXSProfile2 in _origXSProfile)
		{
			bool origXSProfile2Term = false;
			if (_xsSubset == XSSubSet.ChannelOnly)
			{
				origXSProfile2Term = origXSProfile2.X >= origXSBankPointStaElevTerm && origXSProfile2.X <= origXSBankPointStaElevTerm2;
			}
			else if (_xsSubset == XSSubSet.OverBanksOnly)
			{
				origXSProfile2Term = origXSProfile2.X < origXSBankPointStaElevTerm || origXSProfile2.X > origXSBankPointStaElevTerm2;
			}
			if (origXSProfile2Term)
			{
				Point feature = new Point(_origXSPolyline.PointAtDistance(origXSProfile2.X).PointM);
				elevationControlPointLayer.AddFeature(feature);
				elevationControlPointLayer.SetElevation(checked(elevationControlPointLayer.FeatureCount() - 1), origXSProfile2.Y);
			}
		}
		double minValue = double.MinValue;
		Extent extent = _origXSPolyline.GetExtent();
		Extent extent2 = _xsLayer.Polyline(_xsid).GetExtent();
		double distanceTo = extent.DistanceTo(extent2.LowerMidside);
		if (distanceTo > minValue)
		{
			minValue = distanceTo;
		}
		distanceTo = extent.DistanceTo(extent2.RightMidside);
		if (distanceTo > minValue)
		{
			minValue = distanceTo;
		}
		distanceTo = extent.DistanceTo(extent2.UpperMidside);
		if (distanceTo > minValue)
		{
			minValue = distanceTo;
		}
		distanceTo = extent.DistanceTo(extent2.LeftMidside);
		if (distanceTo > minValue)
		{
			minValue = distanceTo;
		}
		minValue *= 2.0;
		minValue = Math.Max(_xsLayer.Polyline(_xsid).Length, minValue);
		_profile = ComputeProfileUsingControlPoints(_xsLayer, _xsid, elevationControlPointLayer, minValue, _xsSubset);
	}

	public static Profile ComputeProfileUsingControlPoints(RASXS xsLayer, int xsid, ElevationControlPointLayer elevationPtLayer, double nearDistance, XSSubSet xsSubSet)
	{
		checked
		{
			if (xsid > xsLayer.FeatureCount() - 1)
			{
				MessageBox.Show("Feature does not exist", "Feature Does Not Exist", MessageBoxButtons.Ok, MessageBoxIcon.Error_);
				return null;
			}
			Polyline polyline = xsLayer.Polyline(xsid);
			List<PointM> pointMs = new List<PointM>();
			IProfileInfo profileProperties = xsLayer.GetProfileProperties(xsid);
			xsLayer.SetProfileTrackTerrain(xsid);
			Profile profile = xsLayer.GetProfile(xsid);
			xsLayer.SetProfileProperties(xsid, profileProperties);
			Extent extent = polyline.ComputeExtent();
			extent = extent.Extend(nearDistance * 2.0);
			elevationPtLayer.EnsureQuadtree(extent);
			List<int> spatialQueryFIDs = elevationPtLayer.SpatialQueryFIDs(extent);
			if (profile.Count > 0 && (xsSubSet == XSSubSet.ChannelOnly || xsSubSet == XSSubSet.OverBanksOnly))
			{
				double profileTerm = profile.First().X;
				profile.AdjustToZeroStationing();
				float leftBankStation = xsLayer.GetLeftBankStation(xsid);
				float rightBankStation = xsLayer.GetRightBankStation(xsid);
				if (!float.IsNaN(leftBankStation))
				{
					leftBankStation -= (float)profileTerm;
					if (leftBankStation >= profile.MinX() - 1f && leftBankStation <= profile.MaxX() + 1f)
					{
						pointMs.Add(new PointM(leftBankStation, profile.InterpolateY(leftBankStation)));
					}
					else if (leftBankStation < profile.MinX() - 1f)
					{
						pointMs.Add(profile[0].ToPointM());
					}
					else if (leftBankStation > profile.MaxX() + 1f)
					{
						pointMs.Add(profile[profile.Count - 1].ToPointM());
					}
				}
				else
				{
					pointMs.Add(profile[0].ToPointM());
				}
				if (!float.IsNaN(rightBankStation))
				{
					rightBankStation -= (float)profileTerm;
					if (rightBankStation >= profile.MinX() - 1f && rightBankStation <= profile.MaxX() + 1f)
					{
						pointMs.Add(new PointM(rightBankStation, profile.InterpolateY(rightBankStation)));
					}
					else if (rightBankStation < profile.MinX() - 1f)
					{
						pointMs.Add(profile[0].ToPointM());
					}
					else if (rightBankStation > profile.MaxX() + 1f)
					{
						pointMs.Add(profile[profile.Count - 1].ToPointM());
					}
				}
				else
				{
					pointMs.Add(profile[profile.Count - 1].ToPointM());
				}
			}
			if (xsSubSet == XSSubSet.EntireXS)
			{
				float elevation3 = 0f;
				double maxValue = double.MaxValue;
				float elevation4 = 0f;
				double minValue = double.MinValue;
				int count = spatialQueryFIDs.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM ptM = elevationPtLayer[spatialQueryFIDs[i]].PointM();
					float elevation = elevationPtLayer.GetElevation(spatialQueryFIDs[i]);
					if (double.IsNaN(elevation))
					{
						continue;
					}
					double actualDistance = 0.0;
					PointOnPolyline ptOnPoly = polyline.NearestPointOnPolyline(ptM, ref actualDistance);
					if (actualDistance <= nearDistance)
					{
						double distanceAlong = polyline.DistanceAlong(ptOnPoly);
						if (distanceAlong < maxValue)
						{
							maxValue = distanceAlong;
							elevation3 = elevation;
						}
						if (distanceAlong > minValue)
						{
							minValue = distanceAlong;
							elevation4 = elevation;
						}
					}
				}
				pointMs.Add(new PointM(maxValue, elevation3));
				pointMs.Add(new PointM(minValue, elevation4));
			}
			Profile profile2 = new Profile();
			HashSet<double> doubleSet = new HashSet<double>();
			if (profile != null)
			{
				int count2 = profile.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					Point2D point2D = profile[j];
					Point2D point2D2 = new Point2D
					{
						X = point2D.X,
						Y = point2D.Y
					};
					if (doubleSet.Contains(point2D2.X))
					{
						continue;
					}
					switch (xsSubSet)
					{
					case XSSubSet.EntireXS:
					case XSSubSet.ChannelOnly:
						if ((double)point2D2.X <= pointMs[0].X || (double)point2D2.X >= pointMs[1].X)
						{
							profile2.Add(point2D2);
							doubleSet.Add(point2D2.X);
						}
						break;
					case XSSubSet.OverBanksOnly:
						if ((double)point2D2.X > pointMs[0].X && (double)point2D2.X < pointMs[1].X)
						{
							profile2.Add(point2D2);
							doubleSet.Add(point2D2.X);
						}
						break;
					}
				}
			}
			profile2.SortByStation();
			int count3 = spatialQueryFIDs.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				PointM ptM2 = elevationPtLayer[spatialQueryFIDs[k]].PointM();
				float elevation2 = elevationPtLayer.GetElevation(spatialQueryFIDs[k]);
				if (double.IsNaN(elevation2))
				{
					continue;
				}
				double actualDistance2 = 0.0;
				PointOnPolyline ptOnPoly2 = polyline.NearestPointOnPolyline(ptM2, ref actualDistance2);
				if (!(actualDistance2 <= nearDistance))
				{
					continue;
				}
				double distanceAlong2 = polyline.DistanceAlong(ptOnPoly2);
				if (doubleSet.Contains(distanceAlong2))
				{
					continue;
				}
				switch (xsSubSet)
				{
				case XSSubSet.ChannelOnly:
					if (pointMs.Count == 0 || (distanceAlong2 >= pointMs[0].X && distanceAlong2 <= pointMs[1].X))
					{
						profile2.Add(distanceAlong2, elevation2);
						doubleSet.Add(distanceAlong2);
					}
					break;
				case XSSubSet.OverBanksOnly:
					if (pointMs.Count == 0 || distanceAlong2 < pointMs[0].X || distanceAlong2 > pointMs[1].X)
					{
						profile2.Add(distanceAlong2, elevation2);
						doubleSet.Add(distanceAlong2);
					}
					break;
				case XSSubSet.EntireXS:
					profile2.Add(distanceAlong2, elevation2);
					doubleSet.Add(distanceAlong2);
					break;
				default:
					throw new NotImplementedException();
				}
			}
			profile2.SortByStation();
			return profile2;
		}
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
}
