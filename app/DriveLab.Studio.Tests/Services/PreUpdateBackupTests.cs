// ============================================================================
//  DriveLab
//  PreUpdateBackupTests.cs — Testes da copia de configuracoes tirada antes de atualizar firmware.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using System;
using System.Collections.Generic;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class PreUpdateBackupTests
{
    // Placa que nao respondeu a nenhuma leitura nao tem configuracao para perder. Gravar um arquivo
    // vazio seria pior que nao gravar: quem restaurasse depois sobrescreveria a placa com nada.
    [Fact]
    public void Sem_Nenhum_Valor_Lido_Nao_Ha_O_Que_Salvar()
    {
        var json = PreUpdateBackup.Montar(
            new List<(BaseSettingId, double)>(), new FirmwareVersion(0, 0, 2, 7), DateTimeOffset.UnixEpoch);

        Assert.Null(json);
    }

    // O arquivo tem de dizer de qual firmware e de qual versao de schema ele saiu — e o que permite,
    // na hora de restaurar, saber se aqueles numeros ainda significam a mesma coisa.
    [Fact]
    public void O_Arquivo_Carimba_Firmware_E_Schema_De_Origem()
    {
        var json = PreUpdateBackup.Montar(
            new List<(BaseSettingId, double)> { (BaseSettingId.TotalStrength, 85) },
            new FirmwareVersion(0, 0, 2, 7), DateTimeOffset.UnixEpoch);

        var env = ProfileExchange.Deserialize<BaseProfile>(json!);

        Assert.Equal("0.2.7", env.Device!.Firmware);
        Assert.Equal(BaseSettingsSchema.SchemaVersion, env.Device!.SchemaVersion);
        Assert.Equal(BaseProfileExchange.Module, env.Module);
        Assert.Equal(85, env.Profiles[0].Data!.Settings["TotalStrength"]);
    }
}
