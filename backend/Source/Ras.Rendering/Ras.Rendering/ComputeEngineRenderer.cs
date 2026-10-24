using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Charting;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Mapping;
using Ras.Output;
using Utility.ContextActions;
using Utility.Extensions;
using Utility.Interfaces;

namespace Ras.Rendering;

public class ComputeEngineRenderer : RenderLayer, IBandedRenderLayer, IRenderLayer, IDisplayName, IReadOnlyDisplayName, IProfileStateProvider
{
	private class SplitCache
	{
		public object MeshRenderLayerCache;

		public object BCRenderCache;

		public object MeasureLineCache;
	}

	private readonly Solver _solver;

	private readonly IHorizontalOutputBlock _outputBlock;

	private readonly IResample<float> _terrain;

	private readonly IList<MeshTimestep> _timesteps;

	private readonly double[] _computeTime;

	private readonly DateTime[] _dts;

	private readonly MeshRenderLayer _meshRenderLayer;

	private readonly ResultMapRenderLayer _wsRenderer;

	private readonly PolylineRenderLayer _bcFaceRenderer = new PolylineRenderLayer();

	private readonly PolylineRenderLayer _measureLines = new PolylineRenderLayer();

	public override Extent ValidExtent => _meshRenderLayer.ValidExtent;

	public IProfileState ProfileState { get; }

	public int ProfileCount => ProfileState.ProfileCount;

	public Func<Geospatial.Vectors.Point, double> AnalyticalStage { get; set; }

	public Func<Geospatial.Vectors.Point, double, double> TemporalAnalyticalStage { get; set; }

	public bool ForceHorizontalRendering
	{
		get
		{
			return _wsRenderer?.ForceHorizontalRendering ?? false;
		}
		set
		{
			_wsRenderer.ForceHorizontalRendering = value;
		}
	}

	public ComputeEngineRenderer(Solver solver, IList<MeshTimestep> timesteps, IResample<float> terrain)
	{
		_solver = solver;
		_terrain = terrain;
		_timesteps = timesteps;
		_meshRenderLayer = new MeshRenderLayer(solver.Mesh);
		DateTime epoch = new DateTime(2000, 1, 1);
		_computeTime = _timesteps.Select((MeshTimestep mt) => mt.Time).ToArray();
		_dts = _timesteps.Select((MeshTimestep mt) => epoch.AddSeconds(mt.Time)).ToArray();
		List<string> pfNames = _dts.Select((DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss")).ToList();
		ProfileState = new ProfileState(pfNames);
		Geometry geometry = Geometry.CreateInMemory();
		geometry.FlowAreaLayer.Add(new FlowArea(solver.Mesh, "Mesh"));
		_outputBlock = new ComputedTimestepOutputBlock(geometry, timesteps, solver.PropertyTables, _dts);
		_wsRenderer = new ResultMapRenderLayer(_outputBlock, MapTypes.WaterSurfaceElevation, terrain, new MeshPropertyTables[1] { _solver.PropertyTables });
		_wsRenderer.SetDefaultSymbology();
		LinkTo(_wsRenderer);
		List<Ras.Engine.BoundaryCondition> boundaryConditions = new List<Ras.Engine.BoundaryCondition>();
		boundaryConditions.AddRange(solver.StageBoundaryConditions);
		boundaryConditions.AddRange(solver.FlowBoundaryConditions);
		boundaryConditions.AddRange(solver.NormalDepthBoundaryConditions);
		boundaryConditions.AddRange(solver.VariableStageBoundaryConditions);
		boundaryConditions.AddRange(solver.VariableFlowBoundaryConditions);
		boundaryConditions.AddRange(solver.SpatiallyVariableStageVelocityBoundaryConditions);
		List<Ras.Engine.BoundaryCondition> source = boundaryConditions;
		List<int> bcFaces = source.Where((Ras.Engine.BoundaryCondition bc) => bc?.Faces != null).SelectMany((Ras.Engine.BoundaryCondition bc) => bc.Faces).Distinct()
			.ToList();
		if (bcFaces.Count > 0 || solver.RatingCurveBoundaryConditions.Count > 0)
		{
			List<int> selectMany = solver.StageBoundaryConditions.SelectMany((Ras.Engine.BoundaryCondition sbc) => sbc.Faces).ToList();
			selectMany.AddRange(solver.FlowBoundaryConditions.SelectMany((Ras.Engine.BoundaryCondition sbc) => sbc.Faces));
			selectMany.AddRange(solver.RatingCurveBoundaryConditions.SelectMany((Ras.Engine.BoundaryCondition sbc) => sbc.Faces));
			IEnumerable<Polyline> polylines = selectMany.Select((int fIdx) => solver.Mesh.FacePolyline(fIdx));
			_bcFaceRenderer = new PolylineRenderLayer(polylines);
			_bcFaceRenderer.VectorSymbology.PenWidth = 5.0;
			_bcFaceRenderer.VectorSymbology.PenColor = Color.Blue;
			_bcFaceRenderer.LabelFeatures = true;
			_bcFaceRenderer.SetLabels((int i) => "Face " + bcFaces[i], string.Empty);
		}
		_measureLines = new PolylineRenderLayer();
		_measureLines.VectorSymbology.PenColor = Color.Red;
		_measureLines.VectorSymbology.PenWidth = 3.0;
		AddDefaultMapHover();
		AddDefaultContextActions();
	}

	public ResultMapRenderLayer GetWaterSurfaceRenderer()
	{
		return _wsRenderer;
	}

	private void LinkTo(IBandedRenderLayer irl)
	{
		ProfileState.ProfileChanged += (int idx) =>
		{
			irl.ProfileState.CurrentProfileIndex = idx;
		};
	}

	public RenderLayer GetDeltaFromAnalytical(double minorError, double moderateError, double majorError)
	{
		if (AnalyticalStage != null)
		{
			WaterSurfaceResampler baseRes = new WaterSurfaceResampler(_outputBlock, _terrain, new MeshPropertyTables[1] { _solver.PropertyTables });
			FunctionalResampler<float> res = new FunctionalResampler<float>(ValidExtent, -9999f, (Geospatial.Vectors.Point p) => (float)AnalyticalStage(p));
			BandedRenderLayer bandedRenderLayer = new BandedRenderLayer(baseRes.ComposeWith(res, (Memory<float> bufBase, Memory<float> bufAnalytical) =>
			{
				Span<float> bufBaseSpan = bufBase.Span;
				Span<float> bufAnalyticalSpan = bufAnalytical.Span;
				for (int i = 0; i < bufBase.Length; i++)
				{
					float bufAnalytical2 = bufAnalyticalSpan[i];
					float bufBase2 = bufBaseSpan[i];
					if (bufAnalytical2 == -9999f || bufBase2 == -9999f)
					{
						bufAnalyticalSpan[i] = -9999f;
					}
					else
					{
						bufAnalyticalSpan[i] = bufAnalytical2 - bufBase2;
					}
				}
			}))
			{
				RasterSymbology = GetDeltaSymbology(minorError, moderateError, majorError)
			};
			LinkTo(bandedRenderLayer);
			return bandedRenderLayer;
		}
		if (TemporalAnalyticalStage != null)
		{
			WaterSurfaceResampler baseRes2 = new WaterSurfaceResampler(_outputBlock, _terrain, new MeshPropertyTables[1] { _solver.PropertyTables });
			MultiBandFunctionalResampler<float> altResampler = new MultiBandFunctionalResampler<float>(ProfileCount, ValidExtent, -9999f, (int i, Geospatial.Vectors.Point p) => (float)TemporalAnalyticalStage(p, _computeTime[i]));
			BandedRenderLayer bandedRenderLayer2 = new BandedRenderLayer(baseRes2.ComposeWith(altResampler, (Memory<float> bufBase, Memory<float> bufAnalytical) =>
			{
				Span<float> bufBase3 = bufBase.Span;
				Span<float> bufAnalytical3 = bufAnalytical.Span;
				for (int i = 0; i < bufBase3.Length; i++)
				{
					float bufAnalytical4 = bufAnalytical3[i];
					float bufBase4 = bufBase3[i];
					if (bufAnalytical4 == -9999f || bufBase4 == -9999f)
					{
						bufAnalytical3[i] = -9999f;
					}
					else
					{
						bufAnalytical3[i] = bufAnalytical4 - bufBase4;
					}
				}
			}))
			{
				RasterSymbology = GetDeltaSymbology(minorError, moderateError, majorError)
			};
			LinkTo(bandedRenderLayer2);
			return bandedRenderLayer2;
		}
		return null;
	}

	public MeshRenderLayer GetMeshRenderLayer()
	{
		return _meshRenderLayer;
	}

	public static RasterSymbology GetDeltaSymbology(double minor, double moderate, double large)
	{
		return new RasterSymbology(new InterpolatedSurfaceFill(new Color[7]
		{
			Color.Red,
			Color.Orange,
			Color.Yellow,
			Color.Green,
			Color.Yellow,
			Color.Orange,
			Color.Red
		}, new double[7]
		{
			0.0 - large,
			0.0 - moderate,
			0.0 - minor,
			0.0,
			minor,
			moderate,
			large
		}));
	}

	public void AddProfileLine(Polyline pl)
	{
		_measureLines.Polylines.Add(pl);
	}

	private void AddDefaultMapHover()
	{
		AddMapHover((PlotRequest pr, HitTestArgs args) =>
		{
			using (IEnumerator<int> hitTest = _measureLines.HitTest(args).GetEnumerator())
			{
				if (hitTest.MoveNext())
				{
					int current = hitTest.Current;
					_measureLines.Highlight(current, pr);
					return new MapHover(args.QueryPoint, "Measure Line " + current);
				}
			}
			if (_meshRenderLayer.HitTest(args, out var element, out var meshIndex, out var elementIndex))
			{
				_meshRenderLayer.Highlight(pr, meshIndex, element, elementIndex);
				if (!ProfileState.IsRegularProfile)
				{
					return (MapHover)null;
				}
				MapHover mapHover = new MapHover();
				MeshWaterState waterState = _timesteps[ProfileState.CurrentProfileIndex].WaterState;
				switch (element)
				{
				case MeshElementType.Cell:
					mapHover.Label = "Cell" + elementIndex.ToString().InSquareBrackets() + Environment.NewLine + waterState.DebugCellInfo(elementIndex) + Environment.NewLine + _solver.DebugCellInfo(elementIndex, waterState);
					break;
				case MeshElementType.Face:
					mapHover.Label = "Face" + elementIndex.ToString().InSquareBrackets() + Environment.NewLine + waterState.DebugFaceInfo(elementIndex) + Environment.NewLine + _solver.DebugFaceInfo(elementIndex, waterState);
					break;
				default:
					mapHover.Label = "FP" + elementIndex.ToString().InSquareBrackets();
					break;
				}
				return mapHover;
			}
			return (MapHover)null;
		});
	}

	private double[] TryGetAnalyticalStage(Polyline pl, double[] stations = null)
	{
		if (AnalyticalStage != null)
		{
			if (stations == null)
			{
				stations = pl.CumulativePointStations();
			}
			return pl.Points.Select((Geospatial.Vectors.Point pt) => AnalyticalStage(pt)).ToArray();
		}
		if (TemporalAnalyticalStage != null && ProfileState.IsRegularProfile)
		{
			int currentProfileIndex = ProfileState.CurrentProfileIndex;
			double days = _computeTime[currentProfileIndex];
			if (stations == null)
			{
				stations = pl.CumulativePointStations();
			}
			return pl.Points.Select((Geospatial.Vectors.Point pt) => TemporalAnalyticalStage(pt, days)).ToArray();
		}
		return null;
	}

	private void ReplaceNoDataWithTerrain(double[] data, double[] tr)
	{
		for (int i = 0; i < data.Length; i++)
		{
			if (data[i] == -9999.0)
			{
				data[i] = tr[i];
			}
		}
	}

	private void ReplaceNoDataWithTerrain(double[] data, float[] tr)
	{
		for (int i = 0; i < data.Length; i++)
		{
			if (data[i] == -9999.0)
			{
				data[i] = tr[i];
			}
		}
	}

	private void ReplaceNoDataWithTerrain(float[] data, float[] tr)
	{
		for (int i = 0; i < data.Length; i++)
		{
			if ((double)data[i] == -9999.0)
			{
				data[i] = tr[i];
			}
		}
	}

	private XYSeriesRequest TryGetAnalyticalStageTimeSeries(Geospatial.Vectors.Point pt)
	{
		if (AnalyticalStage != null)
		{
			double analyticalStage = AnalyticalStage(pt);
			return new XYSeriesRequest("Stage (Analytical)", "Time", "Seconds", new double[2]
			{
				_computeTime[0],
				_computeTime.Last()
			}, "Elevation", "m", new double[2] { analyticalStage, analyticalStage })
			{
				Symbology = 
				{
					PenColor = Color.Red
				}
			};
		}
		if (TemporalAnalyticalStage != null && ProfileState.IsRegularProfile)
		{
			double[] doubleValues = new double[_computeTime.Length];
			for (int i = 0; i < _computeTime.Length; i++)
			{
				doubleValues[i] = TemporalAnalyticalStage(pt, _computeTime[i]);
			}
			return new XYSeriesRequest("Stage (Analytical)", "Time", "Seconds", _computeTime, "Elevation", "m", doubleValues)
			{
				Symbology = 
				{
					PenColor = Color.Red
				}
			};
		}
		return null;
	}

	private ContextChartRequest PlotMeasureLineStage(int lineIdx)
	{
		string textValue = "Plot Stage";
		Polyline polyline = _measureLines.Polylines[lineIdx].Clone();
		polyline.Densify(0.1);
		Geospatial.Vectors.Point[] points = polyline.Points.ToArray();
		double[] cumulativePointStations = polyline.CumulativePointStations();
		float[] floatValues = new float[points.Length];
		_terrain.SamplePoints(points, floatValues);
		XYSeriesRequest xYSeriesRequest = new XYSeriesRequest("Terrain", "Station", "m", cumulativePointStations, "Elevation", "m", ((IEnumerable<float>)floatValues).Select((Func<float, double>)((float f) => f)).ToArray());
		xYSeriesRequest.Symbology.PenColor = Color.Green;
		XYSeriesRequest xYSeriesRequest2 = null;
		if (_wsRenderer.Resample(points, out var values))
		{
			ReplaceNoDataWithTerrain(values, floatValues);
			xYSeriesRequest2 = new XYSeriesRequest("Stage", "Station", "m", cumulativePointStations, "Elevation", "m", ((IEnumerable<float>)values).Select((Func<float, double>)((float f) => f)).ToArray());
			xYSeriesRequest2.Symbology.PenColor = Color.Blue;
		}
		double[] analyticalStage = TryGetAnalyticalStage(polyline, cumulativePointStations);
		XYSeriesRequest xYSeriesRequest3 = null;
		if (analyticalStage != null)
		{
			ReplaceNoDataWithTerrain(analyticalStage, floatValues);
			xYSeriesRequest3 = new XYSeriesRequest("Stage (Analytical)", "Station", "m", cumulativePointStations, "Elevation", "m", analyticalStage);
			xYSeriesRequest3.Symbology.PenColor = Color.Red;
		}
		ChartRequest chartRequest = new ChartRequest(textValue);
		chartRequest.TryAddGroup(xYSeriesRequest, xYSeriesRequest2, xYSeriesRequest3);
		return new ContextChartRequest(textValue, enabled: true, chartRequest);
	}

	private void AddDefaultContextActions()
	{
		AddMapHitTest((HitTestArgs args) =>
		{
			using (IEnumerator<int> hitTest = _measureLines.HitTest(args).GetEnumerator())
			{
				if (hitTest.MoveNext())
				{
					int current = hitTest.Current;
					return PlotMeasureLineStage(current);
				}
			}
			if (!_meshRenderLayer.HitTest(args, out var element, out var _, out var elementIndex))
			{
				return (IContextBase)null;
			}
			switch (element)
			{
			case MeshElementType.Cell:
				return new ContextCollection("", enabled: true, flatten: true)
				{
					MakeCellChartRequest(elementIndex),
					MakeCellProps(elementIndex)
				};
			case MeshElementType.Face:
			{
				Mesh mesh = _solver.Mesh;
				ContextChartRequest faceProps = MakeFaceProps(elementIndex);
				ContextCollection contextCollection = new ContextCollection("Plots", enabled: false, flatten: true);
				if (mesh.FaceIsPerimeter(elementIndex))
				{
					ContextChartRequest contextChartRequest = MakeFaceChartRequest(elementIndex);
					ContextChartRequest contextChartRequest2 = MakeCellChartRequest(mesh.Faces[elementIndex].cellA);
					contextCollection.Add(contextChartRequest);
					ChartRequest chartRequest = new ChartRequest("Combined Cell + Face Time Series");
					foreach (IList<ISeriesRequest> sery in contextChartRequest2.ChartRequest.Series)
					{
						chartRequest.Series.Add(sery);
					}
					foreach (IList<ISeriesRequest> seryList in contextChartRequest.ChartRequest.Series)
					{
						chartRequest.Series.Add(seryList);
					}
					ContextChartRequest contextChartRequest3 = new ContextChartRequest("Face + Internal Cell Time Series", enabled: true, chartRequest);
					contextCollection.Add(contextChartRequest3);
					contextCollection.Add(faceProps);
				}
				else
				{
					contextCollection.Add(MakeFaceChartRequest(elementIndex));
					contextCollection.Add(faceProps);
				}
				return contextCollection;
			}
			default:
				return (IContextBase)null;
			}
		});
	}

	private ContextChartRequest MakeCellChartRequest(int cIdx)
	{
		ChartRequest chartRequest = new ChartRequest("Cell Time Series");
		XYSeriesRequest xYSeriesRequest = new XYSeriesRequest("Stage", "Time", "Seconds", _computeTime, "Elevation", "m", _timesteps.Select((MeshTimestep ts) => ts.WaterState.CellElevation[cIdx]).ToArray());
		xYSeriesRequest.Symbology.PenColor = Color.Blue;
		XYSeriesRequest xYSeriesRequest2 = TryGetAnalyticalStageTimeSeries(_solver.Mesh.CellCenters[cIdx]);
		if (xYSeriesRequest2 != null)
		{
			chartRequest.AddGroup(xYSeriesRequest, xYSeriesRequest2);
		}
		else
		{
			chartRequest.Add(xYSeriesRequest);
		}
		XYSeriesRequest xYSeriesRequest3 = new XYSeriesRequest("Cell Volume", "Time", "Seconds", _computeTime, "Volume", "m³", _timesteps.Select((MeshTimestep ts) => ts.WaterState.CellVolume[cIdx]).ToArray());
		xYSeriesRequest3.Symbology.PenColor = Color.Brown;
		XYSeriesRequest xYSeriesRequest4 = new XYSeriesRequest("Cell Inflow", "Time", "Seconds", _computeTime, "Inflow", "m³/s", _timesteps.Select((MeshTimestep ts) => ts.WaterState.CellInflow(cIdx)).ToArray());
		xYSeriesRequest4.Symbology.PenColor = Color.Orange;
		chartRequest.AddGroup(xYSeriesRequest3, xYSeriesRequest4);
		return new ContextChartRequest("Cell Time Series", enabled: true, chartRequest);
	}

	private ContextChartRequest MakeFaceChartRequest(int fIdx)
	{
		ChartRequest chartRequest = new ChartRequest("Face Time Series");
		XYSeriesRequest xYSeriesRequest = new XYSeriesRequest("Stage", "Time", "Seconds", _computeTime, "Elevation", "m", _timesteps.Select((MeshTimestep ts) => ts.WaterState.FaceElevation[fIdx]).ToArray());
		xYSeriesRequest.Symbology.PenColor = Color.Blue;
		XYSeriesRequest xYSeriesRequest2 = TryGetAnalyticalStageTimeSeries(_solver.Mesh.FaceMidPoint(fIdx));
		if (xYSeriesRequest2 != null)
		{
			chartRequest.AddGroup(xYSeriesRequest, xYSeriesRequest2);
		}
		else
		{
			chartRequest.Add(xYSeriesRequest);
		}
		XYSeriesRequest xYSeriesRequest3 = new XYSeriesRequest("Face Wetted Area", "Time", "Seconds", _computeTime, "Area", "m²", _timesteps.Select((MeshTimestep ts) => ts.WaterState.FaceArea[fIdx]).ToArray());
		xYSeriesRequest3.Symbology.PenColor = Color.Brown;
		chartRequest.Add(xYSeriesRequest3);
		return new ContextChartRequest("Face Time Series", enabled: true, chartRequest);
	}

	private ContextChartRequest MakeCellProps(int cIdx)
	{
		string textValue = "Cell Property Tables";
		ChartRequest chartRequest = new ChartRequest(textValue);
		_solver.PropertyTables.GetCellTables(cIdx, out var vols, out var elevs, out var wettedArea);
		double[] xData = AsDouble(vols);
		double[] yData = AsDouble(elevs);
		double[] xData2 = AsDouble(wettedArea);
		XYSeriesRequest xYSeriesRequest = new XYSeriesRequest("Volume vs Elev", "Volume", "ft^3", xData, "Elevation", "ft", yData);
		XYSeriesRequest xYSeriesRequest2 = new XYSeriesRequest("Wet Area vs Elev", "Area", "ft^2", xData2, "Elevation", "ft", yData);
		xYSeriesRequest.Symbology.PenColor = Color.Black;
		xYSeriesRequest2.Symbology.PenColor = Color.Red;
		chartRequest.AddGroup(xYSeriesRequest, xYSeriesRequest2);
		return new ContextChartRequest(textValue, enabled: true, chartRequest);
	}

	private ContextChartRequest MakeFaceProps(int fIdx)
	{
		string textValue = "Face Property Tables";
		ChartRequest chartRequest = new ChartRequest(textValue);
		_solver.PropertyTables.GetFaceTables(fIdx, out var areas, out var elevs, out var wettedPerim, out var hydrRadius);
		double[] xData = AsDouble(areas);
		double[] yData = AsDouble(elevs);
		double[] xData2 = AsDouble(wettedPerim);
		double[] xData3 = AsDouble(hydrRadius);
		XYSeriesRequest xYSeriesRequest = new XYSeriesRequest("Area vs Elev", "Area", "ft^2", xData, "Elevation", "ft", yData);
		XYSeriesRequest xYSeriesRequest2 = new XYSeriesRequest("Wet Perim vs Elev", "Length", "ft", xData2, "Elevation", "ft", yData);
		XYSeriesRequest xYSeriesRequest3 = new XYSeriesRequest("Hydr Radius vs Elev", "Length", "ft", xData3, "Elevation", "ft", yData);
		xYSeriesRequest.Symbology.PenColor = Color.Black;
		xYSeriesRequest2.Symbology.PenColor = Color.Red;
		xYSeriesRequest3.Symbology.PenColor = Color.Blue;
		chartRequest.AddGroup(xYSeriesRequest, xYSeriesRequest2, xYSeriesRequest3);
		return new ContextChartRequest(textValue, enabled: true, chartRequest);
	}

	private static double[] AsDouble(Memory<float> f)
	{
		Span<float> floatSpan = f.Span;
		double[] doubleValues = new double[floatSpan.Length];
		for (int i = 0; i < floatSpan.Length; i++)
		{
			doubleValues[i] = floatSpan[i];
		}
		return doubleValues;
	}

	protected override bool RenderInternal(PlotRequest request, ref object cache)
	{
		SplitCache splitCache = cache as SplitCache;
		if (splitCache == null)
		{
			splitCache = (SplitCache)(cache = new SplitCache());
		}
		bool condition = _meshRenderLayer.Render(request, ref splitCache.MeshRenderLayerCache);
		if (_bcFaceRenderer != null)
		{
			condition |= _bcFaceRenderer.Render(request, ref splitCache.BCRenderCache);
		}
		return condition | _measureLines.Render(request, ref splitCache.MeasureLineCache);
	}

	protected override long GetInternalRenderState()
	{
		return base.GetInternalRenderState() + (ProfileState?.Version ?? 0);
	}
}
