// ============================================================================
//  DriveLab
//  HandbrakeViewModelTests.cs — Testes do VM do freio de mão (dirty-tracking do Salvar).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class HandbrakeViewModelTests
{
    private sealed class FakeStorage : IHandbrakeProfileStorage
    {
        public Task SaveAsync(HandbrakeProfile profile) => Task.CompletedTask;
        public Task<HandbrakeProfile?> LoadAsync() => Task.FromResult<HandbrakeProfile?>(null);
    }

    private static HandbrakeViewModel Make()
    {
        var t = new FakeHandbrakeTransport();
        var s = new HandbrakeDeviceSession(t, new ImmediateUiDispatcher());
        return new HandbrakeViewModel(s, new FakeStorage());
    }

    [Fact]
    public async Task Save_Enabled_Only_When_Dirty()
    {
        var vm = Make();
        await vm.ConnectCommand.ExecuteAsync(null);      // conecta + carrega da placa
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveCommand.CanExecute(null));   // nada alterado

        vm.Smooth = 30;                                   // usuário altera → escreve → dirty
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));

        await vm.SaveCommand.ExecuteAsync(null);          // salva na flash
        Assert.False(vm.IsDirty);                         // firmware == app
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Dispose();
    }
}
