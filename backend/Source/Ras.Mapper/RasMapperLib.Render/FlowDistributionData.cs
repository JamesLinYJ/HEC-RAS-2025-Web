using System;
using System.Linq;
using H5Assist.VB;

namespace RasMapperLib.Render;

public class FlowDistributionData
{
	public struct WSInterpData
	{
		public int IndexAbove;

		public float FractionAbove;

		public int IndexBelow;

		public float FractionBelow;
	}

	private float[][] _elevations;

	private float[][] _stations;

	private float[][,] _divideAreaVelocity;

	private float[][,] _divideAreaShear;

	private ProfileHydraulicPropertiesTable.HydraulicProperties[][,] _hydraulicProperties;

	public bool FlowDatasetsLoaded;

	public const string sHDFGroupName = "Geometry/Cross Sections/Flow Distribution";

	public const string sInfoDataset = "Geometry/Cross Sections/Flow Distribution/Info";

	public const string sStationsDataset = "Geometry/Cross Sections/Flow Distribution/Stations";

	public const string sWaterSurfaceDataset = "Geometry/Cross Sections/Flow Distribution/Water Surface";

	public const string sVelocityFactorDataset = "Geometry/Cross Sections/Flow Distribution/Flow Normalized Velocity Factor";

	public const string sShearFactorDataset = "Geometry/Cross Sections/Flow Distribution/Flow Normalized Shear Factor";

	public float[][] Elevations => _elevations;

	public float[][] Stations => _stations;

	public FlowDistributionData(RASResults results)
	{
		FlowDatasetsLoaded = false;
		RASGeometry geometry = results.Geometry;
		checked
		{
			using H5Reader h5Reader = new H5Reader(results.SourceFilename);
			if (!h5Reader.ExistsAsDataset("Geometry/Cross Sections/Flow Distribution/Info") || !h5Reader.ExistsAsDataset("Geometry/Cross Sections/Flow Distribution/Stations") || !h5Reader.ExistsAsDataset("Geometry/Cross Sections/Flow Distribution/Flow Normalized Shear Factor") || !h5Reader.ExistsAsDataset("Geometry/Cross Sections/Flow Distribution/Water Surface") || !h5Reader.ExistsAsDataset("Geometry/Cross Sections/Flow Distribution/Flow Normalized Velocity Factor"))
			{
				throw new Exception("Some results datasets are missing. The current results file may be from a previous version of RAS. You will need to rerun the model to visualize some output for this dataset.");
			}
			HDFBufferedReader<int> hDFBufferedReader = new HDFBufferedReader<int>(h5Reader, "Geometry/Cross Sections/Flow Distribution/Info");
			HDFBufferedReader<float> hDFBufferedReader2 = new HDFBufferedReader<float>(h5Reader, "Geometry/Cross Sections/Flow Distribution/Stations");
			HDFBufferedReader<float> hDFBufferedReader3 = new HDFBufferedReader<float>(h5Reader, "Geometry/Cross Sections/Flow Distribution/Water Surface");
			HDFBufferedReader<float> hDFBufferedReader4 = new HDFBufferedReader<float>(h5Reader, "Geometry/Cross Sections/Flow Distribution/Flow Normalized Velocity Factor");
			HDFBufferedReader<float> hDFBufferedReader5 = new HDFBufferedReader<float>(h5Reader, "Geometry/Cross Sections/Flow Distribution/Flow Normalized Shear Factor");
			_elevations = new float[hDFBufferedReader.Rows - 1 + 1][];
			_stations = new float[hDFBufferedReader.Rows - 1 + 1][];
			_divideAreaVelocity = new float[hDFBufferedReader.Rows - 1 + 1][,];
			_divideAreaShear = new float[hDFBufferedReader.Rows - 1 + 1][,];
			_hydraulicProperties = new ProfileHydraulicPropertiesTable.HydraulicProperties[hDFBufferedReader.Rows - 1 + 1][,];
			int count = hDFBufferedReader.Rows - 1;
			int col = default;
			int col2 = default;
			int col3 = default;
			for (int i = 0; i <= count; i++)
			{
				hDFBufferedReader.ReadRow(ref col, ref col2, ref col3);
				_elevations[i] = new float[col2 - 1 + 1];
				int count2 = col2 - 1;
				for (int j = 0; j <= count2; j++)
				{
					hDFBufferedReader3.ReadRow(ref _elevations[i][j]);
				}
				float[] stations = new float[col3 + 1];
				hDFBufferedReader2.ReadRow(stations);
				_stations[i] = stations;
				float stationingOffset = geometry.XS.GetStationingOffset(i);
				if (stationingOffset != 0f && !float.IsNaN(stationingOffset))
				{
					int count3 = stations.Length - 1;
					for (int k = 0; k <= count3; k++)
					{
						stations[k] += stationingOffset;
					}
				}
				float[,] divideAreaVelocity = new float[col2 - 1 + 1, col3 - 1 + 1];
				float[] floatValues = new float[col3 - 1 + 1];
				int count4 = col2 - 1;
				for (int l = 0; l <= count4; l++)
				{
					hDFBufferedReader4.ReadRow(floatValues);
					Buffer.BlockCopy(floatValues, 0, divideAreaVelocity, floatValues.Count() * l * 4, floatValues.Count() * 4);
				}
				_divideAreaVelocity[i] = divideAreaVelocity;
				float[,] divideAreaShear = new float[col2 - 1 + 1, col3 - 1 + 1];
				int count5 = col2 - 1;
				for (int m = 0; m <= count5; m++)
				{
					hDFBufferedReader5.ReadRow(floatValues);
					Buffer.BlockCopy(floatValues, 0, divideAreaShear, floatValues.Count() * m * 4, floatValues.Count() * 4);
				}
				_divideAreaShear[i] = divideAreaShear;
				geometry.XS.GetProfile(i);
			}
			hDFBufferedReader.Dispose();
			hDFBufferedReader2.Dispose();
			hDFBufferedReader3.Dispose();
			hDFBufferedReader4.Dispose();
			hDFBufferedReader5.Dispose();
		}
	}

	public WSInterpData ComputeWSInterpData(int xsID, float ws)
	{
		WSInterpData result = default;
		float[] elevation = _elevations[xsID];
		checked
		{
			if (ws <= elevation[0])
			{
				result.IndexAbove = 0;
				result.FractionAbove = 0f;
				result.IndexBelow = 0;
				result.FractionBelow = 1f;
			}
			else if (ws >= elevation.Last())
			{
				result.IndexAbove = elevation.Count() - 1;
				result.FractionAbove = 1f;
				result.IndexBelow = elevation.Count() - 1;
				result.FractionBelow = 0f;
			}
			else
			{
				int indexAbove = Array.BinarySearch(elevation, ws);
				if (indexAbove > 0)
				{
					result.IndexAbove = indexAbove;
					result.FractionAbove = 1f;
					result.IndexBelow = indexAbove - 1;
					result.FractionBelow = 0f;
				}
				else
				{
					indexAbove = ~indexAbove;
					float fractionAbove = (ws - elevation[indexAbove - 1]) / (elevation[indexAbove] - elevation[indexAbove - 1]);
					result.IndexAbove = indexAbove;
					result.FractionAbove = fractionAbove;
					result.IndexBelow = indexAbove - 1;
					result.FractionBelow = 1f - fractionAbove;
				}
			}
			return result;
		}
	}

	public float Velocity(int xsID, WSInterpData wsinterpdata, float station, float flow)
	{
		return VelocityAndShearDividedArea1D(_divideAreaVelocity[xsID], xsID, wsinterpdata, station) * flow;
	}

	public float Shear(int xsID, WSInterpData wsinterpdata, float station, float flowSquared)
	{
		return VelocityAndShearDividedArea1D(_divideAreaShear[xsID], xsID, wsinterpdata, station) * flowSquared;
	}

	public float DepthTimesVelocity(int xsID, WSInterpData wsinterpdata, float station, float flow)
	{
		float d = default;
		float velDivFlow = default;
		VelocityAndHydrDepth(_divideAreaVelocity[xsID], _hydraulicProperties[xsID], xsID, wsinterpdata, station, ref velDivFlow, ref d);
		velDivFlow *= flow;
		return d * velDivFlow;
	}

	public float DepthTimesVelocitySquared(int xsID, WSInterpData wsinterpdata, float station, float flow)
	{
		float d = default;
		float velDivFlow = default;
		VelocityAndHydrDepth(_divideAreaVelocity[xsID], _hydraulicProperties[xsID], xsID, wsinterpdata, station, ref velDivFlow, ref d);
		velDivFlow *= flow;
		return d * velDivFlow * velDivFlow;
	}

	public float[] ComputeShearAlongSubsections(int xsID, WSInterpData wsinterpdata, float flowSquared)
	{
		float[] source = _stations[xsID];
		checked
		{
			float[] shearAlongSubsections = new float[source.Count() - 2 + 1];
			int count = source.Count() - 2;
			for (int i = 0; i <= count; i++)
			{
				float divideAreaShearXsIDTerm = _divideAreaShear[xsID][wsinterpdata.IndexAbove, i] * wsinterpdata.FractionAbove + _divideAreaShear[xsID][wsinterpdata.IndexBelow, i] * wsinterpdata.FractionBelow;
				shearAlongSubsections[i] = divideAreaShearXsIDTerm * flowSquared;
			}
			return shearAlongSubsections;
		}
	}

	public float[] ComputeDVAlongSubsections(int xsID, WSInterpData wsinterpdata, float flow)
	{
		float[] source = _stations[xsID];
		ProfileHydraulicPropertiesTable.HydraulicProperties[,] hydraulicProperty = _hydraulicProperties[xsID];
		checked
		{
			float[] dvAlongSubsections = new float[source.Count() - 2 + 1];
			int count = source.Count() - 2;
			for (int i = 0; i <= count; i++)
			{
				float divideAreaVelocityXsIDTerm = _divideAreaVelocity[xsID][wsinterpdata.IndexAbove, i] * wsinterpdata.FractionAbove + _divideAreaVelocity[xsID][wsinterpdata.IndexBelow, i] * wsinterpdata.FractionBelow;
				float depthHydraulicPropertyTerm = hydraulicProperty[wsinterpdata.IndexAbove, i].HydraulicDepth() * wsinterpdata.FractionAbove + hydraulicProperty[wsinterpdata.IndexBelow, i].HydraulicDepth() * wsinterpdata.FractionBelow;
				dvAlongSubsections[i] = depthHydraulicPropertyTerm * divideAreaVelocityXsIDTerm * flow;
			}
			return dvAlongSubsections;
		}
	}

	public float[] ComputeDVSqAlongSubsections(int xsID, WSInterpData wsinterpdata, float flow)
	{
		float[] source = _stations[xsID];
		ProfileHydraulicPropertiesTable.HydraulicProperties[,] hydraulicProperty = _hydraulicProperties[xsID];
		checked
		{
			float[] dvSqAlongSubsections = new float[source.Count() - 2 + 1];
			int count = source.Count() - 2;
			for (int i = 0; i <= count; i++)
			{
				float divideAreaVelocityXsIDTerm = _divideAreaVelocity[xsID][wsinterpdata.IndexAbove, i] * wsinterpdata.FractionAbove + _divideAreaVelocity[xsID][wsinterpdata.IndexBelow, i] * wsinterpdata.FractionBelow;
				float depthHydraulicPropertyTerm = hydraulicProperty[wsinterpdata.IndexAbove, i].HydraulicDepth() * wsinterpdata.FractionAbove + hydraulicProperty[wsinterpdata.IndexBelow, i].HydraulicDepth() * wsinterpdata.FractionBelow;
				float flowTerm = divideAreaVelocityXsIDTerm * flow;
				dvSqAlongSubsections[i] = depthHydraulicPropertyTerm * flowTerm * flowTerm;
			}
			return dvSqAlongSubsections;
		}
	}

	private void GetHorizontalIndex(int xsID, float station, ref int horizIndex, ref int horizIndexR, ref float fractionR)
	{
		float[] stationArray = _stations[xsID];
		fractionR = 0f;
		int stationLength = stationArray.Length;
		if (station <= stationArray[0])
		{
			horizIndex = 0;
			return;
		}
		checked
		{
			if (station >= stationArray[stationLength - 1])
			{
				horizIndex = stationLength - 2;
				return;
			}
			horizIndex = Array.BinarySearch(stationArray, station);
			if (horizIndex < 0)
			{
				horizIndex = ~horizIndex - 1;
			}
			float stationArrayHorizIndexTerm = (float)((double)(stationArray[horizIndex] + stationArray[horizIndex + 1]) * 0.5);
			if (station < stationArrayHorizIndexTerm)
			{
				if (horizIndex > 0)
				{
					horizIndexR = horizIndex;
					horizIndex--;
					float stationArrayHorizIndexTerm2 = (float)((double)(stationArray[horizIndex] + stationArray[horizIndex + 1]) * 0.5);
					float computed = stationArrayHorizIndexTerm;
					fractionR = (station - stationArrayHorizIndexTerm2) / (computed - stationArrayHorizIndexTerm2);
				}
			}
			else if (horizIndex < stationLength - 2)
			{
				horizIndexR = horizIndex + 1;
				float computed2 = stationArrayHorizIndexTerm;
				float stationArrayHorizIndexRTerm = (float)((double)(stationArray[horizIndexR] + stationArray[horizIndexR + 1]) * 0.5);
				fractionR = (station - computed2) / (stationArrayHorizIndexRTerm - computed2);
			}
		}
	}

	public float AveragedMValue(int xsID, float station, float[] valuePerSubSection)
	{
		int horizIndexR = 0;
		int horizIndex = default;
		float fractionR = default;
		GetHorizontalIndex(xsID, station, ref horizIndex, ref horizIndexR, ref fractionR);
		if (horizIndexR > 0)
		{
			float valuePerSubSection2 = valuePerSubSection[horizIndex];
			float valuePerSubSection3 = valuePerSubSection[horizIndexR];
			return valuePerSubSection2 * (1f - fractionR) + valuePerSubSection3 * fractionR;
		}
		return valuePerSubSection[horizIndex];
	}

	private float VelocityAndShearDividedArea1D(float[,] divAreas, int xsID, WSInterpData wsinterpdata, float station)
	{
		int horizIndexR = 0;
		int horizIndex = default;
		float fractionR = default;
		GetHorizontalIndex(xsID, station, ref horizIndex, ref horizIndexR, ref fractionR);
		float fractionRTerm = divAreas[wsinterpdata.IndexAbove, horizIndex] * wsinterpdata.FractionAbove + divAreas[wsinterpdata.IndexBelow, horizIndex] * wsinterpdata.FractionBelow;
		if (horizIndexR > 0 && fractionR > 0f)
		{
			float divAreasIndexAboveTerm = divAreas[wsinterpdata.IndexAbove, horizIndexR] * wsinterpdata.FractionAbove + divAreas[wsinterpdata.IndexBelow, horizIndexR] * wsinterpdata.FractionBelow;
			fractionRTerm = fractionRTerm * (1f - fractionR) + divAreasIndexAboveTerm * fractionR;
		}
		return fractionRTerm;
	}

	private void VelocityAndHydrDepth(float[,] divAreas, ProfileHydraulicPropertiesTable.HydraulicProperties[,] hydrProperties, int xsID, WSInterpData wsinterpdata, float station, ref float velDivFlow, ref float d)
	{
		int horizIndexR = 0;
		int horizIndex = default;
		float fractionR = default;
		GetHorizontalIndex(xsID, station, ref horizIndex, ref horizIndexR, ref fractionR);
		velDivFlow = divAreas[wsinterpdata.IndexAbove, horizIndex] * wsinterpdata.FractionAbove + divAreas[wsinterpdata.IndexBelow, horizIndex] * wsinterpdata.FractionBelow;
		if (horizIndexR > 0)
		{
			float divAreasIndexAboveTerm = divAreas[wsinterpdata.IndexAbove, horizIndexR] * wsinterpdata.FractionAbove + divAreas[wsinterpdata.IndexBelow, horizIndexR] * wsinterpdata.FractionBelow;
			velDivFlow = velDivFlow * (1f - fractionR) + divAreasIndexAboveTerm * fractionR;
		}
		d = hydrProperties[wsinterpdata.IndexAbove, horizIndex].HydraulicDepth() * wsinterpdata.FractionAbove + hydrProperties[wsinterpdata.IndexBelow, horizIndex].HydraulicDepth() * wsinterpdata.FractionBelow;
		if (horizIndexR > 0)
		{
			float depthHydrPropertiesTerm = hydrProperties[wsinterpdata.IndexAbove, horizIndexR].HydraulicDepth() * wsinterpdata.FractionAbove + hydrProperties[wsinterpdata.IndexBelow, horizIndexR].HydraulicDepth() * wsinterpdata.FractionBelow;
			d = d * (1f - fractionR) + depthHydrPropertiesTerm * fractionR;
		}
	}

	private float HydrDepth(float[,] divAreas, ProfileHydraulicPropertiesTable.HydraulicProperties[,] hydrProperties, int xsID, WSInterpData wsinterpdata, float station)
	{
		int horizIndexR = 0;
		int horizIndex = default;
		float fractionR = default;
		GetHorizontalIndex(xsID, station, ref horizIndex, ref horizIndexR, ref fractionR);
		float fractionRTerm = hydrProperties[wsinterpdata.IndexAbove, horizIndex].HydraulicDepth() * wsinterpdata.FractionAbove + hydrProperties[wsinterpdata.IndexBelow, horizIndex].HydraulicDepth() * wsinterpdata.FractionBelow;
		if (horizIndexR > 0)
		{
			float depthHydrPropertiesTerm = hydrProperties[wsinterpdata.IndexAbove, horizIndexR].HydraulicDepth() * wsinterpdata.FractionAbove + hydrProperties[wsinterpdata.IndexBelow, horizIndexR].HydraulicDepth() * wsinterpdata.FractionBelow;
			fractionRTerm = fractionRTerm * (1f - fractionR) + depthHydrPropertiesTerm * fractionR;
		}
		return fractionRTerm;
	}
}
