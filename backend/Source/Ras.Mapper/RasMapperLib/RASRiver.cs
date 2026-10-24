using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Render;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Systems;

namespace RasMapperLib;

public class RASRiver : PolylineFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/River Centerlines";

	public const string sHDFStationingInfo = "Geometry/River Centerlines/User Stationing Info";

	public const string sHDFStationingValues = "Geometry/River Centerlines/User Stationing Values";

	public const string sUSType = "US Type";

	public const string sDSType = "DS Type";

	public const string sUSName = "US Name";

	public const string sDSName = "DS Name";

	public const string sUSXSDistance = "US XS Distance";

	public const string sDSXSDistance = "DS XS Distance";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Rivers";
string IGeometryLayer.HDFGroupName=> "Geometry/River Centerlines";

	public override HashSet<string> PasteAttributeBlackList
	{
		get
		{
			HashSet<string> pasteAttributeBlackList = base.PasteAttributeBlackList;
			HashSetExtensionMethods.AddRange(pasteAttributeBlackList, new string[4] { "US Type", "US Name", "DS Type", "DS Name" });
			return pasteAttributeBlackList;
		}
	}

	public RASRiver(RASGeometry geometry)
		: base("Rivers", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "River";
		CanRename = false;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnGroup dataColumnGroup = new DataColumnGroup("River, Reach", isVisible: true);
		AddManagedAttributeColumn("River", "River Name", typeof(string), dataColumnGroup);
		AddManagedAttributeColumn("Reach", "Reach Name", typeof(string), dataColumnGroup);
		DataColumnGroup dataColumnGroup2 = new DataColumnGroup("US/DS Connections", isVisible: false);
		AddManagedAttributeColumn("US Type", "US Type", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("US Name", "US Name", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS Type", "DS Type", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS Name", "DS Name", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("US XS Distance", "Junction to US XS", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS XS Distance", "DS XS to Junction", typeof(float), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/River Centerlines"))
		{
			HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/River Centerlines/Polyline");
			string path = "Geometry/River Centerlines/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				ReadManagedColumns(hr, path);
			}
			else
			{
				HDFLoadFeatureTable_503(hr);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFLoadFeatureTable_503(H5Reader hr)
	{
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/River Centerlines/River Names", "River");
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/River Centerlines/Reach Names", "Reach");
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureTable().Rows.Count == 0)
		{
			hw.Delete("Geometry/River Centerlines");
			return;
		}
		hw.CreateGroup("Geometry/River Centerlines");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/River Centerlines/Polyline");
		WriteManagedColumns(hw, "Geometry/River Centerlines/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		foreach (int orderBy in args.FeatureIndicesChanged.OrderBy([SpecialName] (int a) => a))
		{
			if (args.ChangeType == FeatureChangedType.FeatureAdded)
			{
				SetupNewRiver(orderBy);
			}
			else if (args.ChangeType != FeatureChangedType.FeatureDeleted)
			{
				_ = args.ChangeType;
			}
		}
	}

	private void SetupNewRiver(int rivId)
	{
		SetRiverName(rivId, GetUniqueColumnStringValue("River", "River"));
		SetReachName(rivId, GetUniqueColumnStringValue("Reach", "Reach"));
		SetIsDefaultName(rivId, isDefaultName: true);
		SetUpstreamReachConnection(rivId, "External", "");
		SetDownstreamReachConnection(rivId, "External", "");
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline(i).Stream(bw);
				Pipe.WriteString(bw, GetRiverName(i));
				Pipe.WriteString(bw, GetReachName(i));
				Pipe.WriteString(bw, GetUpstreamType(i));
				Pipe.WriteString(bw, GetUpstreamName(i));
				Pipe.WriteString(bw, GetDownstreamType(i));
				Pipe.WriteString(bw, GetDownstreamName(i));
				bw.Write(GetUpstreamXSDistance(i));
				bw.Write(GetDownstreamXSDistance(i));
			}
		}
	}

	public static RASRiver Stream(BinaryReader br, RASGeometry geom)
	{
		RASRiver rASRiver = new RASRiver(geom);
		rASRiver.InitializeEmptyFeatureTable();
		rASRiver.SuppressTableReload(boolValue: true);
		rASRiver.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline feature = RasMapperLib.Polyline.Stream(br);
				rASRiver.AddFeature(feature);
				rASRiver.SetRiverName(i, Pipe.ReadString(br));
				rASRiver.SetReachName(i, Pipe.ReadString(br));
				rASRiver.SetUpstreamType(i, Pipe.ReadString(br));
				rASRiver.SetUpstreamName(i, Pipe.ReadString(br));
				rASRiver.SetDownstreamType(i, Pipe.ReadString(br));
				rASRiver.SetDownstreamName(i, Pipe.ReadString(br));
				rASRiver.SetUpstreamXSDistance(i, br.ReadSingle());
				rASRiver.SetDownstreamXSDistance(i, br.ReadSingle());
			}
			return rASRiver;
		}
	}

	public override string GetFeatureName(int fid)
	{
		return GetRivReachName(fid);
	}

	public string GetRivReachName(int rivId)
	{
		return GetRiverName(rivId) + " - " + GetReachName(rivId);
	}

	public List<string> GetAllRiverNames()
	{
		List<string> allRiverNames = new List<string>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string riverName = GetRiverName(i);
				if (!allRiverNames.Contains(riverName))
				{
					allRiverNames.Add(riverName);
				}
			}
			return allRiverNames;
		}
	}

	public string GetRiverName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "River");
	}

	public void SetRiverName(int fid, string rivName)
	{
		FeatureRow(fid)["River"] = rivName;
		SetIsDefaultName(fid, isDefaultName: false);
	}

	public string GetReachName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Reach");
	}

	public void SetReachName(int fid, string rchName)
	{
		FeatureRow(fid)["Reach"] = rchName;
		SetIsDefaultName(fid, isDefaultName: false);
	}

	public string GetUpstreamName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US Name");
	}

	public void SetUpstreamName(int fid, string name)
	{
		FeatureRow(fid)["US Name"] = name;
	}

	public string GetDownstreamName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS Name");
	}

	public void SetDownstreamName(int fid, string name)
	{
		FeatureRow(fid)["DS Name"] = name;
	}

	public string GetUpstreamType(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US Type");
	}

	public void SetUpstreamType(int fid, string type)
	{
		FeatureRow(fid)["US Type"] = type;
	}

	public string GetDownstreamType(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS Type");
	}

	public void SetDownstreamType(int fid, string type)
	{
		FeatureRow(fid)["DS Type"] = type;
	}

	public bool UpstreamIsJunction(int fid)
	{
		if (Operators.CompareString(GetUpstreamType(fid), "Junction", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool DownstreamIsJunction(int fid)
	{
		if (Operators.CompareString(GetDownstreamType(fid), "Junction", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool UpstreamIsStorageArea(int fid)
	{
		if (Operators.CompareString(GetUpstreamType(fid), "Storage Area", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool DownstreamIsStorageArea(int fid)
	{
		if (Operators.CompareString(GetDownstreamType(fid), "Storage Area", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool UpstreamIs2DArea(int fid)
	{
		if (Operators.CompareString(GetUpstreamType(fid), "2D Flow Area", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool DownstreamIs2DArea(int fid)
	{
		if (Operators.CompareString(GetDownstreamType(fid), "2D Flow Area", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool UpstreamIsExternal(int fid)
	{
		if (Operators.CompareString(GetUpstreamType(fid), "External", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool DownstreamIsExternal(int fid)
	{
		if (Operators.CompareString(GetDownstreamType(fid), "External", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}

	public string GetUpstreamJunction(int fid)
	{
		return GetReachConnection(fid, "US Type", "US Name", "Junction");
	}

	public void SetUpstreamJunction(int fid, string jxnName)
	{
		SetUpstreamReachConnection(fid, "Junction", jxnName);
	}

	public string GetDownstreamJunction(int fid)
	{
		return GetReachConnection(fid, "DS Type", "DS Name", "Junction");
	}

	public void SetDownstreamJunction(int fid, string jxnName)
	{
		SetDownstreamReachConnection(fid, "Junction", jxnName);
	}

	public string GetUpstreamStorageArea(int fid)
	{
		return GetReachConnection(fid, "US Type", "US Name", "Storage Area");
	}

	public void SetUpstreamStorageArea(int fid, string saName)
	{
		SetUpstreamReachConnection(fid, "Storage Area", saName);
	}

	public string GetDownstreamStorageArea(int fid)
	{
		return GetReachConnection(fid, "DS Type", "DS Name", "Storage Area");
	}

	public void SetDownstreamStorageArea(int fid, string saName)
	{
		SetDownstreamReachConnection(fid, "Storage Area", saName);
	}

	public string GetUpstreamD2FlowArea(int fid)
	{
		return GetReachConnection(fid, "US Type", "US Name", "2D Flow Area");
	}

	public void SetUpstreamD2FlowArea(int fid, string d2Name)
	{
		SetUpstreamReachConnection(fid, "2D Flow Area", d2Name);
	}

	public string GetDownstreamD2FlowArea(int fid)
	{
		return GetReachConnection(fid, "DS Type", "DS Name", "2D Flow Area");
	}

	public void SetDownstreamD2FlowArea(int fid, string d2Name)
	{
		SetDownstreamReachConnection(fid, "2D Flow Area", d2Name);
	}

	public void SetUpstreamExternal(int fid)
	{
		SetUpstreamReachConnection(fid, "External", "");
	}

	public void SetDownstreamExternal(int fid)
	{
		SetDownstreamReachConnection(fid, "External", "");
	}

	public float GetUpstreamXSDistance(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "US XS Distance");
	}

	public void SetUpstreamXSDistance(int fid, float distance)
	{
		FeatureRow(fid)["US XS Distance"] = System.Math.Round(distance, SharedData.HorizontalDecimalPlaces);
	}

	public float GetDownstreamXSDistance(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "DS XS Distance");
	}

	public void SetDownstreamXSDistance(int fid, float distance)
	{
		FeatureRow(fid)["DS XS Distance"] = System.Math.Round(distance, SharedData.HorizontalDecimalPlaces);
	}

	private string GetReachConnection(int fid, string USDSconnTypeColumn, string USDSconnNameColumn, string connTypeExpected)
	{
		DataTable dataTable = FeatureTable();
		DataRow row = dataTable.Rows[fid];
		if (Operators.CompareString(Form.DataRowItemToString(row, dataTable.Columns.IndexOf(USDSconnTypeColumn)), connTypeExpected, TextCompare: false) == 0)
		{
			return Form.DataRowItemToString(row, dataTable.Columns.IndexOf(USDSconnNameColumn));
		}
		return "";
	}

	private void SetUpstreamReachConnection(int fid, string connType, string connName)
	{
		DataRow dataRow = FeatureRow(fid);
		dataRow["US Type"] = connType;
		dataRow["US Name"] = connName;
	}

	private void SetDownstreamReachConnection(int fid, string connType, string connName)
	{
		DataRow dataRow = FeatureRow(fid);
		dataRow["DS Type"] = connType;
		dataRow["DS Name"] = connName;
	}

	public void ClearUpstreamConnection(int fid)
	{
		SetUpstreamReachConnection(fid, "", "");
	}

	public void ClearDownstreamConnection(int fid)
	{
		SetDownstreamReachConnection(fid, "", "");
	}

	public int GetFID(string rivName, string rchName)
	{
		if (string.IsNullOrEmpty(rivName))
		{
			return -1;
		}
		if (string.IsNullOrEmpty(rchName))
		{
			return -1;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(GetRiverName(i), rivName, TextCompare: false) == 0 && Operators.CompareString(GetReachName(i), rchName, TextCompare: false) == 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	public DataRow ReachRow(string river, string reach)
	{
		DataRow[] dataRows = FeatureTable().Select("River='" + river + "' AND Reach='" + reach + "'");
		if (dataRows.Count() == 1)
		{
			return dataRows[0];
		}
		return null;
	}

	public Polyline GetCombinedRiverPolyline(string rivName, RiverMap rivMap = null)
	{
		if (rivMap == null)
		{
			rivMap = Geometry.CreateRiverMap(loadSpatialIntersectionData: false);
		}
		foreach (RiverMap.River river in rivMap.Rivers)
		{
			if (Operators.CompareString(river.Name, rivName, TextCompare: false) != 0)
			{
				continue;
			}
			List<RiverMap.Reach> reachesDownstreamToUpstream = river.GetReachesDownstreamToUpstream();
			reachesDownstreamToUpstream.Reverse();
			PointMs pointMs = new PointMs();
			foreach (RiverMap.Reach reachesDownstreamToUpstream2 in reachesDownstreamToUpstream)
			{
				Polyline points = Polyline(reachesDownstreamToUpstream2.ID);
				pointMs.AddRange(points);
			}
			return new Polyline(pointMs);
		}
		return null;
	}

	public float[] GetSummedFlowTimeSeries(Polyline pline, ref float[] volume_aggregation, [Optional][DefaultParameterValue(null)] ref List<int> crossSectionsUsed)
	{
		if (Geometry.Results == null)
		{
			return null;
		}
		checked
		{
			float[] floatValues = new float[Geometry.Results.ProfileCount - 1 + 1];
			volume_aggregation = new float[Geometry.Results.ProfileCount - 1 + 1];
			float mappingDTSeconds = Geometry.Results.MappingDTSeconds;
			HashSet<int> intSet = new HashSet<int>();
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = Polyline(i);
				if (!RasMapperLib.Polyline.IsValidPolyline(polyline))
				{
					continue;
				}
				SpatialIndex<int> segmentSpIdx = null;
				List<PointIntersect> lineIntersectionMulti = polyline.GetLineIntersectionMulti(pline, ref segmentSpIdx);
				if (lineIntersectionMulti.IsNullOrEmpty())
				{
					continue;
				}
				foreach (PointIntersect lineIntersectionMulti2 in lineIntersectionMulti)
				{
					PointOnPolyline aPointOnPolyline = lineIntersectionMulti2.APointOnPolyline;
					int nearestCrossSection = Geometry.FindNearestCrossSection(i, aPointOnPolyline);
					if (nearestCrossSection == -1 || intSet.Contains(nearestCrossSection))
					{
						continue;
					}
					intSet.Add(nearestCrossSection);
					float[] read1DTimeSeries = FlowRenderer.Read1DTimeSeries(Geometry.Results, nearestCrossSection);
					SegmentM segmentM = pline.Segment(lineIntersectionMulti2.BSegmentIndex);
					SegmentM segmentM2 = polyline.Segment(lineIntersectionMulti2.ASegmentIndex);
					float crossProductVectorTerm = 0f;
					crossProductVectorTerm = ((!(Vector.CrossProduct(segmentM.A, segmentM.B, segmentM2.B) > 0.0)) ? (-1f) : 1f);
					float computed = 0f;
					int count2 = read1DTimeSeries.Count() - 1;
					for (int j = 0; j <= count2; j++)
					{
						float read1DTimeSeriesTerm = read1DTimeSeries[j] * crossProductVectorTerm;
						floatValues[j] += read1DTimeSeriesTerm;
						if (j > 0)
						{
							float computed2;
							float computed3;
							if (computed > read1DTimeSeriesTerm)
							{
								computed2 = read1DTimeSeriesTerm;
								computed3 = computed;
							}
							else
							{
								computed2 = computed;
								computed3 = read1DTimeSeriesTerm;
							}
							volume_aggregation[j] += (float)((double)(computed2 * mappingDTSeconds) + 0.5 * (double)mappingDTSeconds * (double)(computed3 - computed2));
						}
						computed = read1DTimeSeriesTerm;
					}
				}
			}
			crossSectionsUsed = intSet.ToList();
			return floatValues;
		}
	}
}
