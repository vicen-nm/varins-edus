namespace VarinsEdu.Domain.Constants;

// The fields an Excel column can be mapped to when importing students.
public static class StudentImportFields
{
    public const string IdentificationNumber = "identificationNumber";
    public const string IdentificationType = "identificationType";
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string Group = "group";
    public const string Phone = "phone";
    public const string Email = "email";

    public static readonly string[] All =
    [
        IdentificationNumber, IdentificationType, FirstName, LastName, Group, Phone, Email
    ];

    public static readonly string[] Required = [IdentificationNumber, FirstName, LastName];
}
