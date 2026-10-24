using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ras.Updates;
using Utility.Extensions;
using Utility.Filtering;

namespace Ras.Process.SampleCommands;

internal static class SampleResolver
{
	public readonly struct ResolveResult(bool success, ExampleProjectDetails details, ExampleProjectCollection collection)
	{
		public bool Success { get; } = success;

		public ExampleProjectDetails Details { get; } = details;

		public ExampleProjectCollection Collection { get; } = collection;
	}

	private const float FuzzyFloor = 0.55f;

	private const int MaxSuggestions = 5;

	public static async Task<ResolveResult> TryResolveAsync(string title, string verbHint = null)
	{
		ExampleProjectCollection exampleProjectCollection = await ExampleProjectHelper.TryGetAllExamplesAsync();
		if (exampleProjectCollection == null)
		{
			Console.WriteLine("ERROR: Could not retrieve the global example-project manifest from S3.");
			return new ResolveResult(success: false, null, null);
		}
		if (string.IsNullOrWhiteSpace(title))
		{
			Console.WriteLine("ERROR: Title must not be empty.");
			return new ResolveResult(success: false, null, exampleProjectCollection);
		}
		List<ExampleProjectDetails> exampleProjectDetails = (from p in exampleProjectCollection.AllProjects
			where string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase)
			orderby p.PublishDate descending
			select p).ToList();
		if (exampleProjectDetails.Count > 0)
		{
			return new ResolveResult(success: true, exampleProjectDetails[0], exampleProjectCollection);
		}
		PrintNotFound(title, exampleProjectCollection, verbHint);
		return new ResolveResult(success: false, null, exampleProjectCollection);
	}

	private static void PrintNotFound(string title, ExampleProjectCollection collection, string verbHint)
	{
		Console.WriteLine("ERROR: No sample project with title " + title.InSingleQuotes() + " was found.");
		List<string> suggestions = FindSuggestions(title, collection);
		if (suggestions.Count == 0)
		{
			Console.WriteLine("Run 'ras sample list' to see available titles.");
			return;
		}
		if (suggestions.Count == 1)
		{
			Console.WriteLine("Did you mean " + FormatSuggestion(suggestions[0], verbHint) + " ?");
			return;
		}
		Console.WriteLine("Did you mean one of:");
		foreach (string suggestion in suggestions)
		{
			Console.WriteLine("  " + FormatSuggestion(suggestion, verbHint));
		}
	}

	private static string FormatSuggestion(string title, string verbHint)
	{
		string titleTerm = "\"" + title + "\"";
		if (string.IsNullOrWhiteSpace(verbHint))
		{
			return titleTerm;
		}
		return "'ras " + verbHint + " " + titleTerm + "'";
	}

	private static List<string> FindSuggestions(string title, ExampleProjectCollection collection)
	{
		List<string> source = (from g in (collection.AllProjects ?? new List<ExampleProjectDetails>()).Where((ExampleProjectDetails p) => !string.IsNullOrWhiteSpace(p.Title)).GroupBy((ExampleProjectDetails p) => p.Title, StringComparer.OrdinalIgnoreCase)
			select g.OrderByDescending((ExampleProjectDetails p) => p.PublishDate).First().Title).ToList();
		List<string> take = source.Where((string t) => t.Contains(title, StringComparison.OrdinalIgnoreCase) || title.Contains(t, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
		if (take.Count > 0)
		{
			return take;
		}
		return (from x in (from t in source
				select (Title: t, Score: FuzzyStringMatcher.Match(title, t)) into x
				where x.Score >= 0.55f
				orderby x.Score descending
				select x).Take(5)
			select x.Title).ToList();
	}
}
