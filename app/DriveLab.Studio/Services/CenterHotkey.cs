// ============================================================================
//  DriveLab
//  CenterHotkey.cs — Casa um CenterBinding contra os snapshots de entrada e dispara a centralização na
//    BORDA DE APERTO (uma vez por aperto). Combo = todos os bits juntos. Genérico: aro, HID ou teclado.
//    Sucessor do CenterButtonWatcher (que só via o aro). Puro e testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Linq;

namespace DriveLab.Studio.Services;

/// <summary>Detecta a borda de aperto do <see cref="Binding"/> mapeado e chama a ação de centralizar.
/// Só olha snapshots da MESMA fonte/dispositivo do binding — snapshots de outros dispositivos são
/// ignorados (não afetam a detecção de borda), então múltiplas fontes convivem sem re-disparo espúrio.</summary>
public sealed class CenterHotkey
{
    private readonly Action _center;
    private bool _wasPressed;

    /// <summary>Mapeamento ativo (None = desabilitado).</summary>
    public CenterBinding Binding { get; set; } = CenterBinding.None;

    public CenterHotkey(Action center)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
    }

    /// <summary>Alimentar com cada snapshot recebido. Dispara na transição solto→apertado.</summary>
    public void Feed(InputSnapshot s)
    {
        if (!Binding.IsSet) { _wasPressed = false; return; }
        if (!SameSource(Binding, s)) return;   // outro dispositivo: não mexe na borda deste binding

        bool pressed = Matches(Binding, s);
        if (pressed && !_wasPressed) _center();
        _wasPressed = pressed;
    }

    /// <summary>Zera o estado de borda (ex.: ao reconectar a fonte) p/ não disparar num aperto "herdado".</summary>
    public void Reset() => _wasPressed = false;

    // Mesma origem: mesmo tipo e, p/ HID, mesmo dispositivo.
    private static bool SameSource(CenterBinding b, InputSnapshot s)
    {
        if (b.Kind != s.Kind) return false;
        if (b.Kind == CenterSourceKind.Hid) return b.DeviceId == s.DeviceId;
        return true;
    }

    // Combo HID = todos os bits da máscara presentes (bits extras não atrapalham).
    // Combo de teclado = todas as teclas do binding pressionadas (teclas extras não atrapalham).
    private static bool Matches(CenterBinding b, InputSnapshot s) =>
        b.Kind == CenterSourceKind.Keyboard
            ? b.Keys.Count > 0 && b.Keys.All(s.Keys.Contains)
            : b.Mask != 0u && (s.Buttons & b.Mask) == b.Mask;
}
