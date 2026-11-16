using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parking_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auto_closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_parking_events_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parking_events_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parking_events_users_entered_by_user_id",
                        column: x => x.entered_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parking_events_users_exited_by_user_id",
                        column: x => x.exited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parking_events_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parking_events_entered_by_user_id",
                table: "parking_events",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_events_exited_by_user_id",
                table: "parking_events",
                column: "exited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_events_institution_id_service_date",
                table: "parking_events",
                columns: new[] { "institution_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_events_person_id",
                table: "parking_events",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_parking_events_vehicle_id",
                table: "parking_events",
                column: "vehicle_id",
                unique: true,
                filter: "exited_at IS NULL AND auto_closed = false");

            // The catalog first: during a deployment the migrations run before the seeder.
            migrationBuilder.Sql("""
            INSERT INTO permissions (key, description) VALUES
                ('parking.scan', 'Register vehicle entries and exits at the gate by scanning student cards.'),
                ('parking.view', 'View the parking entries and exits.')
            ON CONFLICT (key) DO NOTHING;
            """);

            // Roles of institutions that already existed do not get new template permissions on their own.
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, k.key
            FROM roles r
            CROSS JOIN (VALUES ('parking.scan'), ('parking.view')) AS k(key)
            WHERE r.key IN ('institution_admin', 'gate_staff') AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parking_events");
        }
    }
}
