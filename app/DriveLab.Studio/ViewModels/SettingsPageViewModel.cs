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

    private void OnFieldEdited(object? sender, EventArgs e) => IsDirty = true;

    /// <summary>Todos os campos de setting das abas (cada aba de config é um SettingsGroupViewModel).</summary>
    private IEnumerable<SettingFieldViewModel> AllFields() =>
        Tabs.Select(t => t.Content).OfType<SettingsGroupViewModel>().SelectMany(g => g.Fields);

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session.IsConnected;
        // Ao (re)conectar os grupos recarregam da placa via read (não dispara SettingChanged):
        // app passa a refletir a flash, então zera o dirty.
        IsDirty = false;
    }

    // SettingChanged só dispara em WriteSettingAsync (nunca em read/load) → todo write marca dirty.
    private void OnSettingWritten(object? sender, SettingChangedEventArgs e)
    {
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // ENVIA primeiro, persiste depois. Mexer nos controles não manda mais nada para a base
        // (SettingFieldViewModel.OnValueChanged) — o usuário monta o ajuste inteiro na tela e só aqui
        // ele vai para a placa, de uma vez. Evita reconfigurar a base ao vivo com o motor armado e a
        // rajada de writes de quando se arrasta um slider.
        var enviados = new List<SettingFieldViewModel>();
        foreach (var field in AllFields())
            if (field.IsModified)
            {
                await field.WriteAsync();
                enviados.Add(field);   // só estes precisam ser conferidos depois
            }

        // ⚠️ VERIFICAR O QUE FICOU GRAVADO, e não acreditar num aviso.
        //
        // A primeira versão disto perguntava à base "gravou?" por um contador na telemetria. Frágil
        // dos dois lados: a telemetria é justamente o caminho onde o firmware trava, e um contador só
        // diz que ALGO aconteceu — não QUE valor ficou lá. Se um campo não fosse enviado, o contador
        // subiria igual e o app diria "salvo" sobre um ajuste que não foi.
        //
        // Agora o app relê os campos da memória permanente e compara com o que mandou. Se ler 4096,
        // gravou 4096: não há o que interpretar. E se este caminho falhar, ele falha VISIVELMENTE (a
        // leitura não volta) em vez de mentir — falhar avisando é aceitável, mentir não é.
        //
        // A gravação exige o motor PARADO (ela congela a CPU por ~250 ms), então numa base presa
        // tentando calibrar ela pode não acontecer. É exatamente esse o caso que precisa ser dito.
        await _session.SendCommandAsync(BaseCommand.SaveSettings);

        MensagemDeSalvar = null;
        var naoGravou = await ConferirGravacaoAsync(enviados);
        IsDirty = naoGravou.Count > 0;
        MensagemDeSalvar = naoGravou.Count == 0 ? L.Get("Settings_Saved") : L.Get("Settings_SaveBusy");
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
                    // Comparação numérica: o valor volta pelo mesmo tipo com que foi escrito, e a
                    // tolerância cobre o ida-e-volta de float sem deixar passar diferença real.
                    if (Math.Abs(gravado.AsDouble - campo.Value) > 0.001) restam.Add(campo);
                }
                catch
                {
                    // Leitura não voltou: não dá para afirmar que gravou. Fica pendente e tenta de novo.
                    restam.Add(campo);
                }
            }
            pendentes = restam;
        }
        return pendentes;
    }

    /// <summary>O que dizer depois de "Salvar" — vazio enquanto ninguém salvou nada nesta sessão.</summary>
    [ObservableProperty] private string? _mensagemDeSalvar;

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
