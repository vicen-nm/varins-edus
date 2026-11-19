using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    identification_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    host = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    signature_png = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    handled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => x.id);
                    table.ForeignKey(
                        name: "FK_visits_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_users_exited_by_user_id",
                        column: x => x.exited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_users_handled_by_user_id",
                        column: x => x.handled_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visits_exited_by_user_id",
                table: "visits",
                column: "exited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_visits_handled_by_user_id",
                table: "visits",
                column: "handled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_visits_institution_id_identification_number_service_date",
                table: "visits",
                columns: new[] { "institution_id", "identification_number", "service_date" });

            migrationBuilder.CreateIndex(
                name: "IX_visits_institution_id_service_date",
                table: "visits",
                columns: new[] { "institution_id", "service_date" });

            // The catalog first: during a deployment the migrations run before the seeder.
            migrationBuilder.Sql("""
            INSERT INTO permissions (key, description) VALUES
                ('visits.manage', 'Admit or reject visitors and register their exit.'),
                ('visits.view', 'View the visitors and their signatures.')
            ON CONFLICT (key) DO NOTHING;
            """);

            // Roles of institutions that already existed do not get new template permissions on their own.
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, k.key
            FROM roles r
            CROSS JOIN (VALUES ('visits.manage'), ('visits.view')) AS k(key)
            WHERE r.key IN ('institution_admin', 'gate_staff') AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visits");
        }
    }
}
