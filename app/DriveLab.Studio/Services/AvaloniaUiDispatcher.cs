// ============================================================================
//  DriveLab
//  AvaloniaUiDispatcher.cs — Implementação de IUiDispatcher que posta ações no Dispatcher de UI do Avalonia.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia.Threading;

namespace DriveLab.Studio.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
