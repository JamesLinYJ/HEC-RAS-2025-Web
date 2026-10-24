using System;
using System.Threading.Tasks;

namespace Utility.Search;

public class SearchInteraction
{
	public Func<Task> OnInteractAsync { get; set; }

	public Action OnInteract { get; set; }

	public bool HasInteraction
	{
		get
		{
			if (OnInteractAsync == null)
			{
				return OnInteract != null;
			}
			return true;
		}
	}

	public string Resource { get; set; }

	public SearchInteraction(string resourceName, Action onClick)
	{
		Resource = resourceName;
		OnInteract = onClick;
	}

	public SearchInteraction(string resourceName, Func<Task> onClickAsync)
	{
		Resource = resourceName;
		OnInteractAsync = onClickAsync;
	}
}
