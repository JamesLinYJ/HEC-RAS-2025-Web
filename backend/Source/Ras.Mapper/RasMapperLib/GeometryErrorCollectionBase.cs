using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public abstract class GeometryErrorCollectionBase
{
	public List<GeometryError> Errors;

	public GeometryErrorCollectionBase(IList<GeometryError> errs)
	{
		Errors = errs.ToList();
	}

	public abstract string GetLayerNames();

	public abstract string GetFeatureNames();

	public abstract Feature GetCurrentFeature();

	public string GetErrorDescription()
	{
		return string.Join(", ", Errors.Select([SpecialName] (GeometryError gerr) => gerr.Message));
	}

	public string GetParsedErrorDescription()
	{
		return string.Join(Environment.NewLine, Errors.Select([SpecialName] (GeometryError gerr) => gerr.Message));
	}
}
