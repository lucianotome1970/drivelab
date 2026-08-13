// ============================================================================
//  DriveLab
//  FfbPlaybackViewModelTests.cs — Testes do executor da reproducao de voltas.
//
//  Mesmo foco dos testes de forca, e por um motivo mais forte: aqui a forca vem
//  de um ARQUIVO, que pode ter sido gravado por outra pessoa, numa base mais
//  forte, numa volta cuja intensidade ninguem deste lado conhece de antemao. O
//  que precisa estar provado e que nenhum caminho de saida deixa forca aplicada,
//  e que o teto e a contagem regressiva nao podem ser pulados por acidente.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using DriveLab.Core.Capture;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class FfbPlaybackViewModelTests
{
    /// <summary>Relogio virtual: o tempo so anda quando o executor espera, e a espera cede de
    /// verdade. Torna o teste instantaneo, inclusive a contagem regressiva de 3 s.</summary>
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

    private sealed class SeletorFalso : ICaptureFilePicker
    {
        public string? Caminho;
        public Task<string?> PickCaptureAsync() => Task.FromResult(Caminho);
    }

    /// <summary>Meio segundo de forca cheia para um lado, a 1 kHz.</summary>
    private static string ArquivoDeVolta(double forca = 1.0, int quadros = 500)
    {
        var caminho = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dlffb");
        using var f = File.Create(caminho);
        FfbCaptureFile.Write(f, new FfbCapture(
            new FfbCaptureHeader { RateHz = 1000, Game = "ACC", Track = "Monza", VideoStartS = 12.5 },
            Enumerable.Repeat(new FfbFrame(forca, 0, 0), quadros).ToList()));
        return caminho;
    }

    private static (FfbPlaybackViewModel vm, FakeTransport t, SeletorFalso s) Make()
    {
        var transport = new FakeTransport();
        transport.ConnectAsync().GetAwaiter().GetResult();
        var seletor = new SeletorFalso();
        var vm = new FfbPlaybackViewModel(
            new BaseSession(transport, new ImmediateUiDispatcher()), seletor, new RelogioFalso())
        {
            IsConnected = true,
            ForceEnabled = true,
        };
        return (vm, transport, seletor);
    }

    private static async Task<FfbPlaybackViewModel> ComVolta(FakeTransport t, SeletorFalso s,
                                                             FfbPlaybackViewModel vm, string arquivo)
    {
        s.Caminho = arquivo;
        await vm.CarregarCommand.ExecuteAsync(null);
        return vm;
    }

    // ── Seguranca ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_Teto_Nasce_Em_30_Porcento()
    {
        // O valor que vale na PRIMEIRA reproducao, quando quem carregou o arquivo ainda nao sabe o
        // que aquela volta faz. Ver o mesmo padrao em FfbPlayback.TetoPct.
        var (vm, _, _) = Make();
        Assert.Equal(30, vm.TetoPct);
    }

    [Fact]
    public async Task O_Teto_Da_Tela_Limita_A_Forca_Que_Sai()
    {
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta(forca: 1.0));
        vm.TetoPct = 40;

        await vm.TocarCommand.ExecuteAsync(null);

        // Forca cheia gravada, 40% de teto → nada acima de 4000 na escala de ±10000.
        Assert.True(t.Controls.Max(c => (int)c.ConstantForce) <= 4000);
    }

    [Fact]
    public async Task Termina_A_Volta_Com_Forca_ZERO()
    {
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());

        await vm.TocarCommand.ExecuteAsync(null);

        Assert.Equal(0, t.Controls[^1].ConstantForce);
        Assert.False(vm.Tocando);
    }

    [Fact]
    public async Task Parar_No_Meio_Zera_A_Forca()
    {
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta(quadros: 5000));

        var tocando = vm.TocarCommand.ExecuteAsync(null);
        vm.PararCommand.Execute(null);
        await tocando;

        Assert.Equal(0, t.Controls[^1].ConstantForce);
        Assert.False(vm.Tocando);
    }

    [Fact]
    public async Task Perder_A_Conexao_No_Meio_Aborta()
    {
        // Nao e so higiene de estado: a base que reconecta com uma demanda antiga pendurada volta
        // aplicando torque que ninguem pediu.
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta(quadros: 5000));

        var tocando = vm.TocarCommand.ExecuteAsync(null);
        await t.DisconnectAsync();
        await tocando;

        Assert.False(vm.Tocando);
    }

    [Fact]
    public async Task Nao_Toca_Com_O_Motor_DESARMADO()
    {
        // Mandar forca para uma base desarmada nao faz nada agora — e volta a aplicar torque no
        // instante em que ela rearmar, com a pessoa ja sem esperar por isso.
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());
        vm.ForceEnabled = false;
        t.Controls.Clear();

        await vm.TocarCommand.ExecuteAsync(null);

        Assert.Empty(t.Controls);
        Assert.NotNull(vm.Aviso);
    }

    [Fact]
    public async Task Nao_Toca_Sem_Volta_Carregada()
    {
        var (vm, t, _) = Make();
        await vm.TocarCommand.ExecuteAsync(null);
        Assert.Empty(t.Controls);
        Assert.False(vm.Tocando);
    }

    [Fact]
    public async Task A_Contagem_Regressiva_Acontece_ANTES_De_Qualquer_Forca()
    {
        // E o aviso de tirar as maos do aro. Se a forca comecasse durante a contagem, a contagem
        // seria decoracao — e o volante arrancaria com a mao de alguem ainda nele.
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta(quadros: 5000));

        var contagemVista = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Contagem) && vm.Contagem > 0)
                contagemVista = Math.Max(contagemVista, vm.Contagem);
            // Enquanto a contagem corre, nada pode ter sido enviado.
            if (vm.Contagem > 0) Assert.Empty(t.Controls);
        };

        var tocando = vm.TocarCommand.ExecuteAsync(null);
        vm.PararCommand.Execute(null);
        await tocando;

        Assert.Equal(FfbPlaybackViewModel.ContagemS, contagemVista);
    }

    // ── Arquivo ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Carregar_Traz_O_Cabecalho_Para_A_Tela()
    {
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());

        Assert.True(vm.TemVolta);
        Assert.Contains("Monza", vm.Descricao);
    }

    [Fact]
    public async Task Arquivo_Invalido_Nao_Deixa_Uma_Volta_Pela_Metade_Carregada()
    {
        // Ficar com a volta ANTERIOR carregada depois de um erro faria a pessoa tocar uma volta
        // diferente da que ela acabou de escolher, achando que e a nova.
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());

        var lixo = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dlffb");
        await File.WriteAllTextAsync(lixo, "isto nao e uma gravacao\n");
        await ComVolta(t, s, vm, lixo);

        Assert.False(vm.TemVolta);
        Assert.NotNull(vm.Aviso);
    }

    [Fact]
    public async Task Desistir_Do_Seletor_Nao_Mexe_No_Que_Ja_Estava_Carregado()
    {
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());

        s.Caminho = null;
        await vm.CarregarCommand.ExecuteAsync(null);

        Assert.True(vm.TemVolta);
    }

    // ── Sincronia ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task O_Ajuste_Da_Tela_Chega_No_Reprodutor()
    {
        // A tela pensa em milissegundos e o reprodutor em segundos; a conversao errada aqui daria
        // um ajuste mil vezes maior, e a volta inteira sairia fora do lugar.
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());

        vm.AjusteMs = 250;

        Assert.Equal(0.25, vm.Volta!.AjusteS, 6);
    }

    [Fact]
    public async Task O_Ajuste_Vale_Tambem_Para_Uma_Volta_Carregada_DEPOIS()
    {
        // Quem ja acertou a sincronia e troca de arquivo nao espera perder o ajuste em silencio.
        var (vm, t, s) = Make();
        vm.AjusteMs = -120;
        vm.TetoPct = 55;

        await ComVolta(t, s, vm, ArquivoDeVolta());

        Assert.Equal(-0.12, vm.Volta!.AjusteS, 6);
        Assert.Equal(55, vm.Volta!.TetoPct);
    }

    [Fact]
    public async Task Os_Botoes_De_Sincronia_Somam_E_Nao_Substituem()
    {
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta());

        vm.AjustarSincroniaCommand.Execute("100");
        vm.AjustarSincroniaCommand.Execute("10");
        vm.AjustarSincroniaCommand.Execute("-10");

        Assert.Equal(100, vm.AjusteMs, 6);
    }
    [Fact]
    public async Task O_Botao_Parar_Fica_HABILITADO_Enquanto_Toca()
    {
        // NAO basta CanExecute devolver true: ele e avaliado ao vivo e sempre devolve o valor certo
        // quando alguem PERGUNTA. Quem faz a UI perguntar de novo e o evento CanExecuteChanged — sem
        // ele o botao fica cinza pelo resto da reproducao. Um Parar visivel e inerte e pior que
        // nenhum: a pessoa clica com o volante se mexendo, nada acontece, e ela perde os segundos
        // que tinha para reagir. Por isso o teste observa o EVENTO, nao o valor.
        var (vm, t, s) = Make();
        await ComVolta(t, s, vm, ArquivoDeVolta(quadros: 5000));

        var avisos = 0;
        vm.PararCommand.CanExecuteChanged += (_, _) => avisos++;

        var tocando = vm.TocarCommand.ExecuteAsync(null);
        vm.PararCommand.Execute(null);
        await tocando;

        Assert.True(avisos > 0, "a UI precisa ser avisada de que o Parar ficou disponivel");
    }
}
