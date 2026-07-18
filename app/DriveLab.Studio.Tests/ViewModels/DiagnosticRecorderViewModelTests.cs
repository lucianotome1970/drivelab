// ============================================================================
//  DriveLab
//  DiagnosticRecorderViewModelTests.cs — Testes do gravador de diagnóstico ligado à telemetria da base.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class DiagnosticRecorderViewModelTests
{
    [Fact]
    public void Records_Telemetry_And_Marks_While_Recording()
    {
        var t = new FakeTransport();
        var session = new BaseSession(t, new ImmediateUiDispatcher());
        var sw = new StringWriter();
        var vm = new DiagnosticRecorderViewModel(session, () => sw);

        // Antes de gravar: telemetria é ignorada.
        t.Emit(new BaseState { AngleDeciDeg = 100, Torque = 5 });
        Assert.False(vm.IsRecording);
        Assert.Equal(0, vm.RowCount);

        vm.StartCommand.Execute(null);
        Assert.True(vm.IsRecording);
        Assert.False(vm.StartCommand.CanExecute(null));   // não reinicia enquanto grava

        t.Emit(new BaseState { AngleDeciDeg = 200, Torque = 10, MotorCurrentMa = 500, BusVoltageMv = 24000 });
        t.Emit(new BaseState { AngleDeciDeg = 300, Torque = -8 });
        Assert.Equal(2, vm.RowCount);

        vm.MarkNote = "tremeu aqui";
        vm.MarkCommand.Execute(null);
        Assert.Equal(3, vm.RowCount);
        Assert.Equal("", vm.MarkNote);                     // limpa após marcar

        vm.StopCommand.Execute(null);
        Assert.False(vm.IsRecording);

        // Depois de parar: telemetria volta a ser ignorada.
        t.Emit(new BaseState { AngleDeciDeg = 400 });
        Assert.Equal(3, vm.RowCount);

        var lines = sw.ToString().Trim().Replace("\r\n", "\n").Split('\n');
        Assert.Equal("t_ms,angle_deg,torque,current_mA,bus_mV,fet_C,motor_C,mark", lines[0]);
        Assert.Contains("20", lines[1]);                   // angle 200 deci = 20.0°
        Assert.EndsWith("tremeu aqui", lines[3]);          // linha de marcação
        vm.Dispose();
    }
}
