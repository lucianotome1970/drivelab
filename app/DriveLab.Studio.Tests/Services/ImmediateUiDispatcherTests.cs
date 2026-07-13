using Xunit;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class ImmediateUiDispatcherTests
{
    [Fact]
    public void Post_Runs_Action_Synchronously()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var ran = false;

        dispatcher.Post(() => ran = true);

        Assert.True(ran);
    }
}
