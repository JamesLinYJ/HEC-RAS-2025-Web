using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Geospatial.Vectors.Topology;
using Utility.Validation;
using Visual.Observables;

namespace Visual.Mapping.Observables;

public class ObservableConceptualMeshControlPoint : ObservableBaseControlPoint
{
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? halfConstantSizeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? doubleConstantSizeCommand;

	public override ObservableConceptualMeshTopology Topology => base.Topology as ObservableConceptualMeshTopology;

	[NotifyPropertyChangedForTableColumn("PatchType")]
	public PatchType PatchType
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.PatchTypeColumn];
			if (tableRow == null)
			{
				return PatchType.Quad;
			}
			return (PatchType)tableRow;
		}
		set
		{
			SetProperty(PatchType, value, this, (ObservableConceptualMeshControlPoint model, PatchType patchType) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.PatchTypeColumn.Name, patchType);
			}, "PatchType");
		}
	}

	[NotifyPropertyChangedForTableColumn("ConstantSize")]
	public float ConstantSize
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.ConstantSizeColumn];
			if (tableRow == null)
			{
				return MeshRegionMetadata.DefaultConstantSize;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(ConstantSize, value, this, (ObservableConceptualMeshControlPoint model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.ConstantSizeColumn.Name, computed);
			}, "ConstantSize");
		}
	}

	[NotifyPropertyChangedForTableColumn("SecondaryConstantSize")]
	public float SecondaryConstantSize
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.SecondaryConstantSizeColumn];
			if (tableRow == null)
			{
				return MeshRegionMetadata.DefaultConstantSize;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(SecondaryConstantSize, value, this, (ObservableConceptualMeshControlPoint model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.SecondaryConstantSizeColumn.Name, computed);
			}, "SecondaryConstantSize");
		}
	}

	[NotifyPropertyChangedForTableColumn("RotationDegrees")]
	public float RotationDegrees
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.RotationDegreesColumn];
			if (tableRow == null)
			{
				return 0f;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(RotationDegrees, value, this, (ObservableConceptualMeshControlPoint model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.RotationDegreesColumn.Name, computed);
			}, "RotationDegrees");
		}
	}

	[RecommendedBounds(0.0, 0.5, "Max stretching rate outside of recommended range", false)]
	[Float(true)]
	[NotifyPropertyChangedForTableColumn("StretchRate")]
	public float BiasInternal
	{
		get
		{
			float result = 0.1f;
			object tableRow = TableRow[MeshExtensions.StretchRateColumn];
			if (tableRow == null)
			{
				return result;
			}
			if (tableRow is string s && float.TryParse(s, out result))
			{
				return result;
			}
			if (tableRow is float)
			{
				return (float)tableRow;
			}
			return result;
		}
		set
		{
			SetProperty(BiasInternal, value, this, (ObservableConceptualMeshControlPoint model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.StretchRateColumn.Name, computed);
			}, "BiasInternal");
		}
	}

	[NotifyPropertyChangedForTableColumn("Multipoint")]
	public bool Multipoint
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.MultipointColumn];
			if (tableRow == null)
			{
				return true;
			}
			return (float)tableRow > 0f;
		}
		set
		{
			SetProperty(Multipoint, value, this, (ObservableConceptualMeshControlPoint model, bool condition) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.MultipointColumn.Name, condition ? 1f : 0f);
			}, "Multipoint");
		}
	}

	[AbsoluteBounds(-1.0, 50.0, "0.0: minimal TIN, 50: Highly detailed curved faces (slow)")]
	[RecommendedBounds(0.0, 10.0, "0.0: minimal TIN, 10: Detailed curved faces (slow)", false)]
	[NotifyPropertyChangedForTableColumn("TINResolution")]
	public float TinResolution
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.TinResolutionColumn];
			if (tableRow == null)
			{
				return 1f;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(TinResolution, value, this, (ObservableConceptualMeshControlPoint model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.TinResolutionColumn.Name, computed);
			}, "TinResolution");
		}
	}

	[AbsoluteBounds(0.0, 1.0, "0.0: match scale, 1.0: minimize cell count transitions")]
	[NotifyPropertyChangedForTableColumn("SizeTolerance")]
	public float SizeTolerance
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.SizeToleranceColumn];
			if (tableRow == null)
			{
				return 1f;
			}
			return (float)tableRow;
		}
		set
		{
			SetProperty(SizeTolerance, value, this, (ObservableConceptualMeshControlPoint model, float computed) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.SizeToleranceColumn.Name, computed);
			}, "SizeTolerance");
		}
	}

	[NotifyPropertyChangedForTableColumn("Recipe")]
	public string Recipe
	{
		get
		{
			object tableRow = TableRow[MeshExtensions.RecipeColumn];
			if (tableRow == null)
			{
				return "";
			}
			return (string)tableRow;
		}
		set
		{
			SetProperty(Recipe, value, this, (ObservableConceptualMeshControlPoint model, string textValue) =>
			{
				model._table.SetCellValue(model.Fid, MeshExtensions.RecipeColumn.Name, textValue);
			}, "Recipe");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand HalfConstantSizeCommand => halfConstantSizeCommand ?? (halfConstantSizeCommand = new RelayCommand(HalfConstantSize));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DoubleConstantSizeCommand => doubleConstantSizeCommand ?? (doubleConstantSizeCommand = new RelayCommand(DoubleConstantSize));

	public ObservableConceptualMeshControlPoint(ObservableTopology topo, int fid)
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
