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

    private static HandbrakeViewModel Make() => Make(out _);

    private static HandbrakeViewModel Make(out FakeHandbrakeTransport transport)
    {
        transport = new FakeHandbrakeTransport();
        var s = new HandbrakeDeviceSession(transport, new ImmediateUiDispatcher());
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

    // Regressão: se uma leitura estourar (0x16 perdido/timeout), LoadAsync não pode deixar
    // _loading travado — senão toda edição é suprimida e o Salvar nunca habilita.
    [Fact]
    public async Task Failed_Load_Does_Not_Brick_Editing()
    {
        var vm = Make(out var t);
        t.ThrowOnRead = true;
        // conecta; LoadAsync estoura no 1º read e propaga — o DeviceAutoConnector engole (try/catch),
        // mas a conexão já foi estabelecida (Connected disparou antes do LoadAsync falhar).
        try { await vm.ConnectCommand.ExecuteAsync(null); } catch (TimeoutException) { }
        Assert.True(vm.IsConnected);
        Assert.False(vm.IsDirty);

        vm.Smooth = 30;                                   // edição deve registrar (não ficar suprimida)
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.Dispose();
    }
}
