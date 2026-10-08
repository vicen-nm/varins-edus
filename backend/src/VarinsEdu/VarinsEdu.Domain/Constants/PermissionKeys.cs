namespace VarinsEdu.Domain.Constants;

public static class PermissionKeys
{
    // Permissions that can be granted inside an institution.
    public const string BrandingManage = "branding.manage";
    public const string AccessibilityManage = "accessibility.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string StudentsView = "students.view";
    public const string StudentsImport = "students.import";
    public const string AuditView = "audit.view";

    // Platform-level permission: only the platform role gets it, never an institution role.
    public const string InstitutionsManage = "institutions.manage";

    public static readonly string[] ForInstitutions =
    [
        BrandingManage,
        AccessibilityManage,
        UsersManage,
        RolesManage,
        StudentsView,
        StudentsImport,
        AuditView
    ];

    // Every permission, including the platform-level ones.
    public static readonly string[] All = [.. ForInstitutions, InstitutionsManage];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [BrandingManage] = "Change the institution name, logo and colors.",
        [AccessibilityManage] = "Configure display preferences (simple mode) for users.",
        [UsersManage] = "Create and manage user accounts.",
        [RolesManage] = "Create roles and assign permissions to them.",
        [StudentsView] = "View students and groups.",
        [StudentsImport] = "Import students from Excel.",
        [AuditView] = "View the audit log.",
        [InstitutionsManage] = "Create and manage institutions (platform only)."
    };
}
