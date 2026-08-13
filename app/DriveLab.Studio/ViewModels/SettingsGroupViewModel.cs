// ============================================================================
//  DriveLab
//  SettingsGroupViewModel.cs — VM de uma página de settings para um conjunto curado de campos, carregados do dispositivo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Página de ajustes para um conjunto curado de settings (ex.: "Base do Volante",
/// "Avançado"). Carrega os valores do dispositivo ao conectar.
/// </summary>
public class SettingsGroupViewModel : ViewModelBase
{
    private readonly BaseSession _session;

    public string Title { get; }
    public IReadOnlyList<SettingFieldViewModel> Fields { get; }

    /// <summary>Metade dos campos, para o layout em 2 colunas (estilo MOZA).</summary>
    ///
    /// <remarks>Tem setter protegido porque uma aba pode ter campo que NÃO se edita por slider —
    /// hoje a curva de força, que é um gráfico. Esses campos precisam continuar no
    /// <see cref="Fields"/> (é de lá que sai o carregamento da placa e o Salvar), mas sair das
    /// colunas, senão apareceriam duas vezes: uma no controle próprio e outra como slider solto.</remarks>
    public IReadOnlyList<SettingFieldViewModel> LeftColumn { get; protected set; }
    public IReadOnlyList<SettingFieldViewModel> RightColumn { get; protected set; }

    /// <summary>Refaz a divisão em colunas com o subconjunto dado. Para abas que tiram campos da
    /// lista sem tirá-los do grupo.</summary>
    protected void DividirEmColunas(IReadOnlyList<SettingFieldViewModel> campos)
    {
        var metade = (campos.Count + 1) / 2;
        LeftColumn = campos.Take(metade).ToList();
        RightColumn = campos.Skip(metade).ToList();
    }

    public SettingsGroupViewModel(BaseSession session, string title, IEnumerable<BaseSettingId> ids)
    {
        _session = session;
        Title = title;
        Fields = ids.Select(id => new SettingFieldViewModel(session, BaseSettingsSchema.Get(id))).ToList();

        var half = (Fields.Count + 1) / 2; // coluna esquerda leva o excedente
        LeftColumn = Fields.Take(half).ToList();
        RightColumn = Fields.Skip(half).ToList();

        _session.Connected += OnConnected;
        // A base pode JÁ estar conectada quando esta aba é criada (a sessão sobe antes das telas).
        // Nesse caso o evento Connected nunca dispara e a aba ficaria vazia para sempre — que é
        // exatamente o "salvei na placa e o app não traz de volta" relatado na bancada.
        if (_session.IsConnected)
            _ = LoadAsync();
    }

    /// <summary>Lê da placa o valor de cada campo. Um campo que falhe (timeout, device sumiu) NÃO
    /// derruba os demais: antes a exceção abortava o foreach e todos os campos seguintes ficavam
    /// sem carregar, silenciosamente (a chamada é fire-and-forget).</summary>
    public async Task LoadAsync()
    {
        // Re-tenta o que não veio. A leitura tem timeout, e uma rodada pode pegar o canal ocupado
        // (o app acabou de subir, o jogo largou o device, a base está re-armando) — sem repetir, a
        // aba inteira ficava em "—" para sempre, porque o evento Connected não vem duas vezes.
        for (var rodada = 0; rodada < 3; rodada++)
        {
            var faltou = false;
            foreach (var field in Fields)
            {
                if (field.IsLoaded) continue;                 // já veio numa rodada anterior
                try { await field.LoadAsync(); }
                catch { faltou = true; }                      // um campo que falha não derruba os outros
            }
            if (!faltou) return;
            await Task.Delay(400);
        }
    }

    private void OnConnected(object? sender, EventArgs e) => _ = LoadAsync();

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        foreach (var field in Fields)
            field.Dispose();
        base.Dispose();
    }
}
