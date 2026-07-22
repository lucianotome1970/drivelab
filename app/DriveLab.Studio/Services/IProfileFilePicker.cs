// ============================================================================
//  DriveLab
//  IProfileFilePicker.cs — Seleção de arquivo .json para exportar/importar perfis (seam testável).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

/// <summary>Escolhe onde salvar / de onde ler um arquivo de perfis (.json). Separado do
/// <see cref="IFilePicker"/> (firmware) para não quebrar quem já o implementa. Null = usuário cancelou.</summary>
public interface IProfileFilePicker
{
    Task<string?> PickSaveAsync(string suggestedFileName);
    Task<string?> PickOpenAsync();
}
