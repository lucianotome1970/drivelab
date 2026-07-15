// ============================================================================
//  DriveLab
//  IWheelProfileStorage.cs — Contrato e registros de persistência do perfil do volante (cores de botões e pás).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Services;

public sealed record WheelButtonColor(string Name, string ColorHex);

public sealed record WheelProfile(
    WheelButtonColor[] Buttons,
    int PaddleCount,
    PaddleFunction BottomFunction,
    PaddleMode BottomMode,
    PaddleActuation BottomActuation,
    int BottomBitePoint);

public interface IWheelProfileStorage
{
    Task SaveAsync(WheelProfile profile);
    Task<WheelProfile?> LoadAsync();
}
