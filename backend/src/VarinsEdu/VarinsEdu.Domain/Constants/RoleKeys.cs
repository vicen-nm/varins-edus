namespace VarinsEdu.Domain.Constants;

public static class RoleKeys
{
    public const string PlatformAdmin = "platform_admin";
    public const string InstitutionAdmin = "institution_admin";
    public const string Teacher = "teacher";
    public const string CafeteriaStaff = "cafeteria_staff";
    public const string Student = "student";

    // The roles that can be given to staff accounts. Students get theirs when they activate.
    public static readonly string[] StaffRoles = [InstitutionAdmin, Teacher, CafeteriaStaff];
}
