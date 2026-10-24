using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;

namespace RasMapperLib;

public class UserStationingComputer
{
	public class RiverMileEntry
	{
		public readonly PointOnPolyline Point;

		public readonly double Station;

		public double UserStation;

		public RiverMileEntry(double station, double userStation, PointOnPolyline point)
		{
			Point = point;
			Station = station;
			UserStation = userStation;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__11_002D0
	{
		public string rivName;

		public _Closure_0024__11_002D0(_Closure_0024__11_002D0 other)
		{
			if (other != null)
			{
				rivName = other.rivName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(RiverMap.River r)
		{
			return Operators.CompareString(r.Name, rivName, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__11_002D1
	{
		public string rsRchName;

		public _Closure_0024__11_002D1(_Closure_0024__11_002D1 other)
		{
			if (other != null)
			{
				rsRchName = other.rsRchName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(RiverMap.Reach r)
		{
			return Operators.CompareString(r.Name, rsRchName, TextCompare: false) == 0;
		}
	}

	public List<RiverMileEntry> MileMarkers;

	private Polyline _keyPolyline;

	public int Count => MileMarkers.Count;

	public Polyline Polyline => _keyPolyline;

	public UserStationingComputer(Polyline pline)
	{
		MileMarkers = new List<RiverMileEntry>();
		_keyPolyline = pline.Clone();
		ResetScale();
	}

	protected UserStationingComputer()
	{
		MileMarkers = new List<RiverMileEntry>();
	}

	public UserStationingComputer Clone()
	{
		UserStationingComputer userStationingComputer = new UserStationingComputer();
		userStationingComputer._keyPolyline = _keyPolyline.Clone();
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				RiverMileEntry riverMileEntry = MileMarkers[i];
				userStationingComputer.Add(riverMileEntry.Station, riverMileEntry.UserStation, riverMileEntry.Point);
			}
			return userStationingComputer;
		}
	}

	public static UserStationingComputer Load(Polyline pline, double[] stations, double[] userStations)
	{
		UserStationingComputer userStationingComputer = new UserStationingComputer();
		userStationingComputer._keyPolyline = pline.Clone();
		checked
		{
			int count = stations.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				userStationingComputer.Add(stations[i], userStations[i], pline.PointAtDistance(stations[i]));
			}
			return userStationingComputer;
		}
	}

	public static UserStationingComputer LoadReachStationing(RASGeometry geom, int rivId)
	{
		RASRiver river = geom.River;
		RiverStations riverStationing = geom.RiverStationing;
		river.Polyline(rivId);
		string riverName = river.GetRiverName(rivId);
		string reachName = river.GetReachName(rivId);
		List<Tuple<double, double>> valueTupleList = new List<Tuple<double, double>>();
		checked
		{
			int count = riverStationing.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string river2 = riverStationing.GetRiver(i);
				string reach = riverStationing.GetReach(i);
				if (Operators.CompareString(river2, riverName, TextCompare: false) == 0 && Operators.CompareString(reach, reachName, TextCompare: false) == 0)
				{
					valueTupleList.Add(new Tuple<double, double>(riverStationing.GetDistanceAlongReach(i), riverStationing.GetUserStation(i)));
				}
			}
			valueTupleList = valueTupleList.OrderBy([SpecialName] (Tuple<double, double> a) => a.Item1).ToList();
			return Load(river.Polyline(rivId), valueTupleList.Select([SpecialName] (Tuple<double, double> tup) => tup.Item1).ToArray(), valueTupleList.Select([SpecialName] (Tuple<double, double> tup) => tup.Item2).ToArray());
		}
	}

	public static UserStationingComputer LoadRiverStationing(RASGeometry geom, string rivName, List<Tuple<double, double>> injectTempStations = null, RiverMap rivMap = null)
	{
		_Closure_0024__11_002D0 capturedLocals = default;
		_Closure_0024__11_002D0 capturedLocals2 = new _Closure_0024__11_002D0(capturedLocals);
		capturedLocals2.rivName = rivName;
		RASRiver river = geom.River;
		RiverStations riverStationing = geom.RiverStationing;
		if (rivMap == null)
		{
			rivMap = geom.CreateRiverMap(loadSpatialIntersectionData: false);
		}
		List<Tuple<double, double>> valueTupleList = new List<Tuple<double, double>>();
		List<RiverMap.River> rivers = rivMap.Rivers.Where([SpecialName] (RiverMap.River r) => Operators.CompareString(r.Name, capturedLocals2.rivName, TextCompare: false) == 0).ToList();
		if (rivers.Count != 1)
		{
			return null;
		}
		RiverMap.River river2 = rivers.First();
		Polyline polyline = river2.CombinedPolyline;
		if (polyline == null)
		{
			polyline = (river2.CombinedPolyline = river.GetCombinedRiverPolyline(capturedLocals2.rivName, rivMap));
		}
		if (polyline == null || polyline.Count <= 1)
		{
			return null;
		}
		checked
		{
			int count = riverStationing.FeatureCount() - 1;
			_Closure_0024__11_002D1 capturedLocals3 = default;
			for (int ordinal = 0; ordinal <= count; ordinal++)
			{
				capturedLocals3 = new _Closure_0024__11_002D1(capturedLocals3);
				string river3 = riverStationing.GetRiver(ordinal);
				capturedLocals3.rsRchName = riverStationing.GetReach(ordinal);
				if (Operators.CompareString(river3, capturedLocals2.rivName, TextCompare: false) != 0)
				{
					continue;
				}
				RiverMap.Reach reach = river2.Reaches.FirstOrDefault(capturedLocals3._Lambda_0024__1);
				if (reach == null)
				{
					continue;
				}
				double upstreamDistanceAlongRiver = reach.GetUpstreamDistanceAlongRiver();
				double userStation = riverStationing.GetUserStation(ordinal);
				bool condition = true;
				foreach (Tuple<double, double> tuple in valueTupleList)
				{
					if (Math.Abs(tuple.Item2 - userStation) < 0.001)
					{
						condition = false;
					}
				}
				if (condition)
				{
					valueTupleList.Add(new Tuple<double, double>(upstreamDistanceAlongRiver + (double)riverStationing.GetDistanceAlongReach(ordinal), riverStationing.GetUserStation(ordinal)));
				}
			}
			if (injectTempStations != null)
			{
				foreach (Tuple<double, double> injectTempStation in injectTempStations)
				{
					valueTupleList.Add(injectTempStation);
				}
			}
			if (valueTupleList.Count == 0)
			{
				valueTupleList.Add(new Tuple<double, double>(0.0, polyline.Length * SharedData.RSConversionFactor()));
				valueTupleList.Add(new Tuple<double, double>(polyline.Length, 0.0));
			}
			else if (valueTupleList.Count == 1)
			{
				double computed = valueTupleList.First().Item1;
				double polylineTerm = polyline.Length - computed;
				double computed2 = valueTupleList.First().Item2;
				double rsConversionFactorSharedDataTerm = computed2 - polylineTerm * SharedData.RSConversionFactor();
				double rsConversionFactorSharedDataTerm2 = computed2 + computed * SharedData.RSConversionFactor();
				if (computed < 0.01)
				{
					valueTupleList.Add(new Tuple<double, double>(polyline.Length, rsConversionFactorSharedDataTerm));
				}
				else if (Math.Abs(polylineTerm) < 0.01)
				{
					valueTupleList.Add(new Tuple<double, double>(0.0, rsConversionFactorSharedDataTerm2));
				}
				else
				{
					valueTupleList.Add(new Tuple<double, double>(0.0, rsConversionFactorSharedDataTerm2));
					valueTupleList.Add(new Tuple<double, double>(polyline.Length, rsConversionFactorSharedDataTerm));
				}
			}
			else
			{
				valueTupleList = valueTupleList.OrderByDescending([SpecialName] (Tuple<double, double> a) => a.Item1).ToList();
				double computed3 = valueTupleList.First().Item1;
				if (!(Math.Abs(computed3) < 0.01))
				{
					double rsConversionFactorSharedDataTerm3 = valueTupleList.First().Item2 + computed3 * SharedData.RSConversionFactor();
					valueTupleList.Add(new Tuple<double, double>(0.0, rsConversionFactorSharedDataTerm3));
				}
				double polylineTerm2 = polyline.Length - valueTupleList.Last().Item1;
				if (!(polylineTerm2 < 0.01))
				{
					double rsConversionFactorSharedDataTerm4 = valueTupleList.Last().Item2 - polylineTerm2 * SharedData.RSConversionFactor();
					valueTupleList.Add(new Tuple<double, double>(polyline.Length, rsConversionFactorSharedDataTerm4));
				}
			}
			valueTupleList = valueTupleList.OrderBy([SpecialName] (Tuple<double, double> a) => a.Item1).ToList();
			return Load(polyline, valueTupleList.Select([SpecialName] (Tuple<double, double> tup) => tup.Item1).ToArray(), valueTupleList.Select([SpecialName] (Tuple<double, double> tup) => tup.Item2).ToArray());
		}
	}

	public void ResetScale()
	{
		MileMarkers.Clear();
		Add(0.0, _keyPolyline.Length, _keyPolyline.FirstPointOnPolyline());
		Add(_keyPolyline.Length, 0.0, _keyPolyline.LastPointOnPolyline());
	}

	public bool IsValidInsertion(double stationAlong, double userStation)
	{
		RiverMileEntry riverMileEntry = MileMarkers.First();
		RiverMileEntry riverMileEntry2 = MileMarkers.Last();
		if (stationAlong >= riverMileEntry2.Station)
		{
			return false;
		}
		if (stationAlong <= riverMileEntry.Station)
		{
			return false;
		}
		if (userStation <= riverMileEntry2.UserStation)
		{
			return false;
		}
		if (userStation >= riverMileEntry.UserStation)
		{
			return false;
		}
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				RiverMileEntry riverMileEntry3 = MileMarkers[i];
				if (riverMileEntry3.Station < stationAlong)
				{
					riverMileEntry = riverMileEntry3;
					continue;
				}
				riverMileEntry2 = riverMileEntry3;
				break;
			}
			if (userStation <= riverMileEntry2.UserStation)
			{
				return false;
			}
			if (userStation >= riverMileEntry.UserStation)
			{
				return false;
			}
			return true;
		}
	}

	private bool IsDefaultScale()
	{
		if (MileMarkers.Count != 2)
		{
			return false;
		}
		if (Math.Abs(MileMarkers[1].UserStation) > 0.001)
		{
			return false;
		}
		if (Math.Abs(MileMarkers[0].UserStation - _keyPolyline.Length) > 0.001)
		{
			return false;
		}
		return true;
	}

	public void Add(double station, double userStation, PointOnPolyline ptOnPoly)
	{
		RiverMileEntry riverMileEntry = new RiverMileEntry(station, userStation, ptOnPoly);
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				if (MileMarkers[i].Station > station)
				{
					MileMarkers.Insert(i, riverMileEntry);
					return;
				}
			}
			MileMarkers.Add(riverMileEntry);
		}
	}

	public void Rescale(Polyline newPline)
	{
		Polyline keyPolyline = _keyPolyline;
		bool isDefaultScale = IsDefaultScale();
		_keyPolyline = newPline.Clone();
		if (isDefaultScale)
		{
			ResetScale();
			return;
		}
		checked
		{
			if (keyPolyline.Count == newPline.Count && Math.Abs(keyPolyline.Length - newPline.Length) < 0.0001)
			{
				int count = MileMarkers.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					RiverMileEntry riverMileEntry = MileMarkers[i];
					PointOnPolyline point = riverMileEntry.Point;
					PointM pointM = newPline.PointMOnLine(point.SegmentIndex, point.Fraction);
					MileMarkers[i] = new RiverMileEntry(riverMileEntry.Station, riverMileEntry.UserStation, new PointOnPolyline(pointM, point.SegmentIndex, point.Fraction));
				}
				return;
			}
			double length = newPline.Length;
			List<RiverMileEntry> mileMarkers = new List<RiverMileEntry>();
			double actualDistance = 0.0;
			int count2 = MileMarkers.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				RiverMileEntry riverMileEntry2 = MileMarkers[j];
				PointOnPolyline pointOnPolyline = newPline.NearestPointOnPolyline(riverMileEntry2.Point.PointM, ref actualDistance);
				if (actualDistance < 0.01)
				{
					double distanceAlong = newPline.DistanceAlong(pointOnPolyline);
					if (distanceAlong < 0.01)
					{
						distanceAlong = 0.0;
					}
					if (distanceAlong > length - 0.01)
					{
						distanceAlong = length;
					}
					mileMarkers.Add(new RiverMileEntry(distanceAlong, riverMileEntry2.UserStation, pointOnPolyline));
				}
			}
			mileMarkers = mileMarkers.OrderBy([SpecialName] (RiverMileEntry mm) => mm.Station).ToList();
			if (mileMarkers.Count < 2)
			{
				ResetScale();
			}
			else
			{
				RiverMileEntry riverMileEntry3 = mileMarkers.First();
				RiverMileEntry riverMileEntry4 = mileMarkers[1];
				RiverMileEntry riverMileEntry5 = mileMarkers.Last();
				RiverMileEntry riverMileEntry6 = mileMarkers[mileMarkers.Count - 2];
				if (riverMileEntry3.Station > 0.0)
				{
					double userStationDifferenceOverStationDifference = (riverMileEntry4.UserStation - riverMileEntry3.UserStation) / (riverMileEntry4.Station - riverMileEntry3.Station);
					double stationRiverMileEntry3Term = riverMileEntry3.Station * userStationDifferenceOverStationDifference;
					mileMarkers.Insert(0, new RiverMileEntry(0.0, riverMileEntry3.UserStation - stationRiverMileEntry3Term, new PointOnPolyline(newPline.FirstPointM(), 0, 0.0)));
				}
				if (riverMileEntry5.Station < newPline.Length)
				{
					double userStationDifferenceOverStationDifference2 = (riverMileEntry5.UserStation - riverMileEntry6.UserStation) / (riverMileEntry5.Station - riverMileEntry6.Station);
					double lengthStationTerm = (length - riverMileEntry5.Station) * userStationDifferenceOverStationDifference2;
					mileMarkers.Add(new RiverMileEntry(length, riverMileEntry5.UserStation + lengthStationTerm, new PointOnPolyline(newPline.LastPointM(), newPline.SegmentCount() - 1, 1.0)));
				}
			}
			MileMarkers = mileMarkers;
		}
	}

	public void TranslateStation(double newStartingUserStation)
	{
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				MileMarkers[i].UserStation += newStartingUserStation;
			}
		}
	}

	public void ScaleStations(double scaleFactor)
	{
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				MileMarkers[i].UserStation *= scaleFactor;
			}
		}
	}

	public bool ClosestMileEntry(double fromStation, double withinDistance, ref RiverMileEntry entry)
	{
		bool result = false;
		double maxValue = double.MaxValue;
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				RiverMileEntry riverMileEntry = MileMarkers[i];
				if (!(riverMileEntry.Station < fromStation - withinDistance))
				{
					if (riverMileEntry.Station > fromStation + withinDistance)
					{
						return result;
					}
					double maxValue2 = Math.Abs(riverMileEntry.Station - fromStation);
					if (maxValue2 < maxValue)
					{
						entry = riverMileEntry;
						result = true;
						maxValue = maxValue2;
					}
				}
			}
			return result;
		}
	}

	public double InterpolateUserStation(double trueStation)
	{
		int ordinal = -1;
		checked
		{
			int count = MileMarkers.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (MileMarkers[i].Station > trueStation)
				{
					ordinal = i - 1;
					break;
				}
			}
			if (ordinal == -1)
			{
				return double.NaN;
			}
			double station = MileMarkers[ordinal].Station;
			double station2 = MileMarkers[ordinal + 1].Station;
			double userStation = MileMarkers[ordinal].UserStation;
			double userStation2 = MileMarkers[ordinal + 1].UserStation;
			double submergenceRatio = (trueStation - station) / (station2 - station);
			return userStation2 * submergenceRatio + userStation * (1.0 - submergenceRatio);
		}
	}

	public override string ToString()
	{
		return MileMarkers.First().UserStation + " to " + MileMarkers.Last().UserStation;
	}
}
