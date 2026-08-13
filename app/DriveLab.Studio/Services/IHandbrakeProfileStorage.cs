// ============================================================================
//  DriveLab
//  IHandbrakeProfileStorage.cs — Contrato e registro (record) de persistência do perfil do freio de mão.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

public sealed record HandbrakeProfile(
    int Sensor, int InputMin, int InputMax, bool Invert, int Smooth, double[] Curve, int LoadCellScale,
    int ButtonThreshold, bool ButtonEnabled, int LoadCellMaxKg = 100, bool UnitKg = false);

public interface IHandbrakeProfileStorage
{
    Task SaveAsync(HandbrakeProfile profile);
    Task<HandbrakeProfile?> LoadAsync();
}
