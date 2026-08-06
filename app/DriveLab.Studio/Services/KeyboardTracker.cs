// ============================================================================
//  DriveLab
//  KeyboardTracker.cs — Lógica PURA da fonte de teclado: transforma eventos crus (tecla down/up) em
//    transições limpas (suprime auto-repeat), p/ o CenterHotkey detectar borda sem re-disparo. Separado
//    do hook do Windows (que não tem teste) para poder testar sem plataforma. Também os nomes de tecla.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DriveLab.Studio.Services;

/// <summary>Acompanha o CONJUNTO de teclas pressionadas e emite só quando ele MUDA (primeira descida ou
/// subida de uma tecla). Auto-repeat (descida de tecla já pressionada) é ignorado. Retorna o conjunto
/// atual — é o que permite montar/casar um COMBO de teclado sem re-disparo enquanto se segura.</summary>
public sealed class KeyboardTracker
{
    private readonly HashSet<int> _down = new();

    /// <summary>Processa um evento cru. Retorna true se o conjunto mudou (deve emitir), com
    /// <paramref name="pressed"/> = as teclas pressionadas agora (ordenadas; vazio quando soltou tudo).</summary>
    public bool Process(int vk, bool down, out IReadOnlyList<int> pressed)
    {
        bool changed = down ? _down.Add(vk) : _down.Remove(vk);
        pressed = changed ? _down.OrderBy(k => k).ToArray() : Array.Empty<int>();
        return changed;
    }
}

/// <summary>Nomes amigáveis das teclas virtuais (Windows VK) mais comuns, p/ o rótulo do atalho.</summary>
public static class KeyNames
{
    public static string For(int vk)
    {
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture); // F1..F24
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();                                        // A..Z
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();                                        // 0..9
        return vk switch
        {
            0x20 => "Espaço",
            0x0D => "Enter",
            0x1B => "Esc",
            0x09 => "Tab",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            // Modificadores (genéricos e L/R), comuns em combos tipo Ctrl+F9.
            0x10 or 0xA0 or 0xA1 => "Shift",
            0x11 or 0xA2 or 0xA3 => "Ctrl",
            0x12 or 0xA4 or 0xA5 => "Alt",
            _ => "tecla " + vk.ToString(CultureInfo.InvariantCulture),
        };
    }
}
