using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Names;
using Utility;
using Utility.Extensions;

namespace RasMapperLib.Render;

public class AuxillaryRenderer : Renderer
{
	[Serializable]
	[CompilerGenerated]
	internal new sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<Point2D, float> _0024I13_002D0;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal float _Lambda_0024__13_002D0(Point2D volElev)
		{
			return volElev.Y;
		}
	}

	private Names _wsNames;

	private Names _auxNames;

	private H5RowCache<float> _rowCache;

	private DepthRenderer _dr;

	private Generic2DMapParameters _d2MapParams;

	public static void Compute(RASResultsMap map, string varName, int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] values, [Optional][DefaultParameterValue(null)] ref float[] vx, [Optional][DefaultParameterValue(null)] ref float[] vy, CacheCollection cache = null)
	{
		new AuxillaryRenderer(map, varName, cache).ComputeVariable(profile, mps, terrain, ref values, ref vx, ref vy);
	}

	public AuxillaryRenderer(RASResultsMap map, string varName, CacheCollection cache = null)
		: base(map.Results, cache, needsVelocityFlowData: true, needsPostProcessFile: false)
	{
		_wsNames = GetWaterSurfaceDatasetNames();
		_wsNames.Validate(_hdfFilename);
		_auxNames = new Names();
		_auxNames.Profile1D = map.OutputBlock.GetXSDatasetPath(map.Results.HydraulicComputeMethod, varName);
		_auxNames.ProfileSA = map.OutputBlock.GetSADatasetPath(map.Results.HydraulicComputeMethod, varName);
		List<string> profile2D = new List<string>();
		checked
		{
			int count = _d2FlowAreaNames.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				profile2D.Add(map.OutputBlock.Get2DDatasetPath(map.Results.HydraulicComputeMethod, _d2FlowAreaNames[i], varName));
			}
			_auxNames.Profile2D = profile2D;
			_auxNames.Validate(_hdfFilename);
			_rowCache = cache.GetRowCache<float>(_hdfFilename);
			_dr = new DepthRenderer(map.Results, cache);
			using H5Reader hr = new H5Reader(_hdfFilename);
			try
			{
				if (!Generic2DMapParameters.Parse(hr, _auxNames.Profile2D[0], ref _d2MapParams))
				{
					_d2MapParams = null;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				throw new Exception("Error loading map: " + map.Name);
			}
		}
	}

	public bool VariableUsesVXVY(RASGeometryMapPoints mps)
	{
		if (!_auxNames.Profile2D.IsNullOrEmpty() && !_wsNames.Profile2D.IsNullOrEmpty() && mps.HasMapped2DCells())
		{
			using (H5Reader hr = new H5Reader(_hdfFilename))
			{
				Generic2DMapParameters mapParams = null;
				if (Generic2DMapParameters.Parse(hr, _auxNames.Profile2D[0], ref mapParams))
				{
					return mapParams.Location == Location.Face && mapParams.Orientation == Orientation.Normal;
				}
				return false;
			}
		}
		return false;
	}

	public void ComputeVariable(int profile, RASGeometryMapPoints mps, float[] terrain, ref float[] values, ref float[] vx, ref float[] vy)
	{
		if (profile == int.MaxValue || profile == int.MinValue)
		{
			throw new ArgumentException("[EXPERIMENTAL VARIABLE] RAS only supports auxillary variables for time-series data, not max/min profiles.");
		}
		Buffers.ArraySetAllVB(ref values, -9999f, mps.InputCount);
		float[] depths = null;
		_dr.Compute(profile, mps, terrain, ref depths);
		if (!string.IsNullOrEmpty(_auxNames.Profile1D) && mps.MappedXSISs.Count > 0)
		{
			ComputeVariableInternal1D(_auxNames.Profile1D, profile, mps, _rowCache, depths, values);
		}
		if (!string.IsNullOrEmpty(_auxNames.ProfileSA) && mps.MappedSAs.Count > 0)
		{
			ComputeVariableInternalSA(_auxNames.ProfileSA, profile, mps, _rowCache, depths, values);
		}
		if (_auxNames.Profile2D.IsNullOrEmpty() || _wsNames.Profile2D.IsNullOrEmpty() || !mps.HasMapped2DCells())
		{
			return;
		}
		checked
		{
			using (new H5Reader(_hdfFilename))
			{
				if (_d2MapParams == null)
				{
					return;
				}
				float[][] meshCellWaterSurfaces = ReadMappedMeshProfile(_wsNames.Profile2D, profile, _rowCache, mps);
				float[][] mappedMeshProfile = ReadMappedMeshProfile(_auxNames.Profile2D, profile, _rowCache, mps);
				if (_d2MapParams.Location == Location.Cell)
				{
					int count = mappedMeshProfile.Length - 1;
					for (int i = 0; i <= count; i++)
					{
						if (mappedMeshProfile[i] == null)
						{
							continue;
						}
						MeshFV2D meshFV2D = _d2FlowArea.Mesh(i);
						int count2 = mappedMeshProfile[i].Length - 1;
						for (int j = 0; j <= count2; j++)
						{
							float minimumElevation = meshFV2D.Cell(j).MinimumElevation;
							_ = mappedMeshProfile[i][j];
							_ = RASResults.MinWSPlotTolerance;
							if (meshFV2D.CellIsVirtual(j))
							{
								int[] cellFaces = meshFV2D.CellFaces(j);
								if (meshFV2D.Faces[cellFaces[0]].cellA == j)
								{
									mappedMeshProfile[i][j] = mappedMeshProfile[i][meshFV2D.Faces[cellFaces[0]].cellB];
								}
								else
								{
									mappedMeshProfile[i][j] = mappedMeshProfile[i][meshFV2D.Faces[cellFaces[0]].cellA];
								}
							}
						}
					}
					ComputeFromCellValues(mps, mappedMeshProfile, meshCellWaterSurfaces, values, _d2MapParams.Coverage, _d2MapParams.CanInterpolate, depths);
				}
				else if (_d2MapParams.Location == Location.Face)
				{
					vx = new float[values.Count() - 1 + 1];
					vy = new float[values.Count() - 1 + 1];
					if (_d2MapParams.Orientation == Orientation.Normal)
					{
						ComputeFromFaceNormalValues(mps, mappedMeshProfile, meshCellWaterSurfaces, _d2MapParams.Coverage, depths, vx, vy, values);
					}
					else if (_d2MapParams.Orientation == Orientation.Scalar)
					{
						ComputeFromFaceScalarValues(mps, mappedMeshProfile, meshCellWaterSurfaces, _d2MapParams.Coverage, values, depths);
					}
				}
				else if (_d2MapParams.Location == Location.SubCell)
				{
					List<string> stringList = null;
					string lengthAttributeDataset = null;
					if (_d2MapParams.HDFDataset.Contains("Hydraulic"))
					{
						stringList = new List<string>();
						List<int> range = ((mps == null) ? Enumerable.Range(0, _d2FlowAreaNames.Count()).ToList() : mps.AllMappedMeshIndexes().ToList());
						foreach (int range2 in range)
						{
							string featureName = _result.Geometry.D2FlowArea.GetFeatureName(range2);
							string featureNameTerm = "Geometry/2D Flow Areas/" + featureName;
							stringList.Add(featureNameTerm + "/Cells Volume Elevation Info");
						}
					}
					else
					{
						lengthAttributeDataset = "Plan Data/Sediment/Sediment Run Parameters";
					}
					float[][][] meshElevs = null;
					float[][][] meshInitialElevs = null;
					bool outputAsChange = false;
					int count3 = _auxNames.Profile2D.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						string profile2D = _auxNames.Profile2D[k];
						string[] parts = profile2D.Split('/');
						if (parts.Last().Contains("Bed Elevation"))
						{
							parts[parts.Length - 1] = parts.Last().Replace("Bed Elevation", "Bed Change");
							profile2D = string.Join('/', parts);
							_auxNames.Profile2D[k] = profile2D;
							outputAsChange = true;
						}
					}
					ReadMappedMeshProfileSubValues(_auxNames.Profile2D, isSubcell: true, profile, _rowCache, ref meshInitialElevs, ref meshElevs, mps, stringList, lengthAttributeDataset);
					ComputeFromSubCellValues(mps, terrain, meshInitialElevs, meshElevs, values, outputAsChange);
				}
				else
				{
					if (_d2MapParams.Location != Location.SubFace)
					{
						return;
					}
					List<string> stringListList = new List<string>();
					List<int> rangeList = ((mps == null) ? Enumerable.Range(0, _d2FlowAreaNames.Count()).ToList() : mps.AllMappedMeshIndexes().ToList());
					foreach (int range3 in rangeList)
					{
						string featureName2 = _result.Geometry.D2FlowArea.GetFeatureName(range3);
						string featureName2Term = "Geometry/2D Flow Areas/" + featureName2;
						stringListList.Add(featureName2Term + "/Faces Area Elevation Info");
					}
					float[][][] meshElevs2 = null;
					float[][][] meshInitialElevs2 = null;
					ReadMappedMeshProfileSubValues(_auxNames.Profile2D, isSubcell: false, profile, _rowCache, ref meshInitialElevs2, ref meshElevs2, mps, stringListList);
					ComputeFromSubFaceValues(mps, terrain, meshInitialElevs2, meshElevs2, values, _d2MapParams.HDFDataset.Contains("Change"));
				}
			}
		}
	}

	private void ComputeVariableInternal1D(string crossSectionDataset, int readRow, RASGeometryMapPoints mps, H5RowCache<float> cache, float[] depths, float[] values)
	{
		float[] ensureGetRow = cache.EnsureGetRow(crossSectionDataset, readRow);
		foreach (MappedXSIS mappedXSIS in mps.MappedXSISs)
		{
			int iD = mappedXSIS.ID;
			int upstreamXSID = _XSIS.GetUpstreamXSID(iD);
			int downstreamXSID = _XSIS.GetDownstreamXSID(iD);
			float ensureGetRow2 = ensureGetRow[upstreamXSID];
			float ensureGetRow3 = ensureGetRow[downstreamXSID];
			foreach (XSISMapPoint mapPoint in mappedXSIS.MapPoints)
			{
				if (depths[mapPoint.Index] > 0f)
				{
					values[mapPoint.Index] = ensureGetRow2 * mapPoint.Z + ensureGetRow3 * (1f - mapPoint.Z);
				}
			}
		}
	}

	private void ComputeVariableInternalSA(string storageAreaDataset, int readRow, RASGeometryMapPoints mps, H5RowCache<float> cache, float[] depths, float[] values)
	{
		float[] ensureGetRow = cache.EnsureGetRow(storageAreaDataset, readRow);
		foreach (SAMap mappedSA in mps.MappedSAs)
		{
			float ensureGetRow2 = ensureGetRow[mappedSA.ID];
			foreach (int mapPoint in mappedSA.MapPoints)
			{
				if (depths[mapPoint] > 0f)
				{
					values[mapPoint] = ensureGetRow2;
				}
			}
		}
	}

	public static void ComputeTimeSeries(RASResults res, Generic2DMapType g2m, RASGeometryMapPoints mps, float[] terrain, ref float[][] pointTimeSeries, CacheCollection cache = null)
	{
		ComputeTimeSeries_InternalInefficient(res, g2m, mps, terrain, ref pointTimeSeries, cache);
	}

	private static void ComputeTimeSeries_InternalInefficient(RASResults res, Generic2DMapType g2m, RASGeometryMapPoints mps, float[] terrain, ref float[][] pointTimeSeries, CacheCollection cache = null)
	{
		int profileCount = res.ProfileCount;
		checked
		{
			pointTimeSeries = new float[mps.InputCount - 1 + 1][];
			int count = mps.InputCount - 1;
			for (int i = 0; i <= count; i++)
			{
				float[] pointTimeSeriesArray = new float[profileCount - 1 + 1];
				pointTimeSeries[i] = pointTimeSeriesArray;
			}
			RASResultsMap map = new RASResultsMap(res, g2m);
			int count2 = profileCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				float[] values = null;
				string hDF5Dataset = g2m.HDF5Dataset;
				int profile = j;
				float[] vx = null;
				float[] vy = null;
				Compute(map, hDF5Dataset, profile, mps, terrain, ref values, ref vx, ref vy, cache);
				int count3 = values.Count() - 1;
				for (int k = 0; k <= count3; k++)
				{
					pointTimeSeries[k][j] = values[k];
				}
			}
		}
	}

	public static float[][][] ReadSubcellElevations(int meshDatasetCount, bool isSubcell, int[][] cellsIdxPerMesh, RASResults result, List<string> infoDSetPerMesh, string lengthAttributeDataset, float[][] mappedMeshProfVals, List<int> meshIndexes, ref float[][][] meshInitialElevs, bool outputChangeAsElevation = false)
	{
		if (infoDSetPerMesh == null && lengthAttributeDataset == null)
		{
			throw new ArgumentException("Must pass in an info dataset or a length attribute, but not both");
		}
		if (infoDSetPerMesh != null && lengthAttributeDataset != null)
		{
			throw new ArgumentException("Info dataset and length attribute should be mutually exclusive");
		}
		checked
		{
			float[][][] subcellElevations = new float[meshDatasetCount - 1 + 1][][];
			meshInitialElevs = new float[meshDatasetCount - 1 + 1][][];
			int result2 = -1;
			List<int[,]> intValuesList = new List<int[,]>();
			using (H5Reader h5Reader = new H5Reader(result.SourceFilename))
			{
				if (infoDSetPerMesh == null)
				{
					if (!h5Reader.ExistsAsAttribute(lengthAttributeDataset, "Max Subgrid Regions"))
					{
						throw new ArgumentException("Provided length dataset does not exist");
					}
					string textValue = "";
					h5Reader.ReadAttributeScalar(lengthAttributeDataset, "Max Subgrid Regions", ref textValue);
					if (!int.TryParse(textValue, out result2))
					{
						throw new ArgumentException("Pointed to length attribute is not an integer, or a string of an integer");
					}
					int count = meshDatasetCount - 1;
					for (int i = 0; i <= count; i++)
					{
						int faceCount = ((!isSubcell) ? result.Geometry.D2FlowArea.Mesh(i).FaceCount : result.Geometry.D2FlowArea.Mesh(i).NonVirtualCellCount);
						meshInitialElevs[i] = new float[faceCount - 1 + 1][];
						string dataset = ResultsDatasets.UnsteadySedimentBed.TimeSeriesOutput.FlowAreas.GetDataset(result.Geometry.D2FlowArea.GetFeatureName(i), "Subcell Initial Bed Elevation");
						int[] cellsIdxPerMeshArray = cellsIdxPerMesh[i];
						float[] values = null;
						h5Reader.ReadRow(dataset, 0, ref values);
						int count2 = cellsIdxPerMeshArray.Length - 1;
						for (int j = 0; j <= count2; j++)
						{
							int cellsIdxPerMesh2 = cellsIdxPerMeshArray[j];
							List<float> floatList = new List<float>();
							int cellsIdxPerMesh2TimesResult2 = cellsIdxPerMesh2 * result2;
							int cellsIdxPerMesh2Result2Term = cellsIdxPerMesh2 * result2 + result2 - 1;
							for (int k = cellsIdxPerMesh2TimesResult2; k <= cellsIdxPerMesh2Result2Term && values[k] != float.NegativeInfinity; k++)
							{
								floatList.Add(values[k]);
							}
							meshInitialElevs[i][cellsIdxPerMesh2] = floatList.ToArray();
						}
					}
				}
				else
				{
					int count3 = meshDatasetCount - 1;
					for (int l = 0; l <= count3; l++)
					{
						int faceCount2 = ((!isSubcell) ? result.Geometry.D2FlowArea.Mesh(l).FaceCount : result.Geometry.D2FlowArea.Mesh(l).NonVirtualCellCount);
						int[,] values2 = new int[1, 1];
						string infoDSetPerMesh2 = infoDSetPerMesh[l];
						if (!h5Reader.ExistsAsDataset(infoDSetPerMesh2))
						{
							throw new ArgumentException("Provided info dataset does not exist");
						}
						h5Reader.ReadDataset(infoDSetPerMesh2, ref values2);
						intValuesList.Add(values2);
						meshInitialElevs[l] = new float[faceCount2 - 1 + 1][];
						List<Point2DCollection> point2DCollections = null;
						if (infoDSetPerMesh2.Contains("Cells Volume Elevation"))
						{
							point2DCollections = result.Geometry.D2FlowArea.ReadCellVolumeTables(l, cellsIdxPerMesh[l]);
						}
						else
						{
							if (!infoDSetPerMesh2.Contains("Faces Area Elevation"))
							{
								throw new NotImplementedException();
							}
							point2DCollections = result.Geometry.D2FlowArea.ReadFaceAreaTables(l, cellsIdxPerMesh[l]);
						}
						int ordinal = 0;
						int[] cellsIdxPerMesh3 = cellsIdxPerMesh[l];
						foreach (int cellsIdxPerMesh4 in cellsIdxPerMesh3)
						{
							meshInitialElevs[l][cellsIdxPerMesh4] = point2DCollections[ordinal].Select((_Closure_0024__._0024I13_002D0 == null) ? (_Closure_0024__._0024I13_002D0 = [SpecialName] (Point2D volElev) => volElev.Y) : _Closure_0024__._0024I13_002D0).ToArray();
							ordinal++;
						}
					}
				}
			}
			foreach (int meshIndex in meshIndexes)
			{
				int faceCount3 = ((!isSubcell) ? result.Geometry.D2FlowArea.Mesh(meshIndex).FaceCount : result.Geometry.D2FlowArea.Mesh(meshIndex).NonVirtualCellCount);
				subcellElevations[meshIndex] = new float[faceCount3 - 1 + 1][];
				int ordinal2 = 0;
				int[] cellsIdxPerMesh5 = cellsIdxPerMesh[meshIndex];
				foreach (int cellsIdxPerMesh6 in cellsIdxPerMesh5)
				{
					if (result2 == -1)
					{
						int[,] intValuesList2 = intValuesList[meshIndex];
						int cellsIdxPerMesh6Term = intValuesList2[cellsIdxPerMesh6, 0];
						int cellsIdxPerMesh6Term2 = intValuesList2[cellsIdxPerMesh6, 1];
						if (cellsIdxPerMesh6Term2 > 0)
						{
							float[] subcellElevationsArray = new float[cellsIdxPerMesh6Term2 - 1 + 1];
							Buffer.BlockCopy(mappedMeshProfVals[meshIndex], cellsIdxPerMesh6Term * 4, subcellElevationsArray, 0, subcellElevationsArray.Length * 4);
							if (outputChangeAsElevation)
							{
								int count4 = subcellElevationsArray.Length - 1;
								for (int intervalIndex = 0; intervalIndex <= count4; intervalIndex++)
								{
									subcellElevationsArray[intervalIndex] += meshInitialElevs[meshIndex][cellsIdxPerMesh6][intervalIndex];
								}
							}
							subcellElevations[meshIndex][cellsIdxPerMesh6] = subcellElevationsArray;
						}
					}
					else
					{
						List<float> floatListList = new List<float>();
						int count5 = result2 - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count5; intervalIndex2++)
						{
							float mappedMeshProfVal = mappedMeshProfVals[meshIndex][cellsIdxPerMesh6 * result2 + intervalIndex2];
							if (mappedMeshProfVal == float.NegativeInfinity)
							{
								break;
							}
							if (outputChangeAsElevation)
							{
								mappedMeshProfVal += meshInitialElevs[meshIndex][cellsIdxPerMesh6][intervalIndex2];
							}
							floatListList.Add(mappedMeshProfVal);
						}
						subcellElevations[meshIndex][cellsIdxPerMesh6] = floatListList.ToArray();
					}
					ordinal2++;
				}
			}
			return subcellElevations;
		}
	}

	public static float[][] GetAreasForCellIdxUsingVolumeElev(RASResults result, int meshIdx, int[] cellIdxs)
	{
		checked
		{
			float[][] areasForCellIdxUsingVolumeElev = new float[cellIdxs.Length - 1 + 1][];
			using H5Reader h5r = new H5Reader(result.SourceFilename);
			List<Point2DCollection> cellVolumeTables = result.Geometry.D2FlowArea.ReadCellVolumeTables(meshIdx, cellIdxs);
			float[] cellSurfaceArea = result.Geometry.D2FlowArea.ReadCellSurfaceArea(h5r, result.Geometry.D2FlowArea.GetFeatureName(meshIdx), cellIdxs);
			int count = cellVolumeTables.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (cellVolumeTables[i] != null)
				{
					float[] areasForCellIdxUsingVolumeElevArray = new float[cellVolumeTables[i].Count - 1 + 1];
					int count2 = areasForCellIdxUsingVolumeElevArray.Length - 2;
					for (int j = 0; j <= count2; j++)
					{
						areasForCellIdxUsingVolumeElevArray[j] = (cellVolumeTables[i][j + 1].X - cellVolumeTables[i][j].X) / (cellVolumeTables[i][j + 1].Y - cellVolumeTables[i][j].Y);
					}
					areasForCellIdxUsingVolumeElevArray[areasForCellIdxUsingVolumeElevArray.Length - 1] = cellSurfaceArea[i];
					areasForCellIdxUsingVolumeElev[i] = areasForCellIdxUsingVolumeElevArray;
				}
			}
			return areasForCellIdxUsingVolumeElev;
		}
	}

	public static float[][] GetAreaForCellIdxFromPropertyTable(RASResults result, int meshIdx, int[] cellIdxs)
	{
		checked
		{
			float[][] areaForCellIdxFromPropertyTable = new float[cellIdxs.Length - 1 + 1][];
			string dataset = ResultsDatasets.UnsteadySedimentBed.TimeSeriesOutput.FlowAreas.GetDataset(result.Geometry.D2FlowArea.GetFeatureName(meshIdx), "Subcell Area");
			string objectName = "Plan Data/Sediment/Sediment Run Parameters";
			int result2 = 0;
			string textValue = "";
			using (H5Reader h5Reader = new H5Reader(result.SourceFilename))
			{
				h5Reader.ReadAttributeScalar(objectName, "Max Subgrid Regions", ref textValue);
				int.TryParse(textValue, out result2);
				if (result2 > 0)
				{
					float[] values = null;
					h5Reader.ReadRow(dataset, 0, ref values);
					List<float> floatList = new List<float>();
					int count = cellIdxs.Length - 1;
					for (int i = 0; i <= count; i++)
					{
						int cellIdx = cellIdxs[i];
						int cellIdxTimesResult2 = cellIdx * result2;
						int cellIdxResult2Term = cellIdx * result2 + result2 - 1;
						for (int j = cellIdxTimesResult2; j <= cellIdxResult2Term && values[j] != float.NegativeInfinity; j++)
						{
							floatList.Add(values[j]);
						}
						areaForCellIdxFromPropertyTable[i] = floatList.ToArray();
					}
				}
			}
			return areaForCellIdxFromPropertyTable;
		}
	}
}
