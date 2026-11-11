using Xunit;

namespace AgentVitals.Tests;

/// <summary>config.json правит человек: что он туда напишет, не должно ронять запуск.</summary>
public class StoreTests
{
	[Fact]
	public void NullListsReadAsEmpty() // "switchedOff": null ронял программу ещё до значка в трее
	{
		var data = Store.Parse("""{ "switchedOff": null, "agentOrder": null, "accountNames": null }""");

		Assert.Equal((0, 0, 0), (data.SwitchedOff.Count, data.AgentOrder.Count, data.AccountNames.Count));
	}
}
