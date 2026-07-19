// ============================================================================
//  DriveLab
//  FirmwareFile.cs — Parser/validador puro da assinatura de firmware
//  (magic "DRVLABFW" + DeviceKind + versão) usada pra conferir, antes de
//  flashear por USB, que um arquivo selecionado é um firmware DriveLab e
//  bate com o dispositivo alvo. Espelha o layout de
//  firmware-base/src/m05/fw_signature.h. Sem IO — recebe os bytes já lidos
//  (o chamador na UI/updater é quem lê o arquivo do disco).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text;

namespace DriveLab.Core.Update;

/// <summary>
/// Resultado de <see cref="FirmwareFile.Read"/>: assinatura encontrada (ou não),
/// o <see cref="DeviceKind"/> e a versão (major.minor.build) embutidos no arquivo.
/// </summary>
public readonly record struct FirmwareInfo(DeviceKind Kind, Version Version, bool Found);

/// <summary>
/// Varre bytes de um arquivo de firmware (.bin ou blocos de payload de um .uf2)
/// procurando a assinatura "DRVLABFW" + kind (1 byte) + ver[3] (major/minor/patch).
/// </summary>
public static class FirmwareFile
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DRVLABFW");

    /// <summary>
    /// Procura a assinatura em <paramref name="file"/>. Bounds-safe: assinatura
    /// truncada perto do fim do buffer (magic presente mas sem os 4 bytes
    /// seguintes de kind+versão) retorna <c>Found=false</c>, sem lançar.
    /// </summary>
    public static FirmwareInfo Read(byte[] file)
    {
        var index = IndexOfMagic(file);
        if (index < 0)
            return new FirmwareInfo(default, new Version(0, 0, 0), Found: false);

        var kindOffset = index + Magic.Length;
        var verOffset = kindOffset + 1;
        if (verOffset + 3 > file.Length)
            return new FirmwareInfo(default, new Version(0, 0, 0), Found: false);

        var kind = (DeviceKind)file[kindOffset];
        var version = new Version(file[verOffset], file[verOffset + 1], file[verOffset + 2]);
        return new FirmwareInfo(kind, version, Found: true);
    }

    /// <summary>True se o arquivo tem a assinatura e ela bate com <paramref name="kind"/>.</summary>
    public static bool Matches(byte[] file, DeviceKind kind)
    {
        var info = Read(file);
        return info.Found && info.Kind == kind;
    }

    private static int IndexOfMagic(byte[] file)
    {
        if (file.Length < Magic.Length)
            return -1;

        for (var i = 0; i <= file.Length - Magic.Length; i++)
        {
            var match = true;
            for (var j = 0; j < Magic.Length; j++)
            {
                if (file[i + j] != Magic[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
}
