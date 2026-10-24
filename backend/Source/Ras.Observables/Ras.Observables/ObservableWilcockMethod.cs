using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Hydraulics.HydraulicDesign;

namespace Ras.Observables;

public class ObservableWilcockMethod : ObservableObject
{
	public class ObservableSBTM : ObservableObject
	{
		public readonly WilcockMethod.SBTM Layer;

		private BindableArrayWrapper<double> _grainDiameter;

		private BindableArrayWrapper<double> _surfaceCdf;

		public double ShearVelocity
		{
			get
			{
				return Layer.ShearVelocity;
			}
			set
			{
				if (SetProperty(Layer.ShearVelocity, value, Layer, (WilcockMethod.SBTM m, double v) =>
				{
					m.ShearVelocity = v;
				}, "ShearVelocity"))
				{
					RefreshOutputs();
				}
			}
		}

		public double WaterDensity
		{
			get
			{
				return Layer.WaterDensity;
			}
			set
			{
				if (SetProperty(Layer.WaterDensity, value, Layer, (WilcockMethod.SBTM m, double v) =>
				{
					m.WaterDensity = v;
				}, "WaterDensity"))
				{
					RefreshOutputs();
				}
			}
		}

		public double SedimentDensity
		{
			get
			{
				return Layer.SedimentDensity;
			}
			set
			{
				if (SetProperty(Layer.SedimentDensity, value, Layer, (WilcockMethod.SBTM m, double v) =>
				{
					m.SedimentDensity = v;
				}, "SedimentDensity"))
				{
					RefreshOutputs();
				}
			}
		}

		public BindableArrayWrapper<double> GrainDiameter
		{
			get
			{
				if (_grainDiameter == null)
				{
					_grainDiameter = new BindableArrayWrapper<double>(Layer.GrainDiameter, (double[] da) => Layer.GrainDiameter = da, 0.0, isFixedSize: false);
					_grainDiameter.CollectionChanged += InputArray_CollectionChanged;
				}
				return _grainDiameter;
			}
		}

		public BindableArrayWrapper<double> SurfaceCdf
		{
			get
			{
				if (_surfaceCdf == null)
				{
					_surfaceCdf = new BindableArrayWrapper<double>(Layer.SurfaceCdf, (double[] da) => Layer.SurfaceCdf = da, 0.0, isFixedSize: false);
					_surfaceCdf.CollectionChanged += InputArray_CollectionChanged;
				}
				return _surfaceCdf;
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportRate
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportRate");
					field = value;
					OnPropertyChanged("TransportRate");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDMin
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceDMin");
					field = value;
					OnPropertyChanged("SurfaceDMin");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDMax
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceDMax");
					field = value;
					OnPropertyChanged("SurfaceDMax");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceD50
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceD50");
					field = value;
					OnPropertyChanged("SurfaceD50");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDg
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceDg");
					field = value;
					OnPropertyChanged("SurfaceDg");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceSigmaG
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceSigmaG");
					field = value;
					OnPropertyChanged("SurfaceSigmaG");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceFs
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceFs");
					field = value;
					OnPropertyChanged("SurfaceFs");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDMin
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportDMin");
					field = value;
					OnPropertyChanged("TransportDMin");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDMax
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportDMax");
					field = value;
					OnPropertyChanged("TransportDMax");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportD50
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportD50");
					field = value;
					OnPropertyChanged("TransportD50");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDg
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportDg");
					field = value;
					OnPropertyChanged("TransportDg");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportSigmaG
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportSigmaG");
					field = value;
					OnPropertyChanged("TransportSigmaG");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportFs
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportFs");
					field = value;
					OnPropertyChanged("TransportFs");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] TransportCdf
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TransportCdf");
					field = value;
					OnPropertyChanged("TransportCdf");
				}
			}
		}

		public ObservableSBTM(WilcockMethod.SBTM layer)
		{
			Layer = layer;
			RefreshOutputs();
		}

		private void InputArray_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			RefreshOutputs();
		}

		private void RefreshOutputs()
		{
			if (GrainDiameter.Count != 0 && SurfaceCdf.Count != 0)
			{
				SBTMResults sBTMResults = WilcockSbtmCalculator.ComputeSbtm(Layer);
				double grainDiameterLayerTerm = (((Layer.GrainDiameter?.Length ?? 0) > 0) ? Layer.GrainDiameter.Min() : 0.0);
				double grainDiameterLayerTerm2 = (((Layer.GrainDiameter?.Length ?? 0) > 0) ? Layer.GrainDiameter.Max() : 0.0);
				TransportRate = sBTMResults.TransportRate;
				SurfaceDMin = grainDiameterLayerTerm;
				SurfaceDMax = grainDiameterLayerTerm2;
				SurfaceD50 = sBTMResults.Ds50;
				SurfaceDg = sBTMResults.Dsm;
				SurfaceSigmaG = sBTMResults.SSig;
				SurfaceFs = sBTMResults.SFs;
				TransportDMin = grainDiameterLayerTerm;
				TransportDMax = grainDiameterLayerTerm2;
				TransportD50 = sBTMResults.Db50;
				TransportDg = sBTMResults.Dbm;
				TransportSigmaG = sBTMResults.BSig;
				TransportFs = sBTMResults.BFs;
				TransportCdf = sBTMResults.CumulativeBedloadDistribution;
			}
		}
	}

	public class ObservableMergedSBTM : ObservableObject
	{
		private bool _isISBTMMode;
		private bool _armorRatioVisible;
		private bool _maxIterationsVisible;
		private bool _iterationsVisible;
		private bool _convergenceToleranceVisible;
		private bool _relativeDifferenceSurfaceDgVisible;
		private bool _maxAbsErrorFractionVisible;
		private bool _maxAbsErrorSandFractionVisible;
		private bool _relativeDifferenceShearVelocityVisible;
		private bool _relativeDifferenceTransportRateVisible;
		private bool _shearVelocityAsInputVisible;
		private bool _totalTransportRateAsInputVisible;
		private bool _waterDensityVisible;
		private bool _sedimentDensityVisible;

		public readonly WilcockMethod.SBTM SbtmLayer;

		public readonly WilcockMethod.ISBTM IsbtmLayer;

		private double _shearVelocity;

		private double _totalTransportRate;

		private BindableArrayWrapper<double> _grainDiameter;

		private BindableArrayWrapper<double> _surfaceCdf;

		private double[] _surfaceCdfOutput;

		private BindableArrayWrapper<double> _transportCdf;

		private double[] _transportCdfOutput;
		private double _surfaceDMin;
		private double _surfaceDMax;
		private double _surfaceD50;
		private double _surfaceDg;
		private double _surfaceSigmaG;
		private double _surfaceFs;
		private double _transportDMin;
		private double _transportDMax;
		private double _transportD50;
		private double _transportDg;
		private double _transportSigmaG;
		private double _transportFs;
		private double _armorRatio;
		private int _maxIterations;
		private int _iterations;
		private double _tolerance;
		private double _dres;
		private double _maxFres;
		private double _fsres;
		private double _ures;
		private double _qres;

		public bool IsISBTMMode
		{
			get
			{
				return _isISBTMMode;
			}
			set
			{
				if (SetProperty(ref _isISBTMMode, value, "IsISBTMMode"))
				{
					UpdateVisibilityProperties();
					RefreshOutputs();
				}
			}
		}

		public double ShearVelocity
		{
			get
			{
				if (!_isISBTMMode)
				{
					return SbtmLayer.ShearVelocity;
				}
				return _shearVelocity;
			}
			set
			{
				if (!_isISBTMMode && SetProperty(SbtmLayer.ShearVelocity, value, SbtmLayer, (WilcockMethod.SBTM m, double v) =>
				{
					m.ShearVelocity = v;
				}, "ShearVelocity"))
				{
					RefreshOutputs();
				}
			}
		}

		public double TotalTransportRate
		{
			get
			{
				if (!_isISBTMMode)
				{
					return _totalTransportRate;
				}
				return IsbtmLayer.TotalTransportRate;
			}
			set
			{
				if (_isISBTMMode && SetProperty(IsbtmLayer.TotalTransportRate, value, IsbtmLayer, (WilcockMethod.ISBTM m, double v) =>
				{
					m.TotalTransportRate = v;
				}, "TotalTransportRate"))
				{
					RefreshOutputs();
				}
			}
		}

		public double WaterDensity
		{
			get
			{
				return SbtmLayer.WaterDensity;
			}
			set
			{
				if (!_isISBTMMode && SetProperty(SbtmLayer.WaterDensity, value, SbtmLayer, (WilcockMethod.SBTM m, double v) =>
				{
					m.WaterDensity = v;
				}, "WaterDensity"))
				{
					RefreshOutputs();
				}
			}
		}

		public double SedimentDensity
		{
			get
			{
				return SbtmLayer.SedimentDensity;
			}
			set
			{
				if (!_isISBTMMode && SetProperty(SbtmLayer.SedimentDensity, value, SbtmLayer, (WilcockMethod.SBTM m, double v) =>
				{
					m.SedimentDensity = v;
				}, "SedimentDensity"))
				{
					RefreshOutputs();
				}
			}
		}

		public BindableArrayWrapper<double> GrainDiameter
		{
			get
			{
				if (_grainDiameter == null)
				{
					if (SbtmLayer.GrainDiameter == null)
					{
						SbtmLayer.GrainDiameter = new double[0];
					}
					if (IsbtmLayer.GrainDiameter == null)
					{
						IsbtmLayer.GrainDiameter = new double[0];
					}
					double[] initialValues = (_isISBTMMode ? IsbtmLayer.GrainDiameter : SbtmLayer.GrainDiameter);
					_grainDiameter = new BindableArrayWrapper<double>(initialValues, (double[] da) =>
					{
						if (_isISBTMMode)
						{
							IsbtmLayer.GrainDiameter = da;
						}
						else
						{
							SbtmLayer.GrainDiameter = da;
						}
						return da;
					}, 0.0, isFixedSize: false);
					_grainDiameter.CollectionChanged += InputArray_CollectionChanged;
				}
				return _grainDiameter;
			}
		}

		public BindableArrayWrapper<double> SurfaceCdf
		{
			get
			{
				if (_isISBTMMode)
				{
					if (_surfaceCdfOutput == null)
					{
						_surfaceCdfOutput = new double[0];
					}
					return new BindableArrayWrapper<double>(_surfaceCdfOutput, null, 0.0);
				}
				if (_surfaceCdf == null)
				{
					if (SbtmLayer.SurfaceCdf == null)
					{
						SbtmLayer.SurfaceCdf = new double[0];
					}
					_surfaceCdf = new BindableArrayWrapper<double>(SbtmLayer.SurfaceCdf, (double[] da) =>
					{
						SbtmLayer.SurfaceCdf = da;
						return da;
					}, 0.0, isFixedSize: false);
					_surfaceCdf.CollectionChanged += InputArray_CollectionChanged;
				}
				return _surfaceCdf;
			}
		}

		public BindableArrayWrapper<double> TransportCdf
		{
			get
			{
				if (_isISBTMMode)
				{
					if (_transportCdf == null)
					{
						if (IsbtmLayer.TransportCdf == null)
						{
							IsbtmLayer.TransportCdf = new double[0];
						}
						_transportCdf = new BindableArrayWrapper<double>(IsbtmLayer.TransportCdf, (double[] da) =>
						{
							IsbtmLayer.TransportCdf = da;
							return da;
						}, 0.0, isFixedSize: false);
						_transportCdf.CollectionChanged += InputArray_CollectionChanged;
					}
					return _transportCdf;
				}
				if (_transportCdfOutput == null)
				{
					_transportCdfOutput = new double[0];
				}
				return new BindableArrayWrapper<double>(_transportCdfOutput, null, 0.0);
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool ArmorRatioVisible
		{
			get
			{
				return _armorRatioVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_armorRatioVisible, value))
				{
					OnPropertyChanging("ArmorRatioVisible");
					_armorRatioVisible = value;
					OnPropertyChanged("ArmorRatioVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool MaxIterationsVisible
		{
			get
			{
				return _maxIterationsVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_maxIterationsVisible, value))
				{
					OnPropertyChanging("MaxIterationsVisible");
					_maxIterationsVisible = value;
					OnPropertyChanged("MaxIterationsVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool IterationsVisible
		{
			get
			{
				return _iterationsVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_iterationsVisible, value))
				{
					OnPropertyChanging("IterationsVisible");
					_iterationsVisible = value;
					OnPropertyChanged("IterationsVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool ConvergenceToleranceVisible
		{
			get
			{
				return _convergenceToleranceVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_convergenceToleranceVisible, value))
				{
					OnPropertyChanging("ConvergenceToleranceVisible");
					_convergenceToleranceVisible = value;
					OnPropertyChanged("ConvergenceToleranceVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool RelativeDifferenceSurfaceDgVisible
		{
			get
			{
				return _relativeDifferenceSurfaceDgVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_relativeDifferenceSurfaceDgVisible, value))
				{
					OnPropertyChanging("RelativeDifferenceSurfaceDgVisible");
					_relativeDifferenceSurfaceDgVisible = value;
					OnPropertyChanged("RelativeDifferenceSurfaceDgVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool MaxAbsErrorFractionVisible
		{
			get
			{
				return _maxAbsErrorFractionVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_maxAbsErrorFractionVisible, value))
				{
					OnPropertyChanging("MaxAbsErrorFractionVisible");
					_maxAbsErrorFractionVisible = value;
					OnPropertyChanged("MaxAbsErrorFractionVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool MaxAbsErrorSandFractionVisible
		{
			get
			{
				return _maxAbsErrorSandFractionVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_maxAbsErrorSandFractionVisible, value))
				{
					OnPropertyChanging("MaxAbsErrorSandFractionVisible");
					_maxAbsErrorSandFractionVisible = value;
					OnPropertyChanged("MaxAbsErrorSandFractionVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool RelativeDifferenceShearVelocityVisible
		{
			get
			{
				return _relativeDifferenceShearVelocityVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_relativeDifferenceShearVelocityVisible, value))
				{
					OnPropertyChanging("RelativeDifferenceShearVelocityVisible");
					_relativeDifferenceShearVelocityVisible = value;
					OnPropertyChanged("RelativeDifferenceShearVelocityVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool RelativeDifferenceTransportRateVisible
		{
			get
			{
				return _relativeDifferenceTransportRateVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_relativeDifferenceTransportRateVisible, value))
				{
					OnPropertyChanging("RelativeDifferenceTransportRateVisible");
					_relativeDifferenceTransportRateVisible = value;
					OnPropertyChanged("RelativeDifferenceTransportRateVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool ShearVelocityAsInputVisible
		{
			get
			{
				return _shearVelocityAsInputVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_shearVelocityAsInputVisible, value))
				{
					OnPropertyChanging("ShearVelocityAsInputVisible");
					_shearVelocityAsInputVisible = value;
					OnPropertyChanged("ShearVelocityAsInputVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool TotalTransportRateAsInputVisible
		{
			get
			{
				return _totalTransportRateAsInputVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_totalTransportRateAsInputVisible, value))
				{
					OnPropertyChanging("TotalTransportRateAsInputVisible");
					_totalTransportRateAsInputVisible = value;
					OnPropertyChanged("TotalTransportRateAsInputVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool WaterDensityVisible
		{
			get
			{
				return _waterDensityVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_waterDensityVisible, value))
				{
					OnPropertyChanging("WaterDensityVisible");
					_waterDensityVisible = value;
					OnPropertyChanged("WaterDensityVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public bool SedimentDensityVisible
		{
			get
			{
				return _sedimentDensityVisible;
			}
			set
			{
				if (!EqualityComparer<bool>.Default.Equals(_sedimentDensityVisible, value))
				{
					OnPropertyChanging("SedimentDensityVisible");
					_sedimentDensityVisible = value;
					OnPropertyChanged("SedimentDensityVisible");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDMin
		{
			get
			{
				return _surfaceDMin;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceDMin, value))
				{
					OnPropertyChanging("SurfaceDMin");
					_surfaceDMin = value;
					OnPropertyChanged("SurfaceDMin");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDMax
		{
			get
			{
				return _surfaceDMax;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceDMax, value))
				{
					OnPropertyChanging("SurfaceDMax");
					_surfaceDMax = value;
					OnPropertyChanged("SurfaceDMax");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceD50
		{
			get
			{
				return _surfaceD50;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceD50, value))
				{
					OnPropertyChanging("SurfaceD50");
					_surfaceD50 = value;
					OnPropertyChanged("SurfaceD50");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDg
		{
			get
			{
				return _surfaceDg;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceDg, value))
				{
					OnPropertyChanging("SurfaceDg");
					_surfaceDg = value;
					OnPropertyChanged("SurfaceDg");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceSigmaG
		{
			get
			{
				return _surfaceSigmaG;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceSigmaG, value))
				{
					OnPropertyChanging("SurfaceSigmaG");
					_surfaceSigmaG = value;
					OnPropertyChanged("SurfaceSigmaG");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceFs
		{
			get
			{
				return _surfaceFs;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceFs, value))
				{
					OnPropertyChanging("SurfaceFs");
					_surfaceFs = value;
					OnPropertyChanged("SurfaceFs");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDMin
		{
			get
			{
				return _transportDMin;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportDMin, value))
				{
					OnPropertyChanging("TransportDMin");
					_transportDMin = value;
					OnPropertyChanged("TransportDMin");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDMax
		{
			get
			{
				return _transportDMax;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportDMax, value))
				{
					OnPropertyChanging("TransportDMax");
					_transportDMax = value;
					OnPropertyChanged("TransportDMax");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportD50
		{
			get
			{
				return _transportD50;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportD50, value))
				{
					OnPropertyChanging("TransportD50");
					_transportD50 = value;
					OnPropertyChanged("TransportD50");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDg
		{
			get
			{
				return _transportDg;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportDg, value))
				{
					OnPropertyChanging("TransportDg");
					_transportDg = value;
					OnPropertyChanged("TransportDg");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportSigmaG
		{
			get
			{
				return _transportSigmaG;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportSigmaG, value))
				{
					OnPropertyChanging("TransportSigmaG");
					_transportSigmaG = value;
					OnPropertyChanged("TransportSigmaG");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportFs
		{
			get
			{
				return _transportFs;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportFs, value))
				{
					OnPropertyChanging("TransportFs");
					_transportFs = value;
					OnPropertyChanged("TransportFs");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double ArmorRatio
		{
			get
			{
				return _armorRatio;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_armorRatio, value))
				{
					OnPropertyChanging("ArmorRatio");
					_armorRatio = value;
					OnPropertyChanged("ArmorRatio");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public int MaxIterations
		{
			get
			{
				return _maxIterations;
			}
			set
			{
				if (!EqualityComparer<int>.Default.Equals(_maxIterations, value))
				{
					OnPropertyChanging("MaxIterations");
					_maxIterations = value;
					OnPropertyChanged("MaxIterations");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public int Iterations
		{
			get
			{
				return _iterations;
			}
			set
			{
				if (!EqualityComparer<int>.Default.Equals(_iterations, value))
				{
					OnPropertyChanging("Iterations");
					_iterations = value;
					OnPropertyChanged("Iterations");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Tolerance
		{
			get
			{
				return _tolerance;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_tolerance, value))
				{
					OnPropertyChanging("Tolerance");
					_tolerance = value;
					OnPropertyChanged("Tolerance");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Dres
		{
			get
			{
				return _dres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_dres, value))
				{
					OnPropertyChanging("Dres");
					_dres = value;
					OnPropertyChanged("Dres");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double MaxFres
		{
			get
			{
				return _maxFres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_maxFres, value))
				{
					OnPropertyChanging("MaxFres");
					_maxFres = value;
					OnPropertyChanged("MaxFres");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Fsres
		{
			get
			{
				return _fsres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_fsres, value))
				{
					OnPropertyChanging("Fsres");
					_fsres = value;
					OnPropertyChanged("Fsres");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Ures
		{
			get
			{
				return _ures;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_ures, value))
				{
					OnPropertyChanging("Ures");
					_ures = value;
					OnPropertyChanged("Ures");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Qres
		{
			get
			{
				return _qres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_qres, value))
				{
					OnPropertyChanging("Qres");
					_qres = value;
					OnPropertyChanged("Qres");
				}
			}
		}

		public ObservableMergedSBTM(WilcockMethod.SBTM sbtmLayer, WilcockMethod.ISBTM isbtmLayer)
		{
			SbtmLayer = sbtmLayer ?? throw new ArgumentNullException("sbtmLayer");
			IsbtmLayer = isbtmLayer ?? throw new ArgumentNullException("isbtmLayer");
			UpdateVisibilityProperties();
			RefreshOutputs();
		}

		private void InputArray_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			RefreshOutputs();
		}

		private void UpdateVisibilityProperties()
		{
			ArmorRatioVisible = _isISBTMMode;
			MaxIterationsVisible = _isISBTMMode;
			IterationsVisible = _isISBTMMode;
			ConvergenceToleranceVisible = _isISBTMMode;
			RelativeDifferenceSurfaceDgVisible = _isISBTMMode;
			MaxAbsErrorFractionVisible = _isISBTMMode;
			MaxAbsErrorSandFractionVisible = _isISBTMMode;
			RelativeDifferenceShearVelocityVisible = _isISBTMMode;
			RelativeDifferenceTransportRateVisible = _isISBTMMode;
			ShearVelocityAsInputVisible = !_isISBTMMode;
			TotalTransportRateAsInputVisible = _isISBTMMode;
			WaterDensityVisible = !_isISBTMMode;
			SedimentDensityVisible = !_isISBTMMode;
		}

		private void RefreshOutputs()
		{
			if (_isISBTMMode)
			{
				RefreshISBTMOutputs();
			}
			else
			{
				RefreshSBTMOutputs();
			}
		}

		private void RefreshSBTMOutputs()
		{
			BindableArrayWrapper<double> grainDiameter = GrainDiameter;
			if (grainDiameter == null || grainDiameter.Count != 0)
			{
				BindableArrayWrapper<double> surfaceCdf = _surfaceCdf;
				if (surfaceCdf == null || surfaceCdf.Count != 0)
				{
					SBTMResults sBTMResults = WilcockSbtmCalculator.ComputeSbtm(SbtmLayer);
					double grainDiameterSbtmLayerTerm = (((SbtmLayer.GrainDiameter?.Length ?? 0) > 0) ? SbtmLayer.GrainDiameter.Min() : 0.0);
					double grainDiameterSbtmLayerTerm2 = (((SbtmLayer.GrainDiameter?.Length ?? 0) > 0) ? SbtmLayer.GrainDiameter.Max() : 0.0);
					_totalTransportRate = sBTMResults.TransportRate;
					OnPropertyChanged("TotalTransportRate");
					SurfaceDMin = grainDiameterSbtmLayerTerm;
					SurfaceDMax = grainDiameterSbtmLayerTerm2;
					SurfaceD50 = sBTMResults.Ds50;
					SurfaceDg = sBTMResults.Dsm;
					SurfaceSigmaG = sBTMResults.SSig;
					SurfaceFs = sBTMResults.SFs;
					TransportDMin = grainDiameterSbtmLayerTerm;
					TransportDMax = grainDiameterSbtmLayerTerm2;
					TransportD50 = sBTMResults.Db50;
					TransportDg = sBTMResults.Dbm;
					TransportSigmaG = sBTMResults.BSig;
					TransportFs = sBTMResults.BFs;
					_transportCdfOutput = sBTMResults.CumulativeBedloadDistribution;
					OnPropertyChanged("TransportCdf");
					ArmorRatio = 0.0;
					MaxIterations = 0;
					Iterations = 0;
					Tolerance = 0.0;
					Dres = 0.0;
					MaxFres = 0.0;
					Fsres = 0.0;
					Ures = 0.0;
					Qres = 0.0;
				}
			}
		}

		private void RefreshISBTMOutputs()
		{
			BindableArrayWrapper<double> grainDiameter = GrainDiameter;
			if (grainDiameter == null || grainDiameter.Count != 0)
			{
				BindableArrayWrapper<double> transportCdf = _transportCdf;
				if (transportCdf == null || transportCdf.Count != 0)
				{
					ISBTMResults iSBTMResults = WilcockIsbtmCalculator.ComputeIsbtm(IsbtmLayer);
					double grainDiameterIsbtmLayerTerm = (((IsbtmLayer.GrainDiameter?.Length ?? 0) > 0) ? IsbtmLayer.GrainDiameter.Min() : 0.0);
					double grainDiameterIsbtmLayerTerm2 = (((IsbtmLayer.GrainDiameter?.Length ?? 0) > 0) ? IsbtmLayer.GrainDiameter.Max() : 0.0);
					_shearVelocity = iSBTMResults.UStar;
					OnPropertyChanged("ShearVelocity");
					SurfaceDMin = grainDiameterIsbtmLayerTerm;
					SurfaceDMax = grainDiameterIsbtmLayerTerm2;
					SurfaceD50 = iSBTMResults.Ds50;
					SurfaceDg = iSBTMResults.Dsm;
					SurfaceSigmaG = iSBTMResults.SSig;
					SurfaceFs = iSBTMResults.SFs;
					TransportDMin = grainDiameterIsbtmLayerTerm;
					TransportDMax = grainDiameterIsbtmLayerTerm2;
					TransportD50 = iSBTMResults.Db50;
					TransportDg = iSBTMResults.Dbm;
					TransportSigmaG = iSBTMResults.BSig;
					TransportFs = iSBTMResults.BFs;
					ArmorRatio = SurfaceD50 / TransportD50;
					MaxIterations = iSBTMResults.MaxIterations;
					Iterations = iSBTMResults.Iterations;
					Tolerance = iSBTMResults.Tolerance;
					Dres = iSBTMResults.Dres;
					MaxFres = iSBTMResults.MaxFres;
					Fsres = iSBTMResults.Fsres;
					Ures = iSBTMResults.Ures;
					Qres = iSBTMResults.Qres;
					_surfaceCdfOutput = iSBTMResults.SurfaceCdf;
					OnPropertyChanged("SurfaceCdf");
				}
			}
		}
	}

	public class ObservableISBTM : ObservableObject
	{
		public readonly WilcockMethod.ISBTM Layer;

		private BindableArrayWrapper<double> _grainDiameter;

		private BindableArrayWrapper<double> _transportCdf;
		private double _shearVelocity;
		private double _surfaceDMin;
		private double _surfaceDMax;
		private double _surfaceD50;
		private double _surfaceDg;
		private double _surfaceSigmaG;
		private double _surfaceFs;
		private double _transportDMin;
		private double _transportDMax;
		private double _transportD50;
		private double _transportDg;
		private double _transportSigmaG;
		private double _transportFs;
		private double[] _surfaceCdf;
		private double _armorRatio;
		private int _maxIterations;
		private int _iterations;
		private double _tolerance;
		private double _dres;
		private double _maxFres;
		private double _fsres;
		private double _ures;
		private double _qres;

		public double TotalTransportRate
		{
			get
			{
				return Layer.TotalTransportRate;
			}
			set
			{
				if (SetProperty(Layer.TotalTransportRate, value, Layer, (WilcockMethod.ISBTM m, double v) =>
				{
					m.TotalTransportRate = v;
				}, "TotalTransportRate"))
				{
					RefreshOutputs();
				}
			}
		}

		public BindableArrayWrapper<double> GrainDiameter
		{
			get
			{
				if (_grainDiameter == null)
				{
					_grainDiameter = new BindableArrayWrapper<double>(Layer.GrainDiameter, (double[] da) => Layer.GrainDiameter = da, 0.0, isFixedSize: false);
					_grainDiameter.CollectionChanged += InputArray_CollectionChanged;
				}
				return _grainDiameter;
			}
		}

		public BindableArrayWrapper<double> TransportCdf
		{
			get
			{
				if (_transportCdf == null)
				{
					_transportCdf = new BindableArrayWrapper<double>(Layer.TransportCdf, (double[] da) => Layer.TransportCdf = da, 0.0, isFixedSize: false);
					_transportCdf.CollectionChanged += InputArray_CollectionChanged;
				}
				return _transportCdf;
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double ShearVelocity
		{
			get
			{
				return _shearVelocity;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_shearVelocity, value))
				{
					OnPropertyChanging("ShearVelocity");
					_shearVelocity = value;
					OnPropertyChanged("ShearVelocity");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDMin
		{
			get
			{
				return _surfaceDMin;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceDMin, value))
				{
					OnPropertyChanging("SurfaceDMin");
					_surfaceDMin = value;
					OnPropertyChanged("SurfaceDMin");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDMax
		{
			get
			{
				return _surfaceDMax;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceDMax, value))
				{
					OnPropertyChanging("SurfaceDMax");
					_surfaceDMax = value;
					OnPropertyChanged("SurfaceDMax");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceD50
		{
			get
			{
				return _surfaceD50;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceD50, value))
				{
					OnPropertyChanging("SurfaceD50");
					_surfaceD50 = value;
					OnPropertyChanged("SurfaceD50");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceDg
		{
			get
			{
				return _surfaceDg;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceDg, value))
				{
					OnPropertyChanging("SurfaceDg");
					_surfaceDg = value;
					OnPropertyChanged("SurfaceDg");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceSigmaG
		{
			get
			{
				return _surfaceSigmaG;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceSigmaG, value))
				{
					OnPropertyChanging("SurfaceSigmaG");
					_surfaceSigmaG = value;
					OnPropertyChanged("SurfaceSigmaG");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SurfaceFs
		{
			get
			{
				return _surfaceFs;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_surfaceFs, value))
				{
					OnPropertyChanging("SurfaceFs");
					_surfaceFs = value;
					OnPropertyChanged("SurfaceFs");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDMin
		{
			get
			{
				return _transportDMin;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportDMin, value))
				{
					OnPropertyChanging("TransportDMin");
					_transportDMin = value;
					OnPropertyChanged("TransportDMin");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDMax
		{
			get
			{
				return _transportDMax;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportDMax, value))
				{
					OnPropertyChanging("TransportDMax");
					_transportDMax = value;
					OnPropertyChanged("TransportDMax");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportD50
		{
			get
			{
				return _transportD50;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportD50, value))
				{
					OnPropertyChanging("TransportD50");
					_transportD50 = value;
					OnPropertyChanged("TransportD50");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportDg
		{
			get
			{
				return _transportDg;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportDg, value))
				{
					OnPropertyChanging("TransportDg");
					_transportDg = value;
					OnPropertyChanged("TransportDg");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportSigmaG
		{
			get
			{
				return _transportSigmaG;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportSigmaG, value))
				{
					OnPropertyChanging("TransportSigmaG");
					_transportSigmaG = value;
					OnPropertyChanged("TransportSigmaG");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double TransportFs
		{
			get
			{
				return _transportFs;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_transportFs, value))
				{
					OnPropertyChanging("TransportFs");
					_transportFs = value;
					OnPropertyChanged("TransportFs");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] SurfaceCdf
		{
			get
			{
				return _surfaceCdf;
			}
			[MemberNotNull("_surfaceCdf")]
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(_surfaceCdf, value))
				{
					OnPropertyChanging("SurfaceCdf");
					_surfaceCdf = value;
					OnPropertyChanged("SurfaceCdf");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double ArmorRatio
		{
			get
			{
				return _armorRatio;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_armorRatio, value))
				{
					OnPropertyChanging("ArmorRatio");
					_armorRatio = value;
					OnPropertyChanged("ArmorRatio");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public int MaxIterations
		{
			get
			{
				return _maxIterations;
			}
			set
			{
				if (!EqualityComparer<int>.Default.Equals(_maxIterations, value))
				{
					OnPropertyChanging("MaxIterations");
					_maxIterations = value;
					OnPropertyChanged("MaxIterations");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public int Iterations
		{
			get
			{
				return _iterations;
			}
			set
			{
				if (!EqualityComparer<int>.Default.Equals(_iterations, value))
				{
					OnPropertyChanging("Iterations");
					_iterations = value;
					OnPropertyChanged("Iterations");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Tolerance
		{
			get
			{
				return _tolerance;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_tolerance, value))
				{
					OnPropertyChanging("Tolerance");
					_tolerance = value;
					OnPropertyChanged("Tolerance");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Dres
		{
			get
			{
				return _dres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_dres, value))
				{
					OnPropertyChanging("Dres");
					_dres = value;
					OnPropertyChanged("Dres");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double MaxFres
		{
			get
			{
				return _maxFres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_maxFres, value))
				{
					OnPropertyChanging("MaxFres");
					_maxFres = value;
					OnPropertyChanged("MaxFres");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Fsres
		{
			get
			{
				return _fsres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_fsres, value))
				{
					OnPropertyChanging("Fsres");
					_fsres = value;
					OnPropertyChanged("Fsres");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Ures
		{
			get
			{
				return _ures;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_ures, value))
				{
					OnPropertyChanging("Ures");
					_ures = value;
					OnPropertyChanged("Ures");
				}
			}
		}

		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double Qres
		{
			get
			{
				return _qres;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(_qres, value))
				{
					OnPropertyChanging("Qres");
					_qres = value;
					OnPropertyChanged("Qres");
				}
			}
		}

		public ObservableISBTM(WilcockMethod.ISBTM layer)
		{
			Layer = layer;
			RefreshOutputs();
		}

		private void InputArray_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			RefreshOutputs();
		}

		private void RefreshOutputs()
		{
			if (GrainDiameter.Count != 0 && TransportCdf.Count != 0)
			{
				ISBTMResults iSBTMResults = WilcockIsbtmCalculator.ComputeIsbtm(Layer);
				double grainDiameterLayerTerm = (((Layer.GrainDiameter?.Length ?? 0) > 0) ? Layer.GrainDiameter.Min() : 0.0);
				double grainDiameterLayerTerm2 = (((Layer.GrainDiameter?.Length ?? 0) > 0) ? Layer.GrainDiameter.Max() : 0.0);
				ShearVelocity = iSBTMResults.UStar;
				SurfaceDMin = grainDiameterLayerTerm;
				SurfaceDMax = grainDiameterLayerTerm2;
				SurfaceD50 = iSBTMResults.Ds50;
				SurfaceDg = iSBTMResults.Dsm;
				SurfaceSigmaG = iSBTMResults.SSig;
				SurfaceFs = iSBTMResults.SFs;
				TransportDMin = grainDiameterLayerTerm;
				TransportDMax = grainDiameterLayerTerm2;
				TransportD50 = iSBTMResults.Db50;
				TransportDg = iSBTMResults.Dbm;
				TransportSigmaG = iSBTMResults.BSig;
				TransportFs = iSBTMResults.BFs;
				ArmorRatio = SurfaceD50 / TransportD50;
				MaxIterations = iSBTMResults.MaxIterations;
				Iterations = iSBTMResults.Iterations;
				Tolerance = iSBTMResults.Tolerance;
				Dres = iSBTMResults.Dres;
				MaxFres = iSBTMResults.MaxFres;
				Fsres = iSBTMResults.Fsres;
				Ures = iSBTMResults.Ures;
				Qres = iSBTMResults.Qres;
				SurfaceCdf = iSBTMResults.SurfaceCdf;
			}
		}
	}

	public class ObservableSTAB : ObservableObject
	{
		public readonly WilcockMethod.STAB Layer;

		private BindableArrayWrapper<double> _waterDischarges;

		private BindableArrayWrapper<double> _sedimentSupplyRates;

		private BindableArrayWrapper<double> _grainDiameter;

		public BindableArrayWrapper<double> WaterDischarges
		{
			get
			{
				if (_waterDischarges == null)
				{
					_waterDischarges = new BindableArrayWrapper<double>(Layer.WaterDischarges, (double[] da) => Layer.WaterDischarges = da, 0.0, isFixedSize: false);
					_waterDischarges.CollectionChanged += InputArray_CollectionChanged;
				}
				return _waterDischarges;
			}
		}

		public BindableArrayWrapper<double> SedimentSupplyRates
		{
			get
			{
				if (_sedimentSupplyRates == null)
				{
					_sedimentSupplyRates = new BindableArrayWrapper<double>(Layer.SedimentSupplyRates, (double[] da) => Layer.SedimentSupplyRates = da, 0.0, isFixedSize: false);
					_sedimentSupplyRates.CollectionChanged += InputArray_CollectionChanged;
				}
				return _sedimentSupplyRates;
			}
		}

		public double BMin
		{
			get
			{
				return Layer.BMin;
			}
			set
			{
				if (SetProperty(Layer.BMin, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.BMin = v;
				}, "BMin"))
				{
					RefreshOutputs();
				}
			}
		}

		public double BMax
		{
			get
			{
				return Layer.BMax;
			}
			set
			{
				if (SetProperty(Layer.BMax, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.BMax = v;
				}, "BMax"))
				{
					RefreshOutputs();
				}
			}
		}

		public double Z
		{
			get
			{
				return Layer.Z;
			}
			set
			{
				if (SetProperty(Layer.Z, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.Z = v;
				}, "Z"))
				{
					RefreshOutputs();
				}
			}
		}

		public double Ns
		{
			get
			{
				return Layer.Ns;
			}
			set
			{
				if (SetProperty(Layer.Ns, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.Ns = v;
				}, "Ns"))
				{
					RefreshOutputs();
				}
			}
		}

		public double Nspec
		{
			get
			{
				return Layer.Nspec;
			}
			set
			{
				if (SetProperty(Layer.Nspec, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.Nspec = v;
				}, "Nspec"))
				{
					RefreshOutputs();
				}
			}
		}

		public bool UseThresholdSlope
		{
			get
			{
				return Layer.UseThresholdSlope;
			}
			set
			{
				if (SetProperty(Layer.UseThresholdSlope, value, Layer, (WilcockMethod.STAB m, bool v) =>
				{
					m.UseThresholdSlope = v;
				}, "UseThresholdSlope"))
				{
					RefreshOutputs();
				}
			}
		}

		public double TcTauStarC
		{
			get
			{
				return Layer.TcTauStarC;
			}
			set
			{
				if (SetProperty(Layer.TcTauStarC, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.TcTauStarC = v;
				}, "TcTauStarC"))
				{
					RefreshOutputs();
				}
			}
		}

		public double TcD50
		{
			get
			{
				return Layer.TcD50;
			}
			set
			{
				if (SetProperty(Layer.TcD50, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.TcD50 = v;
				}, "TcD50"))
				{
					RefreshOutputs();
				}
			}
		}

		public double TcD84
		{
			get
			{
				return Layer.TcD84;
			}
			set
			{
				if (SetProperty(Layer.TcD84, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.TcD84 = v;
				}, "TcD84"))
				{
					RefreshOutputs();
				}
			}
		}

		public double TcM
		{
			get
			{
				return Layer.TcM;
			}
			set
			{
				if (SetProperty(Layer.TcM, value, Layer, (WilcockMethod.STAB m, double v) =>
				{
					m.TcM = v;
				}, "TcM"))
				{
					RefreshOutputs();
				}
			}
		}

		public BindableArrayWrapper<double> GrainDiameter
		{
			get
			{
				if (_grainDiameter == null)
				{
					_grainDiameter = new BindableArrayWrapper<double>(Layer.GrainDiameter, (double[] da) => Layer.GrainDiameter = da, 0.0, isFixedSize: false);
					_grainDiameter.CollectionChanged += InputArray_CollectionChanged;
				}
				return _grainDiameter;
			}
		}

		public ObservableCollection<BindableArrayWrapper<double>> TransportGrainSizes { get; private set; }
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] DMin
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("DMin");
					field = value;
					OnPropertyChanged("DMin");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] DMax
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("DMax");
					field = value;
					OnPropertyChanged("DMax");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] D50
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("D50");
					field = value;
					OnPropertyChanged("D50");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] Dg
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Dg");
					field = value;
					OnPropertyChanged("Dg");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] SigmaG
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("SigmaG");
					field = value;
					OnPropertyChanged("SigmaG");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] Fs
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Fs");
					field = value;
					OnPropertyChanged("Fs");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] B
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("B");
					field = value;
					OnPropertyChanged("B");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] QT
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("QT");
					field = value;
					OnPropertyChanged("QT");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] UStar
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("UStar");
					field = value;
					OnPropertyChanged("UStar");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] Dsm
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Dsm");
					field = value;
					OnPropertyChanged("Dsm");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] Ds50Array
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Ds50Array");
					field = value;
					OnPropertyChanged("Ds50Array");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] Gamma
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Gamma");
					field = value;
					OnPropertyChanged("Gamma");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] Ds84
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Ds84");
					field = value;
					OnPropertyChanged("Ds84");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] ND
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("ND");
					field = value;
					OnPropertyChanged("ND");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TB
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TB");
					field = value;
					OnPropertyChanged("TB");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] H
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("H");
					field = value;
					OnPropertyChanged("H");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] S
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("S");
					field = value;
					OnPropertyChanged("S");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] NVP
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("NVP");
					field = value;
					OnPropertyChanged("NVP");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] NC
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("NC");
					field = value;
					OnPropertyChanged("NC");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] HSpecN
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("HSpecN");
					field = value;
					OnPropertyChanged("HSpecN");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] SSpecN
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("SSpecN");
					field = value;
					OnPropertyChanged("SSpecN");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] F
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("F");
					field = value;
					OnPropertyChanged("F");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] RelativeDepth
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("RelativeDepth");
					field = value;
					OnPropertyChanged("RelativeDepth");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] SurfaceGSDD
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceGSDD");
					field = value;
					OnPropertyChanged("SurfaceGSDD");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] SurfaceGSDFi
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("SurfaceGSDFi");
					field = value;
					OnPropertyChanged("SurfaceGSDFi");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] QTKgPerHr
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("QTKgPerHr");
					field = value;
					OnPropertyChanged("QTKgPerHr");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[] Cutey
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[]>.Default.Equals(field, value))
				{
					OnPropertyChanging("Cutey");
					field = value;
					OnPropertyChanged("Cutey");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double BMean
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("BMean");
					field = value;
					OnPropertyChanged("BMean");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TUStar
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TUStar");
					field = value;
					OnPropertyChanged("TUStar");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TDsm
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TDsm");
					field = value;
					OnPropertyChanged("TDsm");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TDs50
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TDs50");
					field = value;
					OnPropertyChanged("TDs50");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TGamma
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TGamma");
					field = value;
					OnPropertyChanged("TGamma");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TDs84
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TDs84");
					field = value;
					OnPropertyChanged("TDs84");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TND
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TND");
					field = value;
					OnPropertyChanged("TND");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TTB
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TTB");
					field = value;
					OnPropertyChanged("TTB");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TH
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TH");
					field = value;
					OnPropertyChanged("TH");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TS
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TS");
					field = value;
					OnPropertyChanged("TS");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TNVP
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TNVP");
					field = value;
					OnPropertyChanged("TNVP");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TNC
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TNC");
					field = value;
					OnPropertyChanged("TNC");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double[][] TcS
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double[][]>.Default.Equals(field, value))
				{
					OnPropertyChanging("TcS");
					field = value;
					OnPropertyChanged("TcS");
				}
			}
		}

		public ObservableSTAB(WilcockMethod.STAB layer)
		{
			Layer = layer;
			InitializeTransportGrainSizes();
			RefreshOutputs();
		}

		private void InitializeTransportGrainSizes()
		{
			TransportGrainSizes = new ObservableCollection<BindableArrayWrapper<double>>();
			if (Layer.TransportGrainSizes != null)
			{
				for (int i = 0; i < Layer.TransportGrainSizes.Length; i++)
				{
					int idx = i;
					BindableArrayWrapper<double> bindableArrayWrapper = new BindableArrayWrapper<double>(Layer.TransportGrainSizes[idx], (double[] da) =>
					{
						Layer.TransportGrainSizes[idx] = da;
						RefreshOutputs();
						return da;
					}, 0.0, isFixedSize: false);
					bindableArrayWrapper.CollectionChanged += InputArray_CollectionChanged;
					TransportGrainSizes.Add(bindableArrayWrapper);
				}
			}
			TransportGrainSizes.CollectionChanged += InputArray_CollectionChanged;
		}

		private void InputArray_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			RefreshOutputs();
		}

		private void RefreshOutputs()
		{
			BindableArrayWrapper<double> grainDiameter = GrainDiameter;
			if (grainDiameter != null && grainDiameter.Count == 0)
			{
				return;
			}
			ObservableCollection<BindableArrayWrapper<double>> transportGrainSizes = TransportGrainSizes;
			if (transportGrainSizes == null || transportGrainSizes.Count != 0)
			{
				if (TransportGrainSizes == null)
				{
					InitializeTransportGrainSizes();
				}
				StabResults stabResults = WilcockStabCalculator.ComputeStab(Layer);
				DMin = stabResults.DMin;
				DMax = stabResults.DMax;
				D50 = stabResults.D50;
				Dg = stabResults.Dg;
				SigmaG = stabResults.SigmaG;
				Fs = stabResults.Fs;
				B = stabResults.B;
				QT = stabResults.QT;
				UStar = stabResults.UStar;
				Dsm = stabResults.Dsm;
				Ds50Array = stabResults.Ds50Array;
				Gamma = stabResults.Gamma;
				ND = stabResults.ND;
				TB = stabResults.TB;
				H = stabResults.H;
				S = stabResults.S;
				NVP = stabResults.NVP;
				NC = stabResults.NC;
				Ds84 = stabResults.Ds84;
				HSpecN = stabResults.HSpecN;
				SSpecN = stabResults.SSpecN;
				F = stabResults.F;
				RelativeDepth = stabResults.RelativeDepth;
				SurfaceGSDFi = stabResults.GSDAtMeanWidthFi;
				SurfaceGSDD = stabResults.GSDAtMeanWidthD;
				QTKgPerHr = stabResults.QTKgPerHr;
				Cutey = stabResults.Cutey;
				BMean = stabResults.BMean;
				TUStar = stabResults.TUStar;
				TDsm = stabResults.TDsm;
				TDs50 = stabResults.TDs50;
				TGamma = stabResults.TGamma;
				TND = stabResults.TND;
				TTB = stabResults.TTB;
				TH = stabResults.TH;
				TS = stabResults.TS;
				TNVP = stabResults.TNVP;
				TNC = stabResults.TNC;
				TDs84 = stabResults.TDs84;
				TcS = stabResults.TcS;
			}
		}
	}

	public class ObservableSTATE : ObservableObject
	{
		public readonly WilcockMethod.STATE Layer;

		private BindableArrayWrapper<double> _grainDiameter;

		private BindableArrayWrapper<double> _firstTransportCDFGrainSize;

		private BindableArrayWrapper<double> _secondTransportCDFGrainSize;

		private BindableArrayWrapper<double> _transportRates;

		private BindableArrayWrapper<double> _slopes;

		public BindableArrayWrapper<double> GrainDiameter
		{
			get
			{
				if (_grainDiameter == null)
				{
					_grainDiameter = new BindableArrayWrapper<double>(Layer.GrainDiameter, (double[] da) => Layer.GrainDiameter = da, 0.0, isFixedSize: false);
					_grainDiameter.CollectionChanged += InputArray_CollectionChanged;
				}
				return _grainDiameter;
			}
		}

		public BindableArrayWrapper<double> FirstTransportCDFGrainSize
		{
			get
			{
				if (_firstTransportCDFGrainSize == null)
				{
					_firstTransportCDFGrainSize = new BindableArrayWrapper<double>(Layer.FirstTransportCDFGrainSize, (double[] da) => Layer.FirstTransportCDFGrainSize = da, 0.0, isFixedSize: false);
					_firstTransportCDFGrainSize.CollectionChanged += InputArray_CollectionChanged;
				}
				return _firstTransportCDFGrainSize;
			}
		}

		public BindableArrayWrapper<double> SecondTransportCDFGrainSize
		{
			get
			{
				if (_secondTransportCDFGrainSize == null)
				{
					_secondTransportCDFGrainSize = new BindableArrayWrapper<double>(Layer.SecondTransportCDFGrainSize, (double[] da) => Layer.SecondTransportCDFGrainSize = da, 0.0, isFixedSize: false);
					_secondTransportCDFGrainSize.CollectionChanged += InputArray_CollectionChanged;
				}
				return _secondTransportCDFGrainSize;
			}
		}

		public BindableArrayWrapper<double> TransportRates
		{
			get
			{
				if (_transportRates == null)
				{
					_transportRates = new BindableArrayWrapper<double>(Layer.TransportRates, (double[] da) => Layer.TransportRates = da, 0.0, isFixedSize: false);
					_transportRates.CollectionChanged += InputArray_CollectionChanged;
				}
				return _transportRates;
			}
		}

		public BindableArrayWrapper<double> Slopes
		{
			get
			{
				if (_slopes == null)
				{
					_slopes = new BindableArrayWrapper<double>(Layer.Slopes, (double[] da) => Layer.Slopes = da, 0.0, isFixedSize: false);
					_slopes.CollectionChanged += InputArray_CollectionChanged;
				}
				return _slopes;
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double FirstDMin
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("FirstDMin");
					field = value;
					OnPropertyChanged("FirstDMin");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double FirstDMax
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("FirstDMax");
					field = value;
					OnPropertyChanged("FirstDMax");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double FirstD50
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("FirstD50");
					field = value;
					OnPropertyChanged("FirstD50");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double FirstDg
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("FirstDg");
					field = value;
					OnPropertyChanged("FirstDg");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double FirstSigmaG
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("FirstSigmaG");
					field = value;
					OnPropertyChanged("FirstSigmaG");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double FirstFs
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("FirstFs");
					field = value;
					OnPropertyChanged("FirstFs");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SecondDMin
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SecondDMin");
					field = value;
					OnPropertyChanged("SecondDMin");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SecondDMax
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SecondDMax");
					field = value;
					OnPropertyChanged("SecondDMax");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SecondD50
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SecondD50");
					field = value;
					OnPropertyChanged("SecondD50");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SecondDg
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SecondDg");
					field = value;
					OnPropertyChanged("SecondDg");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SecondSigmaG
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SecondSigmaG");
					field = value;
					OnPropertyChanged("SecondSigmaG");
				}
			}
		}
		[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
		[ExcludeFromCodeCoverage]
		public double SecondFs
		{
			get
			{
				return field;
			}
			set
			{
				if (!EqualityComparer<double>.Default.Equals(field, value))
				{
					OnPropertyChanging("SecondFs");
					field = value;
					OnPropertyChanged("SecondFs");
				}
			}
		}

		public ObservableSTATE(WilcockMethod.STATE layer)
		{
			Layer = layer;
			RefreshOutputs();
		}

		private void InputArray_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			RefreshOutputs();
		}

		private void RefreshOutputs()
		{
			if (GrainDiameter.Count != 0 && FirstTransportCDFGrainSize.Count != 0 && SecondTransportCDFGrainSize.Count != 0 && TransportRates.Count != 0 && Slopes.Count != 0)
			{
				StateResults stateResults = WilcockStateCalculator.ComputeState(Layer);
				FirstDMin = stateResults.FirstDMin;
				FirstDMax = stateResults.FirstDMax;
				FirstD50 = stateResults.FirstD50;
				FirstDg = stateResults.FirstDg;
				FirstSigmaG = stateResults.FirstSigmaG;
				FirstFs = stateResults.FirstFs;
				SecondDMin = stateResults.SecondDMin;
				SecondDMax = stateResults.SecondDMax;
				SecondD50 = stateResults.SecondD50;
				SecondDg = stateResults.SecondDg;
				SecondSigmaG = stateResults.SecondSigmaG;
				SecondFs = stateResults.SecondFs;
			}
		}
	}

	public WilcockMethod Model { get; }

	public ObservableSBTM SBTM { get; }

	public ObservableISBTM ISBTM { get; }

	public ObservableMergedSBTM MergedSBTM { get; }

	public ObservableSTAB STAB { get; }

	public ObservableSTATE STATE { get; }

	public ObservableWilcockMethod(WilcockMethod model)
	{
		Model = model ?? throw new ArgumentNullException("model");
		SBTM = new ObservableSBTM(model.Sbtm);
		ISBTM = new ObservableISBTM(model.Isbtm);
		MergedSBTM = new ObservableMergedSBTM(model.Sbtm, model.Isbtm);
		STAB = new ObservableSTAB(model.Stab);
		STATE = new ObservableSTATE(model.State);
	}

	public static string D2OrSci(double v, double sciBelow = 0.01, double sciAbove = 1000000.0)
	{
		if (double.IsNaN(v) || double.IsInfinity(v))
		{
			return "—";
		}
		double computed = Math.Abs(v);
		if (computed == 0.0)
		{
			return "0.00";
		}
		if (!(computed < sciBelow) && !(computed >= sciAbove))
		{
			return v.ToString("0.00");
		}
		return v.ToString("0.00E+0");
	}
}
