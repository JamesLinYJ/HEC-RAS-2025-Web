using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Caching.VB;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.ArrivalTime;
using RasMapperLib.Mesh;
using RasMapperLib.Names;
using RasMapperLib.Render;
using Utility;
using Utility.Progress;

namespace RasMapperLib;

public class PostProcessor
{
	[Serializable]
	[CompilerGenerated]
	internal sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<Vector2D, float> _0024I42_002D0;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal int _Lambda_0024__32_002D0(float[] arr)
		{
			return arr.Count();
		}

		[SpecialName]
		internal int _Lambda_0024__39_002D0(float[] arr)
		{
			return arr.Count();
		}

		[SpecialName]
		internal float _Lambda_0024__42_002D0(Vector2D vec)
		{
			return vec.Magnitude;
		}

		[SpecialName]
		internal int _Lambda_0024__47_002D0(List<LocalExtrema> e)
		{
			return e.Count;
		}

		[SpecialName]
		internal int _Lambda_0024__48_002D0(List<LocalExtrema> e)
		{
			return e.Count;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__28_002D0
	{
		public MeshFV2D mesh;

		public _Closure_0024__28_002D0(_Closure_0024__28_002D0 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__28_002D1
	{
		public float[] cellWaterSurface;

		public _Closure_0024__28_002D2 outerClosure;

		public _Closure_0024__28_002D1(_Closure_0024__28_002D1 other)
		{
			if (other != null)
			{
				cellWaterSurface = other.cellWaterSurface;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			float area = default;
			float topWidth = default;
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				FaceValues faceValues = outerClosure.outerClosure.mesh.ComputeFaceWaterSurfaces(i, cellWaterSurface);
				if (faceValues.IsHydraulicallyConnected)
				{
					float valueA = faceValues.ValueA;
					Point2DCollection areaTable = outerClosure.faceAreaTables[i];
					Point2DCollection perimeterTable = outerClosure.facePerimTables[i];
					outerClosure.outerClosure.mesh.ComputeFaceAreaAndTopWidth(valueA, outerClosure.faceLength[i], areaTable, ref area, ref topWidth);
					float facePerimeter = outerClosure.outerClosure.mesh.ComputeFacePerimeter(valueA, perimeterTable);
					outerClosure.faceArea[0, i] = area;
					outerClosure.faceHydrDepth[0, i] = area / topWidth;
					outerClosure.faceHydrRadius[0, i] = area / facePerimeter;
				}
				else
				{
					outerClosure.faceArea[0, i] = 0f;
					outerClosure.faceHydrDepth[0, i] = 0f;
					outerClosure.faceHydrRadius[0, i] = 0f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__28_002D2
	{
		public List<Point2DCollection> faceAreaTables;

		public List<Point2DCollection> facePerimTables;

		public float[] faceLength;

		public float[,] faceArea;

		public float[,] faceHydrDepth;

		public float[,] faceHydrRadius;

		public _Closure_0024__28_002D0 outerClosure;

		public _Closure_0024__28_002D2(_Closure_0024__28_002D2 other)
		{
			if (other != null)
			{
				faceAreaTables = other.faceAreaTables;
				facePerimTables = other.facePerimTables;
				faceLength = other.faceLength;
				faceArea = other.faceArea;
				faceHydrDepth = other.faceHydrDepth;
				faceHydrRadius = other.faceHydrRadius;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__35_002D0
	{
		public FaceValues[] faceWSValues;

		public float[] cellWSEL;

		public FacePointVels[] fpVels;

		public float[] faceHydrDepth;

		public _Closure_0024__35_002D1 outerClosure;

		public _Closure_0024__35_002D0(_Closure_0024__35_002D0 other)
		{
			if (other != null)
			{
				faceWSValues = other.faceWSValues;
				cellWSEL = other.cellWSEL;
				fpVels = other.fpVels;
				faceHydrDepth = other.faceHydrDepth;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				faceWSValues[i] = outerClosure.mesh.ComputeFaceWaterSurfaces(i, cellWSEL);
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				FaceValues faceValues = faceWSValues[i];
				if (faceValues.IsHydraulicallyConnected)
				{
					Face face = outerClosure.mesh.Faces[i];
					outerClosure.mesh.Cell(face.cellA);
					FacePoint facePoint = outerClosure.mesh.FacePoints[face.fpA];
					FacePoint facePoint2 = outerClosure.mesh.FacePoints[face.fpB];
					Vector2D vector2D = fpVels[face.fpA].Velocity[facePoint.Faces.IndexOf(i)];
					Vector2D vector2D2 = fpVels[face.fpB].Velocity[facePoint2.Faces.IndexOf(i)];
					Vector2D vector2D3 = new Vector2D((vector2D.X + vector2D2.X) / 2f, (vector2D.Y + vector2D2.Y) / 2f);
					float faceHydrDepth = this.faceHydrDepth[i];
					float magnitude = vector2D3.Magnitude;
					float magnitudeTerm = ((!outerClosure.doDV) ? (magnitude * magnitude * faceHydrDepth) : (magnitude * faceHydrDepth));
					if (magnitudeTerm > outerClosure.maxSoFar[0, i])
					{
						outerClosure.maxSoFar[0, i] = magnitudeTerm;
					}
					if (magnitudeTerm < outerClosure.minSoFar[0, i])
					{
						outerClosure.minSoFar[0, i] = magnitudeTerm;
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__35_002D1
	{
		public MeshFV2D mesh;

		public bool doDV;

		public float[,] maxSoFar;

		public float[,] minSoFar;

		public _Closure_0024__35_002D1(_Closure_0024__35_002D1 other)
		{
			if (other != null)
			{
				mesh = other.mesh;
				doDV = other.doDV;
				maxSoFar = other.maxSoFar;
				minSoFar = other.minSoFar;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__42_002D0
	{
		public float[] fShears;

		public _Closure_0024__42_002D1 outerClosure;

		public _Closure_0024__42_002D0(_Closure_0024__42_002D0 other)
		{
			if (other != null)
			{
				fShears = other.fShears;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				float fShear = fShears[i];
				if (fShear > outerClosure.maxSoFar[0, i])
				{
					outerClosure.maxSoFar[0, i] = fShear;
				}
				if (fShear < outerClosure.minSoFar[0, i])
				{
					outerClosure.minSoFar[0, i] = fShear;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__42_002D1
	{
		public float[,] maxSoFar;

		public float[,] minSoFar;

		public _Closure_0024__42_002D1(_Closure_0024__42_002D1 other)
		{
			if (other != null)
			{
				maxSoFar = other.maxSoFar;
				minSoFar = other.minSoFar;
			}
		}
	}

	public const string sIncreasingValues = "Increasing Regions Values";

	public const string sIncreasingInfo = "Increasing Regions Info";

	public const string sIncreasingMinMaxWS = "Increasing Regions Min Max WS";

	private RASResults _result;

	private RASGeometry _geometry;

	private string _resName;

	private string _filename;

	private List<LocalExtrema>[] _XSRegions;

	private const string POSTPROCESS_VERSION = "1.0";

	public static bool UseNewTransposeMethod = true;

	public string Filename => _filename;

	public PostProcessor(RASResults res)
	{
		_result = res;
		_geometry = res.Geometry;
		_resName = res.SourceFilename;
		_filename = Path.GetFullPath(Path.Combine(_result.OutputFolder, "PostProcessing.hdf"));
	}

	public void EnsurePostProcessPopup()
	{
		EnsureIfPredicatePopup([SpecialName] () => NeedsBaseFileUpdate(), [SpecialName] (ProgressReporter reporter) =>
		{
			CreatePostProcessBase(reporter);
		});
	}

	public void EnsurePostProcess(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (NeedsBaseFileUpdate())
		{
			CreatePostProcessBase(reporter);
		}
	}

	public void EnsureExists(string dset, bool popup)
	{
		if (popup)
		{
			EnsurePostProcessPopup();
		}
		else
		{
			EnsurePostProcess();
		}
		if (!File.Exists(_filename))
		{
			return;
		}
		using (H5Reader h5Reader = new H5Reader(Filename))
		{
			if (h5Reader.ExistsAsDataset(dset))
			{
				return;
			}
		}
		PostProcessWithVerticalChunking(dset, storeLocalExtrema: false);
	}

	public void EnsureMaxMinDVPopup()
	{
		EnsureIfPredicatePopup([SpecialName] () => _result.UnsteadyHydraulics && NeedsMaxMinDVorDVSq(isDV: true), [SpecialName] (ProgressReporter reporter) =>
		{
			EnsureDV(reporter);
		});
	}

	public void EnsureDV(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (_result.UnsteadyHydraulics)
		{
			EnsureMaxMinDVorDVSq2D(doDV: true, reporter);
			EnsureMaxMinDVorDVSq1D(isDV: true, reporter);
		}
	}

	public void EnsureMaxMinDVSquaredPopup()
	{
		EnsureIfPredicatePopup([SpecialName] () => _result.UnsteadyHydraulics && NeedsMaxMinDVorDVSq(isDV: false), [SpecialName] (ProgressReporter reporter) =>
		{
			EnsureDVSquared(reporter);
		});
	}

	public void EnsureDVSquared(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (_result.UnsteadyHydraulics)
		{
			EnsureMaxMinDVorDVSq1D(isDV: false, reporter);
			EnsureMaxMinDVorDVSq2D(doDV: false, reporter);
		}
	}

	public void EnsureShearPopup()
	{
		EnsureIfPredicatePopup([SpecialName] () => _result.UnsteadyHydraulics && NeedsMaxMinShear(), [SpecialName] (ProgressReporter reporter) =>
		{
			EnsureShear(reporter);
		});
	}

	public void EnsureShear(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (_result.UnsteadyHydraulics)
		{
			EnsureMaxShear1D(reporter);
			EnsureMaxMinShear2D(reporter);
		}
	}

	public void EnsureHydraulicFacePropertiesPopup()
	{
		EnsureIfPredicatePopup([SpecialName] () => NeedsHydrFaceProperties().Count > 0, [SpecialName] (ProgressReporter reporter) =>
		{
			EnsureHydraulicFaceProperties(reporter);
		});
	}

	public void EnsureHydraulicFaceProperties(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		RASD2FlowArea d2FlowArea = _geometry.D2FlowArea;
		if (d2FlowArea == null || d2FlowArea.Meshes().Count() == 0)
		{
			return;
		}
		EnsurePostProcess(reporter);
		List<int> needsHydrFaceProperties = NeedsHydrFaceProperties();
		if (needsHydrFaceProperties.Count <= 0)
		{
			return;
		}
		foreach (int needsHydrFaceProperty in needsHydrFaceProperties)
		{
			reporter.ReportMessage("Processing Hydraulic Properties for " + d2FlowArea.GetFeatureName(needsHydrFaceProperty));
			ComputePostProcessHydraulicFaceProperties(d2FlowArea, needsHydrFaceProperty, reporter);
		}
	}

	private void EnsureIfPredicatePopup(Func<bool> predicate, Action<ProgressReporter> compute)
	{
		if (predicate())
		{
			if (SharedData.IsHeadless())
			{
				compute(ProgressReporter.None());
			}
			else
			{
				compute(ProgressReporter.None());
			}
		}
	}

	public bool NeedsBaseFileUpdate()
	{
		bool result;
		if (_result.SteadyHydraulics)
		{
			result = false;
		}
		else if (!Directory.Exists(Path.GetDirectoryName(_filename)))
		{
			result = true;
		}
		else if (!File.Exists(_filename))
		{
			result = true;
		}
		else if (DateTime.Compare(_result.GetBestAvailableComputeTimestamp(), File.GetLastWriteTime(_filename)) > 0)
		{
			result = true;
		}
		else
		{
			try
			{
				string textValue = "";
				using (H5Reader h5Reader = new H5Reader(_filename))
				{
					h5Reader.TryReadAttributeScalar("/", "VERSION", ref textValue);
				}
				if (Operators.CompareString(textValue.Trim(), "1.0", TextCompare: false) != 0)
				{
					result = true;
					goto IL_00bd;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = true;
				ProjectData.ClearProjectError();
				goto IL_00bd;
			}
			result = false;
		}
		goto IL_00bd;
		IL_00bd:
		return result;
	}

	private void CreatePostProcessBase(ProgressReporter reporter)
	{
		reporter.ReportMessage("Generating Time Series Post Process File ...");
		string directoryName = Path.GetDirectoryName(_filename);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.Delete(_filename);
		Stopwatch stopwatch = Stopwatch.StartNew();
		using (H5Writer h5Writer = new H5Writer(_filename))
		{
			h5Writer.WriteAttribute("/", "VERSION", "1.0");
		}
		int ordinal = 0;
		checked
		{
			if (_geometry.XSInterpolationSurface.FeatureCount() > 0)
			{
				ordinal += 3;
			}
			if (_geometry.StorageArea.FeatureCount() > 0)
			{
				ordinal++;
			}
			RASD2FlowArea d2FlowArea = _result.Geometry.D2FlowArea;
			int featureCount = d2FlowArea.FeatureCount();
			ordinal += 2 * featureCount;
			if (_geometry.XSInterpolationSurface.FeatureCount() > 0)
			{
				ProgressReporter reporter2 = reporter.WithProgressModifier(0, 0.5f);
				reporter.ReportMessage("Writing 1D Data: Water-Surface");
				if (!PostProcessWithVerticalChunkingInternal("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface", storeLocalExtrema: true, reporter2))
				{
					reporter.ReportMessage("Warning: Unsteady XS Water-Surface dataset not found.");
				}
				reporter2 = reporter.WithProgressModifier(50, 0.5f);
				reporter.ReportMessage("Writing 1D Data: Flow");
				if (!PostProcessWithVerticalChunkingInternal("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow", storeLocalExtrema: false, reporter2))
				{
					reporter.ReportMessage("Warning: Unsteady XS Flow dataset not found.");
				}
				if (_result.UncertaintyAnalysis && !PostProcessWithVerticalChunkingInternal(ResultsDatasets.Uncertainty.CrossSections, storeLocalExtrema: false, reporter))
				{
					reporter.ReportMessage("Warning: Uncertainty Analysis XS dataset not found.");
				}
			}
			if (_geometry.StorageArea.FeatureCount() > 0)
			{
				reporter.ReportMessage("Writing SA Data: Water-Surface");
				PostProcessWithVerticalChunkingInternal("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface", storeLocalExtrema: true, reporter);
				reporter.ReportMessage("Writing SA Data: Inflow");
				PostProcessWithVerticalChunkingInternal("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Flow", storeLocalExtrema: false, reporter);
			}
			int count = featureCount - 1;
			for (int i = 0; i <= count; i++)
			{
				ProgressReporter reporter3 = reporter.WithProgressModifier((int)Math.Round((double)(100 * i) / (double)featureCount), 0.5f / (float)featureCount);
				reporter.ReportMessage("Writing 2D Data: Water-Surface");
				if (!PostProcessWithVerticalChunkingInternal(ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(d2FlowArea.GetFeatureName(i)), storeLocalExtrema: true, reporter3))
				{
					reporter.ReportMessage("Warning: 2D Water-Surface dataset not found.");
				}
				reporter3 = reporter.WithProgressModifier((int)Math.Round((double)(100 * i) / (double)featureCount + (double)(0.5f / (float)featureCount)), 0.5f / (float)featureCount);
				reporter.ReportMessage("Writing 2D Data: Velocity");
				if (!PostProcessWithVerticalChunkingInternal(ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceVelocity(d2FlowArea.GetFeatureName(i)), storeLocalExtrema: false, reporter3))
				{
					reporter.ReportMessage("Warning: 2D Face-Velocity dataset not found.");
				}
			}
			stopwatch.Stop();
			reporter.ReportMessage("Time Series Post Process file generated [" + Strings.Format(stopwatch.ElapsedMilliseconds) + " ms]");
		}
	}

	private List<int> NeedsHydrFaceProperties()
	{
		List<int> intList = new List<int>();
		RASD2FlowArea d2FlowArea = _geometry.D2FlowArea;
		if (d2FlowArea == null)
		{
			return intList;
		}
		if (d2FlowArea.FeatureCount() == 0)
		{
			return intList;
		}
		checked
		{
			if (File.Exists(_filename))
			{
				using H5Reader h5Reader = new H5Reader(_filename);
				int count = d2FlowArea.Meshes().Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					d2FlowArea.Meshes().ElementAtOrDefault(i);
					string featureName = d2FlowArea.GetFeatureName(i);
					string absolutePath = PostProcessDatasets.Profile.FlowAreas.HydraulicArea(featureName);
					string absolutePath2 = PostProcessDatasets.Profile.FlowAreas.HydraulicDepth(featureName);
					string absolutePath3 = PostProcessDatasets.Profile.FlowAreas.HydraulicRadius(featureName);
					if (!h5Reader.ExistsAsDataset(absolutePath) || !h5Reader.ExistsAsDataset(absolutePath2) || !h5Reader.ExistsAsDataset(absolutePath3))
					{
						intList.Add(i);
					}
				}
			}
			else
			{
				intList = Enumerable.Range(0, d2FlowArea.FeatureCount()).ToList();
			}
			return intList;
		}
	}

	private void ComputePostProcessHydraulicFaceProperties(RASD2FlowArea d2f, int meshIdx, ProgressReporter reporter)
	{
		_Closure_0024__28_002D0 capturedLocals = new _Closure_0024__28_002D0(null);
		capturedLocals.mesh = d2f.Meshes().ElementAtOrDefault(meshIdx);
		string featureName = d2f.GetFeatureName(meshIdx);
		string hydraulicArea = PostProcessDatasets.Profile.FlowAreas.HydraulicArea(featureName);
		string hydraulicDepth = PostProcessDatasets.Profile.FlowAreas.HydraulicDepth(featureName);
		string hydraulicRadius = PostProcessDatasets.Profile.FlowAreas.HydraulicRadius(featureName);
		int profileCount = _result.ProfileCount;
		int numRowsPerChunk = H5Reader.RowChunkSize(profileCount, capturedLocals.mesh.FaceCount, 4194304, 4);
		checked
		{
			using H5Writer h5Writer = new H5Writer(_filename);
			_Closure_0024__28_002D2 capturedLocals2 = new _Closure_0024__28_002D2(null);
			capturedLocals2.outerClosure = capturedLocals;
			if (h5Writer.ExistsAsDataset(hydraulicArea))
			{
				h5Writer.Delete(hydraulicArea);
			}
			if (h5Writer.ExistsAsDataset(hydraulicDepth))
			{
				h5Writer.Delete(hydraulicDepth);
			}
			if (h5Writer.ExistsAsDataset(hydraulicRadius))
			{
				h5Writer.Delete(hydraulicRadius);
			}
			h5Writer.Create2dExtendibleDataset(hydraulicArea, typeof(float), numRowsPerChunk, capturedLocals2.outerClosure.mesh.FaceCount);
			h5Writer.Create2dExtendibleDataset(hydraulicDepth, typeof(float), numRowsPerChunk, capturedLocals2.outerClosure.mesh.FaceCount);
			h5Writer.Create2dExtendibleDataset(hydraulicRadius, typeof(float), numRowsPerChunk, capturedLocals2.outerClosure.mesh.FaceCount);
			capturedLocals2.faceAreaTables = d2f.ReadFaceAreaTables(featureName);
			capturedLocals2.facePerimTables = d2f.ReadFacePerimeterTables(featureName);
			capturedLocals2.faceArea = new float[1, capturedLocals2.outerClosure.mesh.FaceCount - 1 + 1];
			capturedLocals2.faceHydrDepth = new float[1, capturedLocals2.outerClosure.mesh.FaceCount - 1 + 1];
			capturedLocals2.faceHydrRadius = new float[1, capturedLocals2.outerClosure.mesh.FaceCount - 1 + 1];
			capturedLocals2.faceLength = new float[capturedLocals2.outerClosure.mesh.FaceCount - 1 + 1];
			int count = capturedLocals2.outerClosure.mesh.FaceCount - 1;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2.faceLength[i] = (float)capturedLocals2.outerClosure.mesh.PointsOnFace(i).ComputeLength();
			}
			H5RowCache<float> h5RowCache = new H5RowCache<float>(_resName);
			string dataset = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(featureName);
			int count2 = profileCount - 1;
			_Closure_0024__28_002D1 capturedLocals3 = default;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals3 = new _Closure_0024__28_002D1(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				capturedLocals3.cellWaterSurface = h5RowCache.EnsureGetRow(dataset, j);
				Parallel.SmartFor(capturedLocals3.outerClosure.outerClosure.mesh.FaceCount, capturedLocals3._Lambda_0024__0);
				h5Writer.AddRows(hydraulicArea, capturedLocals3.outerClosure.faceArea);
				h5Writer.AddRows(hydraulicDepth, capturedLocals3.outerClosure.faceHydrDepth);
				h5Writer.AddRows(hydraulicRadius, capturedLocals3.outerClosure.faceHydrRadius);
				reporter.ReportProgress((int)Math.Round((double)(100 * (j + 1)) / (double)profileCount));
			}
		}
	}

	private bool NeedsMaxMinDVorDVSq(bool isDV)
	{
		if (NeedsBaseFileUpdate())
		{
			return true;
		}
		if (NeedsMaxMinDVorDVSq1D(isDV))
		{
			return true;
		}
		if (NeedsHydrFaceProperties().Count > 0)
		{
			return true;
		}
		if (NeedsMaxMinDVorDVSq2D(isDV).Count > 0)
		{
			return true;
		}
		return false;
	}

	private void EnsureMaxMinDVorDVSq1D(bool isDV, ProgressReporter reporter)
	{
		if (NeedsBaseFileUpdate())
		{
			CreatePostProcessBase(reporter);
		}
		if (NeedsMaxMinDVorDVSq1D(isDV))
		{
			ComputePostProcessMaxMinDVorDVSq1D(isDV, reporter);
		}
	}

	private bool NeedsMaxMinDVorDVSq1D(bool isDV)
	{
		RASXS xS = _geometry.XS;
		if (xS == null)
		{
			return false;
		}
		if (xS.FeatureCount() == 0)
		{
			return false;
		}
		if (NeedsBaseFileUpdate())
		{
			return true;
		}
		using H5Reader h5Reader = new H5Reader(_filename);
		string textValue = "";
		string textValue2 = "";
		if (isDV)
		{
			textValue = PostProcessDatasets.Profile.CrossSections.MaxDV();
			textValue2 = PostProcessDatasets.Profile.CrossSections.MinDV();
		}
		else
		{
			textValue = PostProcessDatasets.Profile.CrossSections.MaxDVSq();
			textValue2 = PostProcessDatasets.Profile.CrossSections.MinDVSq();
		}
		if (h5Reader.ExistsAsDataset(textValue) && h5Reader.ExistsAsDataset(textValue2))
		{
			return false;
		}
		return true;
	}

	private void ComputePostProcessMaxMinDVorDVSq1D(bool isDV, ProgressReporter reporter)
	{
		string path = ((!isDV) ? "Max/Min D*V^2" : "Max/Min D*V");
		reporter.ReportMessage("Processing " + path + " for 1D areas");
		RASXS xS = _result.Geometry.XS;
		_ = _result.Geometry.XSInterpolationSurface;
		string dataset = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";
		string dataset2 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
		FlowDistributionData flowDistribution = _result.FlowDistribution;
		int featureCount = xS.FeatureCount();
		if (featureCount == 0)
		{
			return;
		}
		checked
		{
			float[][] floatValues = new float[featureCount - 1 + 1][];
			float[][] floatValuesArray = new float[featureCount - 1 + 1][];
			H5RowCache<float> h5RowCache = new H5RowCache<float>(_resName);
			int profileNamesTerm = _result.ProfileNames.Count();
			int count = profileNamesTerm - 1;
			for (int i = 0; i <= count; i++)
			{
				float[] ensureGetRow = h5RowCache.EnsureGetRow(dataset2, i);
				float[] ensureGetRowArray = h5RowCache.EnsureGetRow(dataset, i);
				int count2 = featureCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					FlowDistributionData.WSInterpData wsinterpdata = flowDistribution.ComputeWSInterpData(j, ensureGetRow[j]);
					float[] dvSqAlongSubsections = null;
					dvSqAlongSubsections = ((!isDV) ? flowDistribution.ComputeDVSqAlongSubsections(j, wsinterpdata, ensureGetRowArray[j]) : flowDistribution.ComputeDVAlongSubsections(j, wsinterpdata, ensureGetRowArray[j]));
					if (floatValues[j] == null)
					{
						floatValues[j] = new float[dvSqAlongSubsections.Count() - 1 + 1];
					}
					if (floatValuesArray[j] == null)
					{
						floatValuesArray[j] = new float[dvSqAlongSubsections.Count() - 1 + 1];
						int count3 = dvSqAlongSubsections.Count() - 1;
						for (int k = 0; k <= count3; k++)
						{
							floatValuesArray[j][k] = float.MaxValue;
						}
					}
					int count4 = dvSqAlongSubsections.Count() - 1;
					for (int l = 0; l <= count4; l++)
					{
						float dvSqAlongSubsection = dvSqAlongSubsections[l];
						if (dvSqAlongSubsection > floatValues[j][l])
						{
							floatValues[j][l] = dvSqAlongSubsection;
						}
						if (dvSqAlongSubsection < floatValuesArray[j][l])
						{
							floatValuesArray[j][l] = dvSqAlongSubsection;
						}
					}
				}
				reporter.ReportProgress((int)Math.Round((double)(100 * (i + 1)) / (double)profileNamesTerm));
			}
			string textValue = "";
			string textValue2 = "";
			if (isDV)
			{
				textValue = PostProcessDatasets.Profile.CrossSections.MaxDV();
				textValue2 = PostProcessDatasets.Profile.CrossSections.MinDV();
			}
			else
			{
				textValue = PostProcessDatasets.Profile.CrossSections.MaxDVSq();
				textValue2 = PostProcessDatasets.Profile.CrossSections.MinDVSq();
			}
			int specialNameArrTerm = floatValues.Max([SpecialName] (float[] arr) => arr.Count());
			float[,] floatValues2 = new float[featureCount - 1 + 1, specialNameArrTerm - 1 + 1];
			float[,] floatValues3 = new float[featureCount - 1 + 1, specialNameArrTerm - 1 + 1];
			int count5 = floatValues.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count5; intervalIndex++)
			{
				float[] floatValue = floatValues[intervalIndex];
				float[] floatValueArray = floatValuesArray[intervalIndex];
				int count6 = floatValue.Count() - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count6; intervalIndex2++)
				{
					floatValues2[intervalIndex, intervalIndex2] = floatValue[intervalIndex2];
					if (floatValueArray[intervalIndex2] != float.MaxValue)
					{
						floatValues3[intervalIndex, intervalIndex2] = floatValueArray[intervalIndex2];
					}
				}
			}
			using H5Writer h5Writer = new H5Writer(_filename);
			if (h5Writer.ExistsAsDataset(textValue))
			{
				h5Writer.Delete(textValue);
			}
			if (h5Writer.ExistsAsDataset(textValue2))
			{
				h5Writer.Delete(textValue2);
			}
			h5Writer.WriteArray(textValue, floatValues2);
			h5Writer.WriteArray(textValue2, floatValues3);
		}
	}

	private void EnsureMaxMinDVorDVSq2D(bool doDV, ProgressReporter reporter)
	{
		RASD2FlowArea d2FlowArea = _geometry.D2FlowArea;
		if (d2FlowArea == null || d2FlowArea.Meshes().Count() == 0)
		{
			return;
		}
		EnsureHydraulicFaceProperties(reporter);
		List<int> intList = new List<int>();
		checked
		{
			using (H5Reader h5Reader = new H5Reader(_filename))
			{
				int count = d2FlowArea.Meshes().Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					d2FlowArea.Mesh(i);
					string featureName = d2FlowArea.GetFeatureName(i);
					string textValue = "";
					string textValue2 = "";
					if (doDV)
					{
						textValue = PostProcessDatasets.Profile.FlowAreas.MaxDV(featureName);
						textValue2 = PostProcessDatasets.Profile.FlowAreas.MinDV(featureName);
					}
					else
					{
						textValue = PostProcessDatasets.Profile.FlowAreas.MaxDVSq(featureName);
						textValue2 = PostProcessDatasets.Profile.FlowAreas.MinDVSq(featureName);
					}
					if (!h5Reader.ExistsAsDataset(textValue) || !h5Reader.ExistsAsDataset(textValue2))
					{
						intList.Add(i);
					}
				}
			}
			string doDVTerm = "";
			doDVTerm = ((!doDV) ? "Max DV-Squared" : "Max DV");
			foreach (int ordinal in intList)
			{
				reporter.ReportMessage("Processing " + doDVTerm + " for " + d2FlowArea.GetFeatureName(ordinal));
				ComputeMaxMinDVorSq(d2FlowArea, ordinal, doDV, reporter);
			}
		}
	}

	private List<int> NeedsMaxMinDVorDVSq2D(bool doDV)
	{
		List<int> intList = new List<int>();
		RASD2FlowArea d2FlowArea = _geometry.D2FlowArea;
		if (d2FlowArea == null)
		{
			return intList;
		}
		if (d2FlowArea.Meshes().Count() == 0)
		{
			return intList;
		}
		checked
		{
			using (H5Reader h5Reader = new H5Reader(_filename))
			{
				int count = d2FlowArea.Meshes().Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					d2FlowArea.Meshes().ElementAtOrDefault(i);
					string featureName = d2FlowArea.GetFeatureName(i);
					string textValue = "";
					string textValue2 = "";
					if (doDV)
					{
						textValue = PostProcessDatasets.Profile.FlowAreas.MaxDV(featureName);
						textValue2 = PostProcessDatasets.Profile.FlowAreas.MinDV(featureName);
					}
					else
					{
						textValue = PostProcessDatasets.Profile.FlowAreas.MaxDVSq(featureName);
						textValue2 = PostProcessDatasets.Profile.FlowAreas.MinDVSq(featureName);
					}
					if (!h5Reader.ExistsAsDataset(textValue) || !h5Reader.ExistsAsDataset(textValue2))
					{
						intList.Add(i);
					}
				}
			}
			return intList;
		}
	}

	private void ComputeMaxMinDVorSq(RASD2FlowArea d2f, int meshIdx, bool doDV, ProgressReporter reporter)
	{
		_Closure_0024__35_002D1 capturedLocals = new _Closure_0024__35_002D1(null);
		capturedLocals.doDV = doDV;
		capturedLocals.mesh = d2f.Meshes().ElementAtOrDefault(meshIdx);
		string featureName = d2f.GetFeatureName(meshIdx);
		capturedLocals.mesh.EnsureVelocityComputesReady();
		string dataset = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceVelocity(featureName);
		string dataset2 = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(featureName);
		string hydraulicDepth = PostProcessDatasets.Profile.FlowAreas.HydraulicDepth(featureName);
		using (H5Reader h5Reader = new H5Reader(_filename))
		{
			if (!h5Reader.ExistsAsDataset(hydraulicDepth))
			{
				throw new Exception("Post-process file doesn't have the required datasets: Needs Hydraulic Depth");
			}
		}
		checked
		{
			capturedLocals.maxSoFar = new float[1, capturedLocals.mesh.FaceCount - 1 + 1];
			capturedLocals.minSoFar = new float[1, capturedLocals.mesh.FaceCount - 1 + 1];
			int count = capturedLocals.mesh.FaceCount - 1;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals.minSoFar[0, i] = float.MaxValue;
			}
			H5RowCache<float> h5RowCache = new H5RowCache<float>(_resName);
			H5RowCache<float> h5RowCache2 = new H5RowCache<float>(_filename);
			int profileCount = _result.ProfileCount;
			int count2 = profileCount - 1;
			_Closure_0024__35_002D0 capturedLocals2 = default;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals2 = new _Closure_0024__35_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				float[] perpendicularFaceVelocities = h5RowCache.EnsureGetRow(dataset, j);
				capturedLocals2.cellWSEL = h5RowCache.EnsureGetRow(dataset2, j);
				capturedLocals2.faceHydrDepth = h5RowCache2.EnsureGetRow(hydraulicDepth, j);
				capturedLocals2.faceWSValues = new FaceValues[capturedLocals2.outerClosure.mesh.FaceCount - 1 + 1];
				Parallel.SmartFor(capturedLocals2.outerClosure.mesh.FaceCount, capturedLocals2._Lambda_0024__0, 100);
				capturedLocals2.fpVels = capturedLocals2.outerClosure.mesh.ComputeSlopingFacepointVelocities_LeastSquares(perpendicularFaceVelocities, capturedLocals2.faceWSValues);
				Parallel.SmartFor(capturedLocals2.outerClosure.mesh.FaceCount, capturedLocals2._Lambda_0024__1);
				reporter.ReportProgress((int)Math.Round((double)(100 * (j + 1)) / (double)profileCount));
			}
			int count3 = capturedLocals.mesh.FaceCount - 1;
			for (int k = 0; k <= count3; k++)
			{
				if (capturedLocals.minSoFar[0, k] == float.MaxValue)
				{
					capturedLocals.minSoFar[0, k] = 0f;
				}
			}
			string textValue = "";
			string textValue2 = "";
			if (capturedLocals.doDV)
			{
				textValue = PostProcessDatasets.Profile.FlowAreas.MaxDV(featureName);
				textValue2 = PostProcessDatasets.Profile.FlowAreas.MinDV(featureName);
			}
			else
			{
				textValue = PostProcessDatasets.Profile.FlowAreas.MaxDVSq(featureName);
				textValue2 = PostProcessDatasets.Profile.FlowAreas.MinDVSq(featureName);
			}
			using H5Writer h5Writer = new H5Writer(_filename);
			if (h5Writer.ExistsAsDataset(textValue))
			{
				h5Writer.Delete(textValue);
			}
			if (h5Writer.ExistsAsDataset(textValue2))
			{
				h5Writer.Delete(textValue2);
			}
			h5Writer.WriteArray(textValue, capturedLocals.maxSoFar);
			h5Writer.WriteArray(textValue2, capturedLocals.minSoFar);
		}
	}

	private bool NeedsMaxMinShear()
	{
		if (NeedsBaseFileUpdate())
		{
			return true;
		}
		if (NeedsMaxShear1D())
		{
			return true;
		}
		if (NeedsMaxMinShear2D().Count > 0)
		{
			return true;
		}
		return false;
	}

	private void EnsureMaxShear1D(ProgressReporter reporter)
	{
		if (NeedsBaseFileUpdate())
		{
			CreatePostProcessBase(reporter);
		}
		if (NeedsMaxShear1D())
		{
			reporter.ReportMessage("Processing Max/Min Shear for 1D areas");
			ComputeMaxMinShear1D(reporter);
		}
	}

	private bool NeedsMaxShear1D()
	{
		RASXS xS = _geometry.XS;
		if (xS == null)
		{
			return false;
		}
		if (xS.FeatureCount() == 0)
		{
			return false;
		}
		if (NeedsBaseFileUpdate())
		{
			return true;
		}
		using H5Reader h5Reader = new H5Reader(_filename);
		if (h5Reader.ExistsAsDataset(PostProcessDatasets.Profile.CrossSections.MaxShear()) && h5Reader.ExistsAsDataset(PostProcessDatasets.Profile.CrossSections.MinShear()))
		{
			return false;
		}
		return true;
	}

	private void ComputeMaxMinShear1D(ProgressReporter reporter)
	{
		RASXS xS = _result.Geometry.XS;
		_ = _result.Geometry.XSInterpolationSurface;
		string dataset = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";
		string dataset2 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
		FlowDistributionData flowDistribution = _result.FlowDistribution;
		int featureCount = xS.FeatureCount();
		if (featureCount == 0)
		{
			return;
		}
		checked
		{
			float[][] floatValues = new float[featureCount - 1 + 1][];
			float[][] floatValuesArray = new float[featureCount - 1 + 1][];
			H5RowCache<float> h5RowCache = new H5RowCache<float>(_resName);
			int profileCount = _result.ProfileCount;
			int count = profileCount - 1;
			for (int i = 0; i <= count; i++)
			{
				float[] ensureGetRow = h5RowCache.EnsureGetRow(dataset2, i);
				float[] ensureGetRowArray = h5RowCache.EnsureGetRow(dataset, i);
				int count2 = featureCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					FlowDistributionData.WSInterpData wsinterpdata = flowDistribution.ComputeWSInterpData(j, ensureGetRow[j]);
					float[] shearAlongSubsections = flowDistribution.ComputeShearAlongSubsections(j, wsinterpdata, ensureGetRowArray[j] * ensureGetRowArray[j]);
					if (floatValues[j] == null)
					{
						floatValues[j] = new float[shearAlongSubsections.Count() - 1 + 1];
					}
					if (floatValuesArray[j] == null)
					{
						floatValuesArray[j] = new float[shearAlongSubsections.Count() - 1 + 1];
						int count3 = shearAlongSubsections.Count() - 1;
						for (int k = 0; k <= count3; k++)
						{
							floatValuesArray[j][k] = float.MaxValue;
						}
					}
					int count4 = shearAlongSubsections.Count() - 1;
					for (int l = 0; l <= count4; l++)
					{
						float shearAlongSubsection = shearAlongSubsections[l];
						if (shearAlongSubsection > floatValues[j][l])
						{
							floatValues[j][l] = shearAlongSubsection;
						}
						if (shearAlongSubsection < floatValuesArray[j][l])
						{
							floatValuesArray[j][l] = shearAlongSubsection;
						}
					}
				}
				reporter.ReportProgress((int)Math.Round((double)(100 * (i + 1)) / (double)profileCount));
			}
			string maxShear = PostProcessDatasets.Profile.CrossSections.MaxShear();
			string minShear = PostProcessDatasets.Profile.CrossSections.MinShear();
			int specialNameArrTerm = floatValues.Max([SpecialName] (float[] arr) => arr.Count());
			float[,] floatValues2 = new float[featureCount - 1 + 1, specialNameArrTerm - 1 + 1];
			float[,] floatValues3 = new float[featureCount - 1 + 1, specialNameArrTerm - 1 + 1];
			int count5 = floatValues.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count5; intervalIndex++)
			{
				float[] floatValue = floatValues[intervalIndex];
				float[] floatValueArray = floatValuesArray[intervalIndex];
				int count6 = floatValue.Count() - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count6; intervalIndex2++)
				{
					floatValues2[intervalIndex, intervalIndex2] = floatValue[intervalIndex2];
					if (floatValueArray[intervalIndex2] != float.MaxValue)
					{
						floatValues3[intervalIndex, intervalIndex2] = floatValueArray[intervalIndex2];
					}
				}
			}
			using H5Writer h5Writer = new H5Writer(_filename);
			if (h5Writer.ExistsAsDataset(maxShear))
			{
				h5Writer.Delete(maxShear);
			}
			if (h5Writer.ExistsAsDataset(minShear))
			{
				h5Writer.Delete(minShear);
			}
			h5Writer.WriteArray(maxShear, floatValues2);
			h5Writer.WriteArray(minShear, floatValues3);
		}
	}

	private void EnsureMaxMinShear2D(ProgressReporter reporter)
	{
		RASD2FlowArea d2FlowArea = _geometry.D2FlowArea;
		if (d2FlowArea == null || d2FlowArea.Meshes().Count() == 0)
		{
			return;
		}
		EnsureHydraulicFaceProperties(reporter);
		List<int> needsMaxMinShear2D = NeedsMaxMinShear2D();
		foreach (int needsMaxMinShear2D2 in needsMaxMinShear2D)
		{
			reporter.ReportMessage("Processing Max/Min Shear for " + d2FlowArea.GetFeatureName(needsMaxMinShear2D2));
			ComputeMaxMinShear2D(d2FlowArea, needsMaxMinShear2D2, reporter);
		}
	}

	private List<int> NeedsMaxMinShear2D()
	{
		List<int> intList = new List<int>();
		RASD2FlowArea d2FlowArea = _geometry.D2FlowArea;
		if (d2FlowArea == null)
		{
			return intList;
		}
		if (d2FlowArea.Meshes().Count() == 0)
		{
			return intList;
		}
		if (NeedsHydrFaceProperties().Count > 0)
		{
			return Enumerable.Range(0, d2FlowArea.MeshCount).ToList();
		}
		checked
		{
			using (H5Reader h5Reader = new H5Reader(_filename))
			{
				int count = d2FlowArea.Meshes().Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					d2FlowArea.Meshes().ElementAtOrDefault(i);
					string featureName = d2FlowArea.GetFeatureName(i);
					string absolutePath = PostProcessDatasets.Profile.FlowAreas.MaxShear(featureName);
					string absolutePath2 = PostProcessDatasets.Profile.FlowAreas.MinShear(featureName);
					if (!h5Reader.ExistsAsDataset(absolutePath) || !h5Reader.ExistsAsDataset(absolutePath2))
					{
						intList.Add(i);
					}
				}
			}
			return intList;
		}
	}

	private void ComputeMaxMinShear2D(RASD2FlowArea d2f, int meshIdx, ProgressReporter reporter)
	{
		_Closure_0024__42_002D1 capturedLocals = new _Closure_0024__42_002D1(null);
		MeshFV2D meshFV2D = d2f.Mesh(meshIdx);
		string featureName = d2f.GetFeatureName(meshIdx);
		meshFV2D.EnsureVelocityComputesReady();
		string dataset = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceVelocity(featureName);
		string dataset2 = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(featureName);
		string hydraulicRadius = PostProcessDatasets.Profile.FlowAreas.HydraulicRadius(featureName);
		using (H5Reader h5Reader = new H5Reader(_filename))
		{
			if (!h5Reader.ExistsAsDataset(hydraulicRadius))
			{
				throw new Exception("Post-process file doesn't have the required datasets: Needs Hydraulic Radius");
			}
		}
		checked
		{
			capturedLocals.maxSoFar = new float[1, meshFV2D.FaceCount - 1 + 1];
			capturedLocals.minSoFar = new float[1, meshFV2D.FaceCount - 1 + 1];
			int count = meshFV2D.FaceCount - 1;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals.minSoFar[0, i] = float.MaxValue;
			}
			H5RowCache<float> h5RowCache = new H5RowCache<float>(_resName);
			H5RowCache<float> h5RowCache2 = new H5RowCache<float>(_filename);
			int profileCount = _result.ProfileCount;
			float[] faceMann = d2f.ReadFaceMannValues(featureName);
			int count2 = profileCount - 1;
			_Closure_0024__42_002D0 capturedLocals2 = default;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals2 = new _Closure_0024__42_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				float[] perpendicularFaceVelocities = h5RowCache.EnsureGetRow(dataset, j);
				float[] cellWaterSurface = h5RowCache.EnsureGetRow(dataset2, j);
				float[] faceHydrRad = h5RowCache2.EnsureGetRow(hydraulicRadius, j);
				FaceValues[] slopingWSFaceFactors = SlopingFactors.ComputeSlopingWSFaceFactors(meshFV2D, cellWaterSurface);
				FacePointVels[] fpVels = meshFV2D.ComputeSlopingFacepointVelocities_LeastSquares(perpendicularFaceVelocities, slopingWSFaceFactors);
				Vector2D[] faceTrueVels = meshFV2D.ComputeFaceTrueVelocities(slopingWSFaceFactors, fpVels);
				capturedLocals2.fShears = meshFV2D.ComputeFaceTrueShear(faceTrueVels, faceMann, faceHydrRad).Select((_Closure_0024__._0024I42_002D0 == null) ? (_Closure_0024__._0024I42_002D0 = [SpecialName] (Vector2D vec) => vec.Magnitude) : _Closure_0024__._0024I42_002D0).ToArray();
				Parallel.SmartFor(meshFV2D.FaceCount, capturedLocals2._Lambda_0024__1);
				reporter.ReportProgress((int)Math.Round((double)(100 * (j + 1)) / (double)profileCount));
			}
			string maxShear = PostProcessDatasets.Profile.FlowAreas.MaxShear(featureName);
			string minShear = PostProcessDatasets.Profile.FlowAreas.MinShear(featureName);
			using H5Writer h5Writer = new H5Writer(_filename);
			if (h5Writer.ExistsAsDataset(maxShear))
			{
				h5Writer.Delete(maxShear);
			}
			if (h5Writer.ExistsAsDataset(minShear))
			{
				h5Writer.Delete(minShear);
			}
			h5Writer.WriteArray(maxShear, capturedLocals.maxSoFar);
			h5Writer.WriteArray(minShear, capturedLocals.minSoFar);
		}
	}

	public void EnsureChildPostProcess(string resultsFile)
	{
		string childPostProcessFilename = GetChildPostProcessFilename(resultsFile);
		string directoryName = Path.GetDirectoryName(childPostProcessFilename);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		checked
		{
			if (!File.Exists(childPostProcessFilename))
			{
				Stopwatch stopwatch = new Stopwatch();
				stopwatch.Start();
				SharedData.ReportMessage("Creating Post Process file");
				if (_geometry.XSInterpolationSurface.FeatureCount() > 0)
				{
					SharedData.ReportMessage("Creating Post Process file [writing 1D Data]", replacePreviousIfPossible: true);
					PostProcessChildWithVerticalChunking(resultsFile, childPostProcessFilename, "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface", storeLocalExtrema: true);
					PostProcessChildWithVerticalChunking(resultsFile, childPostProcessFilename, "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow", storeLocalExtrema: false);
				}
				RASD2FlowArea d2FlowArea = _result.Geometry.D2FlowArea;
				int count = d2FlowArea.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					string featureName = d2FlowArea.GetFeatureName(i);
					SharedData.ReportMessage("Creating Post Process file [writing 2D Data'" + featureName + "']");
					PostProcessChildWithVerticalChunking(resultsFile, childPostProcessFilename, ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(featureName), storeLocalExtrema: true);
					PostProcessChildWithVerticalChunking(resultsFile, childPostProcessFilename, ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FaceVelocity(featureName), storeLocalExtrema: false);
				}
				stopwatch.Stop();
				SharedData.ReportMessage("Creating Post Process completed [" + Strings.Format(stopwatch.ElapsedMilliseconds) + " ms]", replacePreviousIfPossible: true);
			}
		}
	}

	public static string GetChildPostProcessFilename(string resultsFile)
	{
		return string.Concat(Path.GetDirectoryName(resultsFile) + Conversions.ToString(Path.DirectorySeparatorChar) + Path.GetFileName(resultsFile).Split('.')[0], "\\PostProcessing.hdf");
	}

	public bool PostProcessWithVerticalChunking(string dataset, bool storeLocalExtrema, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		EnsurePostProcess(reporter);
		return PostProcessWithVerticalChunkingInternal(dataset, storeLocalExtrema, reporter);
	}

	private bool PostProcessWithVerticalChunkingInternal(string dataset, bool storeLocalExtrema, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		bool result;
		checked
		{
			try
			{
				using H5Reader h5Reader = new H5Reader(_resName);
				if (h5Reader.ExistsAsDataset(dataset))
				{
					Stopwatch stopwatch = Stopwatch.StartNew();
					if (!Directory.Exists(Path.GetDirectoryName(_filename)))
					{
						Directory.CreateDirectory(Path.GetDirectoryName(_filename));
					}
					using (H5Writer h5Writer = new H5Writer(_filename))
					{
						long[] datasetDimensions = h5Reader.GetDatasetDimensions(dataset);
						int datasetDimension = (int)datasetDimensions[0];
						int datasetDimension2 = (int)datasetDimensions[1];
						int columnChunkSize = H5Reader.ColumnChunkSize(datasetDimension, datasetDimension2, 1048576, 4);
						ProgressReporter progressReporter = null;
						ProgressReporter prog;
						if (storeLocalExtrema)
						{
							prog = reporter.WithProgressModifier(0, 0.7f);
							progressReporter = reporter.WithProgressModifier(70, 0.3f);
						}
						else
						{
							prog = reporter;
						}
						h5Reader.TransposeToColumnChunkingVB<float>(dataset, h5Writer, dataset, columnChunkSize, prog);
						if (storeLocalExtrema)
						{
							int datasetDimension2ColumnChunkSizeTerm = (int)Math.Ceiling((double)datasetDimension2 / (double)columnChunkSize);
							string datasetName = IncreasingValuesDataset(dataset);
							string datasetName2 = IncreasingInfoDataset(dataset);
							h5Writer.Create2dExtendibleDataset(datasetName2, typeof(int), Math.Min(50000, datasetDimension2), 3);
							h5Writer.Create1dExtendibleDataset(datasetName, typeof(int), Math.Max(1000, Math.Min(50000, datasetDimension2 * 4)));
							int ordinal = 0;
							float[,] values = new float[datasetDimension - 1 + 1, columnChunkSize - 1 + 1];
							int ordinal2 = 0;
							RuntimeHelpers.GetObjectValue(new object());
							using (H5DatasetID dataset2 = h5Writer.OpenDataset(dataset))
							{
								using (h5Writer.OpenDataset(datasetName2))
								{
									using (h5Writer.OpenDataset(datasetName))
									{
										int count = datasetDimension2ColumnChunkSizeTerm - 1;
										for (int i = 0; i <= count; i++)
										{
											int columnChunkSizeTerm = i * columnChunkSize;
											int columnChunkSizeDatasetDimension2Term = Math.Min(columnChunkSize, datasetDimension2 - columnChunkSizeTerm);
											h5Writer.ReadChunkDirectVB(dataset2, new int[2] { 0, i }, ref values);
											int ordinal3 = columnChunkSizeDatasetDimension2Term;
											int datasetDimension3 = datasetDimension;
											List<LocalExtrema>[] localExtremaListArray = new List<LocalExtrema>[ordinal3 - 1 + 1];
											int count2 = ordinal3 - 1;
											for (int j = 0; j <= count2; j++)
											{
												List<LocalExtrema> localExtremas = new List<LocalExtrema>();
												Slope slope;
												if (values.GetLength(0) > 1 && values[1, j] >= values[0, j])
												{
													slope = Slope.Increasing;
													localExtremas.Add(new LocalExtrema(ExtremaType.Minima, 0));
												}
												else
												{
													slope = Slope.Decreasing;
													localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, 0));
												}
												int count3 = datasetDimension3 - 1;
												for (int k = 1; k <= count3; k++)
												{
													float computed = values[k, j];
													float computed2 = values[k - 1, j];
													if (computed >= computed2)
													{
														if (slope != Slope.Increasing)
														{
															localExtremas.Add(new LocalExtrema(ExtremaType.Minima, k - 1));
															slope = Slope.Increasing;
														}
													}
													else if (slope == Slope.Increasing)
													{
														localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, k - 1));
														slope = Slope.Decreasing;
													}
												}
												if (localExtremas.Last().Extrema == ExtremaType.Minima)
												{
													localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, datasetDimension3 - 1));
												}
												else
												{
													localExtremas.Add(new LocalExtrema(ExtremaType.Minima, datasetDimension3 - 1));
												}
												localExtremaListArray[j] = localExtremas;
											}
											int[] intValues = new int[localExtremaListArray.Sum([SpecialName] (List<LocalExtrema> e) => e.Count) - 1 + 1];
											int[,] intValues2 = new int[ordinal3 - 1 + 1, 3];
											int intervalIndex = 0;
											int count4 = ordinal3 - 1;
											for (int intervalIndex2 = 0; intervalIndex2 <= count4; intervalIndex2++)
											{
												intValues2[intervalIndex2, 0] = ordinal;
												intValues2[intervalIndex2, 1] = localExtremaListArray[intervalIndex2].Count;
												intValues2[intervalIndex2, 2] = unchecked((int)localExtremaListArray[intervalIndex2][0].Extrema);
												int count5 = localExtremaListArray[intervalIndex2].Count - 1;
												for (int intervalIndex3 = 0; intervalIndex3 <= count5; intervalIndex3++)
												{
													intValues[intervalIndex + intervalIndex3] = localExtremaListArray[intervalIndex2][intervalIndex3].Time;
												}
												intervalIndex += localExtremaListArray[intervalIndex2].Count;
												ordinal += localExtremaListArray[intervalIndex2].Count;
											}
											h5Writer.AddRows(datasetName2, intValues2);
											h5Writer.AddElements(datasetName, intValues);
											ordinal2++;
											if (unchecked(ordinal2 % 10) == 0)
											{
												progressReporter.ReportProgress((int)Math.Round(100f * (float)ordinal2 / (float)datasetDimension2ColumnChunkSizeTerm));
											}
										}
									}
								}
							}
							goto end_IL_005c;
						}
						result = true;
						goto end_IL_0017;
						end_IL_005c:;
					}
					stopwatch.Stop();
					goto IL_0483;
				}
				result = false;
				end_IL_0017:;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				reporter.ReportError("Error post-processing " + dataset + " in " + _resName);
				result = false;
				ProjectData.ClearProjectError();
			}
			goto IL_0485;
		}
		IL_0485:
		return result;
		IL_0483:
		result = true;
		goto IL_0485;
	}

	private static bool PostProcessChildWithVerticalChunking(string resultsFilename, string postProcessFilename, string dataset, bool storeLocalExtrema)
	{
		checked
		{
			using (H5Reader h5Reader = new H5Reader(resultsFilename))
			{
				if (!h5Reader.ExistsAsDataset(dataset))
				{
					return false;
				}
				Stopwatch stopwatch = Stopwatch.StartNew();
				using (H5Writer h5Writer = new H5Writer(postProcessFilename))
				{
					long[] datasetDimensions = h5Reader.GetDatasetDimensions(dataset);
					int datasetDimension = (int)datasetDimensions[0];
					int datasetDimension2 = (int)datasetDimensions[1];
					int columnChunkSize = H5Reader.ColumnChunkSize(datasetDimension, datasetDimension2, 52428800, 4);
					int numColsPerChunk = H5Reader.ColumnChunkSize(datasetDimension, datasetDimension2, 1048576, 4);
					float[,] values = new float[datasetDimension - 1 + 1, columnChunkSize - 1 + 1];
					int datasetDimension2ColumnChunkSizeTerm = (int)Math.Ceiling((double)datasetDimension2 / (double)columnChunkSize);
					h5Writer.Create2dExtendibleDataset(dataset, typeof(float), datasetDimension, numColsPerChunk);
					string datasetName = dataset + " Increasing Regions Values";
					string datasetName2 = dataset + " Increasing Regions Info";
					if (storeLocalExtrema)
					{
						h5Writer.Create2dExtendibleDataset(datasetName2, typeof(int), Math.Min(50000, datasetDimension2), 3);
						h5Writer.Create1dExtendibleDataset(datasetName, typeof(int), Math.Min(50000, datasetDimension2 * 4));
					}
					int ordinal = 0;
					int count = datasetDimension2ColumnChunkSizeTerm - 1;
					for (int i = 0; i <= count; i++)
					{
						int columnChunkSizeTerm = i * columnChunkSize;
						int numCols = Math.Min(columnChunkSize, datasetDimension2 - columnChunkSizeTerm);
						h5Reader.ReadColumns(dataset, columnChunkSizeTerm, numCols, ref values);
						h5Writer.AddColumns(dataset, values);
						if (!storeLocalExtrema)
						{
							continue;
						}
						int length = values.GetLength(1);
						int length2 = values.GetLength(0);
						List<LocalExtrema>[] localExtremaListArray = new List<LocalExtrema>[length - 1 + 1];
						int count2 = length - 1;
						for (int j = 0; j <= count2; j++)
						{
							List<LocalExtrema> localExtremas = new List<LocalExtrema>();
							Slope slope;
							if (values[1, j] >= values[0, j])
							{
								slope = Slope.Increasing;
								localExtremas.Add(new LocalExtrema(ExtremaType.Minima, 0));
							}
							else
							{
								slope = Slope.Decreasing;
								localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, 0));
							}
							int count3 = length2 - 1;
							for (int k = 1; k <= count3; k++)
							{
								float computed = values[k, j];
								float computed2 = values[k - 1, j];
								if (computed >= computed2)
								{
									if (slope != Slope.Increasing)
									{
										localExtremas.Add(new LocalExtrema(ExtremaType.Minima, k - 1));
										slope = Slope.Increasing;
									}
								}
								else if (slope == Slope.Increasing)
								{
									localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, k - 1));
									slope = Slope.Decreasing;
								}
							}
							if (localExtremas.Last().Extrema == ExtremaType.Minima)
							{
								localExtremas.Add(new LocalExtrema(ExtremaType.Maxima, length2 - 1));
							}
							else
							{
								localExtremas.Add(new LocalExtrema(ExtremaType.Minima, length2 - 1));
							}
							localExtremaListArray[j] = localExtremas;
						}
						int[] intValues = new int[localExtremaListArray.Sum([SpecialName] (List<LocalExtrema> e) => e.Count) - 1 + 1];
						int[,] intValues2 = new int[length - 1 + 1, 3];
						int intervalIndex = 0;
						int count4 = length - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count4; intervalIndex2++)
						{
							intValues2[intervalIndex2, 0] = ordinal;
							intValues2[intervalIndex2, 1] = localExtremaListArray[intervalIndex2].Count;
							intValues2[intervalIndex2, 2] = unchecked((int)localExtremaListArray[intervalIndex2][0].Extrema);
							int count5 = localExtremaListArray[intervalIndex2].Count - 1;
							for (int intervalIndex3 = 0; intervalIndex3 <= count5; intervalIndex3++)
							{
								intValues[intervalIndex + intervalIndex3] = localExtremaListArray[intervalIndex2][intervalIndex3].Time;
							}
							intervalIndex += localExtremaListArray[intervalIndex2].Count;
							ordinal += localExtremaListArray[intervalIndex2].Count;
						}
						h5Writer.AddRows(datasetName2, intValues2);
						h5Writer.AddElements(datasetName, intValues);
					}
				}
				stopwatch.Stop();
			}
			return true;
		}
	}

	public void EnsureXSRegionsLoaded()
	{
		if (_XSRegions == null && _geometry.XSInterpolationSurface.FeatureCount() > 0)
		{
			ReadExtrema1D();
		}
	}

	private void ReadExtrema1D()
	{
		EnsurePostProcess();
		string datasetName = IncreasingValuesDataset("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface");
		string datasetName2 = IncreasingInfoDataset("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface");
		int[,] values = null;
		int[] values2 = null;
		using (H5Reader h5Reader = new H5Reader(_filename))
		{
			h5Reader.ReadDataset(datasetName2, ref values);
			h5Reader.ReadDataset(datasetName, ref values2);
		}
		_XSRegions = ParseLocalExtrema(values, values2);
	}

	private List<LocalExtrema>[] ParseLocalExtrema(int[,] rgnInfo, int[] rgnValues)
	{
		checked
		{
			List<LocalExtrema>[] localExtremaListArray = new List<LocalExtrema>[rgnInfo.GetLength(0) - 1 + 1];
			int count = localExtremaListArray.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				localExtremaListArray[i] = ParseLocalExtremaRow(rgnInfo, rgnValues, i);
			}
			return localExtremaListArray;
		}
	}

	private List<LocalExtrema> ParseLocalExtremaRow(int[,] rgnInfo, int[] rgnValues, int row)
	{
		List<LocalExtrema> localExtremas = new List<LocalExtrema>();
		ExtremaType extremaType = (ExtremaType)rgnInfo[row, 2];
		int rgnInfoRowTerm = rgnInfo[row, 0];
		checked
		{
			int count = rgnInfo[row, 0] + rgnInfo[row, 1] - 1;
			for (int i = rgnInfoRowTerm; i <= count; i++)
			{
				localExtremas.Add(new LocalExtrema(extremaType, rgnValues[i]));
				extremaType = ArrivalTimeExtensions.SwapType(extremaType);
			}
			return localExtremas;
		}
	}

	private List<LocalExtrema> ParseLocalExtremaRow(int[] rgnInfo, int[] rgnValues)
	{
		List<LocalExtrema> localExtremas = new List<LocalExtrema>();
		ExtremaType extremaType = (ExtremaType)rgnInfo[2];
		int rgnInfo2 = rgnInfo[0];
		checked
		{
			int count = rgnInfo[0] + rgnInfo[1] - 1;
			for (int i = rgnInfo2; i <= count; i++)
			{
				localExtremas.Add(new LocalExtrema(extremaType, rgnValues[i]));
				extremaType = ArrivalTimeExtensions.SwapType(extremaType);
			}
			return localExtremas;
		}
	}

	public List<LocalExtrema> GetXSExtrema(int xs)
	{
		return _XSRegions[xs];
	}

	private string IncreasingInfoDataset(string originalDataset)
	{
		return originalDataset + " Increasing Regions Info";
	}

	private string IncreasingValuesDataset(string originalDataset)
	{
		return originalDataset + " Increasing Regions Values";
	}
}
