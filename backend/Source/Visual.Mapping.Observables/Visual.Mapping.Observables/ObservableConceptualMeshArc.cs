using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Geospatial.Vectors.Topology;
using Utility.Validation;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservableConceptualMeshArc : ObservableArc
{
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? halfConstantSizeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? doubleConstantSizeCommand;

	public override ObservableConceptualMeshTopology Topology => base.Topology as ObservableConceptualMeshTopology;

	[NotifyPropertyChangedForTableColumn("Count")]
	public int PointCount
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.ArcPointCountColumn];
			if (tableRow == null || !tableRow.GetType().Equals(typeof(int)))
			{
				return 0;
			}
			return (int)tableRow;
		}
		set
		{
			SetProperty(PointCount, value, this, (ObservableConceptualMeshArc model, int ordinal) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.ArcPointCountColumn.Name, ordinal);
			}, "PointCount");
		}
	}

	[GreaterThanZero]
	[NotifyPropertyChangedForTableColumn("Bias")]
	public float Bias
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.ArcBiasColumn];
			if (tableRow == null)
			{
				return 0f;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(Bias, value, this, (ObservableConceptualMeshArc model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.ArcBiasColumn.Name, computed);
			}, "Bias");
		}
	}

	[GreaterThanZeroOrNaN]
	[RecommendedBounds(double.Epsilon, 10000000.0, "Cell size outside of recommended bounds.", true)]
	[Float(true)]
	[NotifyPropertyChangedForTableColumn("ConstantSize")]
	public float ConstantSize
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.ConstantSizeColumn];
			if (tableRow == null)
			{
				return ArcMetadata.DefaultConstantSize;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(ConstantSize, value, this, (ObservableConceptualMeshArc model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.ConstantSizeColumn.Name, computed);
			}, "ConstantSize");
		}
	}

	[NotifyPropertyChangedForTableColumn("SizeFunction")]
	public LinearSizeFunction SizeFunction
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.ArcSizeFunctionColumn];
			if (tableRow == null)
			{
				return LinearSizeFunction.Constant;
			}
			return (LinearSizeFunction)tableRow;
		}
		set
		{
			SetProperty(SizeFunction, value, this, (ObservableConceptualMeshArc model, LinearSizeFunction linearSizeFunction) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.ArcSizeFunctionColumn.Name, linearSizeFunction);
			}, "SizeFunction");
		}
	}

	[NotifyPropertyChangedForTableColumn("ConstrainStretching")]
	public ConstrainStretchingOptions ConstrainStretching
	{
		get
		{
			return (TableRow[MeshExtensions.ConstrainStretchingColumn] as ConstrainStretchingOptions?).GetValueOrDefault();
		}
		set
		{
			SetProperty(ConstrainStretching, value, this, (ObservableConceptualMeshArc model, ConstrainStretchingOptions constrainStretchingOptions) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.ConstrainStretchingColumn.Name, constrainStretchingOptions);
			}, "ConstrainStretching");
		}
	}

	[NotifyPropertyChangedForTableColumn("LaplaceBC")]
	public LaplaceBCOptions LaplaceBC
	{
		get
		{
			return (TableRow[MeshExtensions.LaplaceBCColumn] as LaplaceBCOptions?).GetValueOrDefault();
		}
		set
		{
			SetProperty(LaplaceBC, value, this, (ObservableConceptualMeshArc model, LaplaceBCOptions laplaceBCOptions) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.LaplaceBCColumn.Name, laplaceBCOptions);
			}, "LaplaceBC");
		}
	}

	[NotifyPropertyChangedForTableColumn("DistanceStraightLine")]
	public bool DistanceStraightLine
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.DistanceStraightLineColumn];
			if (tableRow == null)
			{
				return false;
			}
			return (bool)tableRow;
		}
		set
		{
			SetProperty(DistanceStraightLine, value, this, (ObservableConceptualMeshArc model, bool condition) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.DistanceStraightLineColumn.Name, condition);
			}, "DistanceStraightLine");
		}
	}

	[NotifyPropertyChangedForTableColumn("AreaWeight")]
	public float AreaWeight
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.AreaWeightColumn];
			if (tableRow == null)
			{
				return 0f;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(AreaWeight, value, this, (ObservableConceptualMeshArc model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.AreaWeightColumn.Name, computed);
			}, "AreaWeight");
		}
	}

	[NotifyPropertyChangedForTableColumn("DiscretizeMagnetism")]
	public float DiscretizeMagnetism
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.DiscretizeMagnetismColumn];
			if (tableRow == null)
			{
				return 0f;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(DiscretizeMagnetism, value, this, (ObservableConceptualMeshArc model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.DiscretizeMagnetismColumn.Name, computed);
			}, "DiscretizeMagnetism");
		}
	}

	[NotifyPropertyChangedForTableColumn("FollowExactly")]
	public bool FollowExactly
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.FollowExactlyColumn];
			if (tableRow == null)
			{
				return true;
			}
			return (bool)tableRow;
		}
		set
		{
			SetProperty(FollowExactly, value, this, (ObservableConceptualMeshArc model, bool condition) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.FollowExactlyColumn.Name, condition);
			}, "FollowExactly");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand HalfConstantSizeCommand => halfConstantSizeCommand ?? (halfConstantSizeCommand = new RelayCommand(HalfConstantSize));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DoubleConstantSizeCommand => doubleConstantSizeCommand ?? (doubleConstantSizeCommand = new RelayCommand(DoubleConstantSize));

	public ObservableConceptualMeshArc(ObservableTopology topo, int fid)
		: base(topo, fid)
	{
	}
	public void HalfConstantSize()
	{
		ConstantSize /= 2f;
	}
	public void DoubleConstantSize()
	{
		ConstantSize *= 2f;
	}
}
