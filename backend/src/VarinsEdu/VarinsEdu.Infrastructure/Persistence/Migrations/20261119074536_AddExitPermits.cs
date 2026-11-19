using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExitPermits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exit_permits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    to_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exit_permits", x => x.id);
                    table.ForeignKey(
                        name: "FK_exit_permits_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exit_permits_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exit_permits_users_cancelled_by_user_id",
                        column: x => x.cancelled_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exit_permits_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exit_permits_users_used_by_user_id",
                        column: x => x.used_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exit_permits_cancelled_by_user_id",
                table: "exit_permits",
                column: "cancelled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_exit_permits_created_by_user_id",
                table: "exit_permits",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_exit_permits_institution_id_service_date",
                table: "exit_permits",
                columns: new[] { "institution_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "IX_exit_permits_person_id_service_date",
                table: "exit_permits",
                columns: new[] { "person_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "IX_exit_permits_used_by_user_id",
                table: "exit_permits",
                column: "used_by_user_id");

            // The catalog first: during a deployment the migrations run before the seeder.
            migrationBuilder.Sql("""
            INSERT INTO permissions (key, description) VALUES
                ('exits.manage', 'Create and cancel student exit permits.'),
                ('exits.scan', 'Register student exits at the gate by scanning cards.')
            ON CONFLICT (key) DO NOTHING;
            """);

            // Roles of institutions that already existed do not get new template permissions on their own.
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, k.key
            FROM roles r
            CROSS JOIN (VALUES ('exits.manage'), ('exits.scan')) AS k(key)
            WHERE r.key = 'institution_admin' AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);

            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, 'exits.manage'
            FROM roles r
            WHERE r.key = 'teacher' AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);

            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, 'exits.scan'
            FROM roles r
            WHERE r.key = 'gate_staff' AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exit_permits");
        }
    }
}
