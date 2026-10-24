using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Vectors;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Systems;

namespace RasMapperLib;

public class RASJunction : PointFeatureLayer, IGeometryLayer
{
	private class JunctionShell
	{
		public List<int> UpstreamRivers;

		public List<int> DownstreamRivers;

		public PointM Centroid;

		public JunctionShell()
		{
			UpstreamRivers = new List<int>();
			DownstreamRivers = new List<int>();
		}

		public bool HasRiver(int rivID)
		{
			if (!UpstreamRivers.Contains(rivID))
			{
				return DownstreamRivers.Contains(rivID);
			}
			return true;
		}
	}

	public const string sDescription = "Description";

	public const string sMomentum = "Use Momentum";

	public const string sAddWeight = "Add Weight";

	public const string sAddFriction = "Add Friction";

	public const string sUnsteadyEnergy = "Unsteady Energy";

	private const string HDFGroupName = "Geometry/Junctions";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override string DefaultLayerName => "Junctions";
string IGeometryLayer.HDFGroupName=> "Geometry/Junctions";

	public RASJunction(RASGeometry geometry)
		: base("Junctions", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Junction";
		Geometry = geometry;
	}

	public RASJunction()
		: base("Junctions")
	{
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Junction";
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Description", "Description", typeof(string));
		AddManagedAttributeColumn("Use Momentum", "Momentum", typeof(bool));
		AddManagedAttributeColumn("Add Weight", "Add Weight", typeof(bool));
		AddManagedAttributeColumn("Add Friction", "Add Friction", typeof(bool));
		AddManagedAttributeColumn("Unsteady Energy", "Unsteady Energy", typeof(bool));
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Junctions"))
		{
			string path = "Geometry/Junctions/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				HDF5Storage.ReadPointFeatures(this, hr, "Geometry/Junctions/Points");
				ReadManagedColumns(hr, path);
			}
			else
			{
				HDF5Storage.ReadPointFeatures(this, hr, "Geometry/Junctions/Point");
				HDFLoadFeatureTable_504(hr);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureTable().Rows.Count == 0)
		{
			hw.Delete("Geometry/Junctions");
			return;
		}
		hw.CreateGroup("Geometry/Junctions");
		HDF5Storage.WritePointFeatures(this, hw, "Geometry/Junctions/Points");
		WriteManagedColumns(hw, "Geometry/Junctions/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	private void HDFLoadFeatureTable_504(H5Reader hr)
	{
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Junctions/Names", "Name");
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Point(i).Stream(bw);
				Pipe.WriteString(bw, GetFeatureName(i));
			}
		}
	}

	public static RASJunction Stream(BinaryReader br, RASGeometry geom)
	{
		RASJunction rASJunction = new RASJunction(geom);
		rASJunction.InitializeEmptyFeatureTable();
		rASJunction.SuppressTableReload(boolValue: true);
		rASJunction.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Point feature = RasMapperLib.Point.Stream(br);
				rASJunction.AddFeature(feature);
				rASJunction.SetFeatureName(i, Pipe.ReadString(br));
			}
			return rASJunction;
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType != FeatureChangedType.FeatureAdded && args.ChangeType != FeatureChangedType.FeatureModified)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			if (string.IsNullOrEmpty(GetFeatureName(featureIndicesChanged)))
			{
				SetFeatureName(featureIndicesChanged, GetUniqueFeatureName());
			}
		}
	}

	private void ReevaluateConnections(int jxnIdx)
	{
		double computed = 100.0;
		PointM ptM = Point(jxnIdx).PointM();
		RASRiver river = Geometry.River;
		checked
		{
			int count = river.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = river.Polyline(i);
				if (polyline != null && polyline.Count >= 2)
				{
					PointM pointM = polyline.FirstPointM();
					polyline.LastPointM();
					double distanceTo = pointM.DistanceTo(ptM);
					double distanceTo2 = pointM.DistanceTo(ptM);
					if ((!(distanceTo < distanceTo2) || !(distanceTo < computed)) && !(distanceTo2 < distanceTo))
					{
					}
				}
			}
		}
	}

	private List<JunctionShell> ComputePotentialJunctions()
	{
		double radius = 100.0;
		List<JunctionShell> junctionShells = new List<JunctionShell>();
		RASRiver river = Geometry.River;
		if (river.FeatureCount() == 0)
		{
			return junctionShells;
		}
		NeighborhoodSpatialIndex<Tuple<int, bool>> neighborhoodSpatialIndex = new NeighborhoodSpatialIndex<Tuple<int, bool>>(radius);
		checked
		{
			int count = river.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = river.Polyline(i);
				if (polyline != null && polyline.Count >= 2)
				{
					PointM pointM = polyline.FirstPointM();
					PointM pointM2 = polyline.LastPointM();
					neighborhoodSpatialIndex.Add(new Geospatial.Vectors.Point(pointM.X, pointM.Y), new Tuple<int, bool>(i, item2: true));
					neighborhoodSpatialIndex.Add(new Geospatial.Vectors.Point(pointM2.X, pointM2.Y), new Tuple<int, bool>(i, item2: false));
				}
			}
			foreach (Neighborhood<Tuple<int, bool>> neighborhood in neighborhoodSpatialIndex.Neighborhoods)
			{
				JunctionShell junctionShell = new JunctionShell();
				double computed = 0.0;
				double computed2 = 0.0;
				foreach (Tuple<Geospatial.Vectors.Point, Tuple<int, bool>> tuple in neighborhood.Items)
				{
					computed += tuple.Item1.X;
					computed2 += tuple.Item1.Y;
					if (tuple.Item2.Item2)
					{
						junctionShell.DownstreamRivers.Add(tuple.Item2.Item1);
					}
					else
					{
						junctionShell.UpstreamRivers.Add(tuple.Item2.Item1);
					}
				}
				int neighborhoodTerm = neighborhood.Items.Count();
				junctionShell.Centroid = new PointM(computed / (double)neighborhoodTerm, computed2 / (double)neighborhoodTerm);
				junctionShells.Add(junctionShell);
			}
			return junctionShells;
		}
	}

	public List<XStoJunction> XSsAroundJunction(int junctionID)
	{
		List<XStoJunction> xStoJunctions = new List<XStoJunction>();
		string featureName = GetFeatureName(junctionID);
		if (string.IsNullOrEmpty(featureName))
		{
			return xStoJunctions;
		}
		PointM junctionPoint = Points().ElementAtOrDefault(junctionID).PointM();
		RASGeometry geometry = Geometry;
		RASRiver river = geometry.River;
		RASXS xS = geometry.XS;
		PointMs pointMs = new PointMs();
		checked
		{
			int count = river.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(river.GetUpstreamJunction(i), featureName, TextCompare: false) == 0)
				{
					List<PolylineIntersection> riverXSIntersections = geometry.RiverXSIntersections(i);
					if (riverXSIntersections.Count > 0)
					{
						PolylineIntersection intersection = riverXSIntersections.First();
						int bPolylineIndex = intersection.BPolylineIndex;
						Polyline xSPolyline = xS.Polyline(bPolylineIndex);
						river.Polyline(i).PointsBeforePoP(intersection.APointOnPolyline, pointMs);
						pointMs.Reverse();
						xStoJunctions.Add(new XStoJunction(i, intersection, bPolylineIndex, xSPolyline, XSupstreamOfJunction: false, pointMs, junctionPoint));
					}
				}
				if (Operators.CompareString(river.GetDownstreamJunction(i), featureName, TextCompare: false) == 0)
				{
					List<PolylineIntersection> riverXSIntersectionsList = geometry.RiverXSIntersections(i);
					if (riverXSIntersectionsList.Count > 0)
					{
						PolylineIntersection intersection2 = riverXSIntersectionsList.Last();
						int bPolylineIndex2 = intersection2.BPolylineIndex;
						Polyline xSPolyline2 = xS.Polyline(bPolylineIndex2);
						river.Polyline(i).PointsAfterPoP(intersection2.APointOnPolyline, pointMs);
						xStoJunctions.Add(new XStoJunction(i, intersection2, bPolylineIndex2, xSPolyline2, XSupstreamOfJunction: true, pointMs, junctionPoint));
					}
				}
			}
			if (xStoJunctions.Count > 0)
			{
				xStoJunctions.Sort(XStoJunction.CompareBearing);
			}
			return xStoJunctions;
		}
	}

	public string EnsureValidJunctionName(string jxnName)
	{
		string jxnName2 = jxnName;
		if (jxnName.Length > 16)
		{
			jxnName2 = Strings.Trim(jxnName2.Substring(0, 16));
		}
		RASJunction junctions = Geometry.Junctions;
		if (junctions.ContainsFeatureName(jxnName))
		{
			jxnName2 = junctions.GetUniqueFeatureName();
		}
		return jxnName2;
	}

	public void TransferJunctionTo(RASJunction otherLayer, int curFID, int otherFID)
	{
		otherLayer.SetFeatureName(otherFID, GetFeatureName(curFID));
		otherLayer.SetDescription(otherFID, GetDescription(curFID));
		otherLayer.SetMomentum(otherFID, GetMomentum(curFID));
		otherLayer.SetAddWeight(otherFID, GetAddWeight(curFID));
		otherLayer.SetAddFriction(otherFID, GetAddFriction(curFID));
		otherLayer.SetUnsteadyEnergy(otherFID, GetUnsteadyEnergy(curFID));
	}

	public string GetDescription(int xsid)
	{
		return DataTableExtensions.GetString(FeatureTable(), xsid, "Description");
	}

	private void SetDescription(int xsid, string sta)
	{
		FeatureRow(xsid)["Description"] = sta;
	}

	public bool GetMomentum(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Use Momentum");
	}

	private void SetMomentum(int xsid, bool m)
	{
		FeatureRow(xsid)["Use Momentum"] = m;
	}

	public bool GetAddWeight(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Add Weight");
	}

	private void SetAddWeight(int xsid, bool m)
	{
		FeatureRow(xsid)["Add Weight"] = m;
	}

	public bool GetAddFriction(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Add Friction");
	}

	private void SetAddFriction(int xsid, bool m)
	{
		FeatureRow(xsid)["Add Friction"] = m;
	}

	public bool GetUnsteadyEnergy(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Unsteady Energy");
	}

	private void SetUnsteadyEnergy(int xsid, bool m)
	{
		FeatureRow(xsid)["Unsteady Energy"] = m;
	}
}
