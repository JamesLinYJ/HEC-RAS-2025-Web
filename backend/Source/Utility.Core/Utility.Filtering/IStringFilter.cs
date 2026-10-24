namespace Utility.Filtering;

public interface IStringFilter : IFilter
{
	string SearchText { get; set; }
}
public interface IStringFilter<T> : IFilter<T>, IFilter, IStringFilter
{
}
