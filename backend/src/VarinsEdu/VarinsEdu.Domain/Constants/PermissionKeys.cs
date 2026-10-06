namespace VarinsEdu.Domain.Constants;

public static class PermissionKeys
{
    public const string BrandingManage = "branding.manage";
    public const string AccessibilityManage = "accessibility.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string StudentsView = "students.view";
    public const string StudentsImport = "students.import";
    public const string AuditView = "audit.view";

    public static readonly string[] All =
    [
        BrandingManage,
        AccessibilityManage,
        UsersManage,
        RolesManage,
        StudentsView,
        StudentsImport,
        AuditView
    ];
}
