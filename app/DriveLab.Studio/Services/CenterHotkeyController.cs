// ============================================================================
//  DriveLab
//  CenterHotkeyController.cs — Orquestra o atalho de centralizar com VÁRIOS mapeamentos ao mesmo tempo
//    (estilo ACC: um botão de controle E/OU uma tecla, cada um removível). Junta as fontes (HID/teclado),
//    roda um CenterHotkey por binding, cuida do modo "atribuir" (captura por gesto), da lista e da persistência.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace DriveLab.Studio.Services;

/// <summary>Controla o atalho "centralizar" com N mapeamentos. Qualquer um deles, ao ser apertado,
/// centraliza. As fontes emitem snapshots (possivelmente em background); os updates de UI são marshalados
/// via <see cref="IUiDispatcher"/>. A ação <c>center</c> é fire-and-forget e pode rodar em qualquer thread.</summary>
public sealed class CenterHotkeyController : IDisposable
{
    private readonly List<Entry> _entries = new();
    private readonly CenterCapture _capture = new();
    private readonly IReadOnlyList<IInputSource> _sources;
    private readonly Action _center;
    private readonly Action<IReadOnlyList<CenterBinding>> _save;
    private readonly IUiDispatcher _ui;

    /// <summary>Em modo de captura: o próximo gesto vira um novo mapeamento.</summary>
    public bool IsAssigning { get; private set; }

    /// <summary>Disparado (na thread de UI) quando a lista de mapeamentos ou IsAssigning mudam.</summary>
    public event EventHandler? Changed;

    public CenterHotkeyController(Action center, IEnumerable<IInputSource> sources,
                                  IReadOnlyList<CenterBinding> initial, Action<IReadOnlyList<CenterBinding>> save,
                                  IUiDispatcher ui)
    {
        _center = center;
        _sources = sources.ToList();
        _save = save;
        _ui = ui;
        foreach (var b in initial.Where(b => b.IsSet))
            _entries.Add(new Entry(b, new CenterHotkey(center) { Binding = b }));

        foreach (var s in _sources)
        {
            s.Snapshot += OnSnapshot;
            s.Start();
        }
    }

    /// <summary>Os mapeamentos atuais (com rótulo via <see cref="CenterBinding.Describe"/>).</summary>
    public IReadOnlyList<CenterBinding> Bindings => _entries.Select(e => e.Binding).ToList();

    private void OnSnapshot(InputSnapshot s)
    {
        if (IsAssigning)
        {
            var binding = _capture.Feed(s);
            if (binding is null) return;
            AddBinding(binding);
        }
        else
        {
            foreach (var e in _entries) e.Hotkey.Feed(s);   // qualquer binding pode disparar
        }
    }

    /// <summary>Inicia/cancela a captura. Ao iniciar: segure o botão/combo (ou as teclas) e solte para
    /// confirmar — soltar tudo fecha um novo mapeamento (some ao já existente).</summary>
    public void ToggleAssign()
    {
        _capture.Reset();
        Post(() => IsAssigning = !IsAssigning);
    }

    /// <summary>Remove um mapeamento específico.</summary>
    public void Remove(CenterBinding binding)
    {
        Post(() =>
        {
            _entries.RemoveAll(e => e.Binding.Equals(binding));
            _save(_entries.Select(e => e.Binding).ToList());
        });
    }

    /// <summary>Remove todos os mapeamentos.</summary>
    public void ClearAll()
    {
        Post(() =>
        {
            _entries.Clear();
            _save(_entries.Select(e => e.Binding).ToList());
        });
    }

    private void AddBinding(CenterBinding binding)
    {
        Post(() =>
        {
            IsAssigning = false;
            if (!_entries.Any(e => e.Binding.Equals(binding)))   // não duplica o mesmo atalho
                _entries.Add(new Entry(binding, new CenterHotkey(_center) { Binding = binding }));
            _save(_entries.Select(e => e.Binding).ToList());
        });
    }

    // Marshala a mutação p/ a thread de UI e notifica (a lista alimenta a UI).
    private void Post(Action mutate)
    {
        _ui.Post(() =>
        {
            mutate();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        foreach (var s in _sources)
        {
            s.Snapshot -= OnSnapshot;
            s.Dispose();
        }
    }

    private sealed record Entry(CenterBinding Binding, CenterHotkey Hotkey);
}
