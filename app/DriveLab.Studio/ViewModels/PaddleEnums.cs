namespace DriveLab.Studio.ViewModels;

/// <summary>Função da pá de baixo (mock — não vai ao firmware).</summary>
public enum PaddleFunction { Clutch, Free, Button }

/// <summary>Modo da dupla de pás de embreagem.</summary>
public enum PaddleMode { Combined, Independent }

/// <summary>Acionamento da pá: clique (digital) ou progressão (analógico).</summary>
public enum PaddleActuation { Digital, Progression }
