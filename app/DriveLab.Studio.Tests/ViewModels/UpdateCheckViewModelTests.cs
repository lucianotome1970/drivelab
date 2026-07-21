// ============================================================================
//  DriveLab
//  UpdateCheckViewModelTests.cs — Testes do "verificar atualizações" (GitHub) no UpdateViewModel.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DriveLab.Core.Update;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class UpdateCheckViewModelTests
{
    private sealed class StubUpdater : IDeviceUpdater
    {
        public DeviceKind Kind => DeviceKind.Base;
        public string BootloaderName => "STUB";
        public bool ValidateFirmware(byte[] file, out string error) { error = ""; return true; }
        public Task EnterBootloaderAsync() => Task.CompletedTask;
        public Task<bool> WaitForBootloaderAsync(TimeSpan timeout) => Task.FromResult(true);
        public Task FlashAsync(string filePath, IProgress<double>? progress, CancellationToken ct) => Task.CompletedTask;
    }

    private const string Json = """
    [ {"tag_name":"firmware-base-v0.3.0","name":"Base","assets":[]} ]
    """;

    [Fact]
    public void CanCheckUpdates_FalseWithoutClient()
    {
        var vm = new UpdateViewModel(new IDeviceUpdater[] { new StubUpdater() });
        Assert.False(vm.CanCheckUpdates);
    }

    [Fact]
    public async Task CheckUpdates_ShowsLatestVersion()
    {
        var client = new GitHubReleaseClient(_ => Task.FromResult(Json));
        var vm = new UpdateViewModel(new IDeviceUpdater[] { new StubUpdater() }, releaseClient: client);

        Assert.True(vm.CanCheckUpdates);
        await vm.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("0.3.0", vm.UpdateCheckMessage);
    }
}
