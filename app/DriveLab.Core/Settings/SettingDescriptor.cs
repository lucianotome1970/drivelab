// ============================================================================
//  DriveLab
//  SettingDescriptor.cs — Descritor de um setting do volante: chave, faixa, unidade, aba e valor default.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public sealed record SettingDescriptor(
    BaseSettingId Id,
    string Key,
    string DisplayName,
    SettingType Type,
    double Min,
    double Max,
    string Unit,
    SettingTab Tab,
    double Default,
    /// <summary>Texto de ajuda do campo, exibido no "?" ao lado do rótulo. Vazio = sem ícone.
    /// Preencher SÓ onde a explicação acrescenta: campo que pode queimar hardware, que usa
    /// vocabulário que o iniciante não tem, ou cujo efeito não é óbvio pelo nome. Ícone que
    /// promete ajuda e entrega "este é o valor do parâmetro" é pior que ícone nenhum.</summary>
    string Help = "",
    /// <summary>Renderiza como campo numérico em vez de slider. Para valor EXATO, que se digita:
    /// resolução do encoder, constante de torque. Arrastar um slider até 1000 num range de milhares
    /// é sorte, não ajuste.</summary>
    bool Numeric = false)
{
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}
