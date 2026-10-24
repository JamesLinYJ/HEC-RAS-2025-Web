using System.Collections.Generic;
using System.IO;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Vectors;
using Utility.Memory;
using Utility.Progress;

namespace Testing.Core;

public static class PoiShapefile
{
	public const string NameColumn = "Name";

	public static string PathFor(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Features", "POI.shp");
	}

	public static bool TryRead(string shpPath, out List<PoiPoint> points)
	{
		points = new List<PoiPoint>();
		if (!File.Exists(shpPath))
		{
			return false;
		}
		if (!ShapefileIO.TryRead(shpPath, out PointFeatureCollection collection, (ProgressReporter)null).Result)
		{
			return false;
		}
		Table attributeTable = collection.AttributeTable;
		InterestPointNaming.TryResolveNameColumn(attributeTable, out var columnName);
		for (int i = 0; i < collection.Count; i++)
		{
			Point point = collection[i];
			string name = InterestPointNaming.ReadName(attributeTable, columnName, i);
			points.Add(new PoiPoint(point.X, point.Y, name));
		}
		return true;
	}

	public static void Write(string shpPath, IReadOnlyList<PoiPoint> points, Projection projection = null)
	{
		Table table = new Table();
		int index = table.AddColumn("Name", typeof(string));
		List<Point> pointsList = new List<Point>(points.Count);
		foreach (PoiPoint point in points)
		{
			pointsList.Add(new Point(point.X, point.Y));
			table.AddNewRow()[index] = point.Name ?? "";
		}
		Directory.CreateDirectory(Path.GetDirectoryName(shpPath));
		DeleteExisting(shpPath);
		ShapefileIO.Write(shpPath, pointsList, table, projection);
	}

	private static void DeleteExisting(string shpPath)
	{
		string directoryName = Path.GetDirectoryName(shpPath);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(shpPath);
		if (Directory.Exists(directoryName))
		{
			string[] files = Directory.GetFiles(directoryName, fileNameWithoutExtension + ".*");
			for (int i = 0; i < files.Length; i++)
			{
				File.Delete(files[i]);
			}
		}
	}

	public static void CopyFiles(string sourceProjectDirectory, string destProjectDirectory)
	{
		string path = Path.Combine(sourceProjectDirectory, "Features");
		if (!Directory.Exists(path))
		{
			return;
		}
		string[] files = Directory.GetFiles(path, "POI.*");
		if (files.Length != 0)
		{
			string path2 = Path.Combine(destProjectDirectory, "Features");
			Directory.CreateDirectory(path2);
			string[] filesArray = files;
			foreach (string path3 in filesArray)
			{
				File.Copy(path3, Path.Combine(path2, Path.GetFileName(path3)), overwrite: true);
			}
		}
	}
}
