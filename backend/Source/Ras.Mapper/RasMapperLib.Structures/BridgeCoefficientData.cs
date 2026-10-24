using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib.Structures;

public class BridgeCoefficientData
{
	private class Cols
	{
		public class WSPro
		{
			public const string Use = "Use WSPro";

			public const string ElTopL = "WSPro El Top L";

			public const string ElTopR = "WSPro El Top R";

			public const string ElToeL = "WSPro El Toe L";

			public const string ElToeR = "WSPro El Toe R";

			public const string Type = "WSPro Type";

			public const string Slope = "WSPro Slope";

			public const string Width = "WSPro Width";

			public const string CentroidSta = "WSPro Centroid Sta";

			public const string WingWallType = "WSPro Wing Wall Type";

			public const string WingWallWidth = "WSPro Wing Wall Width";

			public const string WingWallAngle = "WSPro Wing Wall Angle";

			public const string WingWallRadius = "WSPro Wing Wall Radius";

			public const string GuideBanksType = "WSPro Guide Banks Type";

			public const string GuideBanksLength = "WSPro Guide Banks Length";

			public const string GuideBanksOffset = "WSPro Guide Banks Offset";

			public const string GuideBanksAngle = "WSPro Guide Banks Angle";

			public const string PiersContinuous = "WSPro Piers Continuous";

			public const string SfGeomMean = "WSPro Sf Geom Mean";

			public const string UseTables = "WSPro Use Tables";

			public const string UseCE_Approach = "WSPro Use C/E Approach";

			public const string UseCE_GuideBanks = "WSPro Use C/E Guide Banks";

			public const string UseCE_USXS = "WSPro Use C/E US XS";

			public const string UseCE_USBR = "WSPro Use C/E US BR";

			public const string UseCE_DSBR = "WSPro Use C/E DS BR";
		}

		public const string StructID = "Structure ID";

		public const string Method = "Method";

		public const string LowStandardStep = "Low Standard Step";

		public const string UseMomentum = "Use Momentum";

		public const string MomentumCd = "Momentum Cd";

		public const string UseYarnell = "Use Yarnell";

		public const string YarnellK = "Yarnell K";

		public const string UseHighStandardStep = "Use High Standard Step";

		public const string SubmergedInletCd = "Submerged Inlet Cd";

		public const string SubmergedInletOutletCd = "Submerged Inlet-Outlet Cd";

		public const string LowCordWeirCheck = "Low Cord Weir Check";
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__7_002D0
	{
		public H5Writer hw;

		public string attrDset;

		public _Closure_0024__7_002D0(_Closure_0024__7_002D0 other)
		{
			if (other != null)
			{
				hw = other.hw;
				attrDset = other.attrDset;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			if (hw.ExistsAsDataset(attrDset))
			{
				hw.Delete(attrDset);
			}
		}
	}

	private const string AttributesDatasetName = "Bridge Coefficient Attributes";

	private static DatatableLoadManager _dtManager;

	public DataRow Row;

	static BridgeCoefficientData()
	{
		_dtManager = new DatatableLoadManager();
		_dtManager.Add("Structure ID", typeof(int), outputMessageIfNotFound: true);
		_dtManager.Add("Method", typeof(int));
		_dtManager.Add("Low Standard Step", typeof(bool));
		_dtManager.Add("Use Momentum", typeof(bool));
		_dtManager.Add("Momentum Cd", typeof(float));
		_dtManager.Add("Use Yarnell", typeof(bool));
		_dtManager.Add("Yarnell K", typeof(float));
		_dtManager.Add("Use WSPro", typeof(bool));
		_dtManager.Add("WSPro El Top L", typeof(float));
		_dtManager.Add("WSPro El Top R", typeof(float));
		_dtManager.Add("WSPro El Toe L", typeof(float));
		_dtManager.Add("WSPro El Toe R", typeof(float));
		_dtManager.Add("WSPro Type", typeof(int));
		_dtManager.Add("WSPro Slope", typeof(float));
		_dtManager.Add("WSPro Width", typeof(float));
		_dtManager.Add("WSPro Centroid Sta", typeof(float));
		_dtManager.Add("WSPro Wing Wall Type", typeof(int));
		_dtManager.Add("WSPro Wing Wall Width", typeof(float));
		_dtManager.Add("WSPro Wing Wall Angle", typeof(float));
		_dtManager.Add("WSPro Wing Wall Radius", typeof(float));
		_dtManager.Add("WSPro Guide Banks Type", typeof(int));
		_dtManager.Add("WSPro Guide Banks Length", typeof(float));
		_dtManager.Add("WSPro Guide Banks Offset", typeof(float));
		_dtManager.Add("WSPro Guide Banks Angle", typeof(float));
		_dtManager.Add("WSPro Piers Continuous", typeof(bool));
		_dtManager.Add("WSPro Sf Geom Mean", typeof(bool));
		_dtManager.Add("WSPro Use Tables", typeof(bool));
		_dtManager.Add("WSPro Use C/E Approach", typeof(bool));
		_dtManager.Add("WSPro Use C/E Guide Banks", typeof(bool));
		_dtManager.Add("WSPro Use C/E US XS", typeof(bool));
		_dtManager.Add("WSPro Use C/E US BR", typeof(bool));
		_dtManager.Add("WSPro Use C/E DS BR", typeof(bool));
		_dtManager.Add("Use High Standard Step", typeof(bool));
		_dtManager.Add("Submerged Inlet Cd", typeof(float));
		_dtManager.Add("Submerged Inlet-Outlet Cd", typeof(float));
		_dtManager.Add("Low Cord Weir Check", typeof(float));
	}

	public BridgeCoefficientData(DataRow row)
	{
		Row = row;
	}

	public static Dictionary<int, List<BridgeCoefficientData>> ReadCollection(H5Reader hr, string h5Group)
	{
		Dictionary<int, List<BridgeCoefficientData>> collection = new Dictionary<int, List<BridgeCoefficientData>>();
		string h5GroupTerm = h5Group + "/Bridge Coefficient Attributes";
		if (!hr.ExistsAsDataset(h5GroupTerm))
		{
			return collection;
		}
		DataTable dataTable = new DataTable();
		_dtManager.InitializeDataTable(dataTable);
		_dtManager.Read(hr, h5GroupTerm, dataTable);
		int count = dataTable.Rows.Count;
		if (count == 0)
		{
			return collection;
		}
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				DataRow dataRow = dataTable.Rows[i];
				object objectValue = RuntimeHelpers.GetObjectValue(dataRow["Structure ID"]);
				if (!Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) && objectValue != null)
				{
					int key = Conversions.ToInteger(objectValue);
					BridgeCoefficientData dataRow2 = new BridgeCoefficientData(dataRow);
					List<BridgeCoefficientData> bridgeCoefficientDatas = null;
					if (!collection.TryGetValue(key, out bridgeCoefficientDatas))
					{
						bridgeCoefficientDatas = (collection[key] = new List<BridgeCoefficientData>());
					}
					bridgeCoefficientDatas.Add(dataRow2);
				}
			}
			return collection;
		}
	}

	public static void WriteCollection(H5Writer hw, string h5Group, List<BridgeCoefficientData>[] map)
	{
		_Closure_0024__7_002D0 capturedLocals = default;
		_Closure_0024__7_002D0 capturedLocals2 = new _Closure_0024__7_002D0(capturedLocals);
		capturedLocals2.hw = hw;
		capturedLocals2.attrDset = h5Group + "/Bridge Coefficient Attributes";
		Action action = [SpecialName] () =>
		{
			if (capturedLocals2.hw.ExistsAsDataset(capturedLocals2.attrDset))
			{
				capturedLocals2.hw.Delete(capturedLocals2.attrDset);
			}
		};
		if (map.Length == 0)
		{
			action();
			return;
		}
		if (map.Where([SpecialName] (List<BridgeCoefficientData> ab) => ab != null).SelectMany([SpecialName] (List<BridgeCoefficientData> ab) => ab).ToList()
			.Count == 0)
		{
			action();
			return;
		}
		if (!capturedLocals2.hw.ExistsAsGroup(h5Group))
		{
			capturedLocals2.hw.CreateGroup(h5Group);
		}
		DataTable dataTable = new DataTable();
		_dtManager.InitializeDataTable(dataTable);
		checked
		{
			int count = map.Count() - 1;
			for (int dataRow2 = 0; dataRow2 <= count; dataRow2++)
			{
				List<BridgeCoefficientData> mapList = map[dataRow2];
				if (mapList.IsNullOrEmpty())
				{
					continue;
				}
				foreach (BridgeCoefficientData map2 in mapList)
				{
					DataRow dataRow = dataTable.NewRow();
					dataRow.ItemArray = (object[])map2.Row.ItemArray.Clone();
					dataRow["Structure ID"] = dataRow2;
					dataTable.Rows.Add(dataRow);
				}
			}
			_dtManager.Write(capturedLocals2.hw, capturedLocals2.attrDset, dataTable);
		}
	}
}
