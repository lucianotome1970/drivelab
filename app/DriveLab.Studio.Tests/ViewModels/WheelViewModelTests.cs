using System.IO;
using System.Linq;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class WheelViewModelTests
{
    private static WheelViewModel New(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"wheelvm-{System.Guid.NewGuid():N}.json");
        return new WheelViewModel(new JsonWheelProfileStorage(path));
    }

    [Fact]
    public void Builds_Eight_Buttons_With_Default_Colors()
    {
        var vm = New(out _);
        Assert.Equal(8, vm.Buttons.Count);
        Assert.Equal("#BF5AF2", vm.Buttons.First(b => b.Name == "N").ColorHex);
        Assert.Equal("#34C759", vm.Buttons.First(b => b.Name == "DRS").ColorHex);
    }

    [Fact]
    public void SelectButton_Marks_One_Selected()
    {
        var vm = New(out _);
        var pit = vm.Buttons.First(b => b.Name == "PIT");
        vm.SelectButtonCommand.Execute(pit);
        Assert.True(pit.IsSelected);
        Assert.Same(pit, vm.SelectedButton);
        Assert.Equal(1, vm.Buttons.Count(b => b.IsSelected));
    }

    [Fact]
    public void SetColor_Applies_To_Selected_Button()
    {
        var vm = New(out _);
        var n = vm.Buttons.First(b => b.Name == "N");
        vm.SelectButtonCommand.Execute(n);
        vm.SetColorCommand.Execute("#0A84FF");
        Assert.Equal("#0A84FF", n.ColorHex);
    }

    [Fact]
    public void SetColor_NoOp_When_Nothing_Selected()
    {
        var vm = New(out _);
        vm.SetColorCommand.Execute("#0A84FF");
        Assert.All(vm.Buttons, b => Assert.NotEqual("#0A84FF", b.ColorHex));
    }

    [Fact]
    public void SetPaddleCount_Toggles_ShowBottomPair()
    {
        var vm = New(out _);
        vm.SetPaddleCountCommand.Execute("2");
        Assert.Equal(2, vm.PaddleCount);
        Assert.False(vm.ShowBottomPair);
        vm.SetPaddleCountCommand.Execute("4");
        Assert.True(vm.ShowBottomPair);
    }

    [Fact]
    public async Task Save_Then_Load_Restores_Colors_And_Paddles()
    {
        var vm = New(out var path);
        try
        {
            var n = vm.Buttons.First(b => b.Name == "N");
            vm.SelectButtonCommand.Execute(n);
            vm.SetColorCommand.Execute("#FFFFFF");
            vm.SetPaddleCountCommand.Execute("4");
            vm.BottomPair.SetFunctionCommand.Execute("Free");
            await vm.SaveCommand.ExecuteAsync(null);

            var vm2 = new WheelViewModel(new JsonWheelProfileStorage(path));
            await vm2.LoadCommand.ExecuteAsync(null);

            Assert.Equal("#FFFFFF", vm2.Buttons.First(b => b.Name == "N").ColorHex);
            Assert.Equal(PaddleFunction.Free, vm2.BottomPair.Function);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
