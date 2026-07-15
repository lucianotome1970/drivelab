// ============================================================================
//  DriveLab
//  NavItem.cs — Registro de um item de navegação (label, ícone e página).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.ViewModels;

public sealed record NavItem(string Label, string Icon, ViewModelBase Page);
