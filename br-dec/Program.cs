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

	private static readonly Queue<IEnumerable<string>> filess = new Queue<IEnumerable<string>>();
	private static FileShare searchShare = FileShare.None;
	private static Converter<string, string> resultPath;
	private static string? resultPathExt;
	private static bool delete = false;
	private static int threadsCount = 1;
	private static ThreadPriority? threadPriority = ThreadPriority.Lowest;
	private static ProcessPriorityClass? processPriority = ProcessPriorityClass.Idle;

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

			case "-Search":
				try
				{
					var path = args[++i];
					var pattern = args[++i];
					var optionParse = args[++i];
					SearchOption option;
					switch (optionParse)
					{
					default:
						option = Enum.Parse<SearchOption>(optionParse);
						break;

					case "All":
					case "all":
						option = SearchOption.AllDirectories;
						break;

					case "Top":
					case "top":
						option = SearchOption.TopDirectoryOnly;
						break;
					}
					var files = new FilesEnumerable(path, pattern, option);
					filess.Enqueue(files);
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -Search " + exception.Message);
				}
				break;

			case "-File":
				try
				{
					var path = args[++i];
					var files = new string[1] { path };
					filess.Enqueue(files);
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -File " + exception.Message);
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

			case "-ThreadPriority":
				try
				{
					var parse = args[++i];
					switch (parse)
					{
					default:
						threadPriority = Enum.Parse<ThreadPriority>(parse);
						break;

					case "Leave":
						threadPriority = null;
						break;
					}
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -ThreadPriority " + exception.Message);
				}
				break;

			case "-ProcessPriority":
				try
				{
					var parse = args[++i];
					switch (parse)
					{
					default:
						processPriority = Enum.Parse<ProcessPriorityClass>(parse);
						break;

					case "Leave":
						processPriority = null;
						break;
					}
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -ProcessPriority " + exception.Message);
				}
				break;

			case "-ResultPathExtRemove":
				try
				{
					resultPath = ResultPathExtRemove;
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -ResultPathExtRemove " + exception.Message);
				}
				break;

			case "-ResultPathExtReplace":
				try
				{
					resultPathExt = args[++i];
					resultPath = ResultPathExtReplace;
				}
				catch (Exception exception)
				{
					errors.Add("Problems processing -ResultPathExtReplace " + exception.Message);
				}
				break;
			}
		}

		if (filess.Count <= 0)
		{
			errors.Add("Requires atleast one -Search -File");
		}

		if (resultPath == null)
		{
			resultPath = ResultPathExtRemove;
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
		Console.WriteLine("br-dec.exe");
		Console.WriteLine(" Requires atleast one");
		Console.WriteLine("  -Search Path Pattern { All, Top, AllDirectories, TopDirectoryOnly }");
		Console.WriteLine("  -File Path\\File.ext");
		Console.WriteLine(" Optional");
		Console.WriteLine("  -SearchShare { None, Read }");
		Console.WriteLine("  -Delete { false, true }");
		Console.WriteLine("  -Threads 1 //0 use the number of processors");
		Console.WriteLine("  -ThreadPriority { Lowest, BelowNormal, Normal, AboveNormal, Highest, Leave }");
		Console.WriteLine(" Optional only one");
		Console.WriteLine("  -ResultPathExtRemove");
		Console.WriteLine("  -ResultPathExtReplace .ext");
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
		Console.CancelKeyPress += CancelKeyPress;

		try
		{
			if (processPriority.HasValue)
			{
				Process.GetCurrentProcess().PriorityClass = processPriority.Value;
			}

			var starting = Stopwatch.GetTimestamp();

			files = filess.Dequeue().GetEnumerator();
			Console.WriteLine("{0:dd HH:mm:ss.fff} Started", DateTime.Now);

			var threads = new Thread[threadsCount];
			var threadStart = new ThreadStart(Work);
			for (var i = threads.Length - 1; i >= 0; --i)
			{
				var thread = new Thread(threadStart);
				if (threadPriority.HasValue)
				{
					thread.Priority = threadPriority.Value;
				}
				threads[i] = thread;
				thread.Start();
			}

			foreach (var thread in threads)
			{
				thread.Join();
			}

			var took = Stopwatch.GetElapsedTime(starting);
			Console.WriteLine("{0:dd HH:mm:ss.fff} {1:hh\\:mm\\:ss.fff} Deompressed {2}", DateTime.Now, took, filesCount);
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

	private static void CancelKeyPress(object? sender, ConsoleCancelEventArgs args)
	{
		args.Cancel = true;

		lock (Console.Out)
		{
			Console.Error.WriteLine("{0:dd HH:mm:ss.fff} Canceled     will not start any new decompressions", DateTime.Now);
		}

		var locked = false;
		try
		{
			Monitor.Enter(filess, ref locked);
			if (!locked)
			{
				return;
			}
			while (files.MoveNext()) ;
			while (filess.TryDequeue(out var dequeue)) ;
		}
		finally
		{
			if (locked)
			{
				Monitor.Exit(filess);
			}
		}
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
				Monitor.Enter(filess, ref locked);
				if (!locked)
				{
					return;
				}
				while (true)
				{
					if (files.MoveNext())
					{
						sourcePath = files.Current;
						break;
					}
					if (!filess.TryDequeue(out var dequeue))
					{
						return;
					}
					files.Dispose();
					files = dequeue.GetEnumerator();
					continue;
				}
			}
			finally
			{
				if (locked)
				{
					Monitor.Exit(filess);
				}
			}

			var starting = Stopwatch.GetTimestamp();
			var resultPath = Program.resultPath(sourcePath);
			var resultUndo = false;
			var sourceStream = (FileStream?)null;
			var resultStream = (FileStream?)null;
			var sourceReader = (BrotliStream?)null;
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
						Console.Error.WriteLine("{0:dd HH:mm:ss.fff} Can't open   {1}", DateTime.Now, resultPath);
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
						Console.Error.WriteLine("{0:dd HH:mm:ss.fff} Can't create {1}", DateTime.Now, resultPath);
					}
					continue;
				}
				try
				{
					sourceReader = new BrotliStream(sourceStream, CompressionMode.Decompress);
					while (true)
					{
						var read = sourceReader.Read(buffer, 0, bufferLength);
						if (read <= 0)
						{
							break;
						}
						resultStream.Write(buffer, 0, read);
					}
				}
				catch
				{
					resultUndo = true;
					lock (Console.Out)
					{
						Console.Error.WriteLine("{0:dd HH:mm:ss.fff} Can't write  {1}", DateTime.Now, resultPath);
					}
					continue;
				}
			}
			finally
			{
				sourceReader?.Dispose();
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
							Console.Error.WriteLine("{0:dd HH:mm:ss.fff} Can't undo   {1}", DateTime.Now, resultPath);
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
					Console.Out.WriteLine("{0:dd HH:mm:ss.fff} {1:hh\\:mm\\:ss.fff}", DateTime.Now, took, resultPath);
				}
				else
				{
					Console.Out.WriteLine("{0:dd HH:mm:ss.fff} {1:hh\\:mm\\:ss.fff} {2} : {3}", DateTime.Now, took, resultPath, deleteException?.Message);
				}
			}
		}
	}

	private static string ResultPathExtRemove(string sourcePath)
	{
		var resultPath = Path.Combine(
			Path.GetDirectoryName(sourcePath),
			Path.GetFileNameWithoutExtension(sourcePath));
		return resultPath;
	}

	private static string ResultPathExtReplace(string sourcePath)
	{
		var resultPath = Path.Combine(
			Path.GetDirectoryName(sourcePath),
			Path.GetFileNameWithoutExtension(sourcePath) + resultPathExt);
		return resultPath;
	}
}
