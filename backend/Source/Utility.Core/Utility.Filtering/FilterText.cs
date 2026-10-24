using System.Globalization;
using System.Text;

namespace Utility.Filtering;

public static class FilterText
{
	public static string Quote(string stringValue)
	{
		return "'" + (stringValue ?? "").Replace("'", "''") + "'";
	}

	public static string Literal(string typedText, bool numericField = true)
	{
		if (string.IsNullOrEmpty(typedText))
		{
			return "''";
		}
		if (numericField && double.TryParse(typedText, NumberStyles.Float, CultureInfo.CurrentCulture, out var result))
		{
			return result.ToString("R", CultureInfo.InvariantCulture);
		}
		return Quote(typedText);
	}

	public static bool TryFieldReference(string header, out string reference)
	{
		reference = null;
		if (string.IsNullOrWhiteSpace(header))
		{
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder(header.Length + 1);
		bool condition = false;
		foreach (char c in header)
		{
			if (char.IsLetterOrDigit(c))
			{
				if (condition && stringBuilder.Length > 0)
				{
					stringBuilder.Append('_');
				}
				condition = false;
				if (stringBuilder.Length == 0 && !char.IsLetter(c))
				{
					stringBuilder.Append('_');
				}
				stringBuilder.Append(c);
			}
			else
			{
				condition = true;
			}
		}
		if (stringBuilder.Length == 0)
		{
			return false;
		}
		reference = stringBuilder.ToString();
		return true;
	}

	public static string OperatorText(FilterComparisonOperator comparisonOperator)
	{
		return comparisonOperator switch
		{
			FilterComparisonOperator.NotEqual => "!=", 
			FilterComparisonOperator.Less => "<", 
			FilterComparisonOperator.LessOrEqual => "<=", 
			FilterComparisonOperator.Greater => ">", 
			FilterComparisonOperator.GreaterOrEqual => ">=", 
			_ => "=", 
		};
	}

	public static string Comparison(string fieldReference, FilterComparisonOperator comparisonOperator, string typedValue, bool numericField = true)
	{
		return $"{fieldReference} {OperatorText(comparisonOperator)} {Literal(typedValue, numericField)}";
	}

	public static string Contains(string fieldReference, string typedValue, bool negated)
	{
		string replace = (typedValue ?? "").Replace("'", "''");
		return $"{fieldReference} {(negated ? "NOT LIKE" : "LIKE")} '%{replace}%'";
	}

	public static string IsNull(string fieldReference, bool negated)
	{
		return fieldReference + " IS " + (negated ? "NOT NULL" : "NULL");
	}

	public static bool TryRenderClause(FieldFilter clause, out string referenceOperatorTextTerm2)
	{
		referenceOperatorTextTerm2 = null;
		if (!TryFieldReference(clause.FieldName, out var reference))
		{
			return false;
		}
		string referenceOperatorTextTerm;
		if (clause is ComparisonFilter comparisonFilter)
		{
			referenceOperatorTextTerm = $"{reference} {OperatorText(comparisonFilter.Operator)} {comparisonFilter.Value}";
		}
		else if (clause is LikeFilter likeFilter)
		{
			referenceOperatorTextTerm = $"{reference} {(likeFilter.Negated ? "NOT LIKE" : "LIKE")} {Quote(likeFilter.Pattern)}";
		}
		else
		{
			referenceOperatorTextTerm = ((!(clause is NullCheckFilter nullCheckFilter)) ? null : IsNull(reference, nullCheckFilter.Negated));
		}
		referenceOperatorTextTerm2 = referenceOperatorTextTerm;
		return referenceOperatorTextTerm2 != null;
	}

	public static void AppendClause(StringBuilder builder, string clause)
	{
		if (builder.Length > 0)
		{
			builder.Append(" AND ");
		}
		builder.Append(clause);
	}
}
