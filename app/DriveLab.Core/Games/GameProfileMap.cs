// ============================================================================
//  DriveLab
//  GameProfileMap.cs — Mapa jogo → perfis por módulo (base/aro/pedais/freio) para o auto-perfil.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;

namespace DriveLab.Core.Games;

/// <summary>Perfis (por nome) a carregar em cada módulo quando um jogo é detectado. Null = não trocar aquele
/// módulo.</summary>
public sealed class ModuleProfiles
{
    public string? Base { get; set; }
    public string? Wheel { get; set; }
    public string? Pedals { get; set; }
    public string? Handbrake { get; set; }
}

/// <summary>Mapa persistível de <c>gameId</c> → <see cref="ModuleProfiles"/>. Serializa como JSON simples.</summary>
public sealed class GameProfileMap
{
    public Dictionary<string, ModuleProfiles> Bindings { get; set; } = new();

    /// <summary>Perfis mapeados para o jogo, ou null se não houver binding.</summary>
    public ModuleProfiles? For(string gameId) =>
        Bindings.TryGetValue(gameId, out var m) ? m : null;

    /// <summary>Define (ou substitui) o binding de um jogo.</summary>
    public void Set(string gameId, ModuleProfiles profiles) => Bindings[gameId] = profiles;
}
