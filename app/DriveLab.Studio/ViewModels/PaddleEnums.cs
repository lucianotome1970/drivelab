// ============================================================================
//  DriveLab
//  PaddleEnums.cs — Enums de função, modo e acionamento das pás de embreagem (mock).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.ViewModels;

/// <summary>Função da pá de baixo (mock — não vai ao firmware).</summary>
public enum PaddleFunction { Clutch, Free, Button }

/// <summary>Modo da dupla de pás de embreagem.</summary>
public enum PaddleMode { Combined, Independent }

/// <summary>Acionamento da pá: clique (digital) ou progressão (analógico).</summary>
public enum PaddleActuation { Digital, Progression }
