using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantAccessPermissionsToInstitutionAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, k.key
            FROM roles r
            CROSS JOIN (VALUES ('access.manage'), ('access.scan'), ('access.view')) AS k(key)
            WHERE r.key = 'institution_admin' AND r.institution_id IS NOT NULL
              AND EXISTS (SELECT 1 FROM permissions p WHERE p.key = k.key)
            ON CONFLICT DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
