using System.Data;
using System.Runtime.CompilerServices;
using Utility;

namespace RasMapperLib;

public class ManningsNPolygonLayer : PolygonFeatureLayer
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__19_002D0
	{
		public int[] regions;

		public _Closure_0024__19_002D0(_Closure_0024__19_002D0 other)
		{
			if (other != null)
			{
				regions = other.regions;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__19_002D1
	{
		public int fID;

		public _Closure_0024__19_002D0 outerClosure;

		public _Closure_0024__19_002D1(_Closure_0024__19_002D1 other)
		{
			if (other != null)
			{
				fID = other.fID;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			outerClosure.regions[n] = fID;
		}
	}

	public const string sManningsN = "n_Value";

	public override string SimpleFeatureTypeName { get; set; }

	public override string DefaultLayerName => "Manning's n Polygons";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public ManningsNPolygonLayer()
		: base("Manning's n Polygons")
	{
		SimpleFeatureTypeName = "Manning's Region";
		FeaturesRequireUniqueNames = true;
	}

	public ManningsNPolygonLayer(string name)
		: base(name)
	{
		SimpleFeatureTypeName = "Manning's Region";
		FeaturesRequireUniqueNames = true;
	}

	public ManningsNPolygonLayer(string lyrName, string srcFilename)
		: base(lyrName, srcFilename)
	{
		SimpleFeatureTypeName = "Manning's Region";
		FeaturesRequireUniqueNames = true;
	}

	public ManningsNPolygonLayer(DataTable overrideDT)
		: base("Manning's n Polygons")
	{
		SimpleFeatureTypeName = "Manning's Region";
		FeaturesRequireUniqueNames = true;
		OverrideFeatureTable(overrideDT.Copy());
	}

	public override object Clone()
	{
		ManningsNPolygonLayer manningsNPolygonLayer = new ManningsNPolygonLayer(FeatureTable());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if (feature is IEditableFeature)
				{
					manningsNPolygonLayer.SetFeature(i, ((IEditableFeature)feature)?.Save());
				}
			}
			return manningsNPolygonLayer;
		}
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Name", typeof(string));
		AddAttributeColumn("n_Value", typeof(float));
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
	}

	public float GetManningNValue(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "n_Value");
	}

	public void SetManningNValue(int fid, float nVal)
	{
		if (!(nVal < 0f))
		{
			FeatureRow(fid)["n_Value"] = nVal;
		}
	}

	public int[] Rasterize(RasterM raster)
	{
		_Closure_0024__19_002D0 capturedLocals = new _Closure_0024__19_002D0(null);
		checked
		{
			capturedLocals.regions = new int[raster.CellMaxIndex() + 1];
			Buffers.ArraySetAllVB(capturedLocals.regions, -1);
			RasterizePolygon rasterizePolygon = new RasterizePolygon(raster);
			int count = FeatureTable().Rows.Count - 1;
			_Closure_0024__19_002D1 capturedLocals2 = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2 = new _Closure_0024__19_002D1(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.fID = i;
				Polygon polygon = Polygon(capturedLocals2.fID);
				rasterizePolygon.ComputeCells(polygon, capturedLocals2._Lambda_0024__0);
			}
			return capturedLocals.regions;
		}
	}

	public string[] RegionNames()
	{
		int featureCount = FeatureCount();
		if (featureCount == 0)
		{
			return null;
		}
		DataTable dataTable = FeatureTable();
		int indexOf = dataTable.Columns.IndexOf("Name");
		if (indexOf == -1)
		{
			return null;
		}
		checked
		{
			string[] strings = new string[featureCount - 1 + 1];
			int count = featureCount - 1;
			for (int i = 0; i <= count; i++)
			{
				strings[i] = dataTable.Rows[i][indexOf].ToString();
			}
			return strings;
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			if (args.ChangeType == FeatureChangedType.FeatureAdded)
			{
				SetupNewManningRegion(featureIndicesChanged);
			}
			else
			{
				_ = args.ChangeType;
			}
		}
	}

	private void SetupNewManningRegion(int fID)
	{
		SetUniqueFeatureName(fID);
		SetIsDefaultName(fID, isDefaultName: true);
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Name"] = newName;
		SetIsDefaultName(fid, isDefaultName: false);
	}
}
