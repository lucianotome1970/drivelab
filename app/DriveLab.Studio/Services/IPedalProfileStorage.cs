// ============================================================================
//  DriveLab
//  IPedalProfileStorage.cs — Contrato e registro (record) de persistência do perfil dos pedais.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

public sealed record PedalProfileColumn(
    int Sensor, int InputMin, int InputMax, bool Invert, int Smooth, double[] Curve, int LoadCellScale,
    int LoadCellMaxKg = 100, bool BrakeUnitKg = false);

public sealed record PedalProfile(PedalProfileColumn[] Columns);

public interface IPedalProfileStorage
{
    Task SaveAsync(PedalProfile profile);
    Task<PedalProfile?> LoadAsync();
}
