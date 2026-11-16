using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plate = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    make = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.id);
                    table.ForeignKey(
                        name: "FK_vehicles_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vehicles_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vehicles_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vehicles_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_created_by_user_id",
                table: "vehicles",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_institution_id_plate",
                table: "vehicles",
                columns: new[] { "institution_id", "plate" },
                unique: true,
                filter: "status IN ('pending', 'approved', 'suspended')");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_institution_id_status",
                table: "vehicles",
                columns: new[] { "institution_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_person_id",
                table: "vehicles",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_reviewed_by_user_id",
                table: "vehicles",
                column: "reviewed_by_user_id");

            // The catalog first: during a deployment the migrations run before the seeder.
            migrationBuilder.Sql("""
            INSERT INTO permissions (key, description) VALUES
                ('vehicles.manage', 'Register students'' vehicles and approve, reject or suspend them.'),
                ('vehicles.view', 'View students'' vehicles and their approval status.')
            ON CONFLICT (key) DO NOTHING;
            """);

            // Roles of institutions that already existed do not get new template permissions on their own.
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, k.key
            FROM roles r
            CROSS JOIN (VALUES ('vehicles.manage'), ('vehicles.view')) AS k(key)
            WHERE r.key = 'institution_admin' AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicles");
        }
    }
}
