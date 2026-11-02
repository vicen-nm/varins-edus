using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GrantAccessPermissionsToInstitutionAdmins2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The catalog first: during a deployment the migrations run before the seeder.
            migrationBuilder.Sql("""
            INSERT INTO permissions (key, description) VALUES
                ('access.manage', 'Configure access points (cafeteria) and their service schedules.'),
                ('access.scan', 'Register access manually and show an access point''s QR screen.'),
                ('access.view', 'View the access registrations.')
            ON CONFLICT (key) DO NOTHING;
            """);

            // Institutions that already existed do not get new template permissions on their own.
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, k.key
            FROM roles r
            CROSS JOIN (VALUES ('access.manage'), ('access.scan'), ('access.view')) AS k(key)
            WHERE r.key = 'institution_admin' AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: removing the permissions could also remove ones an administrator granted by hand.
        }
    }
}
