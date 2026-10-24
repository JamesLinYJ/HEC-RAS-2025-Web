using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Utility.Progress;

namespace Ras.Engine;

public class FaceFractionsApportionment : IFaceApportionment
{
	public interface ITableSegment
	{
		double MinK { get; }

		double MaxK { get; }

		double MinEta { get; }

		double MaxEta { get; }

		bool Contains(double k);

		bool ContainsEta(double targetEta);

		bool TryInterpolate(double targetK, out double eta, out double[] fractions, double[] result = null);

		bool TryInterpolateByEta(double targetEta, out double estConveyance, out double[] fractions, double[] result = null);
	}

	public class LinearTableSegment : ITableSegment
	{
		public double[] Etas;

		public double[] TotalConveyances;

		public double[][] FaceFractions;

		public double MinK => TotalConveyances[0];

		public double MaxK => TotalConveyances[TotalConveyances.Length - 1];

		public double MinEta => Etas[0];

		public double MaxEta => Etas[Etas.Length - 1];

		public LinearTableSegment(double[] etas, double[] totalConveyances, double[][] faceFractionsTable)
		{
			Etas = etas;
			TotalConveyances = totalConveyances;
			FaceFractions = faceFractionsTable;
		}

		public bool Contains(double k)
		{
			if (k >= TotalConveyances[0])
			{
				return k <= TotalConveyances[TotalConveyances.Length - 1];
			}
			return false;
		}

		public bool ContainsEta(double targetEta)
		{
			if (targetEta >= Etas[0])
			{
				return targetEta <= Etas[Etas.Length - 1];
			}
			return false;
		}

		public bool TryInterpolate(double targetK, out double eta, out double[] fractions, double[] result = null)
		{
			eta = 0.0;
			fractions = null;
			if (targetK < TotalConveyances[0] || targetK > TotalConveyances[TotalConveyances.Length - 1])
			{
				return false;
			}
			int binarySearch = Array.BinarySearch(TotalConveyances, targetK);
			if (binarySearch >= 0)
			{
				eta = Etas[binarySearch];
				if (result == null)
				{
					result = new double[FaceFractions[binarySearch].Length];
				}
				Array.Copy(FaceFractions[binarySearch], result, FaceFractions[binarySearch].Length);
				fractions = result;
				return true;
			}
			int binarySearchTerm = ~binarySearch - 1;
			int binarySearchTerm2 = ~binarySearch;
			eta = InterpolateLinear(targetK, TotalConveyances[binarySearchTerm], TotalConveyances[binarySearchTerm2], Etas[binarySearchTerm], Etas[binarySearchTerm2]);
			fractions = InterpolateLinear(targetK, TotalConveyances[binarySearchTerm], TotalConveyances[binarySearchTerm2], FaceFractions[binarySearchTerm], FaceFractions[binarySearchTerm2], result);
			return true;
		}

		public bool TryInterpolateByEta(double targetEta, out double estConveyance, out double[] fractions, double[] result = null)
		{
			fractions = null;
			estConveyance = 0.0;
			if (targetEta < Etas[0] || targetEta > Etas[Etas.Length - 1])
			{
				return false;
			}
			int binarySearch = Array.BinarySearch(Etas, targetEta);
			if (binarySearch >= 0)
			{
				if (result == null)
				{
					result = new double[FaceFractions[binarySearch].Length];
				}
				Array.Copy(FaceFractions[binarySearch], result, FaceFractions[binarySearch].Length);
				fractions = result;
				estConveyance = TotalConveyances[binarySearch];
				return true;
			}
			int binarySearchTerm = ~binarySearch - 1;
			int binarySearchTerm2 = ~binarySearch;
			estConveyance = InterpolateLinear(targetEta, Etas[binarySearchTerm], Etas[binarySearchTerm2], TotalConveyances[binarySearchTerm], TotalConveyances[binarySearchTerm2]);
			fractions = InterpolateLinear(targetEta, Etas[binarySearchTerm], Etas[binarySearchTerm2], FaceFractions[binarySearchTerm], FaceFractions[binarySearchTerm2], result);
			return true;
		}
	}

	public class PowerLawTableSegment : ITableSegment
	{
		private readonly double _exponent;

		private readonly double _coefficient;

		private readonly double _h0;

		private readonly double _eta1;

		private readonly double _eta2;

		private readonly double[] _f1;

		private readonly double[] _f2;

		public double MinK { get; }

		public double MaxK => double.MaxValue;

		public double MinEta { get; }

		public double MaxEta => double.MaxValue;

		public PowerLawTableSegment(double minEta, double minK, double hMin, double[] etas, double[] ks, double[][] fractions)
		{
			PowerLawTableSegment powerLawTableSegment = this;
			MinEta = minEta;
			MinK = minK;
			if (etas.Length < 2)
			{
				throw new ArgumentException("At least two points are required for PowerLawTableSegment fit.");
			}
			_eta1 = etas[0];
			_eta2 = etas[etas.Length - 1];
			_f1 = fractions[0];
			_f2 = fractions[^1];
			double h0 = etas[0] - 0.0001;
			if (hMin < h0)
			{
				_h0 = OptimizeGoldenSection((double h) => powerLawTableSegment.FitModel(etas, ks, (double eta) => new double[2]
				{
					Math.Log(eta - h),
					1.0
				}, out var _), hMin, h0, 0.0001);
			}
			else
			{
				_h0 = h0;
			}
			FitModel(etas, ks, (double eta) => new double[2]
			{
				Math.Log(eta - powerLawTableSegment._h0),
				1.0
			}, out var parameters);
			_exponent = parameters[0];
			_coefficient = Math.Exp(parameters[1]);
		}

		private double FitModel(double[] independent, double[] dependent, Func<double, double[]> featureExtractor, out double[] parameters)
		{
			int independentLength = independent.Length;
			int featureExtractorLength = featureExtractor(independent[0]).Length;
			if (featureExtractorLength != 2)
			{
				throw new ArgumentException($"FitModel only supports 2-parameter (slope + intercept) models; featureExtractor returned {featureExtractorLength} features.", "featureExtractor");
			}
			double featureExtractorSum = 0.0;
			double computed = 0.0;
			double computed2 = 0.0;
			double featureExtractor2PolynomialSum = 0.0;
			int count = 0;
			List<double> doubleList = new List<double>(independentLength);
			List<double> doubleListList = new List<double>(independentLength);
			for (int i = 0; i < independentLength; i++)
			{
				double featureExtractor2 = featureExtractor(independent[i])[0];
				double dependentTerm = Math.Log(dependent[i]);
				if (double.IsFinite(featureExtractor2) && double.IsFinite(dependentTerm))
				{
					featureExtractorSum += featureExtractor2;
					computed += dependentTerm;
					computed2 += featureExtractor2 * dependentTerm;
					featureExtractor2PolynomialSum += featureExtractor2 * featureExtractor2;
					doubleList.Add(featureExtractor2);
					doubleListList.Add(dependentTerm);
					count++;
				}
			}
			if (count < 2)
			{
				parameters = new double[2] { 1.0, 0.0 };
				return double.MaxValue;
			}
			double countFeatureExtractorSumTerm = (double)count * featureExtractor2PolynomialSum - featureExtractorSum * featureExtractorSum;
			if (Math.Abs(countFeatureExtractorSumTerm) < 1E-12)
			{
				parameters = new double[2]
				{
					1.0,
					computed / (double)count
				};
				return double.MaxValue;
			}
			double countFeatureExtractorSumTerm2 = ((double)count * computed2 - featureExtractorSum * computed) / countFeatureExtractorSumTerm;
			double featureExtractorSumCountTerm = (computed - countFeatureExtractorSumTerm2 * featureExtractorSum) / (double)count;
			parameters = new double[2] { countFeatureExtractorSumTerm2, featureExtractorSumCountTerm };
			double computed3 = 0.0;
			for (int j = 0; j < count; j++)
			{
				double computed4 = featureExtractorSumCountTerm + countFeatureExtractorSumTerm2 * doubleList[j] - doubleListList[j];
				computed3 += computed4 * computed4;
			}
			return computed3;
		}

		public bool Contains(double k)
		{
			return k >= MinK;
		}

		public bool ContainsEta(double targetEta)
		{
			return targetEta >= MinEta;
		}

		public bool TryInterpolate(double targetK, out double eta, out double[] fractions, double[] result = null)
		{
			if (targetK < MinK)
			{
				eta = MinEta;
				if (result == null)
				{
					result = new double[_f1.Length];
				}
				Array.Copy(_f1, result, _f1.Length);
				fractions = result;
				return false;
			}
			if (_coefficient > 1E-12 && Math.Abs(_exponent) > 1E-12)
			{
				eta = Math.Pow(targetK / _coefficient, 1.0 / _exponent) + _h0;
			}
			else
			{
				eta = _eta2;
			}
			fractions = ExtrapolateFractions(eta, result);
			return true;
		}

		public bool TryInterpolateByEta(double targetEta, out double estConveyance, out double[] fractions, double[] result = null)
		{
			if (targetEta < MinEta)
			{
				estConveyance = MinK;
				if (result == null)
				{
					result = new double[_f1.Length];
				}
				Array.Copy(_f1, result, _f1.Length);
				fractions = result;
				return false;
			}
			double targetEtaAboveH0 = targetEta - _h0;
			estConveyance = ((targetEtaAboveH0 > 0.0) ? (_coefficient * Math.Pow(targetEtaAboveH0, _exponent)) : 0.0);
			fractions = ExtrapolateFractions(targetEta, result);
			return true;
		}

		private double[] ExtrapolateFractions(double targetEta, double[] resultBuffer = null)
		{
			double[] resultBufferArray = resultBuffer ?? new double[_f1.Length];
			double targetEtaAboveEta1OverEta2AboveEta1 = (targetEta - _eta1) / Math.Max(1E-12, _eta2 - _eta1);
			double resultBufferSum = 0.0;
			for (int i = 0; i < _f1.Length; i++)
			{
				resultBufferArray[i] = Math.Max(0.0, _f1[i] + targetEtaAboveEta1OverEta2AboveEta1 * (_f2[i] - _f1[i]));
				resultBufferSum += resultBufferArray[i];
			}
			if (resultBufferSum > 0.0)
			{
				for (int j = 0; j < resultBufferArray.Length; j++)
				{
					resultBufferArray[j] /= resultBufferSum;
				}
			}
			else if (resultBufferArray.Length != 0)
			{
				double resultBufferArray2 = 1.0 / (double)resultBufferArray.Length;
				for (int k = 0; k < resultBufferArray.Length; k++)
				{
					resultBufferArray[k] = resultBufferArray2;
				}
			}
			return resultBufferArray;
		}
	}

	protected BoundaryCondition _bc;

	private readonly MeshPropertyTables _propertyTables;

	protected Func<MeshWaterState, int, double> _faceConveyanceFunc;

	protected Func<MeshWaterState, double, double[]> _facesConveyanceFunc;

	protected readonly double _absTolEta;

	protected readonly double _relKTol;

	private readonly double _absFractionTol;

	protected double _minEta;

	protected double _maxEta;

	private const int MaxSubdivideDepth = 10;

	private const int MaxGoldenSectionIterations = 200;

	private const int TailFitPointCount = 5;

	private const int DegenerateRangeSteps = 64;

	protected List<ITableSegment> _tableBank = new List<ITableSegment>();

	private const double FlatSpanRelTol = 1E-12;

	public FaceFractionsApportionment(MeshPropertyTables propertyTables, double absTolEta, double relKTol)
		: this(propertyTables, absTolEta, relKTol, double.PositiveInfinity)
	{
	}

	internal FaceFractionsApportionment(MeshPropertyTables propertyTables, double absTolEta, double relKTol, double absFractionTol)
	{
		_propertyTables = propertyTables;
		_absTolEta = absTolEta;
		_relKTol = relKTol;
		_absFractionTol = absFractionTol;
	}

	protected virtual void DetermineBounds()
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		int[] faces = _bc.Faces;
		foreach (int faceIndex in faces)
		{
			int faceTableStart = _propertyTables.FaceTableStart[faceIndex];
			int faceTableCount = _propertyTables.FaceTableCount[faceIndex];
			double faceElevationTable = _propertyTables.FaceElevationTable[faceTableStart];
			double faceElevationTable2 = _propertyTables.FaceElevationTable[faceTableStart + faceTableCount - 1];
			maxValue = Math.Min(faceElevationTable, maxValue);
			minValue = Math.Max(faceElevationTable2, minValue);
		}
		_minEta = maxValue;
		_maxEta = minValue;
	}

	public double[] GetFaceFractions(Mesh mesh, MeshWaterState ws, double targetConveyance, double[] result = null)
	{
		double eta;
		return GetFaceFractions(mesh, ws, targetConveyance, out eta, result);
	}

	public double[] GetFaceFractionsByStage(Mesh mesh, MeshWaterState ws, double stage, double[] result = null)
	{
		if (_tableBank.Count == 0)
		{
			throw new InvalidOperationException("Face fractions table is not prepared.");
		}
		if (!(FindSegmentByEta(stage) ?? throw new InvalidOperationException($"Stage {stage} is below the face-apportionment table range, which starts at {_tableBank[0].MinEta}.")).TryInterpolateByEta(stage, out var _, out var fractions, result))
		{
			throw new InvalidOperationException($"Failed to interpolate face fractions at stage {stage}.");
		}
		ws.UpdateFacesProperties(_bc.Faces, stage);
		return fractions;
	}

	public double[] GetFaceFractions(Mesh mesh, MeshWaterState ws, double targetConveyance, out double eta, double[] result = null)
	{
		eta = 0.0;
		if (_tableBank.Count == 0)
		{
			throw new InvalidOperationException("Face fractions table is not prepared.");
		}
		double[] fractions = null;
		double eta2;
		if (targetConveyance <= _tableBank[0].MinK)
		{
			ITableSegment tableSegment = _tableBank[0];
			eta = tableSegment.MinEta;
			if (result == null)
			{
				result = new double[_bc.Faces.Length];
			}
			if (tableSegment is LinearTableSegment linearTableSegment)
			{
				Array.Copy(linearTableSegment.FaceFractions[0], result, linearTableSegment.FaceFractions[0].Length);
				fractions = result;
			}
			else
			{
				tableSegment.TryInterpolate(targetConveyance, out eta, out fractions, result);
			}
		}
		else
		{
			ITableSegment tableSegment = FindSegmentByConveyance(targetConveyance);
			if (tableSegment == null)
			{
				tableSegment = _tableBank[_tableBank.Count - 1];
			}
			if (!tableSegment.TryInterpolate(targetConveyance, out eta, out fractions, result))
			{
				eta = tableSegment.MinEta;
				tableSegment.TryInterpolate(tableSegment.MinK, out eta2, out fractions, result);
			}
		}
		double maxCellElevation = GetMaxCellElevation(mesh, ws, _bc.Faces);
		if (maxCellElevation > eta)
		{
			eta = maxCellElevation;
			ITableSegment tableSegment2 = FindSegmentByEta(eta);
			if (tableSegment2 == null)
			{
				tableSegment2 = ((eta < _tableBank[0].MinEta) ? _tableBank[0] : _tableBank[_tableBank.Count - 1]);
			}
			if (!tableSegment2.TryInterpolateByEta(eta, out eta2, out fractions, result))
			{
				if (eta <= tableSegment2.MinEta)
				{
					tableSegment2.TryInterpolateByEta(tableSegment2.MinEta, out eta2, out fractions, result);
				}
				else
				{
					tableSegment2.TryInterpolateByEta(tableSegment2.MaxEta, out eta2, out fractions, result);
				}
			}
		}
		ws.UpdateFacesProperties(_bc.Faces, eta);
		return fractions;
	}

	private ITableSegment FindSegmentByConveyance(double targetConveyance)
	{
		ITableSegment result = null;
		int count = _tableBank.Count;
		for (int i = 0; i < count; i++)
		{
			ITableSegment tableSegment = _tableBank[i];
			if (tableSegment.Contains(targetConveyance))
			{
				result = tableSegment;
				break;
			}
		}
		return result;
	}

	private ITableSegment FindSegmentByEta(double eta)
	{
		ITableSegment result = null;
		int count = _tableBank.Count;
		for (int i = 0; i < count; i++)
		{
			ITableSegment tableSegment = _tableBank[i];
			if (tableSegment.ContainsEta(eta))
			{
				result = tableSegment;
				break;
			}
		}
		return result;
	}

	private double GetMaxCellElevation(Mesh mesh, MeshWaterState ws, int[] faceIndexes)
	{
		double minValue = double.MinValue;
		foreach (int faceIndex in faceIndexes)
		{
			int cellA = mesh.Faces[faceIndex].cellA;
			if (cellA != -1 && !(ws.CellVolume[cellA] <= 0.0))
			{
				double cellElevation = ws.CellElevation[cellA];
				minValue = Math.Max(minValue, cellElevation);
			}
		}
		return minValue;
	}

	private double GetMaxCellElevation(Mesh mesh, int[] faceIndexes)
	{
		double minValue = double.MinValue;
		foreach (int faceIndex in faceIndexes)
		{
			int cellA = mesh.Faces[faceIndex].cellA;
			if (cellA != -1)
			{
				float cellElevationTable = _propertyTables.CellElevationTable[cellA];
				minValue = Math.Max(minValue, cellElevationTable);
			}
		}
		return minValue;
	}

	public static double OptimizeGoldenSection(Func<double, double> func, double min, double max, double tolerance)
	{
		double d = (Math.Sqrt(5.0) - 1.0) / 2.0;
		double maxAboveMin = max - min;
		if (!(maxAboveMin > 0.0))
		{
			return min;
		}
		int ordinal = (int)Math.Ceiling(Math.Log(tolerance / maxAboveMin) / Math.Log(d));
		ordinal = Math.Clamp(ordinal, 1, 200);
		return OptimizeGoldenSection(func, min, max, ordinal);
	}

	public static double OptimizeGoldenSection(Func<double, double> func, double min, double max, int iterations = 30)
	{
		double computed = (Math.Sqrt(5.0) - 1.0) / 2.0;
		double min2 = max - computed * (max - min);
		double max2 = min + computed * (max - min);
		double func2 = func(min2);
		double func3 = func(max2);
		for (int i = 0; i < iterations; i++)
		{
			if (func2 < func3)
			{
				max = max2;
				max2 = min2;
				func3 = func2;
				min2 = max - computed * (max - min);
				func2 = func(min2);
			}
			else
			{
				min = min2;
				min2 = max2;
				func2 = func3;
				max2 = min + computed * (max - min);
				func3 = func(max2);
			}
		}
		return (min + max) / 2.0;
	}

	private static bool IsFlatSpan(double k1, double k2)
	{
		return Math.Abs(k2 - k1) <= 1E-12 * Math.Max(Math.Abs(k1), Math.Abs(k2));
	}

	public static double InterpolateLinear(double targetK, double k1, double k2, double eta1, double eta2)
	{
		if (IsFlatSpan(k1, k2))
		{
			if (!(targetK <= k1))
			{
				return eta2;
			}
			return eta1;
		}
		double submergenceRatio = (targetK - k1) / (k2 - k1);
		return eta1 + submergenceRatio * (eta2 - eta1);
	}

	public static double[] InterpolateLinear(double targetK, double k1, double k2, double[] eta1s, double[] eta2s, double[] result = null)
	{
		if (IsFlatSpan(k1, k2))
		{
			double[] eta1sArray = ((targetK <= k1) ? eta1s : eta2s);
			if (result == null)
			{
				result = new double[eta1sArray.Length];
			}
			Array.Copy(eta1sArray, result, eta1sArray.Length);
			return result;
		}
		double submergenceRatio = (targetK - k1) / (k2 - k1);
		if (result == null)
		{
			result = new double[eta1s.Length];
		}
		for (int i = 0; i < eta1s.Length; i++)
		{
			result[i] = eta1s[i] + submergenceRatio * (eta2s[i] - eta1s[i]);
		}
		return result;
	}

	public static double InterpolateLogLog(double targetK, double k1, double k2, double eta1, double eta2)
	{
		if (IsFlatSpan(k1, k2))
		{
			if (!(targetK <= k1))
			{
				return eta2;
			}
			return eta1;
		}
		if (targetK < 1E-10 || k1 < 1E-10 || k2 < 1E-10 || eta1 < 1E-10 || eta2 < 1E-10)
		{
			return InterpolateLinear(targetK, k1, k2, eta1, eta2);
		}
		double targetKTerm = Math.Log(targetK);
		double k1Term = Math.Log(k1);
		double k2Term = Math.Log(k2);
		double eta1Term = Math.Log(eta1);
		double eta2Term = Math.Log(eta2);
		double submergenceRatio = (targetKTerm - k1Term) / (k2Term - k1Term);
		double computed = Math.Exp(eta1Term + submergenceRatio * (eta2Term - eta1Term));
		double min = Math.Min(eta1, eta2);
		double max = Math.Max(eta1, eta2);
		return Math.Clamp(computed, min, max);
	}

	public void Prepare(ProgressReporter pr, BoundaryCondition bc, Mesh mesh, MeshWaterState ws, Func<MeshWaterState, int, double> faceConveyanceFunc)
	{
		_bc = bc ?? throw new ArgumentNullException("bc");
		if (faceConveyanceFunc != null)
		{
			SetFaceConveyanceFunc(faceConveyanceFunc);
		}
		else if (_facesConveyanceFunc == null)
		{
			throw new ArgumentNullException("faceConveyanceFunc", "No face conveyance function was provided, and none was set directly via SetFacesConveyanceFunc.");
		}
		pr?.ReportMessage($"Building face apportionment table for {_bc.Faces.Length} boundary faces");
		if (pr != null && pr.CancelRequested)
		{
			return;
		}
		double[] savedElevations = SaveFaceElevations(ws);
		try
		{
			DetermineBounds();
			LinearTableSegment linearTableSegment = BuildRecursive(ws, _minEta, _maxEta);
			_tableBank.Clear();
			_tableBank.Add(linearTableSegment);
			double maxEtaAboveMinEta = linearTableSegment.MaxEta - linearTableSegment.MinEta;
			if (!(maxEtaAboveMinEta > 0.0))
			{
				maxEtaAboveMinEta = 64.0 * _absTolEta;
			}
			double[] doubleValues = new double[5];
			double[] doubleValuesArray = new double[5];
			double[][] doubleValues2 = new double[5][];
			for (int i = 0; i < 5; i++)
			{
				double computed = 1.05 + (double)i / 4.0 * 0.95;
				doubleValues[i] = linearTableSegment.MinEta + computed * maxEtaAboveMinEta;
				double[] facesConveyanceFunc = _facesConveyanceFunc(ws, doubleValues[i]);
				doubleValuesArray[i] = facesConveyanceFunc.Sum();
				doubleValues2[i] = GetFaceFractions(doubleValuesArray[i], facesConveyanceFunc);
			}
			PowerLawTableSegment powerLawTableSegment = new PowerLawTableSegment(linearTableSegment.MaxEta, linearTableSegment.MaxK, _minEta, doubleValues, doubleValuesArray, doubleValues2);
			_tableBank.Add(powerLawTableSegment);
		}
		finally
		{
			RestoreFaceElevations(ws, savedElevations);
		}
	}

	private double[] SaveFaceElevations(MeshWaterState ws)
	{
		double[] doubleValues = new double[_bc.Faces.Length];
		for (int i = 0; i < _bc.Faces.Length; i++)
		{
			doubleValues[i] = ws.FaceElevation[_bc.Faces[i]];
		}
		return doubleValues;
	}

	private void RestoreFaceElevations(MeshWaterState ws, double[] savedElevations)
	{
		for (int i = 0; i < _bc.Faces.Length; i++)
		{
			ws.UpdateFacesProperties(new int[1] { _bc.Faces[i] }, savedElevations[i]);
		}
	}

	protected virtual double[] GetFaceConveyances(MeshWaterState ws, double eta, Func<MeshWaterState, int, double> func)
	{
		double[] faceConveyances = new double[_bc.Faces.Length];
		ws.UpdateFacesProperties(_bc.Faces, eta);
		for (int i = 0; i < _bc.Faces.Length; i++)
		{
			int faceIndex = _bc.Faces[i];
			double func2 = 0.0;
			if (ws.FaceArea[faceIndex] > 0.0)
			{
				func2 = func(ws, faceIndex);
				if (!double.IsFinite(func2))
				{
					func2 = 0.0;
				}
			}
			faceConveyances[i] = func2;
		}
		return faceConveyances;
	}

	private void SetFaceConveyanceFunc(Func<MeshWaterState, int, double> faceConveyanceFunc)
	{
		if (_faceConveyanceFunc != faceConveyanceFunc)
		{
			_faceConveyanceFunc = faceConveyanceFunc;
			Func<MeshWaterState, double, double[]> facesConveyanceFunc = (MeshWaterState waterState, double eta) => GetFaceConveyances(waterState, eta, _faceConveyanceFunc);
			SetFacesConveyanceFunc(facesConveyanceFunc);
		}
	}

	public void SetFacesConveyanceFunc(Func<MeshWaterState, double, double[]> facesConveyanceFunc)
	{
		_facesConveyanceFunc = facesConveyanceFunc;
	}

	protected LinearTableSegment BuildRecursive(MeshWaterState ws, double minEta, double minTotalConveyance, double[] minFaceFractions, double maxEta)
	{
		List<double> doubleList = new List<double>();
		List<double> doubleListList = new List<double>();
		List<double[]> doubleValuesList = new List<double[]>();
		double absTolEta = _absTolEta;
		double[] facesConveyanceFunc = _facesConveyanceFunc(ws, maxEta);
		double sum = facesConveyanceFunc.Sum();
		double[] faceFractions = GetFaceFractions(sum, facesConveyanceFunc);
		double sumAboveMinTotalConveyance = sum - minTotalConveyance;
		double absKTol = Math.Max(1E-06, sumAboveMinTotalConveyance * 0.0005);
		doubleList.Add(minEta);
		doubleListList.Add(minTotalConveyance);
		doubleValuesList.Add(minFaceFractions);
		Subdivide(ws, minEta, maxEta, minTotalConveyance, sum, minFaceFractions, faceFractions, doubleList, doubleListList, doubleValuesList, absKTol, _relKTol, absTolEta, 0);
		doubleList.Add(maxEta);
		doubleListList.Add(sum);
		doubleValuesList.Add(faceFractions);
		double[] etas = doubleList.ToArray();
		double[] totalConveyances = doubleListList.ToArray();
		double[][] faceFractionsTable = doubleValuesList.ToArray();
		return new LinearTableSegment(etas, totalConveyances, faceFractionsTable);
	}

	protected LinearTableSegment BuildRecursive(MeshWaterState ws, double minEta, double maxEta)
	{
		double[] facesConveyanceFunc = _facesConveyanceFunc(ws, minEta);
		double sum = facesConveyanceFunc.Sum();
		double[] faceFractions = GetFaceFractions(sum, facesConveyanceFunc);
		return BuildRecursive(ws, minEta, sum, faceFractions, maxEta);
	}

	private void Subdivide(MeshWaterState ws, double eta1, double eta2, double k1, double k2, double[] fractions1, double[] fractions2, List<double> etas, List<double> conveyances, List<double[]> fractions, double absKTol, double relKTol, double minStep, int depth, int maxDepth = 10)
	{
		if (depth > maxDepth)
		{
			return;
		}
		double eta1Eta2Term = (eta1 + eta2) * 0.5;
		if (!(eta1Eta2Term - eta1 < minStep))
		{
			double[] facesConveyanceFunc = _facesConveyanceFunc(ws, eta1Eta2Term);
			double sum = facesConveyanceFunc.Sum();
			double[] faceFractions = GetFaceFractions(sum, facesConveyanceFunc);
			double interpolateLinear = InterpolateLinear(eta1Eta2Term, eta1, eta2, k1, k2);
			double absoluteSumAboveInterpolateLinear = Math.Abs(sum - interpolateLinear);
			double absKTolRelKTolTerm = Math.Max(absKTol, relKTol * sum);
			double faceFractionsInterpolateLinear2Term = 0.0;
			for (int i = 0; i < faceFractions.Length; i++)
			{
				double fractions1Fractions2Term = 0.5 * (fractions1[i] + fractions2[i]);
				double interpolateLinear2 = ((k1 == k2) ? fractions1Fractions2Term : InterpolateLinear(sum, k1, k2, fractions1[i], fractions2[i]));
				faceFractionsInterpolateLinear2Term = Math.Max(faceFractionsInterpolateLinear2Term, Math.Max(Math.Abs(faceFractions[i] - fractions1Fractions2Term), Math.Abs(faceFractions[i] - interpolateLinear2)));
			}
			if (absoluteSumAboveInterpolateLinear > absKTolRelKTolTerm || faceFractionsInterpolateLinear2Term > _absFractionTol)
			{
				Subdivide(ws, eta1, eta1Eta2Term, k1, sum, fractions1, faceFractions, etas, conveyances, fractions, absKTol, relKTol, minStep, depth + 1, maxDepth);
				etas.Add(eta1Eta2Term);
				conveyances.Add(sum);
				fractions.Add(faceFractions);
				Subdivide(ws, eta1Eta2Term, eta2, sum, k2, faceFractions, fractions2, etas, conveyances, fractions, absKTol, relKTol, minStep, depth + 1, maxDepth);
			}
			else
			{
				etas.Add(eta1Eta2Term);
				conveyances.Add(sum);
				fractions.Add(faceFractions);
			}
		}
	}

	private double[] GetFaceFractions(double totalConveyance, double[] faceConveyances, double[] result = null)
	{
		if (result == null)
		{
			result = new double[_bc.Faces.Length];
		}
		if (totalConveyance > 0.0)
		{
			for (int i = 0; i < _bc.Faces.Length; i++)
			{
				result[i] = faceConveyances[i] / totalConveyance;
			}
		}
		else
		{
			Array.Clear(result, 0, result.Length);
		}
		return result;
	}
}
