// ============================================================================
//  DriveLab
//  ImmediateUiDispatcher.cs — IUiDispatcher que executa ações imediatamente, sem thread de UI (uso em testes).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
