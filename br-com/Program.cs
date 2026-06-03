using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace Innovoft.IO.Compression;

internal static class Program
{
	private const int bufferLength = 4096;

	internal static int Main(string[] args)
	{
		var errors = ParseCommandLine(args);
		if (errors != null)
		{
			PrintHelp(errors);
			return -1;
		}
		Prepare();
		return Run();
	}

	private static string? searchPath = null;
	private static string? searchPattern = null;
	private static SearchOption searchOption = SearchOption.AllDirectories;
	private static FileShare searchShare = FileShare.None;
	private static bool delete = false;
	private static int threadsCount = 1;

	private static IEnumerator<string> files;
	private static long filesCount = 0;

	private static List<string>? ParseCommandLine(string[] args)
	{
		var errors = new List<string>();
		if (args == null || args.Length <= 0)
		{
			return errors;
		}

		for (var i = 0; i < args.Length; ++i)
		{
			switch (args[i])
			{
			default:
				errors.Add("Invalid Parameter " + args[i]);
				break;

			case "-SearchPath":
				try
				{
					searchPath = args[++i];
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -SearchPath " + exception.Message);
				}
				break;

			case "-SearchPattern":
				try
				{
					searchPattern = args[++i];
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -SearchPattern " + exception.Message);
				}
				break;

			case "-SearchOption":
				try
				{
					searchOption = Enum.Parse<SearchOption>(args[++i]);
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -SearchOption " + exception.Message);
				}
				break;

			case "-SearchShare":
				try
				{
					switch (args[++i])
					{
					default:
						errors.Add("Invalid -SearchShare option " + args[i]);
						break;

					case "Read":
						searchShare = FileShare.Read;
						break;

					case "None":
						searchShare = FileShare.None;
						break;
					}
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -SearchShare " + exception.Message);
				}
				break;

			case "-Delete":
				try
				{
					delete = bool.Parse(args[++i]);
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -Delete " + exception.Message);
				}
				break;

			case "-Threads":
				try
				{
					threadsCount = int.Parse(args[++i]);
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -Threads " + exception.Message);
				}
				break;
			}
		}

		if (searchPath == null)
		{
			searchPath = ".";
		}
		if (searchPattern == null)
		{
			errors.Add("Missing -SearchPattern");
		}

		if (errors.Count <= 0)
		{
			return null;
		}
		else
		{
			return errors;
		}
	}

	private static void PrintHelp(List<string> errors)
	{
		Console.WriteLine();
		Console.WriteLine("br-com.exe");
		Console.WriteLine(" Required");
		Console.WriteLine("  -SearchPattern *.*");
		Console.WriteLine(" Optional");
		Console.WriteLine("  -SearchPath .");
		Console.WriteLine("  -SearchOption { AllDirectories, TopDirectoryOnly }");
		Console.WriteLine("  -SearchShare { None, Read }");
		Console.WriteLine("  -Delete { false, true }");
		Console.WriteLine("  -Threads 1 //0 use the number of processors");
		Console.WriteLine();

		if (errors != null && errors.Count > 0)
		{
			foreach (var error in errors)
			{
				Console.Error.WriteLine(error);
			}
			Console.WriteLine();
		}
	}

	private static void Prepare()
	{
		PrepareThreads();
	}

	private static void PrepareThreads()
	{
		if (threadsCount > 0)
		{
			return;
		}

		threadsCount = Environment.ProcessorCount;
	}

	private static int Run()
	{
		try
		{
			var starting = Stopwatch.GetTimestamp();
			files = Directory.EnumerateFiles(searchPath, searchPattern, searchOption).GetEnumerator();

			var threads = new Thread[threadsCount];
			var threadStart = new ThreadStart(Work);
			for (var i = threads.Length - 1; i >= 0; --i)
			{
				var thread = new Thread(threadStart);
				threads[i] = thread;
				thread.Start();
			}

			foreach (var thread in threads)
			{
				thread.Join();
			}

			var took = Stopwatch.GetElapsedTime(starting);
			Console.WriteLine("Compressed {0} {1}", filesCount, took);
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return -1;
		}
		finally
		{
			files?.Dispose();
		}
		return 0;
	}

	private static void Work()
	{
		var buffer = new byte[bufferLength];

		while (true)
		{
			string sourcePath;
			var locked = false;
			try
			{
				Monitor.Enter(files, ref locked);
				if (locked)
				{
					if (files.MoveNext())
					{
						sourcePath = files.Current;
					}
					else
					{
						break;
					}
				}
				else
				{
					break;
				}
			}
			finally
			{
				if (locked)
				{
					Monitor.Exit(files);
				}
			}

			var starting = Stopwatch.GetTimestamp();
			var resultPath = sourcePath + ".br";
			var resultUndo = false;
			var sourceStream = (FileStream?)null;
			var resultStream = (FileStream?)null;
			var resultWriter = (BrotliStream?)null;
			try
			{
				try
				{
					sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, searchShare);
				}
				catch
				{
					lock (Console.Out)
					{
						Console.Error.WriteLine("{0} Can't open", sourcePath);
					}
					continue;
				}
				try
				{
					resultStream = new FileStream(resultPath, FileMode.Create, FileAccess.Write, FileShare.Read);
				}
				catch
				{
					lock (Console.Out)
					{
						Console.Error.WriteLine("{0} Can't create", sourcePath);
					}
					continue;
				}
				try
				{
					resultWriter = new BrotliStream(resultStream, CompressionLevel.SmallestSize);
					while (true)
					{
						var read = sourceStream.Read(buffer, 0, bufferLength);
						if (read <= 0)
						{
							break;
						}
						resultWriter.Write(buffer, 0, read);
					}
				}
				catch
				{
					resultUndo = true;
					lock (Console.Out)
					{
						Console.Error.WriteLine("{0} Can't write", sourcePath);
					}
					continue;
				}
			}
			finally
			{
				resultWriter?.Dispose();
				resultStream?.Dispose();
				sourceStream?.Dispose();
				if (resultUndo)
				{
					try
					{
						File.Delete(resultPath);
					}
					catch
					{
						lock (Console.Out)
						{
							Console.Error.WriteLine("{0} Can't undo", sourcePath);
						}
					}
				}
			}
			Interlocked.Increment(ref filesCount);
			var deleteException = (Exception?)null;
			if (delete)
			{
				try
				{
					File.Delete(sourcePath);
				}
				catch (Exception exception)
				{
					deleteException = exception;
				}
			}
			var took = Stopwatch.GetElapsedTime(starting);
			lock (Console.Out)
			{
				if (deleteException == null)
				{
					Console.Out.WriteLine("{0} {1}", sourcePath, took);
				}
				else
				{
					Console.Out.WriteLine("{0} {1} {2}", sourcePath, took, deleteException?.Message);
				}
			}
		}
	}
}
