// ============================================================================
//  DriveLab
//  UpdateCheckViewModelTests.cs — Testes do "verificar atualizações" (GitHub) no UpdateViewModel.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Linq;
using System.Text;
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

    private const string JsonWithAsset = """
    [ {"tag_name":"firmware-base-v0.3.0","name":"Base","assets":[
        {"name":"firmware-base-0.3.0.bin","browser_download_url":"https://x/base.bin"}]} ]
    """;

    [Fact]
    public async Task Download_SetsFirmwarePath_AndValidates()
    {
        var fw = Encoding.ASCII.GetBytes("DRVLABFW").Concat(new byte[] { 1, 0, 3, 0 }).ToArray();  // kind=Base
        var client = new GitHubReleaseClient(_ => Task.FromResult(JsonWithAsset));
        var vm = new UpdateViewModel(new IDeviceUpdater[] { new StubUpdater() },
            releaseClient: client, downloadBytes: _ => Task.FromResult(fw));

        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.True(vm.UpdateDownloadable);

        await vm.DownloadUpdateCommand.ExecuteAsync(null);

        Assert.EndsWith("firmware-base-0.3.0.bin", vm.FirmwarePath);
        Assert.True(System.IO.File.Exists(vm.FirmwarePath));
        Assert.True(vm.IsFirmwareValid);   // StubUpdater valida qualquer coisa
    }
}
