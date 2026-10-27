using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessPointsServicesAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    radius_meters = table.Column<int>(type: "integer", nullable: false),
                    max_accuracy_meters = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_access_points_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_services", x => x.id);
                    table.ForeignKey(
                        name: "FK_access_services_access_points_access_point_id",
                        column: x => x.access_point_id,
                        principalTable: "access_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_services_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    distance_meters = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_access_events_access_points_access_point_id",
                        column: x => x.access_point_id,
                        principalTable: "access_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_events_access_services_access_service_id",
                        column: x => x.access_service_id,
                        principalTable: "access_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_events_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_events_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_access_events_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_events_access_point_id",
                table: "access_events",
                column: "access_point_id");

            migrationBuilder.CreateIndex(
                name: "IX_access_events_access_service_id_person_id_service_date",
                table: "access_events",
                columns: new[] { "access_service_id", "person_id", "service_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_events_institution_id_service_date",
                table: "access_events",
                columns: new[] { "institution_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "IX_access_events_person_id",
                table: "access_events",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_access_events_recorded_by_user_id",
                table: "access_events",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_access_points_institution_id_name",
                table: "access_points",
                columns: new[] { "institution_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_services_access_point_id_name",
                table: "access_services",
                columns: new[] { "access_point_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_services_institution_id",
                table: "access_services",
                column: "institution_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_events");

            migrationBuilder.DropTable(
                name: "access_services");

            migrationBuilder.DropTable(
                name: "access_points");
        }
    }
}
