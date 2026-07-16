using System;
using System.IO;

namespace GamingMode.Services;

public sealed class FileLogger
{
	private readonly string _path;

	private readonly object _sync = new object();

	public FileLogger(string path)
	{
		_path = path;
	}

	public void Info(string message)
	{
		Write("INFO", message);
	}

	public void Error(string message, Exception? exception = null)
	{
		string message2 = ((exception == null) ? message : $"{message}: {exception}");
		Write("ERROR", message2);
	}

	private void Write(string level, string message)
	{
		lock (_sync)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
		}
	}
}
