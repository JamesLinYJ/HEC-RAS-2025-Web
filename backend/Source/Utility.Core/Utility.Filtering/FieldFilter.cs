using System.Collections.Generic;

namespace Utility.Filtering;

public abstract class FieldFilter : FilterExpression
{
	public string FieldName { get; }

	public string NormalizedFieldName { get; }

	protected FieldFilter(string fieldName)
	{
		FieldName = fieldName;
		NormalizedFieldName = FilterNames.Normalize(fieldName);
	}

	internal override void CollectFields(List<FilterField> fields)
	{
		fields.Add(new FilterField(FieldName, NormalizedFieldName));
	}
}
