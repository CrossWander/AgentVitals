using System;
using System.IO;

namespace AgentVitals.Tests;

/// <summary>Временная папка теста, удаляется вместе с содержимым.</summary>
internal sealed class TempDirectory : IDisposable
{
	public TempDirectory() => Directory.CreateDirectory(Path);

	public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentvitals-tests", Guid.NewGuid().ToString("N"));

	public string File(string name) => System.IO.Path.Combine(Path, name);

	public void Dispose() => Directory.Delete(Path, recursive: true);
}
