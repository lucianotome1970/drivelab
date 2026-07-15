// ============================================================================
//  DriveLab
//  PedalCurvePreset.cs — Presets de curva de pedal (nome + pontos) e chip de seleção estilo Pit House.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

public sealed record PedalCurvePreset(string Name, double[] Points);

/// <summary>Chip selecionável de preset (estilo Pit House): expõe o preset + estado de seleção.</summary>
public sealed partial class PedalPresetOption : ObservableObject
{
    public PedalCurvePreset Preset { get; }
    public string Name => Preset.Name;

    [ObservableProperty]
    private bool _isSelected;

    public PedalPresetOption(PedalCurvePreset preset) => Preset = preset;
}

public static class PedalCurvePresets
{
    public static IReadOnlyList<PedalCurvePreset> All { get; } = new List<PedalCurvePreset>
    {
        new("Linear", new double[] { 0, 20, 40, 60, 80, 100 }),
        new("S-Curve", new double[] { 0, 8, 28, 72, 92, 100 }),
        new("Fast", new double[] { 0, 38, 62, 78, 90, 100 }),
        new("Slow", new double[] { 0, 10, 22, 38, 62, 100 }),
    };
}
