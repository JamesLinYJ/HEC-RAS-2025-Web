using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Utility.Files;

namespace Ras.Migration.Native;

public class RasMapper
{
	private static List<Geometry> ReadGeometries(XElement geoRoot, Project prj, string relPath)
	{
		List<Geometry> geometries = new List<Geometry>();
		foreach (XElement xElement in from l in geoRoot.Elements()
			where l.Attribute("Type")?.Value == "RASGeometry"
			select l)
		{
			string attribute = (string?)xElement.Attribute("Filename");
			if (!string.IsNullOrEmpty(attribute))
			{
				attribute = FileHelpers.MakeRelativeOLD(relPath, attribute);
				geometries.Add(new Geometry(prj, attribute));
			}
		}
		return geometries;
	}

	private static List<Result> ReadResults(XElement resRoot, string relPath)
	{
		List<Result> results = new List<Result>();
		foreach (XElement xElement in from l in resRoot.Elements()
			where l.Attribute("Type")?.Value == "RASResults"
			select l)
		{
			_ = (string?)xElement.Attribute("Name");
			string attribute = (string?)xElement.Attribute("ProjectFilename");
			if (!string.IsNullOrEmpty(attribute))
			{
				attribute = FileHelpers.MakeRelativeOLD(relPath, attribute);
				Result attribute2 = new Result(attribute);
				results.Add(attribute2);
			}
		}
		return results;
	}

	private static void LoadRasmapFile(string rasmapFile)
	{
		if (!File.Exists(rasmapFile))
		{
			return;
		}
		try
		{
			XElement xElement = XElement.Load(rasmapFile);
			XElement xElement2 = xElement.Descendants("Geometries").FirstOrDefault();
			XElement? xElement3 = xElement.Descendants("Results").FirstOrDefault();
			if (xElement2 == null)
			{
				throw new Exception("Bad Rasmap file");
			}
			if (xElement3 == null)
			{
				throw new Exception("Bad Rasmap file");
			}
			throw new NotImplementedException();
		}
		catch
		{
		}
	}

	public static List<Terrain> LoadTerrains(XElement rootDoc, string rasmapFile)
	{
		XElement xElement = rootDoc.Descendants("Terrains").FirstOrDefault();
		if (xElement == null)
		{
			return null;
		}
		List<Terrain> terrains = new List<Terrain>();
		foreach (XElement element in xElement.Elements())
		{
			_ = (string?)element.Attribute("Name");
			string attribute = (string?)element.Attribute("Filename");
			if (!string.IsNullOrEmpty(attribute))
			{
				attribute = FileHelpers.MakeAbsoluteOLD(attribute, rasmapFile);
				terrains.Add(new Terrain(attribute));
			}
		}
		return terrains;
	}

	private static List<MapLayer> LoadMapLayers(XElement rootDoc, string rasmapFile)
	{
		XElement xElement = rootDoc.Descendants("MapLayers").FirstOrDefault();
		if (xElement == null)
		{
			return null;
		}
		List<MapLayer> mapLayers = new List<MapLayer>();
		foreach (XElement element in xElement.Elements())
		{
			string layerName = (string?)element.Attribute("Name");
			string attribute = (string?)element.Attribute("ProjectFilename");
			if (!string.IsNullOrEmpty(attribute))
			{
				attribute = rasmapFile + attribute.Substring(2);
				mapLayers.Add(new MapLayer(attribute, layerName));
			}
		}
		return mapLayers;
	}
}
