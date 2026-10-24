namespace Utility.Search;

public interface ISearchItem
{
	string Title { get; set; }

	string Subheading { get; set; }

	object Payload { get; }
}
