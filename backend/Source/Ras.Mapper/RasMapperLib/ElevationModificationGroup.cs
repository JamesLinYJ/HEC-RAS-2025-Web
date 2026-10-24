using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class ElevationModificationGroup : Layer, IElevationModificationLayer
{
	public const ElevationModificationType DefaultModType = ElevationModificationType.SetValue;

	public const string sElevationType = "Elevation Type";

	public const string sElevation = "Elevation Value";

	private TerrainLayer _terrainLayer;

	public const string HDFModificationGroup = "/Modifications";

	public const string HDFTypeAttribute = "Type";

	public const string HDFPriorityAttribute = "Priority";

	public override bool CanRemove { get; }

	public override bool CanRename { get; }

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanEdit { get; }

	public override bool UsesParentSourceFilename { get; }

	public override Extent Extent
	{
		get
		{
			Extent extent = null;
			foreach (Layer allModifier in AllModifiers())
			{
				extent = Extent.Union(extent, allModifier.Extent);
			}
			return extent;
		}
	}

	public string LayerName => Name;

	public IEnumerable<IElevationModificationLayer> AllModifiers()
	{
		foreach (IElevationModificationLayer node in Nodes)
		{
			yield return node;
		}
	}

	public IEnumerable<EllipseElevationModificationLayer> CircleModifiers()
	{
		foreach (EllipseElevationModificationLayer node in Nodes)
		{
			yield return node;
		}
	}

	public IEnumerable<PolygonElevationModificationLayer> PolygonModifiers()
	{
		foreach (PolygonElevationModificationLayer node in Nodes)
		{
			yield return node;
		}
	}

	public IEnumerable<RectangleElevationModificationLayer> RectangleModifiers()
	{
		foreach (RectangleElevationModificationLayer node in Nodes)
		{
			yield return node;
		}
	}

	public ElevationModificationGroup(TerrainLayer terrainLayer)
		: base("Modifications")
	{
		CanRemove = true;
		CanRename = false;
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		CanEdit = true;
		UsesParentSourceFilename = true;
		_terrainLayer = terrainLayer;
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		if (!SourceFileExists)
		{
			return;
		}
		LoadModificationsFromHDF5();
		if (xmlEl == null)
		{
			return;
		}
		XmlNodeList xmlNodeList = xmlEl.SelectNodes("Layer");
		foreach (Layer allModifier in AllModifiers())
		{
			foreach (XmlElement xmlNodeList2 in xmlNodeList)
			{
				if (string.Equals(Layer.GetLayerNameFromXML(xmlNodeList2), allModifier.Name))
				{
					allModifier.XMLLoad(xmlNodeList2);
					break;
				}
			}
		}
	}

	internal void WriteModificationsToHDF()
	{
		using H5Writer h5Writer = new H5Writer(SourceFilename);
		if (!h5Writer.ExistsAsGroup("/Modifications"))
		{
			return;
		}
		foreach (string groupName in h5Writer.GetGroupNames("/Modifications"))
		{
			bool condition = false;
			int ordinal = 0;
			foreach (IElevationModificationLayer allModifier in AllModifiers())
			{
				if (string.Equals(allModifier.LayerName, groupName))
				{
					h5Writer.WriteAttribute("/Modifications/" + groupName, "Priority", ordinal);
					condition = true;
					break;
				}
				ordinal = checked(ordinal + 1);
			}
			if (!condition)
			{
				h5Writer.Delete("/Modifications/" + groupName);
			}
		}
	}

	public void LoadModificationsFromHDF5()
	{
		try
		{
			Nodes.Clear();
			if (!SourceFileExists)
			{
				return;
			}
			Dictionary<Layer, int> intByLayer = new Dictionary<Layer, int>();
			using (H5Reader h5Reader = new H5Reader(SourceFilename))
			{
				if (h5Reader.ExistsAsGroup("/Modifications"))
				{
					foreach (string groupName in h5Reader.GetGroupNames("/Modifications"))
					{
						string groupNameTerm = "/Modifications/" + groupName;
						string tryReadStringAttributeScalar = h5Reader.TryReadStringAttributeScalar(groupNameTerm, "Type");
						int maxValue = 0;
						if (!h5Reader.TryReadAttributeScalar(groupNameTerm, "Priority", ref maxValue))
						{
							maxValue = int.MaxValue;
						}
						switch (tryReadStringAttributeScalar)
						{
						case "Circle":
						{
							EllipseElevationModificationLayer key6 = new EllipseElevationModificationLayer(groupName);
							intByLayer.Add(key6, maxValue);
							break;
						}
						case "Rectangle":
						{
							RectangleElevationModificationLayer key5 = new RectangleElevationModificationLayer(groupName);
							intByLayer.Add(key5, maxValue);
							break;
						}
						case "Triangle":
						{
							TriangleElevationModificationLayer key4 = new TriangleElevationModificationLayer(groupName);
							intByLayer.Add(key4, maxValue);
							break;
						}
						case "Polygon":
						{
							PolygonElevationModificationLayer key3 = new PolygonElevationModificationLayer(groupName, _terrainLayer);
							intByLayer.Add(key3, maxValue);
							break;
						}
						case "Levee":
						{
							GroundLineModificationLayer key2 = new GroundLineModificationLayer(groupName, _terrainLayer);
							intByLayer.Add(key2, maxValue);
							break;
						}
						case "Pier":
						{
							PierElevationModificationLayer key = new PierElevationModificationLayer(groupName);
							intByLayer.Add(key, maxValue);
							break;
						}
						}
					}
				}
			}
			foreach (KeyValuePair<Layer, int> orderBy in intByLayer.OrderBy([SpecialName] (KeyValuePair<Layer, int> a) => a.Value))
			{
				Nodes.Add(orderBy.Key);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Error loading some modification layers: " + ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	public static void InitializeElevationModTypeSerializer()
	{
		Func<object, string> serializer = null;
		Func<string, object> deserializer = null;
		DiskNameAttribute.GetDefaultEnumSerializers(out serializer, out deserializer, out var maxStringLength, ElevationModificationType.SetValue);
		HDF5MemoryDiskMap.SetTypeSerializer<ElevationModificationType>(serializer, deserializer, maxStringLength);
	}

	public static string SanitizeHDFDatasetName(string name)
	{
		return name.Replace('\\', '_').Replace('/', '_');
	}

	public void ForceLoad()
	{
		FeatureTable();
		foreach (IElevationModificationLayer allModifier in AllModifiers())
		{
			allModifier.FeatureTable();
		}
	}

	public void ObstructRaster(RasterM raster, float[] values)
	{
		foreach (IElevationModificationLayer reverse in AllModifiers().Reverse())
		{
			reverse.ObstructRaster(raster, values);
		}
	}

	void IElevationModificationLayer.ObstructRaster(RasterM raster, float[] values)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructRaster
		this.ObstructRaster(raster, values);
	}

	public void ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles)
	{
		foreach (IElevationModificationLayer reverse in AllModifiers().Reverse())
		{
			reverse.ObstructProfiles(polylines, profiles);
		}
	}

	void IElevationModificationLayer.ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructProfiles
		this.ObstructProfiles(polylines, profiles);
	}

	public void ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx)
	{
		foreach (IElevationModificationLayer reverse in AllModifiers().Reverse())
		{
			reverse.ObstructPoints(points, valuesPerPoint, pointSpIdx);
		}
	}

	void IElevationModificationLayer.ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructPoints
		this.ObstructPoints(points, valuesPerPoint, pointSpIdx);
	}

	public static float GetReplacementValue(float origValue, ElevationModificationType modType, float modValue)
	{
		if (origValue == -9999f)
		{
			return modValue;
		}
		if (modValue == -9999f)
		{
			return origValue;
		}
		switch (modType)
		{
		case ElevationModificationType.SetValue:
			return modValue;
		case ElevationModificationType.AddValue:
			return origValue + modValue;
		case ElevationModificationType.TakeHigher:
			if (modValue > origValue)
			{
				return modValue;
			}
			return origValue;
		case ElevationModificationType.TakeLower:
			if (modValue < origValue)
			{
				return modValue;
			}
			return origValue;
		default:
			throw new NotImplementedException();
		}
	}

	public static void ReplaceProfileSubsection(ref Profile pf, float pfLength, float startStation, float endStation, ElevationModificationType modType, float modValue)
	{
		if (startStation > endStation)
		{
			float startStation2 = startStation;
			startStation = endStation;
			endStation = startStation2;
		}
		if (startStation != endStation)
		{
			Profile profile = new Profile();
			profile.Add(new Point2D(startStation, modValue));
			profile.Add(new Point2D(endStation, modValue));
			ReplaceProfileSubsection(ref pf, pfLength, startStation, endStation, modType, profile);
		}
	}

	public static void ReplaceProfileSubsection(ref Profile pf, float pfLength, float startStation, float endStation, ElevationModificationType modType, Profile subsectionProfile)
	{
		if (subsectionProfile == null)
		{
			return;
		}
		_ = pfLength / 1000f;
		if (startStation > endStation)
		{
			float startStation2 = startStation;
			startStation = endStation;
			endStation = startStation2;
		}
		if (startStation == endStation)
		{
			return;
		}
		subsectionProfile.SortByStation();
		Profile profile = new Profile();
		profile.Add(new Point2D(startStation, subsectionProfile.Query(startStation)));
		checked
		{
			int count = subsectionProfile.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (subsectionProfile[i].X > startStation && subsectionProfile[i].X < endStation && (double)Math.Abs(profile[profile.Count - 1].X - endStation) > 0.01)
				{
					profile.Add(subsectionProfile[i]);
				}
			}
			profile.Add(new Point2D(endStation, subsectionProfile.Query(endStation)));
			switch (modType)
			{
			case ElevationModificationType.SetValue:
				if (pf.Count == 0)
				{
					pf = profile;
					break;
				}
				pf.InjectPointsBetweenStations(startStation, endStation, removeCoveredPoints: true);
				pf.AddRange(profile);
				pf.SortByStation();
				break;
			case ElevationModificationType.AddValue:
				pf = pf.MergeAdd(profile);
				break;
			case ElevationModificationType.TakeHigher:
				pf.InjectPointsBetweenStations(startStation, endStation, removeCoveredPoints: false);
				pf = pf.MergeTakeHigher(profile);
				break;
			case ElevationModificationType.TakeLower:
				pf.InjectPointsBetweenStations(startStation, endStation, removeCoveredPoints: false);
				pf = pf.MergeTakeLower(profile);
				break;
			default:
				throw new NotImplementedException();
			}
		}
	}

	public void CopyModificationsFrom(IList<IElevationModificationLayer> modificationLayers)
	{
		if (!_terrainLayer.Nodes.Contains(this))
		{
			_terrainLayer.Nodes.Add(this);
		}
		checked
		{
			for (int i = modificationLayers.Count - 1; i >= 0; i += -1)
			{
				IElevationModificationLayer elevationModificationLayer = modificationLayers[i];
				if (elevationModificationLayer is ElevationModificationGroup)
				{
					ElevationModificationGroup elevationModificationGroup = (ElevationModificationGroup)elevationModificationLayer;
					List<IElevationModificationLayer> iElevationModificationLayers = new List<IElevationModificationLayer>();
					foreach (TreeNode node in elevationModificationGroup.Nodes)
					{
						if (node is IElevationModificationLayer)
						{
							iElevationModificationLayers.Add((IElevationModificationLayer)node);
						}
					}
					CopyModificationsFrom(iElevationModificationLayers);
					continue;
				}
				FeatureLayer featureLayer;
				if (elevationModificationLayer is PolygonElevationModificationLayer)
				{
					featureLayer = new PolygonElevationModificationLayer(elevationModificationLayer.LayerName, _terrainLayer);
					featureLayer.CopyFeaturesAndAttributes((PolygonElevationModificationLayer)elevationModificationLayer);
					((PolygonElevationModificationLayer)featureLayer).ControlPoints.CopyFeaturesAndAttributes(((PolygonElevationModificationLayer)elevationModificationLayer).ControlPoints);
				}
				else if (elevationModificationLayer is GroundLineModificationLayer)
				{
					featureLayer = new GroundLineModificationLayer(elevationModificationLayer.LayerName, _terrainLayer);
					featureLayer.CopyFeaturesAndAttributes((GroundLineModificationLayer)elevationModificationLayer);
					((GroundLineModificationLayer)featureLayer).ControlPoints.CopyFeaturesAndAttributes(((GroundLineModificationLayer)elevationModificationLayer).ControlPoints);
				}
				else if (elevationModificationLayer is EllipseElevationModificationLayer)
				{
					featureLayer = new EllipseElevationModificationLayer(elevationModificationLayer.LayerName);
					featureLayer.CopyFeaturesAndAttributes((EllipseElevationModificationLayer)elevationModificationLayer);
				}
				else if (elevationModificationLayer is RectangleElevationModificationLayer)
				{
					featureLayer = new RectangleElevationModificationLayer(elevationModificationLayer.LayerName);
					featureLayer.CopyFeaturesAndAttributes((RectangleElevationModificationLayer)elevationModificationLayer);
				}
				else if (elevationModificationLayer is PierElevationModificationLayer)
				{
					featureLayer = new PierElevationModificationLayer(elevationModificationLayer.LayerName);
					featureLayer.CopyFeaturesAndAttributes((PierElevationModificationLayer)elevationModificationLayer);
				}
				else
				{
					if (!(elevationModificationLayer is TriangleElevationModificationLayer))
					{
						throw new NotImplementedException();
					}
					featureLayer = new TriangleElevationModificationLayer(elevationModificationLayer.LayerName);
					featureLayer.CopyFeaturesAndAttributes((TriangleElevationModificationLayer)elevationModificationLayer);
				}
				Nodes.Insert(0, featureLayer);
			}
		}
	}

	public DataTable FeatureTable()
	{
		return null;
	}

	DataTable IFeatureLayer.FeatureTable()
	{
		//ILSpy generated this explicit interface implementation from .override directive in FeatureTable
		return this.FeatureTable();
	}
}
