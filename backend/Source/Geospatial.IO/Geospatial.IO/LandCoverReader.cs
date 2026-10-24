using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.Rasters.Classifications;
using H5Assist;
using Utility.Extensions;
using Utility.Files;

namespace Geospatial.IO;

public static class LandCoverReader
{
	private const string IDsDataset = "IDs";

	private const string NamesDataset = "Names";

	private const string GUIDAttrName = "GUID";

	private const string HasRasterFileAttributeName = "Has Raster File";

	public const float NoDataValue = -9999f;

	public static Classification ParseLandCoverFile(string filename, out ParameterSet vars, out bool hasRasterFile)
	{
		if (!File.Exists(filename))
		{
			throw new NotImplementedException();
		}
		using H5Reader h5r = new H5Reader(filename);
		return ParseLandCoverFile(h5r, out vars, out hasRasterFile);
	}

	public static Classification ParseLandCoverFile(H5Reader h5r, out ParameterSet vars, out bool hasRasterFile)
	{
		Dictionary<int, string> stringByInt = new Dictionary<int, string>();
		vars = new ParameterSet();
		AssertExpectedDataset(h5r, "IDs");
		AssertExpectedDataset(h5r, "Names");
		h5r.ReadDatasetAs<int>("IDs", out var values);
		h5r.ReadDataset("Names", out var values2);
		if (values == null || values2 == null || values.Length != values2.Length)
		{
			throw new Exception("Malformed LandCover dataset: Error with IDs/Names datasets. " + h5r.Filename.InParentheses());
		}
		for (int i = 0; i < values.Length; i++)
		{
			int key = values[i];
			string values22 = values2[i];
			stringByInt[key] = values22;
		}
		if (h5r.ExistsAsAttribute("/", "GUID"))
		{
			h5r.ReadAttributeScalar("/", "GUID", out var _);
		}
		if (h5r.TryReadAttributeScalar("/", "Has Raster File", out var localValue))
		{
			if (!bool.TryParse(localValue, out hasRasterFile))
			{
				throw new Exception("Malformed Land Cover dataset - corrupt attribute Has Raster File");
			}
		}
		else
		{
			hasRasterFile = true;
		}
		foreach (string datasetName in h5r.GetDatasetNames("/"))
		{
			if (datasetName.Equals("IDs", StringComparison.InvariantCultureIgnoreCase) || datasetName.Equals("Names", StringComparison.InvariantCultureIgnoreCase))
			{
				continue;
			}
			Type datasetType = h5r.GetDatasetType(datasetName);
			if (datasetType != typeof(float) && datasetType != typeof(double))
			{
				continue;
			}
			h5r.ReadDatasetAs<float>("/" + datasetName, out var values3);
			if (values3 == null || values3.Length != values.Length)
			{
				continue;
			}
			Dictionary<string, float> floatByString = new Dictionary<string, float>();
			for (int j = 0; j < values.Length; j++)
			{
				float values32 = values3[j];
				if (!IsVariableNoData(values32))
				{
					floatByString[values2[j]] = values32;
				}
			}
			vars.Add(new Parameter(datasetName, floatByString));
		}
		return new Classification(stringByInt);
	}

	public static bool IsVariableNoData(float floatValue)
	{
		if (floatValue != -9999f && floatValue != float.MaxValue)
		{
			return float.IsNaN(floatValue);
		}
		return true;
	}

	public static void Save(string fn, Classification cls, List<Parameter> variables = null, bool hasRasterFile = true)
	{
		if (!FileHelpers.TryDelete(fn))
		{
			throw new Exception("Could not delete file " + fn);
		}
		using H5Writer h5Writer = new H5Writer(fn);
		string newGuid = Guid.NewGuid().ToString();
		h5Writer.WriteAttribute("/", "GUID", newGuid);
		h5Writer.WriteAttribute("/", "Has Raster File", hasRasterFile.ToString());
		List<KeyValuePair<int, string>> source = (from kvp in cls.GetAllClassifications()
			orderby kvp.Key
			select kvp).ToList();
		int[] values = source.Select((KeyValuePair<int, string> kvp) => kvp.Key).ToArray();
		string[] strings = source.Select((KeyValuePair<int, string> kvp) => kvp.Value).ToArray();
		h5Writer.WriteDataset("IDs", values);
		h5Writer.WriteDataset("Names", strings);
		if (variables == null)
		{
			return;
		}
		foreach (Parameter clsVar in variables)
		{
			float[] values2 = strings.Select((string n) => clsVar.Values.TryGetValue(n, out var localValue) ? localValue : (-9999f)).ToArray();
			h5Writer.WriteDataset(clsVar.VariableName, values2);
		}
	}

	private static void AssertExpectedDataset(H5Reader h5r, string dataset)
	{
		if (!h5r.ExistsAsDataset(dataset))
		{
			throw new Exception("Error, expected datataset " + dataset + " did not exist.");
		}
	}
}
