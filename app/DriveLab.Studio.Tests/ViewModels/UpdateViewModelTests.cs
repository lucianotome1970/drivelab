// ============================================================================
//  DriveLab
//  UpdateViewModelTests.cs — Testes do módulo de atualização de firmware:
//  gate de validação (Send só habilita com firmware válido) e o fluxo
//  EnterDfu → WaitForBootloader → Flash, incluindo os caminhos de falha.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text;
using DriveLab.Core.Update;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class UpdateViewModelTests
{
    private static byte[] MakeFirmwareBytes(DeviceKind kind, byte major = 1, byte minor = 2, byte patch = 3) =>
        Encoding.ASCII.GetBytes("DRVLABFW").Concat(new byte[] { (byte)kind, major, minor, patch }).ToArray();

    private sealed class FakeUpdater : IDeviceUpdater
    {
        public DeviceKind Kind => DeviceKind.Base;
        public string BootloaderName => "FAKE BOOTLOADER";
        public bool EnterCalled;
        public bool FlashCalled;
        public bool BootloaderFound = true;
        public Queue<bool>? BootloaderResults;   // se setado, cada Wait consome um resultado (roteiro auto→manual)
        public Exception? FlashThrows;
        public string? LastFlashPath;
        public List<string>? Events;

        public bool ValidateFirmware(byte[] file, out string error)
        {
            var info = FirmwareFile.Read(file);
            if (!info.Found) { error = "sem assinatura"; return false; }
            if (info.Kind != Kind) { error = $"firmware é para {info.Kind}"; return false; }
            error = "";
            return true;
        }

        public Task EnterBootloaderAsync()
        {
            EnterCalled = true;
            Events?.Add("enter");
            return Task.CompletedTask;
        }

        public Task<bool> WaitForBootloaderAsync(TimeSpan timeout)
        {
            Events?.Add("wait");
            if (BootloaderResults is { Count: > 0 })
                return Task.FromResult(BootloaderResults.Dequeue());
            return Task.FromResult(BootloaderFound);
        }

        /// <summary>Presença do bootloader para a VIGÍLIA da tela (o aviso "pronta para gravar").
        ///
        /// <para>Separada de <see cref="WaitForBootloaderAsync"/> de propósito: aquela é consumida
        /// pelo fluxo de gravação e os testes controlam a fila dela passo a passo. Se a vigília, que
        /// roda sozinha em segundo plano, comesse da mesma fila, ela roubaria respostas e os testes
        /// de gravação falhariam de forma intermitente — dependendo de quem chegasse primeiro.</para></summary>
        public bool BootloaderPresent { get; set; }
        public Task<bool> IsBootloaderPresentAsync() => Task.FromResult(BootloaderPresent);

        public Task FlashAsync(string filePath, IProgress<double>? progress, CancellationToken ct = default)
        {
            FlashCalled = true;
            LastFlashPath = filePath;
            Events?.Add("flash");
            if (FlashThrows is not null)
                throw FlashThrows;
            progress?.Report(0.5);
            progress?.Report(1.0);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoordinator : IDeviceAccessCoordinator
    {
        public int BeginCalls;
        public int EndCalls;
        public List<string>? Events;

        public Task BeginExclusiveAsync(DeviceKind kind)
        {
            BeginCalls++;
            Events?.Add("begin");
            return Task.CompletedTask;
        }

        public Task EndExclusiveAsync(DeviceKind kind)
        {
            EndCalls++;
            Events?.Add("end");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFilePicker : IFilePicker
    {
        public string? PathToReturn;
        public Task<string?> PickFirmwareFileAsync() => Task.FromResult(PathToReturn);
    }

    private static UpdateViewModel New(FakeUpdater updater, FakeFilePicker picker, byte[] fileBytes,
        IDeviceAccessCoordinator? coordinator = null) =>
        new(new List<IDeviceUpdater> { updater }, picker, _ => Task.FromResult(fileBytes), coordinator);

    [Fact]
    public async Task SelectFile_With_Wrong_Kind_Sets_Invalid_And_Disables_Send()
    {
        var updater = new FakeUpdater();
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Pedal));

        await vm.SelectFileCommand.ExecuteAsync(null);

        Assert.False(vm.IsFirmwareValid);
        Assert.StartsWith("✗", vm.ValidationMessage);
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectFile_With_Matching_Kind_Sets_Valid_And_Enables_Send()
    {
        var updater = new FakeUpdater();
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base, 1, 2, 3));

        await vm.SelectFileCommand.ExecuteAsync(null);

        Assert.True(vm.IsFirmwareValid);
        Assert.StartsWith("✓", vm.ValidationMessage);
        Assert.Contains("1.2.3", vm.ValidationMessage);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectFile_Cancelled_Leaves_State_Untouched()
    {
        var updater = new FakeUpdater();
        var picker = new FakeFilePicker { PathToReturn = null }; // usuário cancelou o diálogo
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));

        await vm.SelectFileCommand.ExecuteAsync(null);

        Assert.False(vm.IsFirmwareValid);
        Assert.Equal("", vm.FirmwarePath);
    }

    [Fact]
    public async Task Send_Runs_EnterDfu_Wait_Flash_And_Reports_Success()
    {
        var updater = new FakeUpdater();
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(updater.EnterCalled);
        Assert.True(updater.FlashCalled);
        Assert.Equal("/tmp/fw.bin", updater.LastFlashPath);
        Assert.False(vm.IsSending);
        Assert.Equal(1.0, vm.Progress);
        Assert.Contains("sucesso", vm.StatusMessage);
    }

    [Fact]
    public async Task Send_Falls_Back_To_Manual_When_Auto_Jump_Fails()
    {
        var updater = new FakeUpdater { BootloaderFound = false };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(updater.EnterCalled);
        Assert.False(updater.FlashCalled);
        Assert.False(vm.IsSending);
        Assert.True(vm.NeedsManualDfu);                       // entra no modo manual (SW1→DFU)
        Assert.Contains("SW1", vm.StatusMessage);
        Assert.True(vm.ContinueDfuCommand.CanExecute(null));
        Assert.True(vm.CancelDfuCommand.CanExecute(null));
        Assert.False(vm.SendCommand.CanExecute(null));        // Enviar bloqueado durante o modo manual
    }

    [Fact]
    public async Task Manual_Continue_Detects_Bootloader_And_Flashes()
    {
        // Auto falha (false), depois o Continuar acha o bootloader (true) → grava.
        var updater = new FakeUpdater { BootloaderResults = new Queue<bool>(new[] { false, true }) };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.NeedsManualDfu);

        await vm.ContinueDfuCommand.ExecuteAsync(null);

        Assert.True(updater.FlashCalled);
        Assert.False(vm.NeedsManualDfu);
        Assert.False(vm.IsSending);
        Assert.Contains("sucesso", vm.StatusMessage);
    }

    [Fact]
    public async Task Manual_Continue_Still_Not_Found_Stays_In_Manual_Mode()
    {
        var updater = new FakeUpdater { BootloaderFound = false };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));
        await vm.SelectFileCommand.ExecuteAsync(null);
        await vm.SendCommand.ExecuteAsync(null);

        await vm.ContinueDfuCommand.ExecuteAsync(null);

        Assert.False(updater.FlashCalled);
        Assert.True(vm.NeedsManualDfu);                       // continua pedindo SW1→DFU
        Assert.False(vm.IsSending);
    }

    [Fact]
    public async Task Manual_Cancel_Resumes_AutoConnect_And_Resets()
    {
        var events = new List<string>();
        var updater = new FakeUpdater { BootloaderFound = false, Events = events };
        var coordinator = new FakeCoordinator { Events = events };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base), coordinator);
        await vm.SelectFileCommand.ExecuteAsync(null);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Equal(0, coordinator.EndCalls);                // exclusivo AINDA retido no modo manual

        await vm.CancelDfuCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.EndCalls);                // Cancelar retoma o auto-connect
        Assert.False(vm.NeedsManualDfu);
        Assert.False(updater.FlashCalled);
        Assert.Equal(new[] { "enter", "begin", "wait", "end" }, events);
    }

    [Fact]
    public async Task Send_Catches_Exception_And_Sets_Friendly_Status()
    {
        var updater = new FakeUpdater { FlashThrows = new InvalidOperationException("dfu-util não encontrado") };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.False(vm.IsSending);
        Assert.Contains("dfu-util não encontrado", vm.StatusMessage);
    }

    [Fact]
    public async Task Shows_Connected_Board_And_Firmware_Version()
    {
        var transport = new FakeTransport();                 // FirmwareVersion fixa = 0.1.0.0 → "v1.0.0"
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var updater = new FakeUpdater();
        var vm = new UpdateViewModel(new List<IDeviceUpdater> { updater }, new FakeFilePicker(),
            _ => Task.FromResult(MakeFirmwareBytes(DeviceKind.Base)), coordinator: null, baseSession: session);

        Assert.Equal("Nenhuma placa detectada.", vm.ConnectedDeviceInfo);

        await session.ConnectAsync();
        Assert.Contains("DriveLab Base detectada", vm.ConnectedDeviceInfo);
        Assert.Contains("v1.0.0", vm.ConnectedDeviceInfo);

        await session.DisconnectAsync();
        Assert.Equal("Nenhuma placa detectada.", vm.ConnectedDeviceInfo);
    }

    [Fact]
    public void Send_Disabled_When_No_File_Selected()
    {
        var updater = new FakeUpdater();
        var picker = new FakeFilePicker();
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base));

        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task Send_Takes_Exclusive_Usb_After_EnterDfu_And_Before_Wait()
    {
        var events = new List<string>();
        var updater = new FakeUpdater { Events = events };
        var coordinator = new FakeCoordinator { Events = events };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base), coordinator);
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);

        // Ordem crítica: EnterDfu usa o transporte (precisa estar conectado); SÓ DEPOIS pausamos o
        // auto-connect + soltamos o handle (begin); então esperamos o DFU e flasheamos; e por fim retomamos.
        Assert.Equal(new[] { "enter", "begin", "wait", "flash", "end" }, events);
    }

    [Fact]
    public async Task Send_Holds_Exclusive_While_Waiting_For_Manual_Dfu()
    {
        var events = new List<string>();
        var updater = new FakeUpdater { BootloaderFound = false, Events = events };
        var coordinator = new FakeCoordinator { Events = events };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base), coordinator);
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.False(updater.FlashCalled);
        // Auto falhou → entra no modo manual e MANTÉM o acesso exclusivo (auto-connect pausado)
        // enquanto o usuário faz SW1→DFU. O End só vem no Continuar (grava) ou no Cancelar.
        Assert.Equal(0, coordinator.EndCalls);
        Assert.True(vm.NeedsManualDfu);
        Assert.Equal(new[] { "enter", "begin", "wait" }, events);
    }

    [Fact]
    public async Task Send_Always_Ends_Exclusive_When_Flash_Throws()
    {
        var events = new List<string>();
        var updater = new FakeUpdater { FlashThrows = new InvalidOperationException("boom"), Events = events };
        var coordinator = new FakeCoordinator { Events = events };
        var picker = new FakeFilePicker { PathToReturn = "/tmp/fw.bin" };
        var vm = New(updater, picker, MakeFirmwareBytes(DeviceKind.Base), coordinator);
        await vm.SelectFileCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.EndCalls);                       // retomado mesmo com exceção no flash
        Assert.Equal(new[] { "enter", "begin", "wait", "flash", "end" }, events);
        Assert.Contains("boom", vm.StatusMessage);
    }

    // ------------------------------------------------------------------------------------------
    // Placa em modo de atualização (DFU/BOOTSEL)
    // ------------------------------------------------------------------------------------------

    /// <summary>Espera a vigília reagir. Ela roda em segundo plano num intervalo curto (injetado nos
    /// testes), então o teste aguarda a CONDIÇÃO em vez de um tempo fixo — dormir um valor fixo é o
    /// que produz teste que falha só na máquina lenta.</summary>
    /// <summary>Intervalo da vigília nos testes. Curto para não esperar relógio, mas NÃO agressivo:
    /// com 10 ms as vigílias de fundo disputavam o pool de threads e faziam OUTROS testes desta
    /// classe falharem de forma intermitente (2 em 3 execuções, sempre em asserts de tempo).</summary>
    private static readonly TimeSpan PollDeTeste = TimeSpan.FromMilliseconds(25);

    private static async Task<bool> AguardarAsync(Func<bool> condicao, int limiteMs = 2000)
    {
        for (var t = 0; t < limiteMs; t += 10)
        {
            if (condicao()) return true;
            await Task.Delay(10);
        }
        return condicao();
    }

    /// <summary>Placa de fábrica em DFU: não conecta como dispositivo normal, e dizer "sem conexão"
    /// faz a pessoa achar que deu errado justamente quando está tudo certo para gravar.</summary>
    [Fact]
    public async Task Placa_Em_Modo_De_Atualizacao_Aparece_No_Status()
    {
        var updater = new FakeUpdater { BootloaderPresent = true };
        using var vm = new UpdateViewModel(new List<IDeviceUpdater> { updater }, new FakeFilePicker(),
            _ => Task.FromResult(MakeFirmwareBytes(DeviceKind.Base)),
            bootloaderPollInterval: PollDeTeste);

        Assert.True(await AguardarAsync(() => vm.BootloaderDetected));
        Assert.Contains("modo de atualização", vm.ConnectedDeviceInfo);
    }

    /// <summary>Sem placa em DFU o texto tem de continuar o de sempre — um aviso que aparece sozinho
    /// seria pior que a mensagem antiga.</summary>
    [Fact]
    public async Task Sem_Bootloader_Mantem_A_Mensagem_De_Sempre()
    {
        var updater = new FakeUpdater { BootloaderPresent = false };
        using var vm = new UpdateViewModel(new List<IDeviceUpdater> { updater }, new FakeFilePicker(),
            _ => Task.FromResult(MakeFirmwareBytes(DeviceKind.Base)),
            bootloaderPollInterval: PollDeTeste);

        await Task.Delay(120);  // deixa a vigília rodar algumas vezes
        Assert.False(vm.BootloaderDetected);
        Assert.Equal("Nenhuma placa detectada.", vm.ConnectedDeviceInfo);
    }

    /// <summary>A placa saiu do modo de atualização (gravou e reiniciou): o aviso tem de SUMIR. Um
    /// aviso que fica preso diria "pronta para gravar" com a base já rodando o firmware.</summary>
    [Fact]
    public async Task Aviso_Some_Quando_A_Placa_Sai_Do_Modo_De_Atualizacao()
    {
        var updater = new FakeUpdater { BootloaderPresent = true };
        using var vm = new UpdateViewModel(new List<IDeviceUpdater> { updater }, new FakeFilePicker(),
            _ => Task.FromResult(MakeFirmwareBytes(DeviceKind.Base)),
            bootloaderPollInterval: PollDeTeste);

        Assert.True(await AguardarAsync(() => vm.BootloaderDetected));

        updater.BootloaderPresent = false;
        Assert.True(await AguardarAsync(() => !vm.BootloaderDetected));
        Assert.Equal("Nenhuma placa detectada.", vm.ConnectedDeviceInfo);
    }

    /// <summary>Descartar a tela encerra a vigília — senão fica um processo externo rodando a cada
    /// poucos segundos pelo resto da vida do app.</summary>
    [Fact]
    public async Task Dispose_Encerra_A_Vigilia()
    {
        var updater = new FakeUpdater { BootloaderPresent = true };
        var vm = new UpdateViewModel(new List<IDeviceUpdater> { updater }, new FakeFilePicker(),
            _ => Task.FromResult(MakeFirmwareBytes(DeviceKind.Base)),
            bootloaderPollInterval: PollDeTeste);

        Assert.True(await AguardarAsync(() => vm.BootloaderDetected));

        vm.Dispose();
        vm.BootloaderDetected = false;    // se a vigília seguisse viva, ela poria true de volta
        await Task.Delay(150);
        Assert.False(vm.BootloaderDetected);
    }
}
