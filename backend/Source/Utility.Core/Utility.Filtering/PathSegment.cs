namespace Utility.Filtering;

public sealed class PathSegment
{
	public string Name { get; }

	public string NormalizedName { get; }

	public PathPredicate Predicate { get; }

	public PathCall Call { get; }

	public PathSegment(string name, PathPredicate predicate, PathCall call = null)
	{
		Name = name;
		NormalizedName = FilterNames.Normalize(name);
		Predicate = predicate;
		Call = call;
	}
}
