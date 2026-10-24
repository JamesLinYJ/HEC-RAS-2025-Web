using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ras.Process.RasCommands.Explorer.Actions;

public class FolderBrowser
{
	private class DirectoryEntry
	{
		public string Name { get; set; }

		public string FullPath { get; set; }

		public bool IsDirectory { get; set; }

		public bool IsParent { get; set; }

		public bool IsError { get; set; }

		public bool IsPickFolder { get; set; }
	}

	private string _currentPath;

	private string _filename;

	private readonly string _extension;

	private readonly string _title;

	private int _selectedIndex;

	private int _scrollOffset;

	private List<DirectoryEntry> _entries = new List<DirectoryEntry>();

	private bool _running = true;

	private bool _confirmed;

	private bool _filenameMode;

	public FolderBrowser(string startPath, string defaultFilename, string extension, string title = "Select Location")
	{
		_currentPath = (Directory.Exists(startPath) ? startPath : Environment.GetFolderPath(Environment.SpecialFolder.Personal));
		_filename = defaultFilename ?? ("output" + extension);
		_extension = extension;
		_title = title;
	}

	public string Browse()
	{
		Console.CursorVisible = false;
		_running = true;
		_confirmed = false;
		_filenameMode = false;
		LoadDirectory();
		while (_running)
		{
			Render();
			ProcessInput();
		}
		if (_confirmed)
		{
			return Path.Combine(_currentPath, _filename);
		}
		return null;
	}

	private void LoadDirectory()
	{
		_entries.Clear();
		_selectedIndex = 0;
		_scrollOffset = 0;
		_entries.Add(new DirectoryEntry
		{
			Name = "[ Pick this Folder ]",
			FullPath = _currentPath,
			IsPickFolder = true
		});
		try
		{
			DirectoryInfo parent = Directory.GetParent(_currentPath);
			if (parent != null)
			{
				_entries.Add(new DirectoryEntry
				{
					Name = "..",
					FullPath = parent.FullName,
					IsDirectory = true,
					IsParent = true
				});
			}
			foreach (string fullPath in from d in Directory.GetDirectories(_currentPath)
				orderby Path.GetFileName(d)
				select d)
			{
				try
				{
					string fileName = Path.GetFileName(fullPath);
					if (!fileName.StartsWith("."))
					{
						_entries.Add(new DirectoryEntry
						{
							Name = fileName,
							FullPath = fullPath,
							IsDirectory = true
						});
					}
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			_entries.Add(new DirectoryEntry
			{
				Name = "Error: " + ex.Message,
				IsError = true
			});
		}
	}

	private void Render()
	{
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		int windowWidth = Console.WindowWidth;
		int windowHeight = Console.WindowHeight;
		int count = windowHeight - 10;
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("═══ " + _title + " ═══");
		Console.ResetColor();
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.Write("Directory: ");
		Console.ForegroundColor = ConsoleColor.White;
		string currentPath = _currentPath;
		if (currentPath.Length > windowWidth - 15)
		{
			currentPath = "..." + currentPath.Substring(currentPath.Length - (windowWidth - 18));
		}
		Console.WriteLine(currentPath);
		Console.ResetColor();
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.WriteLine("─── Folders ───");
		Console.ResetColor();
		Math.Min(count, _entries.Count);
		if (_selectedIndex < _scrollOffset)
		{
			_scrollOffset = _selectedIndex;
		}
		else if (_selectedIndex >= _scrollOffset + count)
		{
			_scrollOffset = _selectedIndex - count + 1;
		}
		for (int i = 0; i < count; i++)
		{
			int scrollOffsetTerm = i + _scrollOffset;
			if (scrollOffsetTerm >= _entries.Count)
			{
				Console.WriteLine();
				continue;
			}
			DirectoryEntry directoryEntry = _entries[scrollOffsetTerm];
			bool notFilenameMode = scrollOffsetTerm == _selectedIndex && !_filenameMode;
			string notFilenameModeTerm = (notFilenameMode ? " > " : "   ");
			string substringTerm = directoryEntry.Name;
			int windowWidthOffset = windowWidth - 6;
			if (substringTerm.Length > windowWidthOffset)
			{
				substringTerm = substringTerm.Substring(0, windowWidthOffset - 3) + "...";
			}
			if (notFilenameMode)
			{
				Console.BackgroundColor = ConsoleColor.DarkBlue;
				Console.ForegroundColor = ConsoleColor.White;
			}
			Console.Write(notFilenameModeTerm);
			if (directoryEntry.IsPickFolder)
			{
				Console.ForegroundColor = ConsoleColor.Green;
			}
			else if (directoryEntry.IsError)
			{
				Console.ForegroundColor = ConsoleColor.Red;
			}
			else if (directoryEntry.IsParent)
			{
				Console.ForegroundColor = (notFilenameMode ? ConsoleColor.Yellow : ConsoleColor.DarkYellow);
			}
			else if (directoryEntry.IsDirectory)
			{
				Console.ForegroundColor = (notFilenameMode ? ConsoleColor.Cyan : ConsoleColor.DarkCyan);
			}
			else
			{
				Console.ForegroundColor = (notFilenameMode ? ConsoleColor.White : ConsoleColor.Gray);
			}
			Console.WriteLine(substringTerm.PadRight(windowWidthOffset));
			Console.ResetColor();
		}
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.WriteLine("─── Filename ───");
		Console.ResetColor();
		if (_filenameMode)
		{
			Console.BackgroundColor = ConsoleColor.DarkBlue;
			Console.ForegroundColor = ConsoleColor.White;
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Gray;
		}
		Console.Write("   ");
		Console.Write(_filename);
		if (_filenameMode)
		{
			Console.Write("_");
		}
		Console.WriteLine();
		Console.ResetColor();
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.Write("Output: ");
		string path = Path.Combine(_currentPath, _filename);
		if (path.Length > windowWidth - 10)
		{
			path = "..." + path.Substring(path.Length - (windowWidth - 13));
		}
		Console.WriteLine(path);
		Console.ResetColor();
		Console.SetCursorPosition(0, windowHeight - 2);
		Console.ForegroundColor = ConsoleColor.DarkGray;
		Console.Write(new string('─', windowWidth));
		Console.SetCursorPosition(0, windowHeight - 1);
		if (_filenameMode)
		{
			Console.Write(" Type filename │ Enter Confirm │ Tab Switch to folders │ Esc Cancel");
		}
		else
		{
			Console.Write(" Up/Down Navigate │ Enter Select │ Tab Edit filename │ Esc Cancel");
		}
		Console.ResetColor();
	}

	private void ProcessInput()
	{
		ConsoleKeyInfo key = Console.ReadKey(intercept: true);
		if (_filenameMode)
		{
			ProcessFilenameInput(key);
		}
		else
		{
			ProcessFolderInput(key);
		}
	}

	private void ProcessFolderInput(ConsoleKeyInfo key)
	{
		switch (key.Key)
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
			if (_selectedIndex < _entries.Count - 1)
			{
				_selectedIndex++;
			}
			break;
		case ConsoleKey.Enter:
			if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
			{
				DirectoryEntry directoryEntry = _entries[_selectedIndex];
				if (directoryEntry.IsPickFolder)
				{
					TryConfirmSelection();
				}
				else if (directoryEntry.IsDirectory && !directoryEntry.IsError)
				{
					_currentPath = directoryEntry.FullPath;
					LoadDirectory();
				}
			}
			break;
		case ConsoleKey.Tab:
			_filenameMode = true;
			break;
		case ConsoleKey.F2:
			TryConfirmSelection();
			break;
		case ConsoleKey.Escape:
			_running = false;
			break;
		}
	}

	private void ProcessFilenameInput(ConsoleKeyInfo key)
	{
		switch (key.Key)
		{
		case ConsoleKey.Tab:
			_filenameMode = false;
			break;
		case ConsoleKey.Enter:
			TryConfirmSelection();
			break;
		case ConsoleKey.Escape:
			_filenameMode = false;
			break;
		case ConsoleKey.Backspace:
			if (_filename.Length > 0)
			{
				_filename = _filename.Substring(0, _filename.Length - 1);
			}
			break;
		default:
			if (!char.IsControl(key.KeyChar) && IsValidFilenameChar(key.KeyChar))
			{
				_filename += key.KeyChar;
			}
			break;
		}
	}

	private void TryConfirmSelection()
	{
		if (_extension != null && !_filename.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
		{
			_filename += _extension;
		}
		string path = Path.Combine(_currentPath, _filename);
		if (File.Exists(path))
		{
			if (PromptOverwrite(path))
			{
				File.Delete(path);
				_confirmed = true;
				_running = false;
			}
		}
		else
		{
			_confirmed = true;
			_running = false;
		}
	}

	private bool PromptOverwrite(string path)
	{
		Console.Clear();
		Console.SetCursorPosition(0, 0);
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine("File already exists:");
		Console.ResetColor();
		Console.WriteLine();
		Console.WriteLine("  " + path);
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write("Overwrite? (");
		Console.ForegroundColor = ConsoleColor.Green;
		Console.Write("Y");
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write("/");
		Console.ForegroundColor = ConsoleColor.Red;
		Console.Write("N");
		Console.ForegroundColor = ConsoleColor.White;
		Console.Write("): ");
		Console.ResetColor();
		ConsoleKeyInfo consoleKeyInfo;
		do
		{
			consoleKeyInfo = Console.ReadKey(intercept: true);
			if (consoleKeyInfo.Key == ConsoleKey.Y)
			{
				return true;
			}
		}
		while (consoleKeyInfo.Key != ConsoleKey.N && consoleKeyInfo.Key != ConsoleKey.Escape);
		return false;
	}

	private static bool IsValidFilenameChar(char c)
	{
		return !Path.GetInvalidFileNameChars().Contains(c);
	}
}
