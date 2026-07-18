// ============================================================================
//  DriveLab
//  NavItem.cs — Registro de um item de navegação (label, ícone e página).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.ViewModels;

// Label   = rótulo curto da sidebar (tooltip). Ex.: "Volante".
// Title   = título completo do módulo, exibido no topo do app ("DriveLab Studio — <Title>").
//           Vazio para a Home (o topo mostra só "DriveLab Studio").
public sealed record NavItem(string Label, string Icon, ViewModelBase Page, string Title = "");
