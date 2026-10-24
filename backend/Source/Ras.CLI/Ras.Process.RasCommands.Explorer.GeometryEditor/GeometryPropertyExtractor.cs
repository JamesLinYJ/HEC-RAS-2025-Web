using System.Collections.Generic;
using System.Reflection;
using Ras.Layers;
using Utility.ORM;

namespace Ras.Process.RasCommands.Explorer.GeometryEditor;

public static class GeometryPropertyExtractor
{
	private static readonly Dictionary<string, string> PropertyDescriptions = new Dictionary<string, string>
	{
		["Name"] = "Display name for this geometry. Used as identifier in project.",
		["Description"] = "Optional long-form description or notes about this geometry."
	};

	private static readonly Dictionary<string, string> ReadOnlyDescriptions = new Dictionary<string, string>
	{
		["DateCreated"] = "Date this geometry file was originally created.",
		["DateLastModified"] = "Date this geometry file was last saved.",
		["LoadedFileVersion"] = "HDF5 file format version.",
		["LoadedSoftwareVersion"] = "Version of the software that last saved this file.",
		["OriginSoftware"] = "Software that originally created this file (e.g., HEC-RAS Importer).",
		["OriginVersion"] = "Version of the origin software."
	};

	public static List<GeometryPropertyInfo> GetEditableProperties(Geometry geometry)
	{
		List<GeometryPropertyInfo> geometryPropertyInfos = new List<GeometryPropertyInfo>();
		PropertyInfo[] properties = typeof(Geometry).GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			MetadataAttribute customAttribute = propertyInfo.GetCustomAttribute<MetadataAttribute>();
			if (customAttribute != null)
			{
				bool containsKey = PropertyDescriptions.ContainsKey(propertyInfo.Name);
				bool containsKey2 = ReadOnlyDescriptions.ContainsKey(propertyInfo.Name);
				if ((containsKey || containsKey2) && (!containsKey || propertyInfo.CanWrite))
				{
					GeometryPropertyInfo geometryPropertyInfo = new GeometryPropertyInfo
					{
						DisplayName = (customAttribute.KeyName ?? propertyInfo.Name),
						PropertyName = propertyInfo.Name,
						ReflectionInfo = propertyInfo,
						ValueType = propertyInfo.PropertyType,
						Description = GetDescription(propertyInfo.Name),
						IsReadOnly = (containsKey2 || !propertyInfo.CanWrite)
					};
					geometryPropertyInfos.Add(geometryPropertyInfo);
				}
			}
		}
		return SortProperties(geometryPropertyInfos);
	}

	private static string GetDescription(string propertyName)
	{
		if (PropertyDescriptions.TryGetValue(propertyName, out var localValue))
		{
			return localValue;
		}
		if (ReadOnlyDescriptions.TryGetValue(propertyName, out localValue))
		{
			return localValue;
		}
		return "No description available.";
	}

	private static List<GeometryPropertyInfo> SortProperties(List<GeometryPropertyInfo> properties)
	{
		string[] coefficients = new string[8] { "Name", "Description", "DateCreated", "DateLastModified", "LoadedFileVersion", "LoadedSoftwareVersion", "OriginSoftware", "OriginVersion" };
		Dictionary<string, int> orderMap = new Dictionary<string, int>();
		for (int i = 0; i < coefficients.Length; i++)
		{
			orderMap[coefficients[i]] = i;
		}
		properties.Sort((GeometryPropertyInfo a, GeometryPropertyInfo b) =>
		{
			int tryGetValueOrderMapTerm = (orderMap.TryGetValue(a.PropertyName, out var localValue) ? localValue : 999);
			int tryGetValueOrderMapTerm2 = (orderMap.TryGetValue(b.PropertyName, out var localValue2) ? localValue2 : 999);
			return tryGetValueOrderMapTerm.CompareTo(tryGetValueOrderMapTerm2);
		});
		return properties;
	}
}
