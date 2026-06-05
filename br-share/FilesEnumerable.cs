using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Innovoft.IO.Compression;

internal sealed class FilesEnumerable : IEnumerable<string>
{
	private readonly string path;
	private readonly string pattern;
	private readonly SearchOption option;

	public FilesEnumerable(string path, string pattern, SearchOption option)
	{
		this.path = path;
		this.pattern = pattern;
		this.option = option;
	}

	public IEnumerator<string> GetEnumerator()
	{
		var enumerable = Directory.EnumerateFiles(path, pattern, option);
		var enumerator = enumerable.GetEnumerator();
		return enumerator;
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
