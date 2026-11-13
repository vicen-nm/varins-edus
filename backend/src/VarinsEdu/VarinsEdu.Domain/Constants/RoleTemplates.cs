namespace VarinsEdu.Domain.Constants;

// Default roles created for every new institution. Each institution can adjust them afterwards.
public static class RoleTemplates
{
    public sealed record RoleTemplate(string Key, string Name, IReadOnlyCollection<string> Permissions);

    public static readonly IReadOnlyList<RoleTemplate> ForInstitutions =
    [
        new(RoleKeys.InstitutionAdmin, "Institution administrator", PermissionKeys.ForInstitutions),
        new(RoleKeys.Teacher, "Teacher", [PermissionKeys.StudentsView]),
        new(
            RoleKeys.CafeteriaStaff,
            "Cafeteria staff",
            [PermissionKeys.AccessScan, PermissionKeys.AccessView, PermissionKeys.AccessDisplay]),
        new(RoleKeys.CafeteriaDisplay, "Cafeteria display screen", [PermissionKeys.AccessDisplay]),
        new(RoleKeys.GateStaff, "Gate staff", [PermissionKeys.CardsScan]),
        new(RoleKeys.Student, "Student", [])
    ];
}
