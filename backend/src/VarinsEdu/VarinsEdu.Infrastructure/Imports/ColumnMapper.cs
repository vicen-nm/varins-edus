using System.Globalization;
using System.Text;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Infrastructure.Imports;

public static class ColumnMapper
{
    // Common header names, in Spanish and English, for each field. They are normalized once, below.
    private static readonly Dictionary<string, HashSet<string>> Synonyms = Build(new Dictionary<string, string[]>
    {
        [StudentImportFields.IdentificationNumber] =
            ["Cédula", "Identificación", "Número de identificación", "Número de cédula", "Documento", "ID"],
        [StudentImportFields.IdentificationType] =
            ["Tipo de identificación", "Tipo de documento", "Tipo de cédula", "Tipo ID"],
        [StudentImportFields.FirstName] = ["Nombre", "Nombres", "Primer nombre"],
        [StudentImportFields.LastName] = ["Apellido", "Apellidos"],
        [StudentImportFields.Group] = ["Sección", "Grupo", "Curso", "Grado"],
        [StudentImportFields.Phone] = ["Teléfono", "Celular", "Móvil", "Tel"],
        [StudentImportFields.Email] = ["Correo", "Correo electrónico", "Email", "Mail"]
    });

    // Guesses which Excel column holds each field. The user can always correct the guess.
    public static Dictionary<string, string> Suggest(IReadOnlyList<string> headers)
    {
        var suggestion = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            var key = Normalize(header);

            if (key.Length == 0)
            {
                continue;
            }

            foreach (var (field, names) in Synonyms)
            {
                if (!suggestion.ContainsKey(field) && names.Contains(key))
                {
                    suggestion[field] = header;
                }
            }
        }

        return suggestion;
    }

    // Lowercase, no accents, only letters and digits: "Correo electrónico" becomes "correoelectronico".
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, HashSet<string>> Build(Dictionary<string, string[]> raw) =>
        raw.ToDictionary(pair => pair.Key, pair => pair.Value.Select(Normalize).ToHashSet());
}
