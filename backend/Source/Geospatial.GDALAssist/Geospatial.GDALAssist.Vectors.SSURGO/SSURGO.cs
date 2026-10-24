using System;
using System.Collections.Generic;
using System.Data;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

public sealed class SSURGO : IDisposable
{
	public const string PolygonDatasetName = "MUPolygon";

	public const string MapUnitKey = "mukey";

	private readonly VectorDataset _drv;

	public VectorDataset Dataset => _drv;

	public SSURGO(string filename)
	{
		_drv = new VectorDataset(filename);
	}

	public VectorLayer GetPolygonLayer()
	{
		return _drv.GetLayer("MUPolygon");
	}

	public List<ComplexPolygonCollection> GetPolygons(Extent filterExt, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		using VectorLayer vectorLayer = GetPolygonLayer();
		return vectorLayer.ReadFeaturesAsPolygonCollections(filterExt, null, prog);
	}

	public List<ComplexPolygonCollection> GetProjectedPolygons(Projection targetProjection, Extent targetExtent, out DataTable table, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		ProgressReporter prog2 = prog.WithProgressModifier(0, 0.8f);
		ProgressReporter prog3 = prog.WithProgressModifier(80, 0.2f);
		using VectorLayer vectorLayer = GetPolygonLayer();
		Projection projection = vectorLayer.GetProjection();
		Extent filterExtent = ((!targetExtent.IsFiniteAndValid()) ? Extent.EffectivelyInfinite : targetExtent.Reproject(targetProjection, projection));
		table = new DataTable();
		table.Columns.Add(new DataColumn("mukey", typeof(string)));
		List<ComplexPolygonCollection> collections = vectorLayer.ReadFeaturesAsPolygonCollections(filterExtent, table, prog2);
		using ReprojectionTransformer reprojectionTransformer = new ReprojectionTransformer(projection, targetProjection);
		List<ComplexPolygonCollection> result = reprojectionTransformer.Transform(collections, prog3);
		prog.ReportProgress(100);
		return result;
	}

	private List<MapUnit> QueryMapUnitTable_Old(ProgressReporter prog = null)
	{
		bool condition = false;
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		List<MapUnit> mapUnits = new List<MapUnit>();
		using (VectorLayer vectorLayer = _drv.ExecuteSQL("SELECT mapunit.mukey, mapunit.musym, muaggatt.hydgrpdcd FROM mapunit LEFT JOIN muaggatt ON mapunit.mukey=muaggatt.mukey"))
		{
			DataTable table = vectorLayer.GetTable(instantiateGeometry: false, prog);
			for (int i = 0; i < table.Rows.Count; i++)
			{
				prog.ReportProgress(i * 100 / table.Rows.Count);
				DataRow dataRow = table.Rows[i];
				string dataRow6 = (string)dataRow["mapunit.mukey"];
				string musym = (string)dataRow["mapunit.musym"];
				string hydrSoilGrp = (string)dataRow["muaggatt.hydgrpdcd"];
				MapUnit mapUnit = new MapUnit(dataRow6, musym, hydrSoilGrp);
				mapUnits.Add(mapUnit);
				using VectorLayer vectorLayer2 = _drv.ExecuteSQL("SELECT cokey, compname, comppct_r, mukey FROM component WHERE mukey='" + dataRow6 + "'");
				DataTable table2 = vectorLayer2.GetTable(instantiateGeometry: false, prog);
				for (int j = 0; j < table2.Rows.Count; j++)
				{
					DataRow dataRow2 = table2.Rows[j];
					string dataRow22 = (string)dataRow2["cokey"];
					string coname = (string)dataRow2["compname"];
					int relPct = (int)dataRow2["comppct_r"];
					Component component = new Component(dataRow22, coname, relPct);
					mapUnit.Components.Add(component);
					using VectorLayer vectorLayer3 = _drv.ExecuteSQL("SELECT chkey, hzname, hzdept_r, hzdepb_r FROM chorizon WHERE cokey='" + dataRow22 + "'");
					DataTable table3 = vectorLayer3.GetTable(instantiateGeometry: false, prog);
					for (int k = 0; k < table3.Rows.Count; k++)
					{
						DataRow dataRow3 = table3.Rows[k];
						string dataRow32 = (string)dataRow3["chkey"];
						string hzName = (string)dataRow3["hzname"];
						int depthTop = (int)dataRow3["hzdept_r"];
						int depthBot = (int)dataRow3["hzdepb_r"];
						Horizon horizon = new Horizon(dataRow32, hzName, depthTop, depthBot);
						component.Horizons.Add(horizon);
						using VectorLayer vectorLayer4 = _drv.ExecuteSQL("SELECT chtgkey, texdesc, texture FROM chtexturegrp WHERE chkey='" + dataRow32 + "'");
						DataTable table4 = vectorLayer4.GetTable(instantiateGeometry: false, prog);
						for (int l = 0; l < table4.Rows.Count; l++)
						{
							DataRow dataRow4 = table4.Rows[l];
							string dataRow42 = (string)dataRow4["chtgkey"];
							string tgDesc = (string)dataRow4["texdesc"];
							string texture = (string)dataRow4["texture"];
							TextureGroup textureGroup = new TextureGroup(dataRow42, tgDesc, texture);
							horizon.TextureGroups.Add(textureGroup);
							if (!condition)
							{
								continue;
							}
							using VectorLayer vectorLayer5 = _drv.ExecuteSQL("SELECT chtkey, texcl, lieutex FROM chtexture WHERE chtgkey='" + dataRow42 + "'");
							DataTable table5 = vectorLayer5.GetTable(instantiateGeometry: false, prog);
							for (int m = 0; m < table5.Rows.Count; m++)
							{
								DataRow dataRow5 = table5.Rows[m];
								string txKey = (string)dataRow5["chtkey"];
								string txName = (string)dataRow5["texcl"];
								string inLieu = (string)dataRow5["lieutex"];
								textureGroup.Textures.Add(new Texture(txKey, txName, inLieu));
							}
						}
					}
				}
			}
		}
		prog.ReportProgress(100);
		return mapUnits;
	}

	public MapUnitCollection ReadMapUnitTableBasic(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		MapUnitCollection mapUnitCollection = new MapUnitCollection();
		using VectorLayer vectorLayer = _drv.ExecuteSQL("SELECT mapunit.mukey, mapunit.musym, muaggatt.hydgrpdcd FROM mapunit LEFT JOIN muaggatt ON mapunit.mukey=muaggatt.mukey");
		DataTable table = vectorLayer.GetTable(instantiateGeometry: false, prog);
		mapUnitCollection.AddRange(table);
		return mapUnitCollection;
	}

	public MapUnitCollection ReadMapUnitTableComplete(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		ProgressReporter prog2 = prog.WithProgressModifier(0, 0.6f);
		ProgressReporter prog3 = prog.WithProgressModifier(60, 0.25f);
		ProgressReporter prog4 = prog.WithProgressModifier(85, 0.05f);
		ProgressReporter prog5 = prog.WithProgressModifier(90, 0.1f);
		TextureGroupCollection textureGroupCollection = new TextureGroupCollection();
		using (VectorLayer vectorLayer = _drv.ExecuteSQL("SELECT chtgkey, chkey, texdesc, rvindicator, texture FROM chtexturegrp"))
		{
			DataTable table = vectorLayer.GetTable(instantiateGeometry: false, prog2);
			textureGroupCollection.AddRange(table);
		}
		prog.ReportProgress(60);
		HorizonCollection horizonCollection = new HorizonCollection();
		using (VectorLayer vectorLayer2 = _drv.ExecuteSQL("SELECT cokey, chkey, hzname, hzdept_r, hzdepb_r FROM chorizon"))
		{
			DataTable table2 = vectorLayer2.GetTable(instantiateGeometry: false, prog3);
			horizonCollection.AddRange(table2);
		}
		horizonCollection.Link(textureGroupCollection);
		ComponentCollection componentCollection = new ComponentCollection();
		using (VectorLayer vectorLayer3 = _drv.ExecuteSQL("SELECT mukey, cokey, compname, comppct_r FROM component"))
		{
			DataTable table3 = vectorLayer3.GetTable(instantiateGeometry: false, prog4);
			componentCollection.AddRange(table3);
		}
		componentCollection.Link(horizonCollection);
		MapUnitCollection mapUnitCollection = new MapUnitCollection();
		using (VectorLayer vectorLayer4 = _drv.ExecuteSQL("SELECT mapunit.mukey, mapunit.musym, muaggatt.hydgrpdcd FROM mapunit LEFT JOIN muaggatt ON mapunit.mukey=muaggatt.mukey"))
		{
			DataTable table4 = vectorLayer4.GetTable(instantiateGeometry: false, prog5);
			mapUnitCollection.AddRange(table4);
		}
		mapUnitCollection.Link(componentCollection);
		prog.ReportProgress(100);
		return mapUnitCollection;
	}

	public void Dispose()
	{
		_drv.Dispose();
	}
}
