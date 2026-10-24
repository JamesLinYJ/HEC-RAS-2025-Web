using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class LandCoverFile
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__39_002D0
	{
		public int keyInt;

		public _Closure_0024__39_002D0(_Closure_0024__39_002D0 other)
		{
			if (other != null)
			{
				keyInt = other.keyInt;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(LandCoverCategory s)
		{
			return s.ID == keyInt;
		}
	}

	public const string INPUT_NAME = "Name Field";

	public const string MAPPED_NAME = "Classification";

	public string Filename;

	public string ShortName;

	public bool IsRaster;

	public DataTable ValueToOutput;

	private string _selectedIdentifierColumnName;

	public List<string> FieldColumns;

	public List<int> FieldColumnIdxs;

	public List<string> AttributeColumns;

	public DataTable FeatureTable;

	public List<int> PolygonsInBounds;

	public PolygonFeatureLayer PolygonLayer;

	public int NoDataValue;

	public double CellSize;

	public string Units;

	public PixelRange DesiredPixelExtents;

	public Geospatial.Vectors.Extent DesiredExtents;

	public bool ToBeCropped;

	public bool UsedProjectProjection;

	public Extent Extent;

	public Extent FullExtent;

	public Projection LocalProjection;

	private Extent _restrictToExtent;

	private Projection _projectProjection;

	public string ErrMsg;

	public bool Success;

	public const string NODATASTR = "NoData";

	private readonly Dictionary<string, int> _vectorLookup;

	private readonly Dictionary<int, int> _rasterLookup;

	public string SelectedIdentifierColumn
	{
		get
		{
			return _selectedIdentifierColumnName;
		}
		set
		{
			if (Operators.CompareString(value, _selectedIdentifierColumnName, TextCompare: false) == 0)
			{
				return;
			}
			_selectedIdentifierColumnName = value;
			ValueToOutput.Rows.Clear();
			int columnIndex = FeatureTable.Columns.IndexOf(SelectedIdentifierColumn);
			HashSet<string> stringSet = new HashSet<string>();
			checked
			{
				int count = PolygonsInBounds.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					DataRow dataRow = FeatureTable.Rows[PolygonsInBounds[i]];
					if (!Information.IsDBNull(RuntimeHelpers.GetObjectValue(dataRow[columnIndex])))
					{
						stringSet.Add(Conversions.ToString(dataRow[columnIndex]));
					}
				}
				foreach (string textValue in stringSet)
				{
					ValueToOutput.Rows.Add(textValue, textValue);
				}
			}
		}
	}

	public LandCoverFile(string fileName, Extent restrictToExtent, Projection projectProjection)
	{
		FieldColumns = new List<string>();
		FieldColumnIdxs = new List<int>();
		PolygonsInBounds = new List<int>();
		UsedProjectProjection = false;
		_vectorLookup = new Dictionary<string, int>();
		_rasterLookup = new Dictionary<int, int>();
		Filename = fileName;
		ShortName = System.IO.Path.GetFileName(fileName);
		_projectProjection = projectProjection;
		if (Extent.IsValid(restrictToExtent))
		{
			_restrictToExtent = restrictToExtent;
		}
		if (_restrictToExtent == null)
		{
			ToBeCropped = false;
		}
		else
		{
			ToBeCropped = true;
		}
		ValueToOutput = new DataTable();
		DataColumn dataColumn = new DataColumn("Name Field", typeof(string));
		DataColumn column = new DataColumn("Classification", typeof(string));
		dataColumn.ReadOnly = true;
		ValueToOutput.Columns.Add(dataColumn);
		ValueToOutput.Columns.Add(column);
		InitializeFile();
	}

	public override string ToString()
	{
		return ShortName;
	}

	public Type ColumnType(string colName)
	{
		return FeatureTable.Columns[colName].DataType;
	}

	private void InitializeFile()
	{
		string? extension = System.IO.Path.GetExtension(Filename);
		string directoryName = System.IO.Path.GetDirectoryName(Filename);
		if (extension.RoughlyEquals(".shp"))
		{
			if (!InitializePolygon())
			{
				return;
			}
		}
		else if (File.Exists(System.IO.Path.Combine(directoryName, "gdb")))
		{
			if (!InitializeSSURGO())
			{
				return;
			}
		}
		else if (!InitializeRaster())
		{
			return;
		}
		ErrMsg = "";
		Success = true;
	}

	private bool InitializePolygon()
	{
		IsRaster = false;
		PolygonLayer = new PolygonFeatureLayer("Polygons SHP", Filename);
		LocalProjection = ShapefileStorage.GetProjection(Filename);
		FeatureTable = PolygonLayer.FeatureTable();
		if (PolygonLayer.FeatureCount() == 0)
		{
			Success = false;
			ErrMsg = ShortName + " has 0 features.";
			return false;
		}
		checked
		{
			int count = PolygonLayer.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = PolygonLayer.Feature(i);
				if (!(feature is Polygon))
				{
					Success = false;
					ErrMsg = ShortName + " is not a polygon shapefile.";
					return false;
				}
				Extent extent = ((Polygon)feature).ComputeExtent();
				FullExtent = new Extent(FullExtent, extent);
				if (!ToBeCropped || extent.Overlaps(_restrictToExtent))
				{
					PolygonsInBounds.Add(i);
					Extent = new Extent(Extent, extent);
				}
			}
			if (PolygonsInBounds.Count == 0)
			{
				Success = false;
				ErrMsg = "There were no valid polygons in the target area for " + ShortName;
				return false;
			}
			AttributeColumns = new List<string>();
			int count2 = FeatureTable.Columns.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				DataColumn dataColumn = FeatureTable.Columns[j];
				if (!dataColumn.ReadOnly)
				{
					AttributeColumns.Add(dataColumn.ColumnName);
				}
			}
			if (AttributeColumns.Count == 0)
			{
				Success = false;
				ErrMsg = "There were no string attribute columns for identifying land-use categories in " + ShortName;
				return false;
			}
			foreach (string attributeColumn in AttributeColumns)
			{
				DataColumn dataColumn2 = FeatureTable.Columns[attributeColumn];
				if (Operators.CompareString(SelectedIdentifierColumn, "", TextCompare: false) == 0 && dataColumn2.DataType == typeof(string))
				{
					SelectedIdentifierColumn = attributeColumn;
				}
			}
			if (Operators.CompareString(SelectedIdentifierColumn, "", TextCompare: false) == 0)
			{
				SelectedIdentifierColumn = AttributeColumns[0];
			}
			return true;
		}
	}

	private bool InitializeSSURGO()
	{
		IsRaster = false;
		if (_restrictToExtent == null)
		{
			_restrictToExtent = Extent.Infinite;
		}
		PolygonLayer = new SsurgoLayer(Filename, _restrictToExtent);
		LocalProjection = ShapefileStorage.GetProjection(Filename);
		FeatureTable = PolygonLayer.FeatureTable();
		if (PolygonLayer.FeatureCount() == 0)
		{
			Success = false;
			ErrMsg = "There were no valid polygons in the target area for " + ShortName;
			return false;
		}
		PolygonsInBounds = Enumerable.Range(0, PolygonLayer.FeatureCount()).ToList();
		AttributeColumns = new List<string>();
		checked
		{
			int count = FeatureTable.Columns.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				DataColumn dataColumn = FeatureTable.Columns[i];
				if (!dataColumn.ReadOnly && dataColumn.DataType == typeof(string))
				{
					AttributeColumns.Add(dataColumn.ColumnName);
				}
			}
			if (AttributeColumns.Count == 0)
			{
				Success = false;
				ErrMsg = "There were no string attribute columns for identifying land-use categories in " + ShortName;
				return false;
			}
			foreach (string attributeColumn in AttributeColumns)
			{
				DataColumn dataColumn2 = FeatureTable.Columns[attributeColumn];
				if (Operators.CompareString(SelectedIdentifierColumn, "", TextCompare: false) == 0 && dataColumn2.DataType == typeof(string))
				{
					SelectedIdentifierColumn = attributeColumn;
				}
				if (Operators.CompareString(attributeColumn, "Hydrologic Group", TextCompare: false) == 0)
				{
					SelectedIdentifierColumn = attributeColumn;
				}
			}
			if (Operators.CompareString(SelectedIdentifierColumn, "", TextCompare: false) == 0)
			{
				SelectedIdentifierColumn = AttributeColumns[0];
			}
			return true;
		}
	}

	private bool InitializeRaster()
	{
		IsRaster = true;
		FeatureTable = null;
		AttributeColumns = null;
		List<int> intList = new List<int>();
		if (!GetUniqueRasterValues(ref intList))
		{
			if (Operators.CompareString(ErrMsg, "", TextCompare: false) == 0)
			{
				ErrMsg = "Unknown error reading file: " + ShortName;
			}
			Success = false;
			return false;
		}
		foreach (int ordinal in intList)
		{
			if (ordinal == NoDataValue)
			{
				ValueToOutput.Rows.Add(ordinal.ToString(), "NoData");
			}
			else
			{
				ValueToOutput.Rows.Add(ordinal.ToString(), ordinal.ToString());
			}
		}
		return true;
	}

	private bool GetUniqueRasterValues(ref List<int> intList)
	{
		intList = new List<int>();
		checked
		{
			bool result;
			try
			{
				PixelRange pixelRange;
				using (GDALRaster gDALRaster = new GDALRaster(Filename))
				{
					LocalProjection = gDALRaster.GetProjection();
					if (LocalProjection == null || LocalProjection.IsValid() == SRSValidation.Corrupt)
					{
						LocalProjection = _projectProjection;
						UsedProjectProjection = true;
						FullExtent = Converter.Convert(gDALRaster.GetFullNativeExtents());
					}
					else
					{
						UsedProjectProjection = false;
						if (_projectProjection != null && _projectProjection.IsValid() == SRSValidation.Valid)
						{
							FullExtent = Converter.Convert(gDALRaster.GetProjectedExtents(_projectProjection));
						}
						else
						{
							FullExtent = Converter.Convert(gDALRaster.GetFullNativeExtents());
						}
					}
					CellSize = gDALRaster.GetCellSizeX();
					Units = GDALRaster.GetLinearUnitsName(Filename);
					if (_restrictToExtent != null)
					{
						Geospatial.Vectors.Extent extent = Converter.Convert(_restrictToExtent);
						pixelRange = ((LocalProjection != null && _projectProjection != null && !LocalProjection.IsEqual(_projectProjection)) ? gDALRaster.GetPixelCorners(extent, _projectProjection) : gDALRaster.GetPixelCorners(extent));
					}
					else
					{
						pixelRange = gDALRaster.GetPixelCorners();
					}
					if (pixelRange == PixelRange.NullRange || pixelRange.Width == 0 || pixelRange.Height == 0)
					{
						ErrMsg = ShortName + " does not overlap the restricted extents.";
						Success = false;
						result = false;
					}
					else
					{
						DesiredPixelExtents = pixelRange;
						DesiredExtents = gDALRaster.GetNativeExtents(DesiredPixelExtents);
						if (DesiredPixelExtents == gDALRaster.GetPixelCorners())
						{
							ToBeCropped = false;
						}
						if (unchecked((long)DesiredPixelExtents.Width) * unchecked((long)DesiredPixelExtents.Height) > 100000000 && MessageBox.Show("Based on your file-restriction selection, you've asked RAS to process over 100 million cells. This might take a long time. Continue?", "Continue?", MessageBoxButtons.OkCancel) == DialogResult.Cancel)
						{
							ErrMsg = "Processing Cancelled.";
							Success = false;
							result = false;
						}
						else
						{
							int bandCount = gDALRaster.GetBandCount();
							if (bandCount == 0)
							{
								ErrMsg = ShortName + " appears to be corrupt: 0 raster bands.";
								Success = false;
								result = false;
							}
							else
							{
								if (bandCount <= 1)
								{
									string toLower = gDALRaster.BandDataType_Names()[0].ToLower();
									switch (toLower)
									{
									default:
										ErrMsg = ShortName.InDoubleQuotes() + " has the wrong type of data: " + toLower + ". We only support importing integer land classification rasters (such as the NLCD) or shapefiles.";
										Success = false;
										result = false;
										goto end_IL_0007;
									case "gdt_byte":
									case "gdt_uint16":
									case "gdt_int16":
									case "gdt_uint32":
									case "gdt_int32":
									{
										double noDataVB = gDALRaster.GetNoDataVB()[0];
										if (double.IsNaN(noDataVB))
										{
											NoDataValue = 0;
										}
										else
										{
											NoDataValue = (int)System.Math.Round(noDataVB);
										}
										break;
									}
									}
									goto end_IL_001b;
								}
								ErrMsg = ShortName + " has more than one raster-band.";
								Success = false;
								result = false;
							}
						}
					}
					goto end_IL_0007;
					end_IL_001b:;
				}
				int ordinal = 1000;
				int ordinal2 = 1000;
				_ = (int)System.Math.Ceiling((double)pixelRange.Width / (double)ordinal);
				_ = (int)System.Math.Ceiling((double)pixelRange.Height / (double)ordinal2);
				new HashSet<int>().Add(NoDataValue);
				Dictionary<int, HashSet<int>> intSetByInt = new Dictionary<int, HashSet<int>>();
				new ThreadLocal<HashSet<int>>([SpecialName] () =>
				{
					if (!intSetByInt.ContainsKey(Thread.CurrentThread.ManagedThreadId))
					{
						intSetByInt[Thread.CurrentThread.ManagedThreadId] = new HashSet<int>();
					}
					return intSetByInt[Thread.CurrentThread.ManagedThreadId];
				});
				throw new NotImplementedException();
				end_IL_0007:;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ErrMsg = "Unknown Error: " + ex2.Message.ToString();
				Success = false;
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}

	public bool KeysAreInts()
	{
		if (ValueToOutput == null || ValueToOutput.Rows.Count == 0)
		{
			return false;
		}
		foreach (DataRow row in ValueToOutput.Rows)
		{
			string s = Conversions.ToString(row["Name Field"]);
			int result = 0;
			if (!int.TryParse(s, out result))
			{
				return false;
			}
		}
		return true;
	}

	public bool VerifyKnownNamingScheme(List<LandCoverCategory> categories)
	{
		if (ValueToOutput == null || ValueToOutput.Rows.Count == 0)
		{
			return false;
		}
		_Closure_0024__39_002D0 capturedLocals = default;
		foreach (DataRow row in ValueToOutput.Rows)
		{
			capturedLocals = new _Closure_0024__39_002D0(capturedLocals);
			string s = Conversions.ToString(row["Name Field"]);
			capturedLocals.keyInt = 0;
			if (!int.TryParse(s, out capturedLocals.keyInt))
			{
				return false;
			}
			LandCoverCategory landCoverCategory = categories.FirstOrDefault(capturedLocals._Lambda_0024__0);
			if (landCoverCategory == null)
			{
				return false;
			}
			if (!landCoverCategory.Name.RoughlyEquals(Conversions.ToString(row["Classification"])))
			{
				return false;
			}
		}
		return true;
	}

	private Dictionary<string, string> GetMappedNames()
	{
		Dictionary<string, string> mappedNames = new Dictionary<string, string>();
		foreach (DataRow row in ValueToOutput.Rows)
		{
			mappedNames.Add(Conversions.ToString(row["Name Field"]), Conversions.ToString(row["Classification"]));
		}
		return mappedNames;
	}

	public static bool IsRoughlyNoDataString(string stringValue)
	{
		string left = stringValue.ToLowerInvariant().Trim();
		if (Operators.CompareString(left, "nodata", TextCompare: false) == 0 || Operators.CompareString(left, "no data", TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}

	public void SetNameIDMap(string[] names, int[] ids)
	{
		Func<string, string> func = [SpecialName] (string s) => IsRoughlyNoDataString(s) ? "NoData" : s.ToLowerInvariant().Trim();
		Dictionary<string, int> intByString = new Dictionary<string, int>();
		checked
		{
			int count = names.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				intByString.Add(func(names[intervalIndex]), ids[intervalIndex]);
			}
			Dictionary<string, string> mappedNames = GetMappedNames();
			if (IsRaster)
			{
				_rasterLookup.Clear();
				{
					foreach (KeyValuePair<string, string> mappedName in mappedNames)
					{
						_rasterLookup.Add(Convert.ToInt32(mappedName.Key), intByString[func(mappedName.Value)]);
					}
					return;
				}
			}
			_vectorLookup.Clear();
			foreach (KeyValuePair<string, string> mappedName2 in mappedNames)
			{
				_vectorLookup.Add(mappedName2.Key, intByString[func(mappedName2.Value)]);
			}
		}
	}

	public int VectorLookup(string stringValue)
	{
		return _vectorLookup[stringValue];
	}

	public int PolygonLookup(int polyIdx)
	{
		int columnIndex = FeatureTable.Columns.IndexOf(SelectedIdentifierColumn);
		return VectorLookup(Conversions.ToString(FeatureTable.Rows[polyIdx][columnIndex]));
	}

	public int RasterLookup(int intValue)
	{
		return _rasterLookup[intValue];
	}
}
