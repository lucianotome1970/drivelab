// ============================================================================
//  DriveLab
//  BaseCommand.cs — Enum dos comandos de dispositivo do volante (reboot, salvar settings, calibrar, DFU, etc.).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Transport;

public enum BaseCommand : byte
{
    Reboot = 1,
    SaveSettings = 2,
    ResetCenter = 3,
    EnterDfu = 4,
    Calibrate = 5,
    SetForceEnabled = 6,

    /// <summary>Dispara a calibração de cogging por-motor (giro lento medindo o ripple, grava a tabela na
    /// flash do dispositivo). Exige motor energizado — só roda na bancada (Stage 1). Só o criador tem acesso
    /// (botão na aba Hardware). Ver coggingCalibRequested no firmware.</summary>
    CalibrateCogging = 7,

    /// <summary>Bancada: arma o brake chopper + duty manual (cmd 8). Enviado por tools/a0_cmd.py, não pela UI.</summary>
    BrakeBench = 8,

    /// <summary>Bancada: arma o brake chopper em modo AUTOMÁTICO (controlador dirige o duty pela tensão do bus; cmd 9). Enviado por tools/a0_cmd.py.</summary>
    BrakeAuto = 9,

    /// <summary>Mede o alinhamento do encoder: gira o motor uma volta em malha aberta e compara o que
    /// o encoder leu com onde o rotor de fato estava. Não altera a calibração — o offset é salvo antes
    /// e devolvido ao terminar. Exige motor energizado e desarmado. Ver encoder_eccentricity.h.</summary>
    TestEncoder = 10,

    /// <summary>Rearma por pedido explícito: destrava a guarda de curso, limpa os erros e pede malha
    /// fechada. Existe porque a guarda travada só zerava no boot — quem está jogando teria de ir até
    /// a fonte desligar a base.
    ///
    /// <para>⚠️ Quem envia isto está passando por cima de uma proteção que decidiu travar. A tela
    /// precisa dizer o que aconteceu antes de oferecer o botão, e não apresentá-lo como um "armar"
    /// qualquer.</para></summary>
    Rearm = 11,
}
