using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class DashboardViewModelDisposeTests
{
    [Fact]
    public void Dispose_Unsubscribes_From_Session()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var vm = new DashboardViewModel(session);

        transport.Emit(new DeviceState { AngleDeciDeg = 100 });
        Assert.Equal(10.0, vm.AngleDegrees);

        vm.Dispose();
        transport.Emit(new DeviceState { AngleDeciDeg = 2000 });
        Assert.Equal(10.0, vm.AngleDegrees); // unchanged after dispose
    }
}
