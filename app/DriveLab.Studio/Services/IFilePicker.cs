// ============================================================================
//  DriveLab
//  IFilePicker.cs — Abstração mínima de seletor de arquivo, injetável na VM
//  para manter a lógica de UpdateViewModel testável sem tocar as APIs de
//  IStorageProvider do Avalonia diretamente.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

/// <summary>Seleciona um arquivo de firmware (.bin/.uf2) no disco. Retorna null se o usuário cancelar.</summary>
public interface IFilePicker
{
    Task<string?> PickFirmwareFileAsync();
}
