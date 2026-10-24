using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Geospatial.IO.Vectors;
using Geospatial.PairedData;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Identifiers;
using Microsoft.Extensions.Logging;
using Utility.Attributes;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;
using Utility.ORM;

namespace Geospatial.IO.ORM;

public static class FeatureIO
{
	private static readonly ILogger _logger;

	public const string DefaultAttributeDatasetName = "Attributes";

	public static List<T> Read<T>(H5Reader h5r, string group, string attrDataset = "Attributes") where T : new()
	{
		if (!h5r.ExistsAsGroup(group))
		{
			return new List<T>();
		}
		string attrDataset2 = DatasetName(ref group, attrDataset);
		List<T> retn = ReadColumns<T>(h5r, attrDataset2);
		Action<int> action = (int count) =>
		{
			if (retn.Count != count)
			{
				if (retn.Count != 0)
				{
					_logger.LogWarning("Attribute table mismatch with other data.");
				}
				for (int i = retn.Count; i < count; i++)
				{
					retn.Add(new T());
				}
			}
		};
		PropertyInfo[] properties = typeof(T).GetProperties();
		PropertyInfo[] propertiesArray = properties;
		foreach (PropertyInfo propertyInfo in propertiesArray)
		{
			foreach (Attribute customAttribute in propertyInfo.GetCustomAttributes())
			{
				if (!(customAttribute is DatasetNameAttribute datasetNameAttribute))
				{
					continue;
				}
				Type propertyType = propertyInfo.PropertyType;
				if (string.IsNullOrEmpty(datasetNameAttribute.DatasetName))
				{
					_logger.LogWarning("No dataset name provided for property {PropName}.", propertyInfo.Name);
					continue;
				}
				string datasetNameDatasetNameAttributeTerm = group + datasetNameAttribute.DatasetName;
				if (propertyType == typeof(Polyline))
				{
					Polyline[] tryReadPolylines = HDF5VectorReader.TryReadPolylines(h5r, datasetNameDatasetNameAttributeTerm);
					if (tryReadPolylines != null)
					{
						action(tryReadPolylines.Length);
						SetProperties(retn, tryReadPolylines, propertyInfo, datasetNameAttribute.Optional);
					}
				}
				else if (propertyType == typeof(Polygon))
				{
					Polygon[] tryReadPolygons = HDF5VectorReader.TryReadPolygons(h5r, datasetNameDatasetNameAttributeTerm);
					if (tryReadPolygons != null)
					{
						action(tryReadPolygons.Length);
						SetProperties(retn, tryReadPolygons, propertyInfo, datasetNameAttribute.Optional);
					}
				}
				else if (propertyType == typeof(Tin))
				{
					List<Tin> tryReadTins = HDF5VectorReader.TryReadTins(h5r, datasetNameDatasetNameAttributeTerm);
					if (tryReadTins != null)
					{
						action(tryReadTins.Count);
						SetProperties(retn, tryReadTins, propertyInfo, datasetNameAttribute.Optional);
					}
				}
				else if (propertyType == typeof(StationElevationProfile))
				{
					StationElevationProfile[] tryReadProfiles = HDF5VectorReader.TryReadProfiles(h5r, datasetNameDatasetNameAttributeTerm);
					if (tryReadProfiles != null)
					{
						action(tryReadProfiles.Length);
						SetProperties(retn, tryReadProfiles, propertyInfo, datasetNameAttribute.Optional);
					}
				}
				else if (propertyType == typeof(PointCollection))
				{
					PointCollection[] tryReadPointCollections = HDF5VectorReader.TryReadPointCollections(h5r, datasetNameDatasetNameAttributeTerm);
					if (tryReadPointCollections != null)
					{
						action(tryReadPointCollections.Length);
						SetProperties(retn, tryReadPointCollections, propertyInfo, datasetNameAttribute.Optional);
					}
				}
				else if (propertyType == typeof(Mesh))
				{
					if (datasetNameAttribute.DatasetName.StartsWith("$"))
					{
						string propName = datasetNameAttribute.DatasetName.Substring(1);
						PropertyInfo propertyInfo2 = properties.FirstOrDefault((PropertyInfo p) => p.Name == propName && p.PropertyType == typeof(string));
						if (propertyInfo2 == null)
						{
							_logger.LogWarning("Error - Property {propName} is not a valid string property.", propName);
							continue;
						}
						for (int intervalIndex = 0; intervalIndex < retn.Count; intervalIndex++)
						{
							object retn2 = retn[intervalIndex];
							string getValue = propertyInfo2.GetValue(retn2) as string;
							if (!string.IsNullOrEmpty(getValue))
							{
								string meshGroup = group + getValue;
								MeshReader.Read(h5r, meshGroup, out var mesh);
								propertyInfo.SetValue(retn2, mesh);
							}
						}
					}
					else
					{
						_logger.LogWarning("Error - Mesh loading requires a prefixed name attribute, e.g. '$Name'");
					}
				}
				else if (propertyType == typeof(string))
				{
					if (!h5r.ExistsAsDataset(datasetNameDatasetNameAttributeTerm))
					{
						_logger.LogWarning("Non-optional {Dataset} not found.", datasetNameDatasetNameAttributeTerm);
						continue;
					}
					string[] values = null;
					h5r.ReadDataset(datasetNameDatasetNameAttributeTerm, out values);
					if (values != null)
					{
						action(values.Length);
						SetProperties(retn, values, propertyInfo, datasetNameAttribute.Optional);
					}
				}
				else if (propertyType.IsArray)
				{
					Type elementType = propertyType.GetElementType();
					int arrayRank = propertyType.GetArrayRank();
					if (elementType == typeof(string) && arrayRank == 1)
					{
						ReadVLenStringArray(h5r, datasetNameDatasetNameAttributeTerm, out var data);
						SetProperties(retn, data, propertyInfo, datasetNameAttribute.Optional);
						continue;
					}
					if (!elementType.IsPrimitive && elementType != typeof(DateTime))
					{
						_logger.LogWarning("Array of type {subtype} not currently supported. ({propName})", elementType, propertyInfo.Name);
						continue;
					}
					switch (arrayRank)
					{
					case 1:
						if (elementType == typeof(double))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out double[][] data8);
							SetProperties(retn, data8, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(float))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out float[][] data9);
							SetProperties(retn, data9, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(long))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out long[][] data10);
							SetProperties(retn, data10, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(int))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out int[][] data11);
							SetProperties(retn, data11, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(short))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out short[][] data12);
							SetProperties(retn, data12, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(byte))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out byte[][] data13);
							SetProperties(retn, data13, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(DateTime))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out long[][] data14);
							SetProperties(retn, data14?.Select((long[] lng) => lng.Select(DateTime.FromBinary).ToArray()).ToArray(), propertyInfo, datasetNameAttribute.Optional);
						}
						else
						{
							_logger.LogWarning("Array of type {subtype} not currently supported. ({propName})", elementType, propertyInfo.Name);
						}
						break;
					case 2:
						if (elementType == typeof(double))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out double[][,] data2);
							SetProperties(retn, data2, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(float))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out float[][,] data3);
							SetProperties(retn, data3, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(long))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out long[][,] data4);
							SetProperties(retn, data4, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(int))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out int[][,] data5);
							SetProperties(retn, data5, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(short))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out short[][,] data6);
							SetProperties(retn, data6, propertyInfo, datasetNameAttribute.Optional);
						}
						else if (elementType == typeof(byte))
						{
							ReadInfoValuesPrimitiveArray(h5r, datasetNameDatasetNameAttributeTerm, out byte[][,] data7);
							SetProperties(retn, data7, propertyInfo, datasetNameAttribute.Optional);
						}
						else
						{
							_logger.LogWarning("Array of type {subtype} not currently supported. ({propName})", elementType, propertyInfo.Name);
						}
						break;
					default:
						_logger.LogWarning("Array Rank {rank} not currently supported. ({propName})", arrayRank, propertyInfo.Name);
						break;
					}
				}
				else
				{
					_logger.LogWarning("Dataset type {ColumnType} not currently supported.", propertyType);
				}
			}
		}
		CheckPostLoad(retn);
		return retn;
	}

	public static List<T> ReadColumns<T>(H5Reader h5r, string attrDataset) where T : new()
	{
		List<T> columns = new List<T>();
		if (!h5r.ExistsAsDataset(attrDataset))
		{
			_logger.LogInformation("{AttributeDataset} does not exist.", attrDataset);
			return columns;
		}
		long[] datasetDimensions = h5r.GetDatasetDimensions(attrDataset);
		if (datasetDimensions.Length != 1)
		{
			_logger.LogWarning("{Path} isn't 1D, can't read attribute table.", attrDataset);
			return columns;
		}
		if (datasetDimensions[0] == 0L)
		{
			return columns;
		}
		Dictionary<CompoundFieldDefinition, Array> compoundTable = h5r.ReadCompoundElements(attrDataset);
		for (int i = 0; i < datasetDimensions[0]; i++)
		{
			columns.Add(new T());
		}
		ReadColumnsRecursive(columns, compoundTable);
		return columns;
	}

	private static void CheckPostLoad<T>(List<T> features)
	{
		MethodInfo[] methods = typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			if (methodInfo.GetCustomAttribute(typeof(PostLoadAttribute)) == null)
			{
				continue;
			}
			for (int j = 0; j < features.Count; j++)
			{
				T feature = features[j];
				if (feature != null)
				{
					methodInfo.Invoke(feature, null);
				}
			}
			break;
		}
	}

	private static void ReadColumnsRecursive<T>(List<T> baseObjects, Dictionary<CompoundFieldDefinition, Array> compoundTable, string prefix = "", bool forceOptional = false)
	{
		T firstOrDefault = baseObjects.FirstOrDefault();
		if (firstOrDefault == null)
		{
			throw new ArgumentNullException("Need to instantiate properties marked as ComplexColumn.");
		}
		Type type = firstOrDefault.GetType();
		PropertyInfo[] properties = type.GetProperties();
		foreach (PropertyInfo prop in properties)
		{
			if (prop.SetMethod == null)
			{
				continue;
			}
			foreach (Attribute customAttribute in prop.GetCustomAttributes())
			{
				if (customAttribute is MetadataAttribute metadataAttribute)
				{
					string colName = metadataAttribute.KeyName;
					if (string.IsNullOrEmpty(colName))
					{
						continue;
					}
					colName = prefix + colName;
					bool optionalWithForceOptional = metadataAttribute.Optional | forceOptional;
					KeyValuePair<CompoundFieldDefinition, Array> firstOrDefault2 = compoundTable.FirstOrDefault((KeyValuePair<CompoundFieldDefinition, Array> kvp) => kvp.Key.Name == colName);
					if ((firstOrDefault2.Key == null || firstOrDefault2.Value == null) && metadataAttribute.LegacyKeyNames != null)
					{
						string[] legacyKeyNames = metadataAttribute.LegacyKeyNames;
						foreach (string legacyKeyName in legacyKeyNames)
						{
							string legacyColName = prefix + legacyKeyName;
							firstOrDefault2 = compoundTable.FirstOrDefault((KeyValuePair<CompoundFieldDefinition, Array> kvp) => kvp.Key.Name == legacyColName);
							if (firstOrDefault2.Key != null && firstOrDefault2.Value != null)
							{
								break;
							}
						}
					}
					if (firstOrDefault2.Key == null || firstOrDefault2.Value == null)
					{
						if (optionalWithForceOptional)
						{
						}
						continue;
					}
					Type propertyType = prop.PropertyType;
					if ((propertyType.IsPrimitive && !propertyType.IsEnum) || propertyType == typeof(string))
					{
						for (int intervalIndex = 0; intervalIndex < baseObjects.Count; intervalIndex++)
						{
							prop.SetValue(baseObjects[intervalIndex], firstOrDefault2.Value.GetValue(intervalIndex));
						}
						continue;
					}
					if (propertyType == typeof(DateTime))
					{
						if (!(firstOrDefault2.Value is long[] longValues))
						{
							_logger.LogWarning("Error reading DateTime data - not written as longs?");
							continue;
						}
						bool condition = false;
						for (int intervalIndex2 = 0; intervalIndex2 < baseObjects.Count; intervalIndex2++)
						{
							long ticksFromBinaryTerm = longValues[intervalIndex2];
							if (ticksFromBinaryTerm < 0 || ticksFromBinaryTerm > DateTime.MaxValue.Ticks)
							{
								condition = true;
								ticksFromBinaryTerm = DateTime.FromBinary(longValues[intervalIndex2]).Ticks;
							}
							prop.SetValue(baseObjects[intervalIndex2], new DateTime(ticksFromBinaryTerm));
						}
						if (condition)
						{
							_logger.LogWarning("DateTime upper bits corrected on load for {TypeName}.{PropertyName}", type, prop.Name);
						}
						continue;
					}
					if (propertyType == typeof(TimeSpan))
					{
						if (!(firstOrDefault2.Value is long[] longValuesArray))
						{
							_logger.LogWarning("Error reading TimeSpan data - not written as longs?");
							continue;
						}
						for (int intervalIndex3 = 0; intervalIndex3 < baseObjects.Count; intervalIndex3++)
						{
							prop.SetValue(baseObjects[intervalIndex3], new TimeSpan(longValuesArray[intervalIndex3]));
						}
						continue;
					}
					if (!propertyType.IsEnum)
					{
						throw new Exception("Cannot parse column type " + propertyType?.ToString() + ", " + colName);
					}
					Dictionary<string, object> deserializeMap = AliasAttribute.GetDeserializeMap(propertyType);
					for (int intervalIndex4 = 0; intervalIndex4 < baseObjects.Count; intervalIndex4++)
					{
						string getValue = firstOrDefault2.Value.GetValue(intervalIndex4).ToString();
						if (deserializeMap.TryGetValue(getValue, out var localValue))
						{
							prop.SetValue(baseObjects[intervalIndex4], localValue);
						}
						else
						{
							OnEnumParseError(colName, getValue, propertyType);
						}
					}
				}
				else if (customAttribute is WrappedColumnNameAttribute { Count: var count } wrappedColumnNameAttribute)
				{
					Type propertyType2 = prop.PropertyType;
					if (!propertyType2.IsValueType)
					{
						throw new Exception(prop.Name + " must be a value type.");
					}
					for (int intervalIndex5 = 0; intervalIndex5 < count; intervalIndex5++)
					{
						string colName2 = wrappedColumnNameAttribute.ColumnNames[intervalIndex5];
						colName2 = prefix + colName2;
						string childPropertiesOrField = wrappedColumnNameAttribute.ChildPropertiesOrFields[intervalIndex5];
						MemberInfo memberInfo = null;
						Type type2 = null;
						PropertyInfo property = propertyType2.GetProperty(childPropertiesOrField);
						if (property != null)
						{
							memberInfo = property;
							type2 = property.PropertyType;
						}
						else
						{
							FieldInfo? field = propertyType2.GetField(childPropertiesOrField);
							if (field == null)
							{
								throw new Exception("Could not find property or field " + childPropertiesOrField + " on " + propertyType2.Name);
							}
							memberInfo = field;
							type2 = field.FieldType;
						}
						bool optionalWithForceOptional2 = wrappedColumnNameAttribute.Optional | forceOptional;
						KeyValuePair<CompoundFieldDefinition, Array> firstOrDefault3 = compoundTable.FirstOrDefault((KeyValuePair<CompoundFieldDefinition, Array> kvp) => kvp.Key.Name == colName2);
						if (firstOrDefault3.Key == null || firstOrDefault3.Value == null)
						{
							if (!optionalWithForceOptional2)
							{
								NonOptionalColumnMissingError(colName2);
							}
							continue;
						}
						if ((type2.IsPrimitive && !type2.IsEnum) || type2 == typeof(string))
						{
							for (int intervalIndex6 = 0; intervalIndex6 < baseObjects.Count; intervalIndex6++)
							{
								object getValue2 = prop.GetValue(baseObjects[intervalIndex6]);
								SetValue(memberInfo, getValue2, firstOrDefault3.Value.GetValue(intervalIndex6));
								prop.SetValue(baseObjects[intervalIndex6], getValue2);
							}
							continue;
						}
						throw new Exception("Error - non primitive or string on " + childPropertiesOrField + " on " + propertyType2.Name);
					}
				}
				else if (customAttribute is ComplexMetadataAttribute complexMetadataAttribute)
				{
					_ = prop.PropertyType;
					bool forceOptional2 = complexMetadataAttribute.Optional | forceOptional;
					List<object> objectList = baseObjects.Select((T a) => prop.GetValue(a)).ToList();
					if (objectList.Count > 0 && objectList[0] == null)
					{
						throw new Exception("Object initialization error - ComplexColumns need to be ctor'd to some neutral state for the ORM to populate. (" + type.Name + "." + prop.Name + ")");
					}
					ReadColumnsRecursive(objectList, compoundTable, prefix + complexMetadataAttribute.UsableColumnNamePrefix, forceOptional2);
				}
			}
		}
	}

	private static void ReadInfoValuesPrimitiveArray<T>(H5Reader h5r, string dset, out T[][] data) where T : unmanaged
	{
		if (!h5r.ExistsAsDataset(dset))
		{
			data = null;
			return;
		}
		if (!h5r.ExistsAsAttribute(dset, "Start") || !h5r.ExistsAsAttribute(dset, "Count"))
		{
			_logger.LogWarning("Bad file format - missing expected Start/Count attributes on {dset}", dset);
			data = null;
			return;
		}
		h5r.ReadDataset(dset, out T[] values);
		h5r.ReadAttribute(dset, "Start", out int[] values2);
		h5r.ReadAttribute(dset, "Count", out int[] values3);
		data = new T[values2.Length][];
		int values3Sum = 0;
		for (int i = 0; i < data.Length; i++)
		{
			_ = values2[i];
			int values32 = values3[i];
			if (values32 == 0)
			{
				data[i] = Array.Empty<T>();
				continue;
			}
			data[i] = new T[values32];
			Array.Copy(values, values3Sum, data[i], 0, values32);
			values3Sum += values32;
		}
	}

	private static void ReadVLenStringArray(H5Reader h5r, string dset, out string[][] data)
	{
		if (!h5r.ExistsAsDataset(dset))
		{
			data = null;
			return;
		}
		h5r.ReadDataset(dset, out var values);
		h5r.ReadAttribute(dset, "Start", out int[] values2);
		h5r.ReadAttribute(dset, "Count", out int[] values3);
		data = new string[values2.Length][];
		int values3Sum = 0;
		for (int i = 0; i < data.Length; i++)
		{
			_ = values2[i];
			int values32 = values3[i];
			if (values32 == 0)
			{
				data[i] = Array.Empty<string>();
				continue;
			}
			data[i] = new string[values32];
			Array.Copy(values, values3Sum, data[i], 0, values32);
			values3Sum += values32;
		}
	}

	private static void ReadInfoValuesPrimitiveArray<T>(H5Reader h5r, string dset, out T[][,] data) where T : unmanaged
	{
		if (!h5r.ExistsAsDataset(dset))
		{
			data = null;
			return;
		}
		if (!h5r.TryReadAttribute(dset, "Start", out int[] values) || !h5r.TryReadAttribute(dset, "Count", out int[] values2))
		{
			_logger.LogWarning("Dataset {dset} does not have required start/count attributes.", dset);
			data = null;
			return;
		}
		if (values.Length != values2.Length)
		{
			throw new Exception("Error loading " + dset);
		}
		h5r.ReadDataset(dset, out T[,] values3);
		int length = values3.GetLength(1);
		data = new T[values.Length][,];
		int values2Sum = 0;
		for (int i = 0; i < data.Length; i++)
		{
			_ = values[i];
			int values22 = values2[i];
			if (values22 == 0)
			{
				data[i] = new T[0, 0];
				continue;
			}
			data[i] = new T[values22, length];
			Array.Copy(values3, values2Sum * length, data[i], 0, values22 * length);
			values2Sum += values22;
		}
	}

	private static void SetProperties<T, K>(IList<T> objects, IList<K> items, PropertyInfo prop, bool optional)
	{
		if (items == null)
		{
			return;
		}
		if (items.Count != objects.Count)
		{
			_logger.LogWarning("Error reading features - different count than attributes dataset.");
			return;
		}
		for (int i = 0; i < objects.Count; i++)
		{
			prop.SetValue(objects[i], items[i]);
		}
	}

	private static void SetValue(MemberInfo memberInfo, object forObject, object localValue)
	{
		switch (memberInfo.MemberType)
		{
		case MemberTypes.Field:
			((FieldInfo)memberInfo).SetValue(forObject, localValue);
			break;
		case MemberTypes.Property:
			((PropertyInfo)memberInfo).SetValue(forObject, localValue);
			break;
		default:
			throw new NotImplementedException();
		}
	}

	private static string DatasetName(ref string group, string attrDataset)
	{
		if (!group.EndsWith("/"))
		{
			group += "/";
		}
		return group + attrDataset;
	}

	private static void NonOptionalColumnMissingError(string colName)
	{
	}

	private static void OnEnumParseError(string colName, string textValue, Type enumType)
	{
	}

	static FeatureIO()
	{
		_logger = LogHelper.GetLogger("FeatureIO");
		GeospatialH5Serializers.Init();
	}

	public static void Write<T>(H5Writer h5w, string group, IList<T> features, string attrDataset = "Attributes")
	{
		if (!features.IsNullOrEmpty())
		{
			string dset = DatasetName(ref group, attrDataset);
			FeatureIOTable featureIOTable = new FeatureIOTable();
			for (int i = 0; i < features.Count; i++)
			{
				featureIOTable.AddRow();
			}
			CheckPreSave(features);
			ExpandFeatures(features, featureIOTable);
			featureIOTable.WriteCompoundDataset(h5w, dset);
			WriteExtraDatasets(h5w, features, group);
		}
	}

	private static void CheckPreSave<T>(IList<T> features)
	{
		MethodInfo[] methods = typeof(T).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo in methods)
		{
			if (methodInfo.GetCustomAttribute(typeof(PreSaveAttribute)) == null)
			{
				continue;
			}
			for (int j = 0; j < features.Count; j++)
			{
				T feature = features[j];
				if (feature != null)
				{
					methodInfo.Invoke(feature, null);
				}
			}
			break;
		}
	}

	private static void WriteExtraDatasets<T>(H5Writer h5w, IList<T> features, string folder)
	{
		if (features.Count == 0)
		{
			return;
		}
		if (!folder.EndsWith("/"))
		{
			folder += "/";
		}
		PropertyInfo[] properties = typeof(T).GetProperties();
		PropertyInfo[] propertiesArray = properties;
		foreach (PropertyInfo prop in propertiesArray)
		{
			IEnumerable<Attribute> customAttributes = prop.GetCustomAttributes();
			if (!(customAttributes.FirstOrDefault((Attribute attr) => attr is DatasetNameAttribute) is DatasetNameAttribute datasetNameAttribute))
			{
				continue;
			}
			SaveIfAttribute saveIf = customAttributes.FirstOrDefault((Attribute attr) => attr is SaveIfAttribute) as SaveIfAttribute;
			PropertyInfo propertyInfo = null;
			if (saveIf != null)
			{
				propertyInfo = properties.FirstOrDefault((PropertyInfo p) => p.Name == saveIf.PropertyName && p.PropertyType == typeof(bool));
				if (propertyInfo == null)
				{
					_logger.LogWarning("SaveIf attribute used, but no matching property found: {SaveIfName}", saveIf.PropertyName);
				}
			}
			Type propertyType = prop.PropertyType;
			if (string.IsNullOrEmpty(datasetNameAttribute.DatasetName))
			{
				_logger.LogWarning("No dataset name provided for property {propName}", prop.Name);
				continue;
			}
			string folderPlusDatasetName = folder + datasetNameAttribute.DatasetName;
			if (propertyType == typeof(Polyline))
			{
				List<Polyline> polylines = new List<Polyline>();
				for (int intervalIndex = 0; intervalIndex < features.Count; intervalIndex++)
				{
					polylines.Add(prop.GetValue(features[intervalIndex]) as Polyline);
				}
				HDF5VectorReader.Write(h5w, polylines, folderPlusDatasetName);
				continue;
			}
			if (propertyType == typeof(Polygon))
			{
				List<Polygon> polygons = new List<Polygon>();
				for (int intervalIndex2 = 0; intervalIndex2 < features.Count; intervalIndex2++)
				{
					polygons.Add(prop.GetValue(features[intervalIndex2]) as Polygon);
				}
				HDF5VectorReader.Write(h5w, polygons, folderPlusDatasetName);
				continue;
			}
			if (propertyType == typeof(Tin))
			{
				List<Tin> tins = new List<Tin>();
				for (int intervalIndex3 = 0; intervalIndex3 < features.Count; intervalIndex3++)
				{
					tins.Add(prop.GetValue(features[intervalIndex3]) as Tin);
				}
				HDF5VectorReader.Write(h5w, tins, folderPlusDatasetName);
				continue;
			}
			if (propertyType == typeof(StationElevationProfile))
			{
				List<StationElevationProfile> stationElevationProfiles = new List<StationElevationProfile>();
				for (int intervalIndex4 = 0; intervalIndex4 < features.Count; intervalIndex4++)
				{
					stationElevationProfiles.Add(prop.GetValue(features[intervalIndex4]) as StationElevationProfile);
				}
				HDF5VectorReader.Write(h5w, stationElevationProfiles, folderPlusDatasetName);
				continue;
			}
			if (propertyType == typeof(PointCollection))
			{
				throw new NotImplementedException();
			}
			if (propertyType == typeof(Mesh))
			{
				if (datasetNameAttribute.DatasetName.StartsWith("$"))
				{
					string propName = datasetNameAttribute.DatasetName.Substring(1);
					PropertyInfo propertyInfo2 = properties.FirstOrDefault((PropertyInfo p) => p.Name == propName && p.PropertyType == typeof(string));
					if (propertyInfo2 == null)
					{
						_logger.LogWarning("Property {propName} is not a valid string property.", propName);
						continue;
					}
					for (int intervalIndex5 = 0; intervalIndex5 < features.Count; intervalIndex5++)
					{
						try
						{
							T feature = features[intervalIndex5];
							string getValue = propertyInfo2.GetValue(feature) as string;
							if (string.IsNullOrEmpty(getValue) || !(prop.GetValue(feature) is Mesh mesh))
							{
								continue;
							}
							string meshGroup = folder + getValue;
							if (propertyInfo != null && !(bool)propertyInfo.GetValue(feature))
							{
								if (saveIf.CleanOldStorage)
								{
									_logger.LogInformation("Mesh {Name} skipped saving ({PropertyName} is false). Clearing old mesh.", getValue, propertyInfo.Name);
									MeshReader.Clear(h5w, meshGroup);
								}
								else
								{
									_logger.LogInformation("Mesh {Name} skipped saving ({PropertyName} is false).", getValue, propertyInfo.Name);
								}
							}
							else
							{
								MeshReader.Write(mesh, h5w, meshGroup);
							}
						}
						catch (Exception exception)
						{
							_logger.LogWarning(exception, "Error saving mesh.");
						}
					}
				}
				else
				{
					_logger.LogWarning("Mesh loading requires a prefixed name attribute, e.g. '$Name'");
				}
			}
			else if (propertyType == typeof(Table))
			{
				if (features.Count != 1)
				{
					_logger.LogWarning("Writing 'Table' type only valid for one feature at a time.");
					continue;
				}
				T feature2 = features[0];
				if (!(prop.GetValue(feature2) is Table table))
				{
					_logger.LogWarning("Property '{PropName}' was null, table not written.", prop.Name);
				}
				else if (table.Columns.Count == 0)
				{
					_logger.LogWarning("Table for '{PropName}' has zero columns, table not written.", prop.Name);
				}
			}
			else if (propertyType == typeof(string))
			{
				string[] strings = new string[features.Count];
				for (int intervalIndex6 = 0; intervalIndex6 < features.Count; intervalIndex6++)
				{
					strings[intervalIndex6] = prop.GetValue(features[intervalIndex6]) as string;
				}
				h5w.WriteVariableLenStringArray(folderPlusDatasetName, strings);
			}
			else if (propertyType.IsArray)
			{
				Type elementType = propertyType.GetElementType();
				int arrayRank = propertyType.GetArrayRank();
				if (elementType == typeof(string) && arrayRank == 1)
				{
					WriteVLenStringArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as string[]).ToArray());
					continue;
				}
				if (!elementType.IsPrimitive && elementType != typeof(DateTime))
				{
					_logger.LogWarning("Array of type {subtype} not currently supported. ({propName})", elementType, prop.Name);
					continue;
				}
				switch (arrayRank)
				{
				case 1:
					if (elementType == typeof(double))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as double[]).ToArray());
					}
					else if (elementType == typeof(float))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as float[]).ToArray());
					}
					else if (elementType == typeof(long))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as long[]).ToArray());
					}
					else if (elementType == typeof(int))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as int[]).ToArray());
					}
					else if (elementType == typeof(short))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as short[]).ToArray());
					}
					else if (elementType == typeof(byte))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as byte[]).ToArray());
					}
					else if (elementType == typeof(DateTime))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => (prop.GetValue(f) as DateTime[] ?? Array.Empty<DateTime>()).Select((DateTime dt) => dt.ToBinary()).ToArray()).ToArray());
						WriteVLenStringArray(h5w, folderPlusDatasetName + " (DESC)", features.Select((T f) => (prop.GetValue(f) as DateTime[] ?? Array.Empty<DateTime>()).Select((DateTime dt) => dt.ToString()).ToArray()).ToArray());
					}
					else
					{
						_logger.LogWarning("Array of type {subtype} not currently supported. ({propName})", elementType, prop.Name);
					}
					break;
				case 2:
					if (elementType == typeof(double))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as double[,]).ToArray());
					}
					else if (elementType == typeof(float))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as float[,]).ToArray());
					}
					else if (elementType == typeof(long))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as long[,]).ToArray());
					}
					else if (elementType == typeof(int))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as int[,]).ToArray());
					}
					else if (elementType == typeof(short))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as short[,]).ToArray());
					}
					else if (elementType == typeof(byte))
					{
						WriteInfoValuesPrimitiveArray(h5w, folderPlusDatasetName, features.Select((T f) => prop.GetValue(f) as byte[,]).ToArray());
					}
					else
					{
						_logger.LogWarning("Array of type {subtype} not currently supported. ({propName})", elementType, prop.Name);
					}
					break;
				default:
					_logger.LogWarning("Array Rank {rank} not currently supported. ({propName})", arrayRank, prop.Name);
					break;
				}
			}
			else
			{
				_logger.LogWarning("Dataset type {ColumnType} not currently supported.", propertyType);
			}
		}
	}

	private static void ExpandFeatures<T>(IList<T> features, FeatureIOTable table, string prefix = "")
	{
		if (features.IsNullOrEmpty())
		{
			throw new Exception();
		}
		Type type = features.First().GetType();
		PropertyInfo[] properties = type.GetProperties();
		foreach (PropertyInfo prop in properties)
		{
			foreach (Attribute customAttribute in prop.GetCustomAttributes())
			{
				if (customAttribute is MetadataAttribute { KeyName: var keyName })
				{
					if (string.IsNullOrEmpty(keyName))
					{
						throw new Exception("Warning: No column name provided for property " + prop.Name);
					}
					string prefixPlusKeyName = prefix + keyName;
					Type propertyType = prop.PropertyType;
					if ((propertyType.IsPrimitive && !propertyType.IsEnum) || propertyType == typeof(string))
					{
						int addColumn = table.AddColumn(prefixPlusKeyName, propertyType);
						for (int j = 0; j < features.Count; j++)
						{
							table.GetRow(j)[addColumn] = prop.GetValue(features[j]);
						}
					}
					else if (propertyType == typeof(DateTime))
					{
						bool condition = false;
						int addColumn2 = table.AddColumn(prefixPlusKeyName, typeof(long));
						for (int k = 0; k < features.Count; k++)
						{
							long toBinary = (prop.GetValue(features[k]) as DateTime?).GetValueOrDefault().ToBinary();
							if (toBinary < 0 || toBinary > DateTime.MaxValue.Ticks)
							{
								toBinary = DateTime.FromBinary(toBinary).Ticks;
								condition = true;
							}
							table.GetRow(k)[addColumn2] = toBinary;
						}
						if (condition)
						{
							_logger.LogWarning("DateTime upper bits corrected on save for {TypeName}.{PropertyName}", type, prop.Name);
						}
						addColumn2 = table.AddColumn(prefixPlusKeyName + " (DESC)", typeof(string));
						for (int l = 0; l < features.Count; l++)
						{
							table.GetRow(l)[addColumn2] = (prop.GetValue(features[l]) as DateTime?).GetValueOrDefault().ToString();
						}
					}
					else if (propertyType == typeof(TimeSpan))
					{
						int addColumn3 = table.AddColumn(prefixPlusKeyName, typeof(long));
						for (int m = 0; m < features.Count; m++)
						{
							table.GetRow(m)[addColumn3] = (prop.GetValue(features[m]) as TimeSpan?).GetValueOrDefault().Ticks;
						}
						addColumn3 = table.AddColumn(prefixPlusKeyName + " (DESC)", typeof(string));
						for (int n = 0; n < features.Count; n++)
						{
							table.GetRow(n)[addColumn3] = (prop.GetValue(features[n]) as TimeSpan?).GetValueOrDefault().ToString();
						}
					}
					else
					{
						if (!propertyType.IsEnum)
						{
							throw new Exception("Cannot parse column type " + propertyType?.ToString() + ", " + prefixPlusKeyName + " (Must be a primitive, string, or enum.");
						}
						Dictionary<object, string> serializeMap = AliasAttribute.GetSerializeMap(propertyType);
						int addColumn4 = table.AddColumn(prefixPlusKeyName, typeof(string));
						for (int intervalIndex = 0; intervalIndex < features.Count; intervalIndex++)
						{
							table.GetRow(intervalIndex)[addColumn4] = serializeMap[prop.GetValue(features[intervalIndex])];
						}
					}
				}
				else if (customAttribute is WrappedColumnNameAttribute wrappedColumnNameAttribute)
				{
					Type propertyType2 = prop.PropertyType;
					if (!propertyType2.IsValueType)
					{
						throw new Exception(propertyType2?.ToString() + " is not a value type. (" + prop.Name + ")");
					}
					int count = wrappedColumnNameAttribute.Count;
					for (int intervalIndex2 = 0; intervalIndex2 < count; intervalIndex2++)
					{
						string columnName = wrappedColumnNameAttribute.ColumnNames[intervalIndex2];
						string childPropertiesOrField = wrappedColumnNameAttribute.ChildPropertiesOrFields[intervalIndex2];
						columnName = prefix + columnName;
						MemberInfo memberInfo = null;
						Type type2 = null;
						PropertyInfo property = propertyType2.GetProperty(childPropertiesOrField);
						if (property != null)
						{
							memberInfo = property;
							type2 = property.PropertyType;
						}
						else
						{
							FieldInfo? field = propertyType2.GetField(childPropertiesOrField);
							if (field == null)
							{
								throw new Exception("Could not find property or field " + childPropertiesOrField + " on " + propertyType2.Name);
							}
							memberInfo = field;
							type2 = field.FieldType;
						}
						int addColumn5 = table.AddColumn(columnName, type2);
						for (int intervalIndex3 = 0; intervalIndex3 < features.Count; intervalIndex3++)
						{
							object forObject = features[intervalIndex3];
							object getValue = GetValue(prop, forObject);
							table.GetRow(intervalIndex3)[addColumn5] = GetValue(memberInfo, getValue);
						}
					}
				}
				else if (customAttribute is ComplexMetadataAttribute complexMetadataAttribute)
				{
					_ = prop.PropertyType;
					ExpandFeatures(features.Select((T a) => prop.GetValue(a)).ToList(), table, prefix + complexMetadataAttribute.UsableColumnNamePrefix);
				}
			}
		}
	}

	private static void WriteInfoValuesPrimitiveArray<T>(H5Writer h5w, string dset, T[][] data) where T : unmanaged
	{
		int count = data.Length;
		int count2 = 0;
		for (int i = 0; i < data.Length; i++)
		{
			count2 += data[i]?.Length ?? 0;
		}
		if (count2 == 0)
		{
			h5w.Delete(dset);
			return;
		}
		T[] values = new T[count2];
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		Span<T> valuesSpan = values.AsSpan();
		int ordinal = 0;
		for (int j = 0; j < count; j++)
		{
			if (data[j] == null)
			{
				intValues[j] = ordinal;
				intValuesArray[j] = 0;
				continue;
			}
			data[j].AsSpan().CopyTo(valuesSpan.Slice(ordinal));
			int ordinal2 = data[j].Length;
			intValues[j] = ordinal;
			intValuesArray[j] = ordinal2;
			ordinal += ordinal2;
		}
		h5w.WriteDataset(dset, values);
		h5w.WriteAttribute(dset, "Start", intValues);
		h5w.WriteAttribute(dset, "Count", intValuesArray);
	}

	private static void WriteVLenStringArray(H5Writer h5w, string dset, string[][] data)
	{
		int count = data.Length;
		int count2 = 0;
		for (int i = 0; i < data.Length; i++)
		{
			count2 += data[i]?.Length ?? 0;
		}
		if (count2 == 0)
		{
			h5w.Delete(dset);
			return;
		}
		string[] strings = new string[count2];
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		int ordinal = 0;
		for (int j = 0; j < count; j++)
		{
			if (data[j] == null)
			{
				intValues[j] = ordinal;
				intValuesArray[j] = 0;
				continue;
			}
			data[j].CopyTo(strings, ordinal);
			int ordinal2 = data[j].Length;
			intValues[j] = ordinal;
			intValuesArray[j] = ordinal2;
			ordinal += ordinal2;
		}
		h5w.WriteVariableLenStringArray(dset, strings);
		h5w.WriteAttribute(dset, "Start", intValues);
		h5w.WriteAttribute(dset, "Count", intValuesArray);
	}

	private static void WriteInfoValuesPrimitiveArray<T>(H5Writer h5w, string dset, T[][,] data) where T : unmanaged
	{
		int count = data.Length;
		int lengthSum = 0;
		int count2 = 0;
		foreach (T[,] rows in data)
		{
			if (rows == null)
			{
				continue;
			}
			int length = rows.GetLength(0);
			int length2 = rows.GetLength(1);
			if (length != 0)
			{
				lengthSum += length;
				if (count2 > 0 && length2 > 0 && count2 != length2)
				{
					_logger.LogOnce(LogLevel.Warning, "2D Jagged datasets are not well-supported with different numbers of columns. " + typeof(T).FullName);
				}
				count2 = Math.Max(count2, length2);
			}
		}
		if (lengthSum == 0 || count2 == 0)
		{
			return;
		}
		T[,] rows2 = new T[lengthSum, count2];
		int[] intValues = new int[count];
		int[] intValuesArray = new int[count];
		int length3Sum = 0;
		for (int j = 0; j < count; j++)
		{
			T[,] rows3 = data[j];
			if (rows3 == null)
			{
				intValues[j] = 0;
				intValuesArray[j] = 0;
				continue;
			}
			int length3 = rows3.GetLength(0);
			int length4 = rows3.GetLength(1);
			for (int k = 0; k < length3; k++)
			{
				for (int l = 0; l < length4; l++)
				{
					rows2[k + length3Sum, l] = rows3[k, l];
				}
			}
			intValues[j] = length3Sum;
			intValuesArray[j] = length3;
			length3Sum += length3;
		}
		h5w.WriteDataset(dset, rows2);
		h5w.WriteAttribute(dset, "Start", intValues);
		h5w.WriteAttribute(dset, "Count", intValuesArray);
	}

	private static object GetValue(MemberInfo memberInfo, object forObject)
	{
		return memberInfo.MemberType switch
		{
			MemberTypes.Field => ((FieldInfo)memberInfo).GetValue(forObject), 
			MemberTypes.Property => ((PropertyInfo)memberInfo).GetValue(forObject), 
			_ => throw new NotImplementedException(), 
		};
	}

	public static void Clean<T>(H5Writer hw, string group, string attrDataset = "Attributes")
	{
		string datasetName = DatasetName(ref group, attrDataset);
		if (hw.ExistsAsDataset(datasetName))
		{
			hw.Delete(datasetName);
		}
		throw new NotImplementedException();
	}
}
