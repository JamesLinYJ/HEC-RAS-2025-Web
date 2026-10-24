using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Mapping;

public class RiverMap
{
	[DebuggerDisplay("{Name}")]
	public abstract class PrimaryGeometricElement
	{
		public string Name;

		public int ID;

		public List<GeometryError> Errors;

		protected PrimaryGeometricElement()
		{
			Errors = new List<GeometryError>();
		}

		public void AddError(string errMsg)
		{
			Errors.Add(new GeometryError(errMsg));
		}
	}

	public class StorageArea : PrimaryGeometricElement
	{
	}

	public class StructureElement : PrimaryGeometricElement
	{
	}

	public class Mesh : PrimaryGeometricElement
	{
		public int CellCount;

		public int NonVirtualCellCount;

		public int FaceCount;

		public int FacePointCount;

		public double CellAverageSize;

		public PropertyTablesState PropertyTablesState;
	}

	[DebuggerDisplay("{Name}")]
	public class River
	{
		public string Name;

		public List<Reach> Reaches;

		public UserStationingComputer UserStationing;

		public Polyline CombinedPolyline;

		public double[] CumulativeSegLengths;

		public double Length => Reaches.Sum([SpecialName] (Reach rch) => rch.Length);

		public River()
		{
			Reaches = new List<Reach>();
		}

		public int ReachStartingSegmentIndex(Reach reach)
		{
			int cumulativeSegLengthsTerm = 0;
			foreach (Reach reachesUpstreamToDownstream in GetReachesUpstreamToDownstream())
			{
				if (reachesUpstreamToDownstream.Equals(reach))
				{
					return cumulativeSegLengthsTerm;
				}
				if (reachesUpstreamToDownstream.CumulativeSegLengths == null)
				{
					return -1;
				}
				cumulativeSegLengthsTerm = checked(cumulativeSegLengthsTerm + (reachesUpstreamToDownstream.CumulativeSegLengths.Count() + 1));
			}
			return -1;
		}

		public List<Reach> GetReachesDownstreamToUpstream()
		{
			if (Reaches.Count == 1)
			{
				return Reaches.ToList();
			}
			Reach reach = Reaches.FirstOrDefault([SpecialName] (Reach rch) => rch.DownstreamConnection.IsMissing() || !rch.DownstreamConnection.ConnectsDownstream(this));
			List<Reach> reaches = new List<Reach>();
			if (reach == null)
			{
				return reaches;
			}
			reaches.Add(reach);
			Reach reach2 = reach;
			while (true)
			{
				ConnectionElement upstreamConnection = reach2.UpstreamConnection;
				if (upstreamConnection == null || upstreamConnection.IsMissing())
				{
					break;
				}
				Reach reach3 = upstreamConnection.USReaches.FirstOrDefault([SpecialName] (Reach rch) => rch.River == this);
				if (reach3 == null || reach3 == reach)
				{
					break;
				}
				reaches.Add(reach3);
				reach2 = reach3;
			}
			return reaches;
		}

		public List<Reach> GetReachesUpstreamToDownstream()
		{
			List<Reach> reachesDownstreamToUpstream = GetReachesDownstreamToUpstream();
			reachesDownstreamToUpstream.Reverse();
			return reachesDownstreamToUpstream;
		}

		public List<CrossSection> GetCrossSections()
		{
			List<Reach> reachesUpstreamToDownstream = GetReachesUpstreamToDownstream();
			List<CrossSection> crossSections = new List<CrossSection>();
			foreach (Reach reachesUpstreamToDownstream2 in reachesUpstreamToDownstream)
			{
				crossSections.AddRange(reachesUpstreamToDownstream2.CrossSections);
			}
			return crossSections.OrderByDescending([SpecialName] (CrossSection xs) => xs.RiverStationComputed_WithRMM).ToList();
		}
	}

	public class Reach : PrimaryGeometricElement
	{
		public static readonly Reach MissingReach = new Reach();

		public River River;

		public double Length;

		public int SegmentCount;

		public List<CrossSection> CrossSections;

		public double[] CumulativeSegLengths;

		public ConnectionElement UpstreamConnection;

		public ConnectionElement DownstreamConnection;

		public Reach()
		{
			CrossSections = new List<CrossSection>();
		}

		public double GetUpstreamDistanceAlongRiver()
		{
			List<Reach> reachesDownstreamToUpstream = River.GetReachesDownstreamToUpstream();
			reachesDownstreamToUpstream.Reverse();
			double upstreamDistanceAlongRiver = 0.0;
			int segmentCountSum = 0;
			checked
			{
				foreach (Reach reachesDownstreamToUpstream2 in reachesDownstreamToUpstream)
				{
					if (reachesDownstreamToUpstream2 == this)
					{
						return upstreamDistanceAlongRiver;
					}
					upstreamDistanceAlongRiver += reachesDownstreamToUpstream2.Length;
					segmentCountSum += reachesDownstreamToUpstream2.SegmentCount;
					if (segmentCountSum < River.CombinedPolyline.SegmentCount())
					{
						upstreamDistanceAlongRiver += River.CombinedPolyline.Segment(segmentCountSum).Length;
						segmentCountSum++;
					}
				}
				return upstreamDistanceAlongRiver;
			}
		}

		public void SortCrossSections()
		{
			CrossSections = (from xs in CrossSections
				orderby xs.DistanceFromTopOfReach, xs.RiverStationUser descending
				select xs).ToList();
		}

		public void UpstreamAndDownstreamCrossSection(double distanceFromTopOfReach, ref CrossSection usXS, ref CrossSection dsXS)
		{
			CrossSection crossSection = null;
			foreach (CrossSection crossSection2 in CrossSections)
			{
				if (distanceFromTopOfReach < crossSection2.DistanceFromTopOfReach)
				{
					usXS = crossSection;
					dsXS = crossSection2;
					break;
				}
				crossSection = crossSection2;
			}
		}

		public CrossSection UpstreamCrossSection()
		{
			return CrossSections.FirstOrDefault();
		}

		public CrossSection DownstreamCrossSection()
		{
			return CrossSections.LastOrDefault();
		}
	}

	public abstract class ConnectionElement : PrimaryGeometricElement
	{
		public List<Reach> USReaches;

		public List<Reach> DSReaches;

		protected ConnectionElement()
		{
			USReaches = new List<Reach>();
			DSReaches = new List<Reach>();
		}

		public bool IsMissing()
		{
			return ID < 0;
		}

		public virtual bool ConnectsUpstream(River riv)
		{
			return USReaches.Select([SpecialName] (Reach rch) => rch.River).Contains(riv);
		}

		public virtual bool ConnectsDownstream(River riv)
		{
			return DSReaches.Select([SpecialName] (Reach rch) => rch.River).Contains(riv);
		}
	}

	public class Junction : ConnectionElement
	{
	}

	public class D2Connection : ConnectionElement
	{
	}

	public class MissingConnection : ConnectionElement
	{
		public MissingConnection()
		{
			Name = "MissingConnection";
			ID = -1;
		}

		public override bool ConnectsUpstream(River riv)
		{
			return false;
		}

		public override bool ConnectsDownstream(River riv)
		{
			return false;
		}
	}

	public class ExternalConnection : ConnectionElement
	{
		public override bool ConnectsUpstream(River riv)
		{
			return false;
		}

		public override bool ConnectsDownstream(River riv)
		{
			return false;
		}
	}

	public class CrossSection : PrimaryGeometricElement
	{
		public Reach Reach;

		public double RiverStationUser;

		public double RiverStationComputed_WithRMM;

		public bool IsInterpolated;

		public double DistanceFromTopOfReach;

		public double DistanceFromEndOfRiver;

		public double ChannelStation;

		public float LeftBankStation;

		public float RightBankStation;

		public float LeftOBLength;

		public float RightOBLength;

		public float ChannelLength;

		public string Description;

		public List<IneffectiveAreas> IneffectiveFlowArea;

		public List<NValues> ManningsNValues;

		public List<Levees> Levees;

		public List<BlockedObstructions> BlockedObstructions;

		public CrossSection()
		{
			DistanceFromTopOfReach = double.NaN;
			DistanceFromEndOfRiver = double.NaN;
		}

		public string GetFullyQualifiedName()
		{
			return Reach.River.Name + " " + Reach.Name + " " + RiverStationUser;
		}

		public CrossSection DownstreamCrossSection()
		{
			if (Reach == null)
			{
				return null;
			}
			int indexOf = Reach.CrossSections.IndexOf(this);
			if (indexOf == -1)
			{
				return null;
			}
			checked
			{
				if (indexOf < Reach.CrossSections.Count - 1)
				{
					return Reach.CrossSections[indexOf + 1];
				}
				return null;
			}
		}

		public CrossSection UpstreamCrossSection()
		{
			if (Reach == null)
			{
				return null;
			}
			int indexOf = Reach.CrossSections.IndexOf(this);
			if (indexOf == -1)
			{
				return null;
			}
			if (indexOf > 0)
			{
				return Reach.CrossSections[checked(indexOf - 1)];
			}
			return null;
		}

		public void Rename(double newXS)
		{
			RiverStationUser = newXS;
		}
	}

	public class FlowPathLine : PrimaryGeometricElement
	{
		public int NPoints;

		public double Length;
	}

	public class BankPoint
	{
		public Bankline Bankline;

		public double StationFraction;

		public PointM Point;
	}

	public class Bankline : PrimaryGeometricElement
	{
		public int NPoints;

		public double Length;
	}

	public class IneffectiveAreas
	{
	}

	public class NValues
	{
	}

	public class Levees
	{
	}

	public class BlockedObstructions
	{
	}

	public class RiverStation : PrimaryGeometricElement
	{
		public string RiverName;

		public float UserStation;

		public RiverStation(string rivName, float uSta)
		{
			RiverName = rivName;
			UserStation = uSta;
		}
	}

	public bool Valid;

	public string InvalidMessage;

	public bool HasSpatialData;

	public List<River> Rivers;

	public List<Junction> Junctions;

	public List<D2Connection> D2Connections;

	public List<CrossSection> CrossSections;

	public List<Bankline> Banklines;

	public List<RiverStation> RiverStations;

	public List<FlowPathLine> FlowPathLines;

	public List<StorageArea> StorageAreas;

	public List<StructureElement> Structures;

	public List<Mesh> Meshes;

	public List<GeometryIntersectionErrorCollection> IntersectionErrors;

	public RiverMap()
	{
		HasSpatialData = false;
		Rivers = new List<River>();
		Junctions = new List<Junction>();
		D2Connections = new List<D2Connection>();
		CrossSections = new List<CrossSection>();
		Banklines = new List<Bankline>();
		RiverStations = new List<RiverStation>();
		FlowPathLines = new List<FlowPathLine>();
		StorageAreas = new List<StorageArea>();
		Structures = new List<StructureElement>();
		Meshes = new List<Mesh>();
		IntersectionErrors = new List<GeometryIntersectionErrorCollection>();
	}

	public void AddIntersectionError(GeometryIntersectionErrorCollection ints)
	{
		IntersectionErrors.Add(ints);
	}

	public void AddIntersectionError(FeatureLayer lyr1, FeatureLayer lyr2, int fid1, int fid2, PointMs intersectionPts, GeometryError geomError)
	{
		IntersectionErrors.Add(new GeometryIntersectionErrorCollection(lyr1, lyr2, fid1, fid2, new MultiPoint(intersectionPts), new List<GeometryError>(new GeometryError[1] { geomError })));
	}

	public double Get1DLengthRatio()
	{
		double absoluteRiverStationUserDifferenceSum = 0.0;
		double absoluteDistanceFromEndOfRiverDifferenceSum = 0.0;
		checked
		{
			foreach (River river in Rivers)
			{
				List<CrossSection> crossSections = river.GetCrossSections();
				int count = crossSections.Count - 2;
				for (int i = 0; i <= count; i++)
				{
					CrossSection crossSection = crossSections[i];
					CrossSection crossSection2 = crossSections[i + 1];
					double absoluteRiverStationUserDifference = Math.Abs(crossSection.RiverStationUser - crossSection2.RiverStationUser);
					double absoluteDistanceFromEndOfRiverDifference = Math.Abs(crossSection.DistanceFromEndOfRiver - crossSection2.DistanceFromEndOfRiver);
					absoluteRiverStationUserDifferenceSum += absoluteRiverStationUserDifference;
					absoluteDistanceFromEndOfRiverDifferenceSum += absoluteDistanceFromEndOfRiverDifference;
				}
			}
			return absoluteRiverStationUserDifferenceSum / absoluteDistanceFromEndOfRiverDifferenceSum;
		}
	}

	public CrossSection GetCrossSection(int xsId)
	{
		return CrossSections.Where([SpecialName] (CrossSection xs) => xs.ID == xsId).FirstOrDefault();
	}

	public CrossSection GetCrossSection(CrossSection xs2match)
	{
		return CrossSections.Where([SpecialName] (CrossSection xs) => Operators.CompareString(xs.Reach.River.Name, xs2match.Reach.River.Name, TextCompare: false) == 0 && Operators.CompareString(xs.Reach.Name, xs2match.Reach.Name, TextCompare: false) == 0 && xs.RiverStationComputed_WithRMM == xs2match.RiverStationComputed_WithRMM).FirstOrDefault();
	}

	public List<CrossSection> GetCrossSectionsOnReach(string rchName)
	{
		return (from xs in CrossSections
			where Operators.CompareString(xs.Reach?.Name, rchName, TextCompare: false) == 0
			orderby xs.RiverStationComputed_WithRMM descending
			select xs).ToList();
	}

	public List<CrossSection> GetCrossSectionsOnRiver(string riverName)
	{
		List<Reach> reachesUpstreamToDownstream = GetRiver(riverName).GetReachesUpstreamToDownstream();
		List<CrossSection> crossSections = new List<CrossSection>();
		foreach (Reach reachesUpstreamToDownstream2 in reachesUpstreamToDownstream)
		{
			crossSections.AddRange(GetCrossSectionsOnReach(reachesUpstreamToDownstream2.Name));
		}
		return crossSections.OrderByDescending([SpecialName] (CrossSection xs) => xs.RiverStationComputed_WithRMM).ToList();
	}

	public CrossSection GetXSonRiverbyRS(string riverName, double xsRS)
	{
		List<CrossSection> crossSectionsOnRiver = GetCrossSectionsOnRiver(riverName);
		foreach (CrossSection crossSectionsOnRiver2 in crossSectionsOnRiver)
		{
			if (crossSectionsOnRiver2.RiverStationUser == xsRS)
			{
				return crossSectionsOnRiver2;
			}
		}
		return null;
	}

	public CrossSection GetXSonRiverbyRSName(string riverName, string xsRSName)
	{
		List<CrossSection> crossSectionsOnRiver = GetCrossSectionsOnRiver(riverName);
		foreach (CrossSection crossSectionsOnRiver2 in crossSectionsOnRiver)
		{
			if (Operators.CompareString(crossSectionsOnRiver2.Name, xsRSName, TextCompare: false) == 0)
			{
				return crossSectionsOnRiver2;
			}
		}
		return null;
	}

	public void RenameXS(string riverName, double xsRS, double newRS)
	{
		List<CrossSection> crossSectionsOnRiver = GetCrossSectionsOnRiver(riverName);
		foreach (CrossSection crossSectionsOnRiver2 in crossSectionsOnRiver)
		{
			if (crossSectionsOnRiver2.RiverStationUser == xsRS)
			{
				crossSectionsOnRiver2.RiverStationUser = newRS;
				break;
			}
		}
	}

	public bool ContainsRiver(string rivName)
	{
		return GetRiver(rivName) != null;
	}

	public River GetRiver(string rivName)
	{
		return Rivers.FirstOrDefault([SpecialName] (River r) => Operators.CompareString(r.Name, rivName, TextCompare: false) == 0);
	}

	public bool ContainsReach(River river, string reachName)
	{
		return GetReach(river, reachName) != null;
	}

	public Reach GetReach(string riverName, string reachName)
	{
		return GetReach(GetRiver(riverName), reachName);
	}

	public Reach GetReach(int fid)
	{
		foreach (River river in Rivers)
		{
			foreach (Reach reach in river.Reaches)
			{
				if (reach.ID == fid)
				{
					return reach;
				}
			}
		}
		return null;
	}

	public Reach GetReach(River river, string reachName)
	{
		return river?.Reaches.FirstOrDefault([SpecialName] (Reach r) => Operators.CompareString(r.Name, reachName, TextCompare: false) == 0);
	}

	public bool Contains2DArea(string d2Name)
	{
		return Get2DArea(d2Name) != null;
	}

	public Mesh Get2DArea(string d2Name)
	{
		return Meshes.FirstOrDefault([SpecialName] (Mesh r) => Operators.CompareString(r.Name, d2Name, TextCompare: false) == 0);
	}
}
