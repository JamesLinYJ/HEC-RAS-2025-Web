using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Geospatial.GDALAssist.Vectors.SSURGO;
using Geospatial.Vectors;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Progress;

namespace RasMapperLib;

public class SsurgoLayer : PolygonFeatureLayer
{
	public enum PaintBy
	{
		MapUnitKey,
		HydrologicGroup,
		TextureGroup
	}

	private const string sMapUnitKey = "Map Unit Key";

	private const string sTextureGroup = "Texture Group";

	public const string sHydrSoilGroup = "Hydrologic Group";

	private const string sMergedGroups = "Merged Hydr : Texture";

	private const string sMergedDelimeter = " : ";

	private const string sEmptyStringReplacement = "(none)";

	private const string sPoPaintHydrGroup = "Paint by Hydrologic Group";

	private const string sPoPaintTextGroup = "Paint by Texture Group";

	private Extent _filterExtent;

	private SSURGO _ssurgo;

	private MapUnitCollection _muCollection;

	private readonly Random _r;

	private Dictionary<string, Color> _muKeyColorDictionary;

	private Dictionary<string, Color> _hydrGrpColorDictionary;

	private Dictionary<string, Color> _texGrpColorDictionary;

	public override bool CanEdit { get; }

	public SsurgoLayer()
	{
		CanEdit = false;
		_r = new Random();
		_muKeyColorDictionary = new Dictionary<string, Color>();
		_hydrGrpColorDictionary = new Dictionary<string, Color>();
		_texGrpColorDictionary = new Dictionary<string, Color>();
	}

	public SsurgoLayer(string filename, Extent ext)
		: base(System.IO.Path.GetFileNameWithoutExtension(filename), filename)
	{
		CanEdit = false;
		_r = new Random();
		_muKeyColorDictionary = new Dictionary<string, Color>();
		_hydrGrpColorDictionary = new Dictionary<string, Color>();
		_texGrpColorDictionary = new Dictionary<string, Color>();
		_filterExtent = ext;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Map Unit Key", typeof(string));
		AddAttributeColumn("Hydrologic Group", typeof(string));
		AddAttributeColumn("Texture Group", typeof(string));
		AddAttributeColumn("Merged Hydr : Texture", typeof(string));
		AddAttributeColumn("Map Unit Symbol", typeof(string));
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Paint by Hydrologic Group"));
		staticPlotOptions.Add(new PlotOption("Paint by Texture Group"));
		return staticPlotOptions;
	}

	protected override void LoadFeatureTable()
	{
		LoadFeatureTableInternal(ProgressReporter.None());
	}

	private void LoadFeatureTableInternal(ProgressReporter prog)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		ProgressReporter prog2 = prog.WithProgressModifier(0, 0.37f);
		ProgressReporter prog3 = prog.WithProgressModifier(37, 62f);
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return;
		}
		if (!SourceFileExists)
		{
			ErrorMessage = SourceFilename + " does not exist.";
			return;
		}
		if (_filterExtent == null)
		{
			ErrorMessage = "No filter extent selected.";
			return;
		}
		if (SharedData.SRSProjection == null)
		{
			ErrorMessage = "RasMapper Projection not found.";
			return;
		}
		checked
		{
			try
			{
				_ssurgo = new SSURGO(System.IO.Path.GetDirectoryName(SourceFilename));
				prog.ReportMessage("Reading Map Unit Table");
				_muCollection = _ssurgo.ReadMapUnitTableComplete(prog2);
				DataTable table = null;
				prog.ReportMessage("Reprojecting Polygons");
				List<ComplexPolygonCollection> projectedPolygons = _ssurgo.GetProjectedPolygons(SharedData.SRSProjection, Converter.Convert(_filterExtent), out table, prog3);
				int count = projectedPolygons.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					ComplexPolygonCollection complexPolygonCollection = projectedPolygons[i];
					string row = table.Rows[i]["mukey"].ToString();
					List<MapUnit> byKey = _muCollection.FindByKey(row);
					if (byKey.Count != 1)
					{
						continue;
					}
					MapUnit mapUnit = byKey.First();
					string bestTextureNameTerm = "";
					if (mapUnit.Components.Count != 0)
					{
						Horizon horizon = mapUnit.Components.OrderByDescending([SpecialName] (Component c) => c.RelativeCompositionPercentage).First().TopHorizon();
						if (horizon != null)
						{
							TextureGroup textureGroup = horizon.DominantTextureGroup();
							if (textureGroup != null)
							{
								string bestTextureName = textureGroup.BestTextureName();
								if (!string.IsNullOrEmpty(bestTextureName))
								{
									bestTextureNameTerm = bestTextureName;
								}
							}
						}
					}
					int count2 = complexPolygonCollection.Count - 1;
					for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
					{
						ComplexPolygon poly = complexPolygonCollection.Polygons[intervalIndex];
						DataRow dataRow = AddFeature(Converter.Convert(poly));
						dataRow["Map Unit Key"] = row;
						string hydrologicSoilGroup = mapUnit.HydrologicSoilGroup;
						if (string.IsNullOrEmpty(hydrologicSoilGroup))
						{
							hydrologicSoilGroup = "(none)";
						}
						string dataRow2 = bestTextureNameTerm;
						if (string.IsNullOrEmpty(dataRow2))
						{
							dataRow2 = "(none)";
						}
						dataRow["Hydrologic Group"] = hydrologicSoilGroup;
						dataRow["Texture Group"] = dataRow2;
						dataRow["Merged Hydr : Texture"] = hydrologicSoilGroup + " : " + dataRow2;
					}
				}
				prog.ReportProgress(100);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ErrorMessage = "Could not load GSSURGO data: " + ex2.Message;
				ProjectData.ClearProjectError();
			}
		}
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlNode? xmlNode = xmlLayer.SelectSingleNode("FilterExtent");
		double minX = XML.DoubleAttribute(xmlNode, "MinX");
		double maxX = XML.DoubleAttribute(xmlNode, "MaxX");
		double minY = XML.DoubleAttribute(xmlNode, "MinY");
		double maxY = XML.DoubleAttribute(xmlNode, "MaxY");
		_filterExtent = new Extent(maxX, minX, maxY, minY);
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		XmlElement xmlElement = doc.CreateElement("FilterExtent");
		xmlElement.SetAttribute("MinX", _filterExtent.MinX.ToString());
		xmlElement.SetAttribute("MaxX", _filterExtent.MaxX.ToString());
		xmlElement.SetAttribute("MinY", _filterExtent.MinY.ToString());
		xmlElement.SetAttribute("MaxY", _filterExtent.MaxY.ToString());
		xmlLayer.AppendChild(xmlElement);
	}

	private PaintBy GetPaintByPlotType()
	{
		if (SelectedPlotOptions == null)
		{
			return PaintBy.MapUnitKey;
		}
		if (SelectedPlotOptions.Contains("Paint by Hydrologic Group"))
		{
			return PaintBy.HydrologicGroup;
		}
		if (SelectedPlotOptions.Contains("Paint by Texture Group"))
		{
			return PaintBy.TextureGroup;
		}
		return PaintBy.MapUnitKey;
	}

	private Color TryGetOrAddColor(string mukey, Dictionary<string, Color> dic, byte alpha)
	{
		if (!dic.TryGetValue(mukey, out var dicMukeyTerm))
		{
			dicMukeyTerm = (dic[mukey] = Color.FromArgb(255, _r.Next(0, 256), _r.Next(0, 256), _r.Next(0, 256)));
		}
		return Color.FromArgb(alpha, dicMukeyTerm);
	}

	public string GetMapUnitKey(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Map Unit Key");
	}

	public string GetHydrologicSoilGroup(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Hydrologic Group");
	}

	public string GetTextureGroup(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Texture Group");
	}
}
