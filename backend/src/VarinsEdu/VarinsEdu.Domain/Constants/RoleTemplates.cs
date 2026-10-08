namespace VarinsEdu.Domain.Constants;

// Default roles created for every new institution. Each institution can adjust them afterwards.
public static class RoleTemplates
{
    public sealed record RoleTemplate(string Key, string Name, IReadOnlyCollection<string> Permissions);

    public static readonly IReadOnlyList<RoleTemplate> ForInstitutions =
    [
        new(RoleKeys.InstitutionAdmin, "Institution administrator", PermissionKeys.ForInstitutions),
        new(RoleKeys.Teacher, "Teacher", [PermissionKeys.StudentsView]),
        new(RoleKeys.Student, "Student", [])
    ];
}
