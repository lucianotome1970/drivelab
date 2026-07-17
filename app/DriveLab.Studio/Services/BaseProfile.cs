// ============================================================================
//  DriveLab
//  BaseProfile.cs — Perfil da base do volante: snapshot dos settings configuráveis (chave→valor).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;

namespace DriveLab.Studio.Services;

/// <summary>Snapshot dos settings da base (força total, batente, mola, damper, hardware…) por chave estável
/// (nome do <c>BaseSettingId</c>) → valor. Aplicar escreve cada um no controlador.</summary>
public sealed record BaseProfile(Dictionary<string, double> Settings);
