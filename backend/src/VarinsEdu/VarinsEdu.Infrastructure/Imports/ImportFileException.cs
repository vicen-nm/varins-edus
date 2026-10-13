namespace VarinsEdu.Infrastructure.Imports;

// A problem with the uploaded file or its column mapping.
// The code is stable, and the frontend translates it.
public sealed class ImportFileException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
