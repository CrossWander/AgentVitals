using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AgentVitals.Tests;

/// <summary>Кто кого знает: каждый проект — только те, что ниже, и ни один — интерфейс (Avalonia).</summary>
public class ArchitectureTests
{
	[Theory]
	[InlineData("AgentVitals.Platform", "")]
	[InlineData("AgentVitals.Core", "AgentVitals.Platform")]
	[InlineData("AgentVitals.Agents", "AgentVitals.Core AgentVitals.Platform")]
	public void ModulesKnowOnlyWhatLiesBelow(string module, string below) =>
		Assert.Equal(
			below.Split(' ', StringSplitOptions.RemoveEmptyEntries),
			Assembly.Load(module).GetReferencedAssemblies()
				.Select(name => name.Name!)
				.Where(name => name.StartsWith("AgentVitals", StringComparison.Ordinal) || name.StartsWith("Avalonia", StringComparison.Ordinal))
				.Order(StringComparer.Ordinal));
}
