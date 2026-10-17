using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVitals.Platform;

/// <summary>Чтение файлов, которые в это же время переписывают Claude Code, git или наши hooks.</summary>
public static class SharedFile
{
	// File.ReadAllText делит файл только на чтение: пока мы читаем, владелец не может подменить или удалить его.
	public static FileStream Open(string path) =>
		new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

	public static string ReadAllText(string path)
	{
		using var reader = new StreamReader(Open(path));

		return reader.ReadToEnd();
	}

	public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellation)
	{
		using var reader = new StreamReader(Open(path));

		return await reader.ReadToEndAsync(cancellation);
	}
}
