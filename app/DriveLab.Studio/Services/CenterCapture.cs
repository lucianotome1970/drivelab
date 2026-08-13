// ============================================================================
//  DriveLab
//  CenterCapture.cs — Captura por GESTO do atalho de centralizar: acumula tudo o que for pressionado
//    junto (numa fonte só) e fecha o mapeamento quando TUDO é solto. É o que permite montar um COMBO
//    (vários botões/teclas): "segure o combo e solte para confirmar". Puro e testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;

namespace DriveLab.Studio.Services;

/// <summary>Acumula um gesto de captura. Trava na PRIMEIRA fonte que apertar algo (ignora as outras até
/// terminar), soma os botões/teclas enquanto segura e devolve o <see cref="CenterBinding"/> quando solta
/// tudo. Reutilizável: após fechar (ou <see cref="Reset"/>) começa um gesto novo.</summary>
public sealed class CenterCapture
{
    private bool _started;
    private CenterSourceKind _kind;
    private string? _deviceId;
    private string _deviceName = "";
    private uint _mask;
    private readonly SortedSet<int> _keys = new();

    /// <summary>Nome amigável do dispositivo do gesto (p/ o rótulo). Válido junto do binding retornado.</summary>
    public string DeviceName => _deviceName;

    /// <summary>Alimenta um snapshot. Retorna o binding quando o gesto fecha (soltou tudo); senão null.</summary>
    public CenterBinding? Feed(InputSnapshot s)
    {
        if (!_started)
        {
            if (!s.HasInput) return null;                 // espera o 1º aperto
            _started = true;
            _kind = s.Kind;
            _deviceId = s.DeviceId;
            _deviceName = s.DeviceName;
        }
        else if (s.Kind != _kind || (_kind == CenterSourceKind.Hid && s.DeviceId != _deviceId))
        {
            return null;                                  // outra fonte no meio do gesto → ignora
        }

        _mask |= s.Buttons;
        foreach (var k in s.Keys) _keys.Add(k);

        if (!s.HasInput)                                  // soltou tudo → fecha o gesto
        {
            var binding = _kind == CenterSourceKind.Keyboard
                ? new CenterBinding(CenterSourceKind.Keyboard, null, 0u, new List<int>(_keys))
                : new CenterBinding(CenterSourceKind.Hid, _deviceId, _mask, null, _deviceName);
            return binding;
        }
        return null;
    }

    /// <summary>Descarta o gesto em andamento.</summary>
    public void Reset()
    {
        _started = false;
        _kind = default;
        _deviceId = null;
        _deviceName = "";
        _mask = 0u;
        _keys.Clear();
    }
}
