// ============================================================================
//  DriveLab
//  CenterBinding.cs — Mapeamento persistido do atalho de "centralizar o volante": de qual fonte
//    (controlador HID ou teclado), de qual dispositivo, e qual botão/combo (máscara HID) ou combo de
//    teclas (conjunto de vk). É o modelo genérico do atalho. Suporta COMBO nas duas fontes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DriveLab.Studio.Services;

/// <summary>De onde vem o atalho de centralizar.</summary>
public enum CenterSourceKind
{
    /// <summary>Um controlador HID: buttonbox, gamepad, outro volante — e o nosso próprio aro (tudo é HID).</summary>
    Hid = 1,
    /// <summary>Uma ou mais teclas do teclado (hook global).</summary>
    Keyboard = 2,
}

/// <summary>Atalho de centralizar já mapeado. Para Hid usa <see cref="Mask"/> (combo = vários bits); para
/// Keyboard usa <see cref="Keys"/> (combo = várias teclas, casadas quando TODAS estão pressionadas).
/// <see cref="DeviceId"/> fixa qual dispositivo HID. Igualdade por VALOR (inclui a sequência de teclas).</summary>
public sealed class CenterBinding : IEquatable<CenterBinding>
{
    public CenterSourceKind Kind { get; }
    public string? DeviceId { get; }
    public uint Mask { get; }
    public IReadOnlyList<int> Keys { get; }

    /// <summary>Nome amigável do dispositivo HID (só p/ exibição — NÃO entra na identidade/igualdade).</summary>
    public string? DeviceName { get; }

    public CenterBinding(CenterSourceKind kind, string? deviceId, uint mask, IReadOnlyList<int>? keys,
                         string? deviceName = null)
    {
        Kind = kind;
        DeviceId = deviceId;
        Mask = mask;
        DeviceName = deviceName;
        // Normaliza as teclas: ordenadas e sem repetição (p/ igualdade estável e casamento simples).
        Keys = keys is null || keys.Count == 0
            ? Array.Empty<int>()
            : keys.Distinct().OrderBy(k => k).ToArray();
    }

    /// <summary>Sem mapeamento (nada centraliza).</summary>
    public static readonly CenterBinding None = new(CenterSourceKind.Hid, null, 0u, null);

    /// <summary>Tem algo mapeado (botão/combo ou tecla/combo).</summary>
    public bool IsSet => Mask != 0u || Keys.Count > 0;

    /// <summary>Rótulo amigável p/ a UI, ex.: "Buttonbox X: Botão 3", "Teclado: Ctrl + F9".</summary>
    public string Describe()
    {
        if (!IsSet) return "—";
        if (Kind == CenterSourceKind.Keyboard)
            return "Teclado: " + string.Join(" + ", Keys.Select(KeyNames.For));
        string src = string.IsNullOrWhiteSpace(DeviceName) ? "Controlador" : DeviceName!;
        return $"{src}: {DescribeMask(Mask)}";
    }

    /// <summary>"Botão 3" ou "Botões 2 + 5" a partir da máscara de bits (bit i = botão i+1).</summary>
    public static string DescribeMask(uint mask)
    {
        if (mask == 0u) return "—";
        var ids = new List<string>();
        for (int i = 0; i < 32; i++)
            if ((mask & (1u << i)) != 0) ids.Add((i + 1).ToString(CultureInfo.InvariantCulture));
        return (ids.Count == 1 ? "Botão " : "Botões ") + string.Join(" + ", ids);
    }

    public bool Equals(CenterBinding? other) =>
        other is not null && Kind == other.Kind && DeviceId == other.DeviceId &&
        Mask == other.Mask && Keys.SequenceEqual(other.Keys);

    public override bool Equals(object? obj) => Equals(obj as CenterBinding);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(Kind); hc.Add(DeviceId); hc.Add(Mask);
        foreach (var k in Keys) hc.Add(k);
        return hc.ToHashCode();
    }
}
