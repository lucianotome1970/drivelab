// ============================================================================
//  DriveLab
//  INamedProfileStore.cs — Contrato de biblioteca de perfis nomeados por módulo (CRUD por nome).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Studio.Services;

/// <summary>Biblioteca de perfis nomeados de um módulo (ex.: volante → "Chuva", "GT3").
/// Cada perfil é um snapshot da config configurável; a persistência concreta decide o formato.</summary>
public interface INamedProfileStore<T> where T : class
{
    /// <summary>Nomes dos perfis existentes, em ordem alfabética.</summary>
    IReadOnlyList<string> ListNames();

    Task<T?> LoadAsync(string name);
    Task SaveAsync(string name, T profile);
    void Delete(string name);

    /// <summary>Renomeia (no-op se o antigo não existir; sobrescreve se o novo já existir).</summary>
    void Rename(string oldName, string newName);
}
