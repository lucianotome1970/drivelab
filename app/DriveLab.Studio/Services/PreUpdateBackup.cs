// ============================================================================
//  DriveLab
//  PreUpdateBackup.cs — A copia das configuracoes tirada ANTES de gravar firmware novo.
//
//  POR QUE EXISTE: a configuracao de uma base custa horas para acertar e vive dentro da placa. Um
//  firmware novo pode chegar com o schema diferente do que estava gravado, e a partir do momento em
//  que a placa e de outra pessoa nao ha bancada nem SWD para socorrer ninguem. Este arquivo e a
//  volta atras.
//
//  Ele carimba de ONDE saiu — firmware e versao de schema. Sem isso o backup e um monte de numeros
//  sem dono: nao da para dizer se aqueles valores ainda significam a mesma coisa no firmware que
//  esta na placa agora.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using System;
using System.Collections.Generic;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Studio.Services;

public static class PreUpdateBackup
{
    /// <summary>Monta o JSON do backup. Devolve <c>null</c> quando nao ha NADA lido da placa —
    /// gravar um arquivo vazio seria pior do que nao gravar, porque quem restaurasse depois
    /// sobrescreveria a propria configuracao com nada.</summary>
    public static string? Montar(IReadOnlyList<(BaseSettingId Id, double Valor)> lidos,
                                 FirmwareVersion firmware, DateTimeOffset quando)
    {
        if (lidos.Count == 0)
            return null;

        var perfil = new BaseProfile();
        foreach (var (id, valor) in lidos)
            perfil.Settings[id.ToString()] = valor;

        var selo = new ProfileDeviceStamp(
            $"{firmware.Major}.{firmware.Minor}.{firmware.Patch}", BaseSettingsSchema.SchemaVersion);

        var nome = $"antes da atualização — {quando:yyyy-MM-dd HH:mm}";
        return ProfileExchange.Serialize(BaseProfileExchange.Module,
                                         new[] { (nome, perfil) }, quando, selo);
    }
}
