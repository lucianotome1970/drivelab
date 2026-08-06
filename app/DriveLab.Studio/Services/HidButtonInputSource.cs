// ============================================================================
//  DriveLab
//  HidButtonInputSource.cs — Fonte HID genérica do atalho de centralizar: enumera TODOS os controladores
//    HID com botões (buttonbox, gamepad, joystick, volante — inclusive o NOSSO aro), lê os reports em
//    background e emite a máscara de botões pressionados. Mais keyboard fica noutra fonte (hook global).
//    O I/O do HidSharp NÃO tem teste unitário (como o resto do caminho USB); a lógica pura está em
//    HidButtons e é testada. Validação ponta a ponta é na bancada, com o hardware.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace DriveLab.Studio.Services;

/// <summary>Lê os botões de qualquer controlador HID e emite <see cref="InputSnapshot"/>(Hid). Reescaneia
/// no hotplug (DeviceList.Changed). Best-effort: qualquer falha ao abrir/ler um dispositivo é engolida —
/// nunca derruba o app; aquele dispositivo só não vira fonte.</summary>
public sealed class HidButtonInputSource : IInputSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Listener> _listeners = new();   // por DevicePath
    private bool _started;

    public event Action<InputSnapshot>? Snapshot;

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        try { DeviceList.Local.Changed += OnDeviceListChanged; } catch { /* sem lista → sem hotplug */ }
        Rescan();
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e) => Rescan();

    // Abre listeners p/ dispositivos elegíveis novos; descarta os que sumiram.
    private void Rescan()
    {
        IEnumerable<HidDevice> devices;
        try { devices = DeviceList.Local.GetHidDevices().ToList(); }
        catch { return; }

        var present = new HashSet<string>();
        foreach (var dev in devices)
        {
            string path;
            try { path = dev.DevicePath; } catch { continue; }
            present.Add(path);

            lock (_gate)
            {
                if (_listeners.ContainsKey(path)) continue;      // já escutando
            }
            if (!IsEligible(dev)) continue;

            var listener = TryOpen(dev, path);
            if (listener is not null)
                lock (_gate) { _listeners[path] = listener; }
        }

        // Remove os que não estão mais presentes.
        List<Listener> gone;
        lock (_gate)
        {
            gone = _listeners.Where(kv => !present.Contains(kv.Key)).Select(kv => kv.Value).ToList();
            foreach (var l in gone) _listeners.Remove(l.Path);
        }
        foreach (var l in gone) l.Dispose();
    }

    // Elegível = tem botões e não é mouse/teclado/keypad do sistema. Em caso de dúvida (descriptor ilegível),
    // deixa passar: um dispositivo extra é inofensivo (só vira fonte se o usuário atribuir um botão dele).
    private static bool IsEligible(HidDevice dev)
    {
        // A BASE fica de fora SEMPRE, e antes de qualquer coisa: a BaseSession já a mantém aberta
        // para o FFB, e abrir um segundo handle no mesmo endpoint HID a derruba do USB
        // (visto na bancada 2026-08-05). O aro (PID 0x0004) segue elegível.
        try
        {
            if (HidButtons.IsOwnBase(dev.VendorID, dev.ProductID)) return false;
        }
        catch { /* se nem VID/PID dá pra ler, segue para a checagem normal abaixo */ }

        try
        {
            var rd = dev.GetReportDescriptor();
            bool hasButtons = rd.DeviceItems
                .SelectMany(di => di.InputReports)
                .SelectMany(r => r.DataItems)
                .SelectMany(d => d.Usages.GetAllValues())
                .Any(u => (u >> 16) == 0x09);   // usage page Button
            uint top = rd.DeviceItems.SelectMany(di => di.Usages.GetAllValues()).FirstOrDefault();
            return HidButtons.ShouldListen(hasButtons, (int)(top >> 16), (int)(top & 0xFFFF));
        }
        catch { return true; }
    }

    private Listener? TryOpen(HidDevice dev, string path)
    {
        try
        {
            if (!dev.TryOpen(out var stream)) return null;
            string name;
            try { name = dev.GetFriendlyName(); } catch { try { name = dev.GetProductName(); } catch { name = "Controlador"; } }
            var listener = new Listener(dev, stream, path, name, OnDeviceSnapshot);
            listener.Start();
            return listener;
        }
        catch { return null; }
    }

    private void OnDeviceSnapshot(InputSnapshot s) => Snapshot?.Invoke(s);

    public void Dispose()
    {
        try { DeviceList.Local.Changed -= OnDeviceListChanged; } catch { }
        List<Listener> all;
        lock (_gate) { all = _listeners.Values.ToList(); _listeners.Clear(); }
        foreach (var l in all) l.Dispose();
    }

    /// <summary>Um dispositivo aberto: recebe reports, extrai a máscara de botões e emite no que mudar.</summary>
    private sealed class Listener : IDisposable
    {
        public string Path { get; }
        private readonly string _name;
        private readonly HidStream _stream;
        private readonly HidDeviceInputReceiver _receiver;
        private readonly List<(DeviceItem item, DeviceItemInputParser parser)> _parsers;
        private readonly byte[] _buffer;
        private readonly Action<InputSnapshot> _emit;
        private uint _lastMask;

        public Listener(HidDevice dev, HidStream stream, string path, string name, Action<InputSnapshot> emit)
        {
            Path = path; _name = name; _stream = stream; _emit = emit;
            var rd = dev.GetReportDescriptor();
            _receiver = rd.CreateHidDeviceInputReceiver();
            _parsers = rd.DeviceItems.Select(di => (di, di.CreateDeviceItemInputParser())).ToList();
            _buffer = new byte[Math.Max(1, dev.GetMaxInputReportLength())];
        }

        public void Start()
        {
            _receiver.Received += OnReceived;
            _receiver.Start(_stream);
        }

        private void OnReceived(object? sender, EventArgs e)
        {
            try
            {
                while (_receiver.TryRead(_buffer, 0, out var report))
                {
                    foreach (var (_, parser) in _parsers)
                    {
                        if (!parser.TryParseReport(_buffer, 0, report)) continue;
                        var pressed = new List<int>();
                        for (int i = 0; i < parser.ValueCount; i++)
                        {
                            var dv = parser.GetValue(i);
                            if (dv.GetLogicalValue() == 0) continue;
                            foreach (var usage in dv.Usages)
                                if ((usage >> 16) == 0x09) pressed.Add((int)(usage & 0xFFFF));
                        }
                        uint mask = HidButtons.MaskFromPressed(pressed);
                        if (mask != _lastMask)
                        {
                            _lastMask = mask;
                            _emit(new InputSnapshot(CenterSourceKind.Hid, Path, _name, mask, InputSnapshot.NoKeys));
                        }
                    }
                }
            }
            catch { /* leitura falhou (device sumiu?) → ignora; o rescan cuida do resto */ }
        }

        public void Dispose()
        {
            try { _receiver.Received -= OnReceived; } catch { }
            try { _stream.Close(); } catch { }
        }
    }
}
