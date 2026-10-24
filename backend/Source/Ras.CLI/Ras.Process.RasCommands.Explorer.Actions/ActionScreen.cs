using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Process.RasCommands.Explorer.Actions.Parameters;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Process.RasCommands.Explorer.Actions;

public class ActionScreen
{
	private readonly ActionRegistry _registry;

	private readonly RasTreeNode _node;

	private readonly Project _project;

	private readonly List<ILayerAction> _actions;

	private int _selectedIndex;

	private bool _running = true;

	public ActionScreen(ActionRegistry registry, RasTreeNode node, Project project)
	{
		_registry = registry;
		_node = node;
		_project = project;
		_actions = _registry.GetActions(node.DataSource).ToList();
	}

	public void Show()
	{
		if (_node?.DataSource == null)
		{
			ShowMessage("No item selected.", ConsoleColor.Yellow);
			return;
		}
		if (_actions.Count == 0)
		{
			ShowMessage("No actions available for " + _node.DataSource.GetType().Name + ".", ConsoleColor.Yellow);
			return;
		}
		Console.CursorVisible = false;
		_running = true;
		_selectedIndex = 0;
		while (_running)
		{
			Render();
			ProcessInput();
		}
	}

	private void Render()
	{
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		int windowWidth = Console.WindowWidth;
		int windowHeight = Console.WindowHeight;
		string nodeTerm = " Actions: " + _node.Name + " ";
		string windowWidthTerm = new string('─', Math.Max(0, (windowWidth - nodeTerm.Length) / 2));
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine(windowWidthTerm + nodeTerm + windowWidthTerm);
		Console.ResetColor();
		Console.WriteLine();
		int scaledWindowWidth = Math.Min(40, windowWidth / 3);
		int ordinal = scaledWindowWidth + 2;
		int width = windowWidth - ordinal - 1;
		RenderActionList(scaledWindowWidth, windowHeight - 6);
		if (_selectedIndex >= 0 && _selectedIndex < _actions.Count)
		{
			RenderActionDetails(_actions[_selectedIndex], ordinal, width, windowHeight - 6);
		}
		Console.SetCursorPosition(0, windowHeight - 2);
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.Write(new string('─', windowWidth));
		Console.SetCursorPosition(0, windowHeight - 1);
		Console.Write(" ↑↓ Navigate │ Enter Execute │ Esc Back");
		Console.ResetColor();
	}

	private void RenderActionList(int width, int maxHeight)
	{
		Console.SetCursorPosition(0, 2);
		Console.ForegroundColor = ConsoleColor.White;
		Console.WriteLine(" Available Actions:");
		Console.ResetColor();
		Console.WriteLine();
		int ordinal = 4;
		for (int i = 0; i < _actions.Count && i < maxHeight - 2; i++)
		{
			Console.SetCursorPosition(0, ordinal + i);
			ILayerAction layerAction = _actions[i];
			bool canExecute = layerAction.CanExecute(_node.DataSource, _project);
			bool selectedIndexTerm = i == _selectedIndex;
			if (selectedIndexTerm)
			{
				Console.BackgroundColor = ConsoleColor.DarkBlue;
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write(" > ");
			}
			else
			{
				Console.Write("   ");
			}
			if (!canExecute)
			{
				Console.ForegroundColor = ConsoleColor.DarkGray;
			}
			else if (!selectedIndexTerm)
			{
				Console.ForegroundColor = ConsoleColor.Gray;
			}
			string substringWidthTerm = layerAction.Name;
			if (substringWidthTerm.Length > width - 4)
			{
				substringWidthTerm = substringWidthTerm.Substring(0, width - 7) + "...";
			}
			Console.Write(substringWidthTerm.PadRight(width - 3));
			Console.ResetColor();
		}
	}

	private void RenderActionDetails(ILayerAction action, int startX, int width, int maxHeight)
	{
		bool canExecute = action.CanExecute(_node.DataSource, _project);
		int ordinal = 2;
		Console.SetCursorPosition(startX, ordinal++);
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.Write(action.Name);
		Console.ResetColor();
		ordinal++;
		if (!string.IsNullOrEmpty(action.Description))
		{
			Console.SetCursorPosition(startX, ordinal++);
			Console.ForegroundColor = ConsoleColor.Gray;
			WriteWrapped(action.Description, startX, width, ref ordinal, 3);
			Console.ResetColor();
			ordinal++;
		}
		Console.SetCursorPosition(startX, ordinal++);
		if (canExecute)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write("[*] Ready to execute");
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Write("[ ] ");
			Console.Write(action.GetDisabledReason(_node.DataSource, _project) ?? "Disabled");
		}
		Console.ResetColor();
		ordinal++;
		if (action.Parameters.Count <= 0)
		{
			return;
		}
		ordinal++;
		Console.SetCursorPosition(startX, ordinal++);
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write("Parameters:");
		Console.ResetColor();
		ordinal++;
		foreach (IActionParameter parameter in action.Parameters)
		{
			if (ordinal >= maxHeight)
			{
				break;
			}
			Console.SetCursorPosition(startX, ordinal++);
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.Write("  " + parameter.DisplayName);
			if (parameter.IsRequired)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write(" *");
			}
			Console.ResetColor();
			if (!string.IsNullOrEmpty(parameter.Description) && ordinal < maxHeight)
			{
				Console.SetCursorPosition(startX + 2, ordinal++);
				Console.ForegroundColor = ConsoleColor.DarkGray;
				string description = parameter.Description;
				if (description.Length > width - 4)
				{
					description = description.Substring(0, width - 7) + "...";
				}
				Console.Write(description);
				Console.ResetColor();
			}
		}
	}

	private void WriteWrapped(string textValue, int startX, int width, ref int row, int maxLines)
	{
		int ordinal = 0;
		int ordinal2 = 0;
		while (ordinal < textValue.Length && ordinal2 < maxLines)
		{
			int ordinal3 = textValue.Length - ordinal;
			int lastIndexOfTerm = Math.Min(ordinal3, width - 2);
			if (lastIndexOfTerm < ordinal3)
			{
				int lastIndexOf = textValue.LastIndexOf(' ', ordinal + lastIndexOfTerm, lastIndexOfTerm);
				if (lastIndexOf > ordinal)
				{
					lastIndexOfTerm = lastIndexOf - ordinal;
				}
			}
			if (ordinal2 > 0)
			{
				Console.SetCursorPosition(startX, row++);
			}
			Console.Write(textValue.Substring(ordinal, lastIndexOfTerm).TrimStart());
			ordinal += lastIndexOfTerm;
			ordinal2++;
		}
	}

	private void ProcessInput()
	{
		switch (Console.ReadKey(intercept: true).Key)
		{
		case ConsoleKey.UpArrow:
		case ConsoleKey.K:
			if (_selectedIndex > 0)
			{
				_selectedIndex--;
			}
			break;
		case ConsoleKey.DownArrow:
		case ConsoleKey.J:
			if (_selectedIndex < _actions.Count - 1)
			{
				_selectedIndex++;
			}
			break;
		case ConsoleKey.Enter:
			ExecuteSelectedAction();
			break;
		case ConsoleKey.Escape:
		case ConsoleKey.Q:
			_running = false;
			break;
		}
	}

	private void ExecuteSelectedAction()
	{
		if (_selectedIndex < 0 || _selectedIndex >= _actions.Count)
		{
			return;
		}
		ILayerAction action = _actions[_selectedIndex];
		if (!action.CanExecute(_node.DataSource, _project))
		{
			ShowMessage(action.GetDisabledReason(_node.DataSource, _project) ?? "Action is disabled.", ConsoleColor.Red);
			return;
		}
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		Console.CursorVisible = true;
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("═══ " + action.Name + " ═══");
		Console.ResetColor();
		if (!string.IsNullOrEmpty(action.Description))
		{
			Console.ForegroundColor = ConsoleColor.Gray;
			Console.WriteLine(action.Description);
			Console.ResetColor();
		}
		Console.WriteLine();
		Dictionary<string, object> promptParameters = PromptParameters(action);
		if (promptParameters == null)
		{
			Console.CursorVisible = false;
			return;
		}
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("═══ Executing: " + action.Name + " ═══");
		Console.ResetColor();
		Console.WriteLine();
		ProgressReporter progressReporter = new ProgressReporter();
		int lastProgress = -1;
		int progressLine = Console.CursorTop;
		int logStartLine = progressLine + 2;
		int maxLogLines = Math.Max(5, Console.WindowHeight - logStartLine - 6);
		List<string> logMessages = new List<string>();
		progressReporter.ProgressReported += (ProgressReporter sender, int progress) =>
		{
			if (progress != lastProgress)
			{
				lastProgress = progress;
				DrawProgressBar(progressLine, progress, action.Name);
			}
		};
		progressReporter.MessageReported += (ProgressReporter sender, Message msg) =>
		{
			string evaluatedMessage = msg.EvaluatedMessage;
			logMessages.Add(evaluatedMessage);
			while (logMessages.Count > maxLogLines)
			{
				logMessages.RemoveAt(0);
			}
			DrawLogMessages(logStartLine, logMessages, maxLogLines);
		};
		DrawProgressBar(progressLine, 0, action.Name);
		ActionResult actionResult;
		try
		{
			actionResult = action.Execute(_node.DataSource, _project, promptParameters, progressReporter);
		}
		catch (Exception ex)
		{
			actionResult = ActionResult.Failed(ex.Message);
		}
		DrawProgressBar(progressLine, 100, action.Name);
		int top = logStartLine + maxLogLines + 1;
		Console.SetCursorPosition(0, top);
		if (actionResult.Success)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("Success: " + (actionResult.Message ?? "Operation completed."));
			if (!string.IsNullOrEmpty(actionResult.OutputPath))
			{
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine("Output: " + actionResult.OutputPath);
			}
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("Failed: " + (actionResult.Message ?? "Unknown error."));
		}
		Console.ResetColor();
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.WriteLine("Press any key to continue...");
		Console.ResetColor();
		Console.ReadKey(intercept: true);
		Console.CursorVisible = false;
	}

	private Dictionary<string, object> PromptParameters(ILayerAction action)
	{
		if (action.Parameters.Count == 0)
		{
			return new Dictionary<string, object>();
		}
		Dictionary<string, object> objectByString = new Dictionary<string, object>();
		foreach (IActionParameter parameter in action.Parameters)
		{
			object defaultValue = action.GetParameterDefault(parameter.Name, _node.DataSource, _project) ?? parameter.DefaultValue;
			if (parameter is FilePathParameter param)
			{
				string promptFilePath = PromptFilePath(param, defaultValue);
				if (promptFilePath == null && parameter.IsRequired)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine("Cancelled.");
					Console.ResetColor();
					return null;
				}
				objectByString[parameter.Name] = promptFilePath ?? parameter.DefaultValue;
				continue;
			}
			Console.ForegroundColor = ConsoleColor.White;
			Console.Write(parameter.DisplayName ?? "");
			if (parameter.IsRequired)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write(" *");
			}
			Console.ResetColor();
			if (!string.IsNullOrEmpty(parameter.Description))
			{
				Console.ForegroundColor = ConsoleColor.DarkGray;
				Console.Write(" (" + parameter.Description + ")");
				Console.ResetColor();
			}
			Console.WriteLine();
			object promptForValue = PromptForValue(parameter, defaultValue);
			if (promptForValue == null && parameter.IsRequired)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("Cancelled.");
				Console.ResetColor();
				return null;
			}
			objectByString[parameter.Name] = promptForValue ?? parameter.DefaultValue;
		}
		return objectByString;
	}

	private string PromptFilePath(FilePathParameter param, object defaultValue)
	{
		string ensureExportDirectoryTerm = null;
		Ras.Layers.Result result = FindResultInHierarchy();
		if (result != null)
		{
			try
			{
				ensureExportDirectoryTerm = result.EnsureExportDirectory();
			}
			catch
			{
				ensureExportDirectoryTerm = Path.GetDirectoryName(result.Filename);
			}
		}
		else if (_project != null)
		{
			try
			{
				ensureExportDirectoryTerm = _project.EnsureDefaultExportDirectory();
			}
			catch
			{
				ensureExportDirectoryTerm = _project.ProjectDirectory;
			}
		}
		if (ensureExportDirectoryTerm == null)
		{
			ensureExportDirectoryTerm = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		}
		string defaultFilename = param.DefaultFilename;
		if (defaultValue is string textValue && !string.IsNullOrEmpty(textValue))
		{
			string directoryName = Path.GetDirectoryName(textValue);
			if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
			{
				ensureExportDirectoryTerm = directoryName;
			}
			defaultFilename = Path.GetFileName(textValue);
		}
		string result2 = new FolderBrowser(ensureExportDirectoryTerm, defaultFilename, param.Extension, param.DisplayName).Browse();
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		return result2;
	}

	private object PromptForValue(IActionParameter param, object defaultValue)
	{
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
		Console.Write("  > ");
		if (!string.IsNullOrEmpty(defaultValue2))
		{
			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.Write("[" + defaultValue2 + "] ");
			Console.ResetColor();
		}
		string line = Console.ReadLine();
		if (string.IsNullOrEmpty(line))
		{
			return defaultValue2;
		}
		string validate = param.Validate(line);
		if (validate != null)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("  " + validate);
			Console.ResetColor();
			return PromptString(param, defaultValue);
		}
		return line;
	}

	private bool PromptBool(IActionParameter param, object defaultValue)
	{
		bool valueOrDefault = defaultValue as bool? == true;
		Console.Write("  > (y/n) [" + (valueOrDefault ? "Y" : "N") + "]: ");
		string readLineConsoleTerm = Console.ReadLine()?.Trim().ToLowerInvariant();
		if (string.IsNullOrEmpty(readLineConsoleTerm))
		{
			return valueOrDefault;
		}
		switch (readLineConsoleTerm)
		{
		default:
			return readLineConsoleTerm == "1";
		case "y":
		case "yes":
		case "true":
			return true;
		}
	}

	private int PromptInt(IActionParameter param, object defaultValue)
	{
		int defaultValueTerm = ((defaultValue is int ordinal) ? ordinal : ((defaultValue is double computed) ? ((int)computed) : 0));
		Console.Write($"  > [{defaultValueTerm}]: ");
		string line = Console.ReadLine();
		if (string.IsNullOrEmpty(line))
		{
			return defaultValueTerm;
		}
		if (int.TryParse(line, out var result))
		{
			string validate = param.Validate(result);
			if (validate != null)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("  " + validate);
				Console.ResetColor();
				return PromptInt(param, defaultValue);
			}
			return result;
		}
		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine("  Invalid number.");
		Console.ResetColor();
		return PromptInt(param, defaultValue);
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
		Console.Write($"  > [{computed}]: ");
		string line = Console.ReadLine();
		if (string.IsNullOrEmpty(line))
		{
			return computed;
		}
		if (double.TryParse(line, out var result))
		{
			string validate = param.Validate(result);
			if (validate != null)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("  " + validate);
				Console.ResetColor();
				return PromptDouble(param, defaultValue);
			}
			return result;
		}
		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine("  Invalid number.");
		Console.ResetColor();
		return PromptDouble(param, defaultValue);
	}

	private Extent PromptExtent(IActionParameter param, object defaultValue)
	{
		Extent extent = ((defaultValue is Extent extent2) ? extent2 : Extent.NullExtent);
		Console.WriteLine("  Enter extent coordinates:");
		Console.Write($"    MinX [{extent.MinX}]: ");
		string line = Console.ReadLine();
		double minX = (string.IsNullOrEmpty(line) ? extent.MinX : double.Parse(line));
		Console.Write($"    MinY [{extent.MinY}]: ");
		string line2 = Console.ReadLine();
		double maxX = (string.IsNullOrEmpty(line2) ? extent.MinY : double.Parse(line2));
		Console.Write($"    MaxX [{extent.MaxX}]: ");
		string line3 = Console.ReadLine();
		double minY = (string.IsNullOrEmpty(line3) ? extent.MaxX : double.Parse(line3));
		Console.Write($"    MaxY [{extent.MaxY}]: ");
		string line4 = Console.ReadLine();
		double maxY = (string.IsNullOrEmpty(line4) ? extent.MaxY : double.Parse(line4));
		return new Extent(minX, maxX, minY, maxY);
	}

	private void ShowMessage(string message, ConsoleColor color)
	{
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		Console.ForegroundColor = color;
		Console.WriteLine(message);
		Console.ResetColor();
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.WriteLine("Press any key to continue...");
		Console.ResetColor();
		Console.ReadKey(intercept: true);
	}

	private static void DrawProgressBar(int line, int progress, string label)
	{
		Console.SetCursorPosition(0, line);
		int windowWidthOffset = Math.Min(50, Console.WindowWidth - 20);
		int progressTerm = (int)((double)(windowWidthOffset * progress) / 100.0);
		int count = windowWidthOffset - progressTerm;
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write("[");
		Console.ForegroundColor = ConsoleColor.Green;
		Console.Write(new string('=', progressTerm));
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.Write(new string('-', count));
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write($"] {progress,3}%");
		Console.ResetColor();
	}

	private static void DrawLogMessages(int startLine, List<string> messages, int maxLines)
	{
		int windowWidthOffset = Console.WindowWidth - 1;
		for (int i = 0; i < maxLines; i++)
		{
			Console.SetCursorPosition(0, startLine + i);
			if (i < messages.Count)
			{
				string message = messages[i];
				if (message.Length > windowWidthOffset)
				{
					message = message.Substring(0, windowWidthOffset - 3) + "...";
				}
				Console.ForegroundColor = ConsoleColor.DarkGray;
				Console.Write(message.PadRight(windowWidthOffset));
				Console.ResetColor();
			}
			else
			{
				Console.Write(new string(' ', windowWidthOffset));
			}
		}
	}

	private Ras.Layers.Result FindResultInHierarchy()
	{
		if (_node?.DataSource is Ras.Layers.Result result)
		{
			return result;
		}
		for (RasTreeNode rasTreeNode = _node?.Parent; rasTreeNode != null; rasTreeNode = rasTreeNode.Parent)
		{
			if (rasTreeNode.DataSource is Ras.Layers.Result result2)
			{
				return result2;
			}
		}
		return null;
	}
}
