// ============================================================================
//  DriveLab
//  KeyboardInputSource.cs — Fonte de teclado do atalho de centralizar via HOOK GLOBAL de baixo nível
//    (Windows WH_KEYBOARD_LL), para capturar a tecla mesmo com o app em background (durante o jogo).
//    Fora do Windows é no-op. A lógica de transição/nome está em KeyboardTracker (pura, testada); aqui
//    fica o P/Invoke — sem teste unitário, validado no Windows.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DriveLab.Studio.Services;

/// <summary>Instala um hook global de teclado numa thread própria com laço de mensagens; emite
/// <see cref="InputSnapshot"/>(Keyboard) nas transições de tecla. Só roda no Windows.</summary>
public sealed class KeyboardInputSource : IInputSource
{
    private readonly KeyboardTracker _tracker = new();
    private Thread? _thread;
    private volatile bool _running;
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;   // mantém o delegate vivo (senão o GC recolhe e o hook quebra)
    private uint _threadId;

    public event Action<InputSnapshot>? Snapshot;

    public void Start()
    {
        if (!OperatingSystem.IsWindows() || _running) return;
        _running = true;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "DriveLab-Keyboard-Hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) { _running = false; return; }

        // Laço de mensagens: o hook LL só entrega eventos numa thread que bombeia mensagens.
        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            // WM_QUIT (0) encerra o GetMessage; nada mais a despachar (não temos janela).
        }

        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            if (down || up)
            {
                int vk = Marshal.ReadInt32(lParam);   // KBDLLHOOKSTRUCT.vkCode (primeiro campo)
                if (_tracker.Process(vk, down, out var pressed))
                    Snapshot?.Invoke(new InputSnapshot(CenterSourceKind.Keyboard, null, "Teclado", 0, pressed));
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_running) return;
        _running = false;
        // Acorda o GetMessage p/ a thread sair e desinstalar o hook.
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(500);
    }

    // ---- P/Invoke (user32) ----
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
