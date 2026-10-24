using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Ras.Engine.Structures;

public class DynamicStructRatingTable
{
	private readonly double _eps = 1E-06;

	public double WselResolution;

	public double MinUpWsel;

	public double MinDnWsel;

	public List<List<(double?, double?)>> StructFlow;

	public HydraulicStructure Structure;

	public DynamicStructRatingTable(HydraulicStructure hydStruct, double minUpWS, double minDnWS, double maxUpWS, double maxDnWS, double resolution = 0.1)
	{
		Structure = hydStruct;
		WselResolution = resolution;
		MinUpWsel = minUpWS;
		MinDnWsel = minDnWS;
		int count = (int)Math.Ceiling((maxUpWS - minUpWS) / resolution) + 1;
		int count2 = (int)Math.Ceiling((maxDnWS - minDnWS) / resolution) + 1;
		StructFlow = new List<List<(double?, double?)>>(count);
		for (int i = 0; i < count; i++)
		{
			List<(double?, double?)> valueTuples = new List<(double?, double?)>(count2);
			for (int j = 0; j < count2; j++)
			{
				double upWS = minUpWS + (double)i * resolution;
				double dnWS = minDnWS + (double)j * resolution;
				(double, double) tuple = ComputeFlowAndVel(upWS, dnWS);
				valueTuples.Add((tuple.Item1, tuple.Item2));
			}
			StructFlow.Add(valueTuples);
		}
	}

	public void ExportRatingTableToCsv(string filePath = "C:\\Temp\\ratingTable.csv")
	{
		using StreamWriter streamWriter = new StreamWriter(filePath);
		streamWriter.WriteLine("WSUS,WSDS,Flow");
		for (int i = 0; i < StructFlow.Count; i++)
		{
			double minUpWselWselResolutionTerm = MinUpWsel + (double)i * WselResolution;
			List<(double?, double?)> structFlow = StructFlow[i];
			for (int j = 0; j < structFlow.Count; j++)
			{
				double minDnWselWselResolutionTerm = MinDnWsel + (double)j * WselResolution;
				double? structFlowTerm = structFlow[j].Item1;
				if (structFlowTerm.HasValue)
				{
					streamWriter.WriteLine($"{minUpWselWselResolutionTerm.ToString("G17", CultureInfo.InvariantCulture)},{minDnWselWselResolutionTerm.ToString("G17", CultureInfo.InvariantCulture)},{structFlowTerm.Value.ToString("G17", CultureInfo.InvariantCulture)}");
				}
			}
		}
	}

	private int GetFloorIndex(double computed, double min)
	{
		return (int)Math.Floor((computed - min) / WselResolution + _eps);
	}

	private double GetTableWsel(int index, double min)
	{
		return min + (double)index * WselResolution;
	}

	private void CheckTableExpand(double upWS, double dsWS)
	{
		int i = GetFloorIndex(upWS, MinUpWsel);
		int floorIndex = GetFloorIndex(dsWS, MinDnWsel);
		if (i < 0 || i >= StructFlow.Count)
		{
			int count = StructFlow[0].Count;
			for (; i < 0; i++)
			{
				StructFlow.Insert(0, Enumerable.Repeat<(double?, double?)>((null, null), count).ToList());
				MinUpWsel -= WselResolution;
			}
			while (i >= StructFlow.Count)
			{
				StructFlow.Add(Enumerable.Repeat<(double?, double?)>((null, null), count).ToList());
			}
		}
		if (floorIndex >= 0 && floorIndex < StructFlow[0].Count)
		{
			return;
		}
		int floorIndex2 = floorIndex;
		for (int j = 0; j < StructFlow.Count; j++)
		{
			List<(double?, double?)> structFlow = StructFlow[j];
			for (floorIndex = floorIndex2; floorIndex < 0; floorIndex++)
			{
				structFlow.Insert(0, (null, null));
				if (j == 0)
				{
					MinDnWsel -= WselResolution;
				}
			}
			while (floorIndex >= structFlow.Count)
			{
				structFlow.Add((null, null));
			}
		}
	}

	public (double flow, double vel, double dFlow_dUp, double dFlow_dDown) GetFlowVelDerivs(double upWS, double dnWS)
	{
		CheckTableExpand(upWS, dnWS);
		int floorIndex = GetFloorIndex(upWS, MinUpWsel);
		int floorIndex2 = GetFloorIndex(dnWS, MinDnWsel);
		double tableWsel = GetTableWsel(floorIndex, MinUpWsel);
		double tableWsel2 = GetTableWsel(floorIndex2, MinDnWsel);
		double tableWselPlusWselResolution = tableWsel + WselResolution;
		double tableWsel2PlusWselResolution = tableWsel2 + WselResolution;
		CheckTableExpand(tableWselPlusWselResolution, tableWsel2PlusWselResolution);
		(double?, double?) orCompute = GetOrCompute(floorIndex, floorIndex2);
		double orComputeTerm = orCompute.Item1.Value;
		double orComputeTerm2 = orCompute.Item2.Value;
		double computed = orComputeTerm;
		orCompute = GetOrCompute(floorIndex + 1, floorIndex2);
		double orComputeTerm3 = orCompute.Item1.Value;
		double orComputeTerm4 = orCompute.Item2.Value;
		double computed2 = orComputeTerm3;
		orCompute = GetOrCompute(floorIndex, floorIndex2 + 1);
		double orComputeTerm5 = orCompute.Item1.Value;
		double orComputeTerm6 = orCompute.Item2.Value;
		double computed3 = orComputeTerm5;
		orCompute = GetOrCompute(floorIndex + 1, floorIndex2 + 1);
		double orComputeTerm7 = orCompute.Item1.Value;
		double orComputeTerm8 = orCompute.Item2.Value;
		double computed4 = orComputeTerm7;
		double tableWselPlusWselResolutionAboveTableWsel = tableWselPlusWselResolution - tableWsel;
		double tableWsel2PlusWselResolutionAboveTableWsel2 = tableWsel2PlusWselResolution - tableWsel2;
		double computed5 = tableWselPlusWselResolutionAboveTableWsel * tableWsel2PlusWselResolutionAboveTableWsel2;
		double upWSDnWSTerm = (computed * (tableWselPlusWselResolution - upWS) * (tableWsel2PlusWselResolution - dnWS) + computed3 * (tableWselPlusWselResolution - upWS) * (dnWS - tableWsel2) + computed2 * (upWS - tableWsel) * (tableWsel2PlusWselResolution - dnWS) + computed4 * (upWS - tableWsel) * (dnWS - tableWsel2)) / computed5;
		double upWSDnWSTerm2 = (orComputeTerm2 * (tableWselPlusWselResolution - upWS) * (tableWsel2PlusWselResolution - dnWS) + orComputeTerm6 * (tableWselPlusWselResolution - upWS) * (dnWS - tableWsel2) + orComputeTerm4 * (upWS - tableWsel) * (tableWsel2PlusWselResolution - dnWS) + orComputeTerm8 * (upWS - tableWsel) * (dnWS - tableWsel2)) / computed5;
		double dnWSTableWsel2Term = ((computed2 - computed) * (tableWsel2PlusWselResolution - dnWS) + (computed4 - computed3) * (dnWS - tableWsel2)) / computed5;
		double upWSTableWselTerm = ((computed3 - computed) * (tableWselPlusWselResolution - upWS) + (computed4 - computed2) * (upWS - tableWsel)) / computed5;
		return (flow: upWSDnWSTerm, vel: upWSDnWSTerm2, dFlow_dUp: dnWSTableWsel2Term, dFlow_dDown: upWSTableWselTerm);
	}

	private (double? q, double? v) GetOrCompute(int i, int j)
	{
		List<(double?, double?)> structFlow = StructFlow[i];
		if (structFlow[j].Item1.HasValue)
		{
			return structFlow[j];
		}
		double tableWsel = GetTableWsel(i, MinUpWsel);
		double tableWsel2 = GetTableWsel(j, MinDnWsel);
		(double q, double v) tuple = ComputeFlowAndVel(tableWsel, tableWsel2);
		double tupleTerm = tuple.q;
		double tupleTerm2 = tuple.v;
		structFlow[j] = (tupleTerm, tupleTerm2);
		return (q: tupleTerm, v: tupleTerm2);
	}

	private (double q, double v) ComputeFlowAndVel(double upWS, double dnWS)
	{
		return Structure.ComputeFlowAndVel(upWS, dnWS);
	}
}
