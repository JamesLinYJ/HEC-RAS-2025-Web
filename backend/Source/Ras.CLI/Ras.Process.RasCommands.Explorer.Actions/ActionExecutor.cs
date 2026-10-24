using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Spectre.Console;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions;

public class ActionExecutor
{
	private readonly ActionRegistry _registry;

	public ActionExecutor(ActionRegistry registry)
	{
		_registry = registry;
	}

	public void ShowAndExecute(RasTreeNode node, Project project)
	{
		if (node == null || node.DataSource == null)
		{
			AnsiConsole.MarkupLine("[yellow]No item selected.[/]");
			WaitForKey();
			return;
		}
		List<ILayerAction> actions = _registry.GetActions(node.DataSource).ToList();
		if (actions.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No actions available for " + node.DataSource.GetType().Name + ".[/]");
			WaitForKey();
			return;
		}
		ILayerAction layerAction = ShowActionMenu(actions, node.Name, node.DataSource, project);
		if (layerAction != null)
		{
			Dictionary<string, object> promptParameters = PromptParameters(layerAction, node.DataSource, project);
			if (promptParameters != null)
			{
				ExecuteAction(layerAction, node.DataSource, project, promptParameters);
			}
		}
	}

	private ILayerAction ShowActionMenu(List<ILayerAction> actions, string nodeName, object dataSource, Project project)
	{
		AnsiConsole.Clear();
		AnsiConsole.Write(new Rule("[yellow]Actions for [bold]" + EscapeMarkup(nodeName) + "[/][/]").LeftJustified());
		AnsiConsole.WriteLine();
		List<(string, ILayerAction, bool)> valueTuples = new List<(string, ILayerAction, bool)>();
		foreach (ILayerAction action in actions)
		{
			bool canExecute = action.CanExecute(dataSource, project);
			string canExecuteGetDisabledReasonTerm = (canExecute ? "" : (" [dim](" + action.GetDisabledReason(dataSource, project) + ")[/]"));
			string canExecuteActionTerm = (canExecute ? (action.Name ?? "") : ("[dim strikethrough]" + action.Name + "[/]" + canExecuteGetDisabledReasonTerm));
			valueTuples.Add((canExecuteActionTerm, action, canExecute));
		}
		valueTuples.Add(("[dim]Cancel[/]", null, true));
		(string, ILayerAction, bool) tuple = AnsiConsole.Prompt(new SelectionPrompt<(string, ILayerAction, bool)>().Title("[green]Select an action:[/]").PageSize(15).AddChoices(valueTuples)
			.UseConverter(((string display, ILayerAction action, bool enabled) tuple2) => tuple2.display));
		if (tuple.Item2 == null)
		{
			return null;
		}
		if (!tuple.Item3)
		{
			AnsiConsole.MarkupLine("[red]Cannot execute: " + tuple.Item2.GetDisabledReason(dataSource, project) + "[/]");
			WaitForKey();
			return null;
		}
		return tuple.Item2;
	}

	private Dictionary<string, object> PromptParameters(ILayerAction action, object dataSource, Project project)
	{
		if (action.Parameters.Count == 0)
		{
			return new Dictionary<string, object>();
		}
		AnsiConsole.WriteLine();
		AnsiConsole.Write(new Rule("[cyan]" + action.Name + "[/]").LeftJustified());
		if (!string.IsNullOrEmpty(action.Description))
		{
			AnsiConsole.MarkupLine("[dim]" + action.Description + "[/]");
		}
		AnsiConsole.WriteLine();
		Dictionary<string, object> objectByString = new Dictionary<string, object>();
		foreach (IActionParameter parameter in action.Parameters)
		{
			object defaultValue = action.GetParameterDefault(parameter.Name, dataSource, project) ?? parameter.DefaultValue;
			object promptForParameter = PromptForParameter(parameter, defaultValue);
			if (promptForParameter == null && parameter.IsRequired)
			{
				AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
				WaitForKey();
				return null;
			}
			objectByString[parameter.Name] = promptForParameter ?? parameter.DefaultValue;
		}
		return objectByString;
	}

	private object PromptForParameter(IActionParameter param, object defaultValue)
	{
		if (!string.IsNullOrEmpty(param.Description))
		{
			AnsiConsole.MarkupLine("[dim]" + param.Description + "[/]");
		}
		Type valueType = param.ValueType;
		if ((object)valueType != null)
		{
			if (valueType == typeof(bool))
			{
				return PromptBool(param, defaultValue);
			}
			if (valueType == typeof(int))
			{
				return PromptInt(param, defaultValue);
			}
			if (valueType == typeof(double))
			{
				return PromptDouble(param, defaultValue);
			}
			if (valueType == typeof(Extent))
			{
				return PromptExtent(param, defaultValue);
			}
		}
		return PromptString(param, defaultValue);
	}

	private string PromptString(IActionParameter param, object defaultValue)
	{
		string defaultValue2 = (defaultValue as string) ?? "";
		TextPrompt<string> textPrompt = new TextPrompt<string>("[green]" + param.DisplayName + "[/]:").AllowEmpty();
		if (!string.IsNullOrEmpty(defaultValue2))
		{
			textPrompt.DefaultValue(defaultValue2);
		}
		string prompt;
		while (true)
		{
			prompt = AnsiConsole.Prompt(textPrompt);
			if (string.IsNullOrEmpty(prompt) && !param.IsRequired)
			{
				return defaultValue2;
			}
			string validate = param.Validate(prompt);
			if (validate == null)
			{
				break;
			}
			AnsiConsole.MarkupLine("[red]" + validate + "[/]");
		}
		return prompt;
	}

	private bool PromptBool(IActionParameter param, object defaultValue)
	{
		bool valueOrDefault = defaultValue as bool? == true;
		return AnsiConsole.Confirm("[green]" + param.DisplayName + "[/]", valueOrDefault);
	}

	private int PromptInt(IActionParameter param, object defaultValue)
	{
		int defaultValueTerm = ((defaultValue is int ordinal) ? ordinal : ((defaultValue is double computed) ? ((int)computed) : 0));
		TextPrompt<int> textPrompt = new TextPrompt<int>("[green]" + param.DisplayName + "[/]:").DefaultValue(defaultValueTerm);
		NumberParameter np = param as NumberParameter;
		if (np != null)
		{
			textPrompt.Validate((int ordinal2) =>
			{
				string validate = np.Validate(ordinal2);
				return (validate != null) ? ValidationResult.Error(validate) : ValidationResult.Success();
			});
		}
		return AnsiConsole.Prompt(textPrompt);
	}

	private double PromptDouble(IActionParameter param, object defaultValue)
	{
		double computed;
		if (defaultValue is double computed2)
		{
			computed = computed2;
		}
		else
		{
			computed = ((defaultValue is int ordinal) ? ((double)ordinal) : 0.0);
		}
		TextPrompt<double> textPrompt = new TextPrompt<double>("[green]" + param.DisplayName + "[/]:").DefaultValue(computed);
		NumberParameter np = param as NumberParameter;
		if (np != null)
		{
			textPrompt.Validate((double computed3) =>
			{
				string validate = np.Validate(computed3);
				return (validate != null) ? ValidationResult.Error(validate) : ValidationResult.Success();
			});
		}
		return AnsiConsole.Prompt(textPrompt);
	}

	private Extent PromptExtent(IActionParameter param, object defaultValue)
	{
		Extent extent = ((defaultValue is Extent extent2) ? extent2 : Extent.NullExtent);
		AnsiConsole.MarkupLine("[green]" + param.DisplayName + "[/]:");
		double minX = AnsiConsole.Prompt(new TextPrompt<double>("  MinX:").DefaultValue(extent.MinX));
		double maxX = AnsiConsole.Prompt(new TextPrompt<double>("  MinY:").DefaultValue(extent.MinY));
		double minY = AnsiConsole.Prompt(new TextPrompt<double>("  MaxX:").DefaultValue(extent.MaxX));
		double maxY = AnsiConsole.Prompt(new TextPrompt<double>("  MaxY:").DefaultValue(extent.MaxY));
		return new Extent(minX, maxX, minY, maxY);
	}

	private void ExecuteAction(ILayerAction action, object dataSource, Project project, Dictionary<string, object> parameters)
	{
		AnsiConsole.WriteLine();
		ActionResult result = null;
		ProgressReporter reporter = ProgressReporter.None();
		AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan"))
			.Start("Executing " + action.Name + "...", (StatusContext ctx) =>
			{
				result = action.Execute(dataSource, project, parameters, reporter);
			});
		AnsiConsole.WriteLine();
		if (result.Success)
		{
			AnsiConsole.MarkupLine("[green]Success![/] " + EscapeMarkup(result.Message ?? ""));
			if (!string.IsNullOrEmpty(result.OutputPath))
			{
				AnsiConsole.MarkupLine("[dim]Output: " + EscapeMarkup(result.OutputPath) + "[/]");
			}
		}
		else
		{
			AnsiConsole.MarkupLine("[red]Failed:[/] " + EscapeMarkup(result.Message ?? "Unknown error"));
		}
		WaitForKey();
	}

	private static void WaitForKey()
	{
		AnsiConsole.WriteLine();
		AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
		Console.ReadKey(intercept: true);
	}

	private static string EscapeMarkup(string textValue)
	{
		return textValue?.Replace("[", "[[").Replace("]", "]]") ?? "";
	}
}
