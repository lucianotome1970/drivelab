// ============================================================================
//  DriveLab
//  BaseReportIds.cs — IDs de report HID usados pelo volante (estado, comando, controle direto, settings).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public static class BaseReportIds
{
    // Remapped from 0x01/0x02: the base ships as ONE combined HID interface (FFB + A0 share it),
    // so these must not collide with the FFB wheel's own DeviceState (0x01) / Command (0x02) reports.
    public const byte DeviceState = 0x21;

    /// <summary>O relatório que vai para o JOGO: botões e oito eixos, sendo o primeiro a direção.
    ///
    /// <para>Ele passa pelo app mil vezes por segundo e era descartado — o desenho do volante
    /// esperava a telemetria a 25 Hz e precisava interpolar para não ficar aos saltos. Ler daqui dá
    /// 40× mais amostras sem custar nada: o relatório já é enviado de qualquer forma.</para>
    ///
    /// <para>E desacopla a tela da telemetria, que é o caminho onde a base trava: se ela parar, o
    /// volante continua girando na tela. Isso é diagnóstico, não só fluidez — dá para ver na hora que
    /// a telemetria caiu enquanto o resto funciona.</para></summary>
    public const byte Joystick = 0x01;
    public const byte Command = 0x22;
    public const byte DirectControl = 0x10;
    public const byte SettingWrite = 0x14;
    public const byte SettingReadRequest = 0x15;
    public const byte SettingValue = 0x16;

    /// <summary>Pede o valor de FÁBRICA de um ajuste. Mesmo payload do 0x15, e a base responde pelo
    /// mesmo 0x16 — por isso as duas leituras não podem estar em voo ao mesmo tempo.</summary>
    public const byte SettingDefaultRequest = 0x17;

    /// <summary>Pede o valor GRAVADO na memória permanente (0x18), respondido pelo mesmo 0x16.
    ///
    /// <para>Existe para o "Salvar" poder VERIFICAR em vez de acreditar: o app relê o campo da flash
    /// e compara com o que mandou. Um contador dizendo "gravei" prova que algo aconteceu, não QUE
    /// valor ficou lá — e, pior, dependia da telemetria, que é justamente o caminho onde o firmware
    /// trava.</para></summary>
    public const byte SettingNvmRequest = 0x18;
}
