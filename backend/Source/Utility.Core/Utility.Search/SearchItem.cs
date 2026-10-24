using System;

namespace Utility.Search;

public class SearchItem<T> : ISearchItem
{
	public string Title { get; set; }

	public string Subheading { get; set; }

	public T Payload { get; set; }

	public Func<bool> MakesSense { get; set; } = () => true;

	object ISearchItem.Payload => Payload;
}
