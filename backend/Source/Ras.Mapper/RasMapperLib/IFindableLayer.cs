using System;
using System.Collections.Generic;
using System.Data;

namespace RasMapperLib;

public interface IFindableLayer
{
	int GetNumberOfFilters { get; }

	string GetInstructions { get; }

	bool ShowExpressionBox { get; }

	string DefaultExpression { get; }

	string[] GetFilters(int filterIndex, string[] currentFilterValues, Predicate<DataRow> overrideFeatureRowFilter);

	string GetFilterLabelText(string[] currentFilters, int filterIndex);

	List<int> ReactToFilterChanged(string[] currentFilterValues, int filterIndex, string changedFilterValue);

	string ValidateExpression(string[] currentFilterValues, string expression);

	bool Find(string[] currentFilterValues, string expression);
}
