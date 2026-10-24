using System;
using System.Collections.Generic;

namespace Utility.Filtering;

public static class SelectorResolver
{
	public static bool TryResolve(ISelectorNode root, PathSelector selector, FilterOptions options, out SelectorResolution resolution, out string error)
	{
		resolution = null;
		error = null;
		options = options ?? FilterOptions.Default;
		IReadOnlyList<PathSegment> segments = selector.Segments;
		int i = 0;
		if (segments[0].Predicate == null && segments[0].Call == null && string.Equals(segments[0].NormalizedName, FilterNames.Normalize(root.Name), StringComparison.Ordinal))
		{
			i = 1;
		}
		ISelectorNode selectorNode = root;
		ISelectorCollection selectorCollection = null;
		PathSegment pathSegment = null;
		for (; i < segments.Count; i++)
		{
			PathSegment pathSegment2 = segments[i];
			if (selectorNode.TryGetCollection(pathSegment2.NormalizedName, out var collection))
			{
				if (pathSegment2.Call != null)
				{
					error = $"'{pathSegment2.Name}' is a collection; a call applies to one of its fields, e.g. {pathSegment2.Name}[...].{FirstSettableExample(collection)}.scale(2).";
					return false;
				}
				selectorCollection = collection;
				pathSegment = pathSegment2;
				i++;
				break;
			}
			if (pathSegment2.Predicate != null)
			{
				error = "'" + pathSegment2.Name + "' is not a collection, so it can't take a [predicate].";
				return false;
			}
			if (selectorNode.TryGetChildNode(pathSegment2.NormalizedName, out var node))
			{
				selectorNode = node;
				continue;
			}
			if (selectorNode.Fields != null && selectorNode.Fields.TryGetField(pathSegment2.NormalizedName, out var type, out var settable))
			{
				if (i != segments.Count - 1)
				{
					error = "Can't navigate past the field '" + pathSegment2.Name + "'.";
					return false;
				}
				return TryResolveScalar(selectorNode, pathSegment2, type, settable, selector, out resolution, out error);
			}
			error = UnknownMember(pathSegment2.Name, selectorNode);
			return false;
		}
		if (selectorCollection == null)
		{
			error = "Selector must reach a collection (e.g. 'region' or 'arc') or a field (e.g. 'description'); it stopped at '" + selectorNode.Name + "'.";
			return false;
		}
		resolution = new SelectorResolution
		{
			Collection = selectorCollection
		};
		if (!TryValidatePredicateFields(selectorCollection, pathSegment.Predicate, out error))
		{
			return false;
		}
		if (!TryApplyPredicate(selectorCollection, pathSegment.Predicate, options, resolution.MatchedIndices, out error))
		{
			return false;
		}
		int segmentsTerm = segments.Count - i;
		if (segmentsTerm == 0)
		{
			if (selector.HasAssignment)
			{
				error = $"An assignment needs a field, e.g. {selectorCollection.Name}[...].{FirstSettableExample(selectorCollection)} = <value>.";
				return false;
			}
			return true;
		}
		PathSegment pathSegment3 = segments[i];
		if (pathSegment3.Predicate != null)
		{
			error = "The field '" + pathSegment3.Name + "' can't take a [predicate].";
			return false;
		}
		if (pathSegment3.Call != null)
		{
			error = $"An operation needs a field, e.g. {selectorCollection.Name}[...].{FirstSettableExample(selectorCollection)}.scale(2).";
			return false;
		}
		if (!selectorCollection.TryGetField(pathSegment3.NormalizedName, out var type2, out var settable2))
		{
			error = UnknownField(pathSegment3.Name, selectorCollection);
			return false;
		}
		resolution.TerminalField = pathSegment3.Name;
		resolution.TerminalFieldNormalized = pathSegment3.NormalizedName;
		resolution.TerminalFieldType = type2;
		if (segmentsTerm == 2)
		{
			PathSegment pathSegment4 = segments[i + 1];
			if (pathSegment4.Call == null)
			{
				error = "Navigating past a collection's field is not supported yet.";
				return false;
			}
			if (selector.HasAssignment)
			{
				error = "Use either an assignment (= value) or an operation (.scale(2)), not both.";
				return false;
			}
			if (!settable2)
			{
				error = "Field '" + pathSegment3.Name + "' is read-only and can't be modified.";
				return false;
			}
			if (!SelectorOperation.IsNumericField(type2))
			{
				error = $"Operations apply to numeric fields; '{pathSegment3.Name}' is {type2.Name}.";
				return false;
			}
			if (!SelectorOperation.TryCreate(pathSegment4.Name, pathSegment4.Call.Arguments, out var operation, out error))
			{
				return false;
			}
			resolution.Operation = operation;
			return true;
		}
		if (segmentsTerm > 2)
		{
			error = "Navigating past a collection's field is not supported yet.";
			return false;
		}
		if (selector.HasAssignment)
		{
			if (!settable2)
			{
				error = "Field '" + pathSegment3.Name + "' is read-only and can't be assigned.";
				return false;
			}
			if (!FilterValues.TryCoerce(selector.AssignmentValue, type2, out var assignmentValue, out error))
			{
				return false;
			}
			resolution.HasAssignment = true;
			resolution.AssignmentValue = assignmentValue;
			resolution.AssignmentLiteral = selector.AssignmentValue;
		}
		return true;
	}

	private static bool TryResolveScalar(ISelectorNode node, PathSegment segment, Type fieldType, bool settable, PathSelector selector, out SelectorResolution resolution, out string error)
	{
		error = null;
		resolution = new SelectorResolution
		{
			ScalarNode = node,
			TerminalField = segment.Name,
			TerminalFieldNormalized = segment.NormalizedName
		};
		if (!selector.HasAssignment)
		{
			return true;
		}
		if (!settable)
		{
			error = "Field '" + segment.Name + "' is read-only and can't be assigned.";
			return false;
		}
		if (!FilterValues.TryCoerce(selector.AssignmentValue, fieldType, out var assignmentValue, out error))
		{
			return false;
		}
		resolution.HasAssignment = true;
		resolution.AssignmentValue = assignmentValue;
		resolution.AssignmentLiteral = selector.AssignmentValue;
		return true;
	}

	private static bool TryApplyPredicate(ISelectorCollection collection, PathPredicate predicate, FilterOptions options, List<int> matched, out string error)
	{
		error = null;
		if (predicate == null)
		{
			for (int i = 0; i < collection.Count; i++)
			{
				matched.Add(i);
			}
			return true;
		}
		if (predicate.IsIndex)
		{
			if (predicate.Index >= collection.Count)
			{
				error = $"Index [{predicate.Index}] is out of range; {collection.Name} has {collection.Count} element(s).";
				return false;
			}
			matched.Add(predicate.Index);
			return true;
		}
		for (int j = 0; j < collection.Count; j++)
		{
			ISelectorElement element = collection.GetElement(j);
			if (predicate.Filter.Evaluate(element, options))
			{
				matched.Add(j);
			}
		}
		return true;
	}

	private static bool TryValidatePredicateFields(ISelectorCollection collection, PathPredicate predicate, out string error)
	{
		error = null;
		if (predicate == null || predicate.IsIndex)
		{
			return true;
		}
		HashSet<string> ordinal = new HashSet<string>(StringComparer.Ordinal);
		foreach (FilterField field in predicate.Filter.GetFields())
		{
			if (!collection.TryGetField(field.NormalizedName, out var _, out var _) && ordinal.Add(field.NormalizedName))
			{
				error = UnknownField(field.RawName, collection);
				return false;
			}
		}
		return true;
	}

	private static string UnknownMember(string name, ISelectorNode node)
	{
		List<string> memberNames = new List<string>(node.MemberNames);
		if (node.Fields != null)
		{
			memberNames.AddRange(node.Fields.FieldNames);
		}
		string closest = FilterNames.Closest(FilterNames.Normalize(name), memberNames);
		string closestTerm = ((closest != null) ? (" Did you mean '" + closest + "'?") : "");
		return $"Unknown member '{name}' on '{node.Name}'.{closestTerm} Available: {string.Join(", ", memberNames)}.";
	}

	private static string UnknownField(string name, ISelectorCollection collection)
	{
		string closest = FilterNames.Closest(FilterNames.Normalize(name), collection.KnownFieldNames);
		string closestTerm = ((closest != null) ? (" Did you mean '" + closest + "'?") : "");
		return $"Unknown field '{name}' on '{collection.Name}'.{closestTerm} Available: {string.Join(", ", collection.KnownFieldNames)}.";
	}

	private static string FirstSettableExample(ISelectorCollection collection)
	{
		foreach (string knownFieldName in collection.KnownFieldNames)
		{
			if (collection.TryGetField(FilterNames.Normalize(knownFieldName), out var _, out var settable) & settable)
			{
				return knownFieldName;
			}
		}
		return "field";
	}
}
