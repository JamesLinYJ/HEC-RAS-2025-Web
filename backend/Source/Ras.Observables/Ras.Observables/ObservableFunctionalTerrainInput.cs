using System;
using System.ComponentModel.DataAnnotations;
using Geospatial.Vectors;
using Ras.Layers.Terrains;
using Utility;

namespace Ras.Observables;

public class ObservableFunctionalTerrainInput : ObservableTerrainInput
{
	private readonly FunctionalTerrainInput _input;

	public override ITerrainInput TerrainInput => _input;

	public string TypeName => _input.TypeName;

	[CustomValidation(typeof(ObservableFunctionalTerrainInput), "ValidateExpression")]
	public string Expression
	{
		get
		{
			return _input.Expression;
		}
		set
		{
			SetProperty(_input.Expression, value, _input, (FunctionalTerrainInput model, string newVal) =>
			{
				model.Expression = newVal;
			}, "Expression");
		}
	}

	public Extent ValidExtent
	{
		get
		{
			return _input.ValidExtent;
		}
		set
		{
			if (SetProperty(_input.ValidExtent, value, _input, (FunctionalTerrainInput model, Extent newVal) =>
			{
				model.ValidExtent = newVal;
			}, "ValidExtent"))
			{
				OnPropertyChanged("MinX");
				OnPropertyChanged("MaxX");
				OnPropertyChanged("MinY");
				OnPropertyChanged("MaxY");
			}
		}
	}

	public double MinX
	{
		get
		{
			return _input.ValidExtent.MinX;
		}
		set
		{
			UpdateExtent(value, _input.ValidExtent.MaxX, _input.ValidExtent.MinY, _input.ValidExtent.MaxY);
		}
	}

	public double MaxX
	{
		get
		{
			return _input.ValidExtent.MaxX;
		}
		set
		{
			UpdateExtent(_input.ValidExtent.MinX, value, _input.ValidExtent.MinY, _input.ValidExtent.MaxY);
		}
	}

	public double MinY
	{
		get
		{
			return _input.ValidExtent.MinY;
		}
		set
		{
			UpdateExtent(_input.ValidExtent.MinX, _input.ValidExtent.MaxX, value, _input.ValidExtent.MaxY);
		}
	}

	public double MaxY
	{
		get
		{
			return _input.ValidExtent.MaxY;
		}
		set
		{
			UpdateExtent(_input.ValidExtent.MinX, _input.ValidExtent.MaxX, _input.ValidExtent.MinY, value);
		}
	}

	public ObservableFunctionalTerrainInput(FunctionalTerrainInput input)
	{
		_input = input;
	}

	private void UpdateExtent(double minX, double maxX, double minY, double maxY)
	{
		ValidExtent = new Extent(minX, maxX, minY, maxY);
	}

	public static ValidationResult ValidateExpression(string stringValue, ValidationContext context)
	{
		if (string.IsNullOrWhiteSpace(stringValue))
		{
			return new ValidationResult("Expression cannot be empty.");
		}
		try
		{
			new ExpressionCompiler<double>(stringValue, new string[2] { "x", "y" }).CompileLambda();
			return ValidationResult.Success;
		}
		catch (Exception ex)
		{
			return new ValidationResult(ex.Message);
		}
	}
}
