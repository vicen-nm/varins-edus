using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VarinsEdu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scanned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_scans", x => x.id);
                    table.ForeignKey(
                        name: "FK_card_scans_institutions_institution_id",
                        column: x => x.institution_id,
                        principalTable: "institutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_card_scans_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_card_scans_users_scanned_by_user_id",
                        column: x => x.scanned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_scans_institution_id_scanned_at",
                table: "card_scans",
                columns: new[] { "institution_id", "scanned_at" });

            migrationBuilder.CreateIndex(
                name: "IX_card_scans_person_id_nonce",
                table: "card_scans",
                columns: new[] { "person_id", "nonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_card_scans_scanned_by_user_id",
                table: "card_scans",
                column: "scanned_by_user_id");

            // The catalog first: during a deployment the migrations run before the seeder.
            migrationBuilder.Sql("""
            INSERT INTO permissions (key, description) VALUES
                ('cards.scan', 'Scan student digital cards to identify students.')
            ON CONFLICT (key) DO NOTHING;
            """);

            // Roles of institutions that already existed do not get new template permissions on their own.
            migrationBuilder.Sql("""
            INSERT INTO role_permissions (role_id, permission_key)
            SELECT r.id, 'cards.scan'
            FROM roles r
            WHERE r.key = 'institution_admin' AND r.institution_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_scans");
        }
    }
}
