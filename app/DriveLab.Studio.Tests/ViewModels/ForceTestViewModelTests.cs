// ============================================================================
//  DriveLab
//  ForceTestViewModelTests.cs — Testes do executor dos testes de forca.
//
//  O foco aqui e SEGURANCA, nao funcionalidade. Isto aplica torque real num
//  volante que pode estar com a mao de alguem, entao o que precisa estar provado
//  e que nao existe caminho de saida que deixe forca aplicada: terminar, abortar,
//  perder a conexao — todos tem de acabar em zero.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Testing;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class ForceTestViewModelTests
{
    /// <summary>Relogio virtual: o tempo so anda quando o executor espera, e a espera CEDE de
    /// verdade (Task.Yield). Isso torna os testes instantaneos e — o que importa mais — permite
    /// interleavar duas execucoes para provar que a segunda e recusada.</summary>
    private sealed class RelogioFalso : IRelogioDeTeste
    {
        private double _s;
        public double DecorridoS => _s;
        public void Reiniciar() => _s = 0;
        public async Task EsperarAsync(int ms, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _s += ms / 1000.0;
            await Task.Yield();
        }
    }

    private static (ForceTestViewModel vm, FakeTransport t) Make()
    {
        var transport = new FakeTransport();
        transport.ConnectAsync().GetAwaiter().GetResult();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new ForceTestViewModel(session, new RelogioFalso())
        {
            IsConnected = true,
            ForceEnabled = true,
            TorqueConstant = 0.397,
        };
        return (vm, transport);
    }

    private static ForceTestItemViewModel Mola(ForceTestViewModel vm) =>
        vm.Testes.First(t => t.Id == "Spring");

    // ── Seguranca ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_Forca_Nasce_Em_10_Porcento()
    {
        // O padrao e o controle de seguranca que mais importa: e o valor que vale na PRIMEIRA vez,
        // quando a pessoa ainda nao sabe o que a base faz. Os testes pedem 50 a 100% do fundo de
        // escala, e num DD isso arranca da mao de quem nao esperava.
        var (vm, _) = Make();
        Assert.Equal(10, vm.ForcaPct);
    }

    [Fact]
    public async Task A_Forca_Enviada_Respeita_O_Multiplicador_Da_Tela()
    {
        var (vm, t) = Make();
        vm.ForcaPct = 10;

        await vm.RodarCommand.ExecuteAsync(Mola(vm));

        // A mola pede ganho 0,5 fixo; a 10% tem de sair 0,05 → 500 na escala de ±10000.
        var pico = t.Controls.Max(c => (int)c.SpringForce);
        Assert.Equal(500, pico);
    }

    [Fact]
    public async Task O_Multiplicador_Vale_Para_A_MOLA_Tambem_Nao_So_Para_As_Forcas()
    {
        // Escalar so Constant/Periodic deixaria a mola no valor cheio, e o teste dela — o mais suave
        // da lista, o primeiro que a pessoa roda — seria o unico imune ao controle de seguranca.
        var (vm, t) = Make();
        vm.ForcaPct = 50;

        await vm.RodarCommand.ExecuteAsync(Mola(vm));

        var picoMola   = t.Controls.Max(c => (int)c.SpringForce);
        var picoDamper = t.Controls.Max(c => (int)c.DamperForce);
        Assert.Equal(2500, picoMola);    // 0,5 x 50%
        Assert.Equal(750, picoDamper);   // 0,15 x 50%
    }

    [Fact]
    public async Task Ao_Terminar_A_Forca_Volta_A_ZERO()
    {
        var (vm, t) = Make();

        await vm.RodarCommand.ExecuteAsync(Mola(vm));

        var ultimo = t.LastControl;
        Assert.NotNull(ultimo);
        Assert.Equal(0, ultimo!.ConstantForce);
        Assert.Equal(0, ultimo.SpringForce);
        Assert.Equal(0, ultimo.PeriodicForce);
        Assert.Equal(0, ultimo.DamperForce);
    }

    [Fact]
    public async Task Perder_A_Conexao_No_Meio_Nao_Deixa_O_Executor_Girando()
    {
        var (vm, t) = Make();
        var corrida = vm.RodarCommand.ExecuteAsync(Mola(vm));
        await t.DisconnectAsync();          // dispara Disconnected -> cancela
        await corrida;

        Assert.Null(vm.EmExecucao);
        Assert.False(Mola(vm).Rodando);
    }

    [Fact]
    public async Task Nao_Roda_Com_A_Forca_Desligada()
    {
        // Sem este aviso o teste "roda", nada acontece, e a pessoa conclui que a base esta morta.
        var (vm, t) = Make();
        vm.ForceEnabled = false;

        await vm.RodarCommand.ExecuteAsync(Mola(vm));

        Assert.Null(t.LastControl);                       // nao mandou forca nenhuma
        Assert.Contains("Ativar motor", vm.Aviso!);
        Assert.False(Mola(vm).TemResultado);
    }

    [Fact]
    public async Task Nao_Roda_Sem_Base_Conectada()
    {
        var (vm, _) = Make();
        vm.IsConnected = false;

        await vm.RodarCommand.ExecuteAsync(Mola(vm));

        Assert.Contains("Conecte a base", vm.Aviso!);
    }

    [Fact]
    public async Task Nao_Roda_Dois_Testes_Ao_Mesmo_Tempo()
    {
        // Dois testes somando forca no mesmo volante e o caminho para um solavanco que ninguem pediu.
        var (vm, _) = Make();
        var primeiro = vm.RodarCommand.ExecuteAsync(Mola(vm));
        var segundo  = vm.RodarCommand.ExecuteAsync(vm.Testes.First(t => t.Id == "Ramp"));
        await Task.WhenAll(primeiro, segundo);

        Assert.False(vm.Testes.First(t => t.Id == "Ramp").TemResultado);
    }

    // ── Execucao ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rodar_Produz_Veredito()
    {
        var (vm, t) = Make();
        t.Emit(new BaseState { Flags = BaseFlags.ForceEnabled, AngleDeciDeg = 300, MotorCurrentMa = 5000 });

        await vm.RodarCommand.ExecuteAsync(Mola(vm));

        Assert.True(Mola(vm).TemResultado);
        Assert.False(string.IsNullOrWhiteSpace(Mola(vm).Resumo));
    }

    [Fact]
    public async Task O_Progresso_Fica_No_Cartao_E_Volta_A_Zero_No_Fim()
    {
        // O progresso mora no ITEM, e nao na pagina: o retorno tem de aparecer onde a pessoa
        // clicou. Barra no topo para um botao la embaixo faz procurar.
        var (vm, _) = Make();
        await vm.RodarCommand.ExecuteAsync(Mola(vm));
        Assert.Equal(0, Mola(vm).Progresso);
        Assert.All(vm.Testes, t => Assert.Equal(0, t.Progresso));
    }

    [Fact]
    public void O_Pico_Exibido_Vem_Do_Proprio_Teste()
    {
        // E o numero que a tela usa para avisar antes de aplicar. Sair do teste, e nao de uma
        // constante da tela, e o que impede o aviso de envelhecer quando o teste mudar.
        var (vm, _) = Make();
        foreach (var item in vm.Testes)
            Assert.Equal((int)Math.Round(item.Teste.PicoDeForca * 100), item.PicoPct);
    }

    [Fact]
    public async Task Perder_A_Forca_No_Meio_Aborta_O_Teste()
    {
        // A base pode desarmar sozinha — parada de emergencia, guarda de coerencia, erro. Continuar
        // mandando forca para uma base desarmada mede nada, e volta a aplicar torque no instante em
        // que ela rearmar.
        var (vm, t) = Make();
        var corrida = vm.RodarCommand.ExecuteAsync(Mola(vm));
        t.Emit(new BaseState());                 // sem a marca de forca habilitada = desarmou
        await corrida;

        Assert.Null(vm.EmExecucao);
        Assert.False(vm.ForceEnabled);
    }
}
