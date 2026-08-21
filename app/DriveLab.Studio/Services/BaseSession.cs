// ============================================================================
//  DriveLab
//  BaseSession.cs — Fachada sobre um IBaseTransport que marshala telemetria do dispositivo para a thread de UI.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Simulator;

namespace DriveLab.Studio.Services;

/// <summary>
/// App-facing facade over an <see cref="IBaseTransport"/>. Marshals device telemetry
/// onto the UI thread via <see cref="IUiDispatcher"/> so ViewModels can bind safely.
/// </summary>
public sealed class BaseSession : IDisposable
{
    private readonly IBaseTransport _transport;
    private readonly IUiDispatcher _dispatcher;

    public BaseSession(IBaseTransport transport, IUiDispatcher dispatcher)
    {
        _transport = transport;
        _dispatcher = dispatcher;
        _transport.StateReceived += OnTransportState;
        _transport.WheelAngleReceived += (_, graus) => _dispatcher.Post(() => WheelAngleReceived?.Invoke(this, graus));
    }

    public event EventHandler<BaseState>? StateReceived;

    /// <summary>Ângulo do volante vindo do relatório que vai para o JOGO — 1 kHz, contra os 25 Hz da
    /// telemetria. É o que move o desenho na tela.</summary>
    public event EventHandler<double>? WheelAngleReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    /// <summary>
    /// Raised (on the UI thread) after a setting is written, so every view bound to
    /// that setting stays in sync regardless of which view triggered the change.
    /// </summary>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public bool IsConnected => _transport.IsConnected;
    public FirmwareVersion FirmwareVersion => _transport.FirmwareVersion;

    public async Task ConnectAsync()
    {
        await _transport.ConnectAsync();

        // Se o transporte não abriu (ex.: hardware ausente no modo real), não dispara
        // Connected — evita que views leiam settings num canal fechado (timeout/crash).
        if (!_transport.IsConnected)
            return;

        // Streaming is currently a simulator capability; a future HidBaseTransport
        // will expose an equivalent start/stop that this line will generalize to.
        (_transport as SimulatorBaseTransport)?.StartStreaming();
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        (_transport as SimulatorBaseTransport)?.StopStreaming();
        await _transport.DisconnectAsync();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task WriteSettingAsync(BaseSettingId id, SettingValue value)
    {
        await _transport.WriteSettingAsync(id, value);
        _dispatcher.Post(() => SettingChanged?.Invoke(this, new SettingChangedEventArgs(id, value)));
    }

    public Task<SettingValue> ReadSettingAsync(BaseSettingId id) => _transport.ReadSettingAsync(id);

    /// <summary>Valor de FÁBRICA do ajuste, perguntado à base. Consulta pura: nada muda na placa
    /// até o Salvar.</summary>
    public Task<SettingValue> ReadSettingDefaultAsync(BaseSettingId id) => _transport.ReadSettingDefaultAsync(id);

    /// <summary>Valor GRAVADO na memória permanente — o que sobrevive a reiniciar. É por ele que o
    /// "Salvar" confere se gravou mesmo.</summary>
    public Task<SettingValue> ReadSettingSavedAsync(BaseSettingId id) => _transport.ReadSettingSavedAsync(id);
    public Task SendDirectControlAsync(BaseDirectControl control) => _transport.SendDirectControlAsync(control);
    public Task SendCommandAsync(BaseCommand command, byte arg = 0) => _transport.SendCommandAsync(command, arg);

    /// <summary>Há quanto tempo a base deu sinal de vida. Null = nunca deu.</summary>
    public TimeSpan? SilencioDaBase => _ultimaTelemetria is { } t ? DateTime.UtcNow - t : null;

    /// <summary>A base está falando conosco AGORA? Dois segundos é folgado: a telemetria chega a
    /// cada 200 ms, então cinco em silêncio já seria anormal.</summary>
    public bool BaseRespondendo => SilencioDaBase is { } s && s < TimeSpan.FromSeconds(2);

    private DateTime? _ultimaTelemetria;

    /// <summary>
    /// ⚠️ COMANDO QUE CONFIRMA QUE ACONTECEU — ou diz que não aconteceu.
    ///
    /// <para>Enviar por USB não é executar. O relatório sai do PC, e daí em diante tudo pode dar
    /// errado sem ninguém avisar: a base pode estar sem conseguir parar o motor para gravar, o canal
    /// pode ter morrido, o Windows pode ter desligado a porta. Até aqui o app mandava e dizia
    /// "pronto" — e o usuário descobria sozinho, testando, que não tinha sido feito. Na bancada de
    /// 18/08/2026 isso chegou ao extremo: nenhum comando funcionava, o app não acusava nada, e o
    /// motivo (o PC tinha parado de falar com a base) só apareceu com um leitor por SWD.</para>
    ///
    /// <para>Um comando que não pode ser verificado não deveria existir num aparelho que aplica
    /// força nas mãos de alguém. Então cada um passa a ter uma PROVA:
    /// <list type="bullet">
    ///   <item>Salvar: o contador de gravações concluídas da base muda de valor;</item>
    ///   <item>Reiniciar: a base fica em silêncio (ela some do barramento) e volta a falar.</item>
    /// </list>
    /// Sem prova dentro do prazo, devolve false — e quem chamou avisa em vez de mentir.</para>
    ///
    /// <para>A verificação começa por perguntar se a base está viva. Se ela já estava muda antes do
    /// comando, nem enviamos: o resultado seria o mesmo e a mensagem seria pior ("não confirmou",
    /// quando a verdade é "não há com quem falar").</para>
    /// </summary>
    public async Task<ResultadoDeComando> ExecutarVerificadoAsync(BaseCommand comando, byte arg = 0,
                                                                  TimeSpan? prazo = null)
    {
        if (!IsConnected) return ResultadoDeComando.SemConexao;

        // ⚠️ NÃO EXIGIR TELEMETRIA PARA TENTAR. A primeira versão recusava o comando quando a base
        // estava calada — e isso inverte a prioridade: a telemetria é o canal que MAIS falha neste
        // projeto, então o comando deixaria de funcionar justamente quando mais se precisa dele.
        // Silêncio serve para EXPLICAR uma falha depois, nunca para impedir a tentativa.
        var limite = prazo ?? TimeSpan.FromSeconds(10);
        var antes  = UltimoEstado?.SaveCount;
        await SendCommandAsync(comando, arg);

        return comando switch
        {
            BaseCommand.SaveSettings => await EsperarAsync(limite,
                () => UltimoEstado?.SaveCount is { } agora && antes is { } a && agora != a),
            BaseCommand.Reboot       => await EsperarReinicioAsync(limite),
            _                        => ResultadoDeComando.Enviado,
        };
        // (a classificação do fracasso vem abaixo, em quem chamou: sem prova + base calada = BaseMuda)
    }

    /// <summary>Reiniciar tem uma prova em DUAS etapas, e a ordem importa: a base precisa PARAR de
    /// falar (é o reinício acontecendo) e depois VOLTAR. Só a segunda metade não serve — se ela
    /// nunca parou, o comando não chegou, e a telemetria continuar chegando "provaria" um reinício
    /// que não houve.</summary>
    private async Task<ResultadoDeComando> EsperarReinicioAsync(TimeSpan limite)
    {
        var metade = TimeSpan.FromMilliseconds(limite.TotalMilliseconds / 2);
        var parou = await EsperarAsync(metade, () => !BaseRespondendo);
        if (parou != ResultadoDeComando.Ok) return ResultadoDeComando.NaoConfirmou;
        return await EsperarAsync(limite, () => BaseRespondendo);
    }

    /// <summary>Espera a prova aparecer. Sem prova no prazo, o resultado depende de haver alguém do
    /// outro lado: base calada explica o fracasso (é de conexão), base falando significa que o
    /// comando chegou e não produziu efeito.</summary>
    private async Task<ResultadoDeComando> EsperarAsync(TimeSpan limite, Func<bool> pronto)
    {
        const int passo = 100;
        for (var esperou = 0; esperou < limite.TotalMilliseconds; esperou += passo)
        {
            await Task.Delay(passo);
            if (pronto()) return ResultadoDeComando.Ok;
        }
        // SilencioDaBase null = nunca houve telemetria (transporte que não a fornece, como nos
        // testes). Isso não é "base muda": é ausência de prova, e quem chamou decide pela releitura.
        return SilencioDaBase is { } sil && sil > TimeSpan.FromSeconds(2)
             ? ResultadoDeComando.BaseMuda
             : ResultadoDeComando.NaoConfirmou;
    }

    /// <summary>Última telemetria recebida, ou null antes da primeira. Guardada aqui porque há
    /// perguntas pontuais — "a base já confirmou a gravação?" — que não justificam cada tela assinar
    /// o evento e manter cópia própria só para consultar um campo.
    ///
    /// <para>Escrita no thread do transporte e lida na UI. É uma referência a um objeto imutável na
    /// prática (o parser cria um novo a cada telemetria), então a leitura pega um estado inteiro e
    /// coerente — nunca metade de um e metade de outro.</para></summary>
    public BaseState? UltimoEstado { get; private set; }

    private void OnTransportState(object? sender, BaseState state)
    {
        UltimoEstado = state;
        _ultimaTelemetria = DateTime.UtcNow;
        _dispatcher.Post(() => StateReceived?.Invoke(this, state));
    }

    public void Dispose()
    {
        _transport.StateReceived -= OnTransportState;
        (_transport as SimulatorBaseTransport)?.StopStreaming();
    }
}

/// <summary>Payload for <see cref="BaseSession.SettingChanged"/>.</summary>
/// <summary>Como terminou um comando verificado. Cada valor existe porque pede uma reação
/// diferente de quem está na frente da tela — juntar tudo em um bool devolveria a pergunta sem
/// resposta ("não funcionou, e agora?").</summary>
public enum ResultadoDeComando
{
    /// <summary>A base confirmou: aconteceu.</summary>
    Ok,
    /// <summary>Comando sem prova definida; foi enviado e não há o que conferir.</summary>
    Enviado,
    /// <summary>Não há base conectada — nem tentamos.</summary>
    SemConexao,
    /// <summary>Conectada no papel, mas calada: não chega telemetria. Comando nenhum vai passar,
    /// e o problema está na ligação (cabo, porta, o Windows ter desligado a porta), não no ajuste.</summary>
    BaseMuda,
    /// <summary>Enviamos e a prova não veio no prazo. O comando pode ter se perdido, ou a base pode
    /// não ter conseguido executá-lo.</summary>
    NaoConfirmou,
}

public sealed class SettingChangedEventArgs : EventArgs
{
    public SettingChangedEventArgs(BaseSettingId id, SettingValue value)
    {
        Id = id;
        Value = value;
    }

    public BaseSettingId Id { get; }
    public SettingValue Value { get; }
}
