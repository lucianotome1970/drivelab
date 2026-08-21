// ============================================================================
//  DriveLab
//  SettingsPageViewModel.cs — VM de página com abas de settings e barra inferior (Padrão/Salvar), estilo MOZA.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

/// <summary>Especificação de uma aba de settings: cabeçalho + settings que ela contém.</summary>
public sealed record SettingsTabSpec(string Header, IReadOnlyList<BaseSettingId> Ids);

/// <summary>Uma aba da página: cabeçalho + conteúdo (renderizado pelo ViewLocator).</summary>
public sealed record PageTab(string Header, ViewModelBase Content);

/// <summary>
/// Página com abas (ex.: "Base do Volante" → Basic / Advanced / Hardware / Telemetria)
/// e a barra inferior (Padrão / Salvar), no estilo MOZA.
/// </summary>
public sealed partial class SettingsPageViewModel : ViewModelBase
{
    private readonly BaseSession _session;
    private readonly IProfileFilePicker? _filePicker;

    public string Title { get; }
    public IReadOnlyList<PageTab> Tabs { get; }

    /// <summary>Resultado da última exportação/importação, exibido abaixo da barra de botões. Vazio
    /// esconde o aviso.</summary>
    [ObservableProperty] private string _exchangeStatus = "";

    /// <summary>Exportar/importar disponível (sem seletor de arquivo — nos testes — os botões somem).</summary>
    public bool CanExchangeFiles => _filePicker is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetDefaultsCommand))]
    private bool _isConnected;

    /// <summary>App difere da flash da placa (alteração não salva) — habilita Salvar; zera ao carregar/salvar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    public SettingsPageViewModel(BaseSession session, string title, IEnumerable<PageTab> tabs,
                                 IProfileFilePicker? filePicker = null)
    {
        _session = session;
        _filePicker = filePicker;
        Title = title;
        Tabs = tabs.ToList();
        _isConnected = session.IsConnected;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
        _session.SettingChanged += OnSettingWritten;
        // Mexer num controle não envia mais nada à base; é isto que passa a habilitar o Salvar.
        foreach (var field in AllFields())
            field.Edited += OnFieldEdited;
    }

    // ⚠️ MEXEU, A MENSAGEM SOME. Ela descreve o resultado de UM clique em Salvar, e ficava na tela
    // até o próximo — inclusive depois de a pessoa mudar os campos, reconectar a base ou resolver o
    // que a impedia. Na bancada em 16/08/2026 o aviso de "não confirmou a gravação" continuou lá com
    // a base já normal e nada pendente, e a leitura natural é que o problema persiste. Aviso que não
    // corresponde mais ao estado é ruído, e ruído se aprende a ignorar — inclusive quando é verdade.
    private void OnFieldEdited(object? sender, EventArgs e)
    {
        IsDirty = true;
        MensagemDeSalvar = null;
    }

    /// <summary>Todos os campos de setting das abas (cada aba de config é um SettingsGroupViewModel).</summary>
    private IEnumerable<SettingFieldViewModel> AllFields() =>
        Tabs.Select(t => t.Content).OfType<SettingsGroupViewModel>().SelectMany(g => g.Fields);

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session.IsConnected;
        // Ao (re)conectar os grupos recarregam da placa via read (não dispara SettingChanged):
        // app passa a refletir a flash, então zera o dirty. A mensagem do último Salvar vai junto:
        // ela fala de uma tentativa contra uma conexão que já não é esta.
        IsDirty = false;
        MensagemDeSalvar = null;
    }

    // SettingChanged só dispara em WriteSettingAsync (nunca em read/load) → todo write marca dirty.
    private void OnSettingWritten(object? sender, SettingChangedEventArgs e)
    {
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // ============================================================================================
        // SALVAR: MANDA, GRAVA, E A BASE DIZ O QUE ACONTECEU
        // ============================================================================================
        // Este método já teve rodadas de reenvio, prazos, releitura campo a campo e três mensagens
        // diferentes de fracasso. Tudo isso era andaime em volta de um mecanismo que não dava
        // resposta: a base empacotava tudo, apagava um setor e reescrevia — precisava do motor
        // parado, e se um ajuste se perdesse no caminho ela gravava o valor velho sem saber.
        //
        // Com a persistência por chave, cada ajuste é gravado individualmente e CONFERIDO na própria
        // base, que agora reporta o balanço: quantas chaves escreveu, quantas já estavam certas e
        // quantas falharam. Não há mais o que deduzir daqui — só perguntar.
        //
        // A releitura continua, e de propósito: ela é a única prova de que o valor que saiu daqui é o
        // que ficou lá. O balanço diz que a gravação funcionou; a releitura diz que gravou O SEU
        // valor. As duas respondem perguntas diferentes, e já erramos por confundi-las.
        MensagemDeSalvar = null;

        var enviados = new List<SettingFieldViewModel>();
        foreach (var field in AllFields())
            if (field.IsModified)
            {
                await field.WriteAsync();
                enviados.Add(field);
            }

        if (enviados.Count == 0) { IsDirty = false; MensagemDeSalvar = L.Get("Settings_Saved"); return; }

        var r = await _session.ExecutarVerificadoAsync(BaseCommand.SaveSettings,
                    prazo: TimeSpan.FromMilliseconds(PrazoDeConfirmacaoMs));

        if (r is ResultadoDeComando.BaseMuda or ResultadoDeComando.SemConexao)
        {
            IsDirty = true;
            MensagemDeSalvar = L.Get("Settings_SaveSemBase");
            return;
        }

        // A base recusou alguma escrita? Isso é falha de flash de verdade — não vale insistir nem
        // fingir sucesso, e o número vem dela, não de suposição nossa.
        if (_session.UltimoEstado is { GravacoesComErro: > 0 } est)
        {
            IsDirty = true;
            MensagemDeSalvar = L.Get("Settings_SaveErroFlash") + $" ({est.GravacoesComErro})";
            return;
        }

        // Um ajuste pode ter se perdido a caminho da base — o relatório vai sem confirmação por
        // campo. Reenviar os divergentes UMA vez cobre esse caso, que é o único que sobrou; se
        // continuar diferente, a base está recusando aquele valor, e aí há o que investigar.
        var naoBateu = await ConferirGravacaoAsync(enviados);
        var divergiram = naoBateu.Where(c => !double.IsNaN(c.UltimoLidoDaMemoria)).ToList();
        if (divergiram.Count > 0)
        {
            foreach (var campo in divergiram) await campo.WriteAsync();
            await _session.ExecutarVerificadoAsync(BaseCommand.SaveSettings,
                      prazo: TimeSpan.FromMilliseconds(PrazoDeConfirmacaoMs));
            naoBateu   = await ConferirGravacaoAsync(divergiram);
            divergiram = naoBateu.Where(c => !double.IsNaN(c.UltimoLidoDaMemoria)).ToList();
        }

        if (divergiram.Count == 0)
        {
            IsDirty = false;
            MensagemDeSalvar = L.Get("Settings_Saved");
            return;
        }

        IsDirty = true;
        MensagemDeSalvar = L.Get("Settings_SaveMismatch") + "\n" +
              string.Join("\n", divergiram.Select(c =>
                  $"  • {c.DisplayName}: enviei {c.Value:0.###}, a base ficou com {c.UltimoLidoDaMemoria:0.###}"));
    }

    /// <summary>Quanto esperar pela confirmação da base antes de dizer que a gravação não aconteceu.
    ///
    /// <para>Público só porque o teste do caso "não gravou" precisa encurtá-lo — esperar dez segundos
    /// de verdade num teste é tempo jogado fora, e um projeto que faz isso acaba deixando de rodar os
    /// testes. Ninguém mais tem motivo para mexer aqui.</para></summary>
    public int PrazoDeConfirmacaoMs { get; set; } = 10_000;

    private async Task<bool> EsperarConfirmacaoAsync(byte? antes)
    {
        if (antes is null) return true;
        const int passo = 200;
        for (var esperou = 0; esperou < PrazoDeConfirmacaoMs; esperou += passo)
        {
            await Task.Delay(passo);
            var agora = _session.UltimoEstado?.SaveCount;
            if (agora is not null && agora != antes) return true;
        }
        return false;
    }

    /// <summary>Relê da memória permanente os campos que acabaram de ser enviados e devolve os que
    /// NÃO bateram. Lista vazia = gravou tudo.
    ///
    /// <para>Tenta por alguns segundos antes de desistir: a base precisa parar o motor para gravar, e
    /// parar leva tempo. Desistir cedo acusaria falha numa gravação que ia acontecer.</para></summary>
    private async Task<List<SettingFieldViewModel>> ConferirGravacaoAsync(List<SettingFieldViewModel> enviados)
    {
        var pendentes = new List<SettingFieldViewModel>(enviados);
        for (var tentativa = 0; tentativa < 6 && pendentes.Count > 0; tentativa++)
        {
            await Task.Delay(500);
            var restam = new List<SettingFieldViewModel>();
            foreach (var campo in pendentes)
            {
                try
                {
                    var gravado = await _session.ReadSettingSavedAsync(campo.SettingId);
                    campo.UltimoLidoDaMemoria = gravado.AsDouble;
                    // ⚠️ TOLERÂNCIA RELATIVA, e não absoluta. Um campo em ampères tolera 0,001 sem
                    // problema; um Kt de 0,397 guardado como float de 32 bits volta como
                    // 0,39699998..., e 0,001 absoluto é apertado o suficiente para acusar diferença
                    // onde não há. Escalar pelo valor cobre os dois sem deixar passar erro real.
                    var tolerancia = Math.Max(0.001, Math.Abs(campo.Value) * 0.001);
                    if (Math.Abs(gravado.AsDouble - campo.Value) > tolerancia) restam.Add(campo);
                }
                catch
                {
                    // Leitura não voltou: não dá para afirmar que gravou. Fica pendente e tenta de novo.
                    campo.UltimoLidoDaMemoria = double.NaN;
                    restam.Add(campo);
                }
            }
            pendentes = restam;
        }
        return pendentes;
    }

    /// <summary>O que dizer depois de "Salvar" — vazio enquanto ninguém salvou nada nesta sessão.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SalvarDeuCerto))]
    private string? _mensagemDeSalvar;

    /// <summary>Deu certo? Governa a COR do aviso.
    ///
    /// <para>A mensagem antiga era sempre da mesma cor, e um "salvo" tinha exatamente a mesma
    /// aparência de um "não gravou". Quem acabou de clicar precisa saber o desfecho antes de ler —
    /// verde e vermelho respondem em um relance, e o texto explica só quando é preciso explicar.</para></summary>
    public bool SalvarDeuCerto => MensagemDeSalvar == L.Get("Settings_Saved");

    private bool CanSave() => IsConnected && IsDirty;

    /// <summary>
    /// Preenche a tela com os valores de FÁBRICA, perguntados à base (report 0x17).
    ///
    /// <para>Não grava nada: a placa continua com o que tinha até alguém clicar em Salvar. Isso é
    /// deliberado — "Padrão" mostra o que a base usaria, e a decisão continua sendo de quem está
    /// na frente do volante.</para>
    ///
    /// <para>Perguntamos em vez de usar o padrão do descritor porque os dois valores moram em
    /// lugares diferentes — o array do firmware e o schema do app. Hoje coincidem, mas nada
    /// garantia isso: bastava editar um lado para "Padrão" escrever valores diferentes dos de uma
    /// placa recém-gravada, com os dois parecendo o padrão. Com a base respondendo, existe uma
    /// resposta só.</para>
    ///
    /// <para>Se a base não responder por um campo, aquele campo cai no padrão do schema — é o
    /// comportamento anterior, e vale mais que deixar a tela pela metade.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task ResetDefaults()
    {
        foreach (var field in AllFields())
        {
            try
            {
                var padrao = await _session.ReadSettingDefaultAsync(field.SettingId);
                field.ApplyDefaultFromDevice(padrao.AsDouble);
            }
            catch (Exception)
            {
                // Firmware antigo (sem o 0x17) ou resposta perdida: usa o do schema.
                field.ResetToDefault();
            }
        }
    }

    // ------------------------------------------------------------------------------------------
    // Exportar / importar em arquivo
    // ------------------------------------------------------------------------------------------
    //
    // O ESCOPO segue as abas VISÍVEIS, e isso é de propósito. No modo cliente a aba Hardware não
    // existe, então os campos que descrevem a MÁQUINA (pares de polos, resolução do encoder, Kt,
    // variante da placa) não são exportados nem aceitos na importação — receber esses valores do
    // arquivo de outra pessoa configuraria a base para um hardware que não é o dela. No modo criador
    // a aba existe e eles entram, que é o caso de quem monta a base do zero: numa placa recém-gravada
    // são justamente eles que precisam vir de algum lugar.
    //
    // ⚠️ O QUE NÃO VIAJA NO ARQUIVO: a calibração do motor (alinhamento do encoder, R/L). Ela mora na
    // NVM do ODrive, não nos settings, e é específica do conjunto motor+encoder montado. Uma placa que
    // recebe este arquivo ainda vai calibrar sozinha no primeiro arme — é o comportamento correto, e
    // não um campo que ficou faltando.

    /// <summary>Todos os campos editáveis desta página (só as abas visíveis no modo atual).</summary>
    private IReadOnlyList<SettingFieldViewModel> CamposEditaveis() => AllFields().ToList();

    [RelayCommand(CanExecute = nameof(CanExchangeFiles))]
    private async Task ExportAsync()
    {
        if (_filePicker is null) return;

        var campos = CamposEditaveis();
        var perfil = BaseProfileExchange.Criar(campos.Select(f => (f.SettingId, f.Value, f.IsLoaded)));

        if (perfil.Settings.Count == 0)
        {
            // Sem nenhum campo lido não há o que exportar, e gravar um arquivo vazio seria pior que
            // não gravar: ele importaria "com sucesso" e não mudaria nada.
            ExchangeStatus = L.Get("BaseProfile_NothingToExport");
            return;
        }

        var path = await _filePicker.PickSaveAsync("drivelab-base.json");
        if (path is null) return;

        var json = ProfileExchange.Serialize(
            BaseProfileExchange.Module,
            new[] { (Name: Title, Data: perfil) },
            DateTimeOffset.Now);
        await File.WriteAllTextAsync(path, json);

        ExchangeStatus = string.Format(L.Get("BaseProfile_Exported"), perfil.Settings.Count);
    }

    [RelayCommand(CanExecute = nameof(CanExchangeFiles))]
    private async Task ImportAsync()
    {
        if (_filePicker is null) return;

        var path = await _filePicker.PickOpenAsync();
        if (path is null) return;

        var envelope = ProfileExchange.Deserialize<BaseProfile>(await File.ReadAllTextAsync(path));
        if (!string.IsNullOrEmpty(envelope.Module) &&
            !string.Equals(envelope.Module, BaseProfileExchange.Module, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                string.Format(L.Get("BaseProfile_WrongModule"), envelope.Module));

        var perfil = envelope.Profiles.FirstOrDefault()?.Data;
        if (perfil is null || perfil.Settings.Count == 0)
            throw new InvalidOperationException(L.Get("BaseProfile_EmptyFile"));

        var campos = CamposEditaveis();
        var porId = campos.ToDictionary(f => f.SettingId);
        var resultado = BaseProfileExchange.Preparar(perfil, porId.Keys.ToHashSet());

        foreach (var (id, valor) in resultado.Aplicar)
            porId[id].ApplyImported(valor);

        ExchangeStatus = MontarResumo(resultado);
    }

    /// <summary>Conta o que entrou e o que ficou de fora. "Importado com sucesso" depois de aplicar
    /// 3 de 40 campos é pior que um erro — a pessoa vai pilotar achando que está com o ajuste que
    /// escolheu. Cada categoria que não estiver vazia aparece.</summary>
    private static string MontarResumo(BaseProfileImport r)
    {
        var partes = new List<string> { string.Format(L.Get("BaseProfile_Imported"), r.Aplicar.Count) };
        if (r.ForaDestaTela.Count > 0)
            partes.Add(string.Format(L.Get("BaseProfile_SkippedHardware"), r.ForaDestaTela.Count));
        if (r.Desconhecidos.Count > 0)
            partes.Add(string.Format(L.Get("BaseProfile_SkippedUnknown"), r.Desconhecidos.Count));
        if (r.Ajustados > 0)
            partes.Add(string.Format(L.Get("BaseProfile_Clamped"), r.Ajustados));
        partes.Add(L.Get("BaseProfile_RememberToSave"));
        return string.Join(" · ", partes);
    }

    public override void Dispose()
    {
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        _session.SettingChanged -= OnSettingWritten;
        foreach (var field in AllFields())
            field.Edited -= OnFieldEdited;
        foreach (var tab in Tabs)
            tab.Content.Dispose();
        base.Dispose();
    }
}
