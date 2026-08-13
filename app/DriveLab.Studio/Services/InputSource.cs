// ============================================================================
//  DriveLab
//  InputSource.cs — Abstração de fonte de entrada p/ o atalho de centralizar: o aro, um HID qualquer
//    (buttonbox/gamepad) ou o teclado emitem "snapshots" do que está pressionado. Uma única base para
//    o CenterHotkey casar o mapeamento, independente da origem física.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;

namespace DriveLab.Studio.Services;

/// <summary>Instantâneo do estado de uma fonte, num momento. Para Hid, <see cref="Buttons"/> é a máscara
/// atualmente pressionada (bit i = botão i) e <see cref="Keys"/> é vazio; para Keyboard, <see cref="Keys"/>
/// é o CONJUNTO de teclas (vk) pressionadas agora e <see cref="Buttons"/>=0. O conjunto (em vez de uma
/// tecla) é o que permite COMBO de teclado.</summary>
public readonly record struct InputSnapshot(
    CenterSourceKind Kind, string? DeviceId, string DeviceName, uint Buttons, IReadOnlyList<int> Keys)
{
    /// <summary>Lista vazia reutilizável (fontes HID não usam teclas).</summary>
    public static readonly IReadOnlyList<int> NoKeys = Array.Empty<int>();

    /// <summary>Há algo pressionado neste instante (serve p/ acumular a captura no modo "atribuir").</summary>
    public bool HasInput => Buttons != 0u || (Keys is { Count: > 0 });
}

/// <summary>Fonte de entrada que emite <see cref="InputSnapshot"/> a cada mudança de estado.
/// Implementações: HID genérico (buttonbox/gamepad/aro), teclado (hook global). O evento pode vir de
/// uma thread de background — o consumidor marshala p/ a UI quando precisar.</summary>
public interface IInputSource : IDisposable
{
    event Action<InputSnapshot>? Snapshot;

    /// <summary>Começa a observar (abre o hook/poll). Idempotente.</summary>
    void Start();
}
