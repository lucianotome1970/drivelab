// ============================================================================
//  DriveLab
//  IUiDispatcher.cs — Contrato de dispatcher para postar ações na thread de UI.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
