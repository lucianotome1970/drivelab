using System.Globalization;
using System.Resources;

namespace DriveLab.Studio.Localization;

/// <summary>
/// Localização EN (padrão) + PT. Os dois idiomas ficam embarcados no assembly
/// principal (evita satellite assemblies, que quebram no publish single-file);
/// o idioma é escolhido no startup pelo idioma do Windows.
/// </summary>
public static class LocalizationManager
{
    private static readonly ResourceManager En =
        new("DriveLab.Studio.Localization.Strings", typeof(LocalizationManager).Assembly);
    private static readonly ResourceManager Pt =
        new("DriveLab.Studio.Localization.StringsPt", typeof(LocalizationManager).Assembly);

    /// <summary>True quando o app está em Português.</summary>
    public static bool IsPortuguese { get; private set; }

    public static void UseCulture(CultureInfo culture) =>
        IsPortuguese = culture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Detecta o idioma do Windows: pt → Português; qualquer outro → Inglês.</summary>
    public static void DetectFromSystem() => UseCulture(CultureInfo.InstalledUICulture);

    /// <summary>Texto traduzido; cai para o Inglês e depois para a própria chave se faltar.</summary>
    public static string Get(string key)
    {
        var primary = IsPortuguese ? Pt : En;
        return primary.GetString(key) ?? En.GetString(key) ?? key;
    }
}
