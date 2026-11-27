using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Parking;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Preferences;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Seeding;

public sealed record DemoAccount(string Username, string Role);

public sealed record DemoActivation(string Student, string Identification, string Code);

public sealed record DemoReport(
    string Slug,
    string Password,
    IReadOnlyList<DemoAccount> Accounts,
    IReadOnlyList<DemoActivation> Activations);

// A fictional school to record the demo video. It is for development only: every name, document number
// and password is invented. Safe to run more than once: if the school exists, nothing is created.
public class DemoDataSeeder(
    DbContextOptions<AppDbContext> options,
    DatabaseSeeder baseSeeder,
    IPasswordHasher passwordHasher,
    ActivationCodeHasher codeHasher,
    AuditSuppression suppression,
    TimeProvider time)
{
    public const string Slug = "liceo-demo";
    public const string Password = "Demo-Liceo-2026";

    private const string ZoneId = "America/Costa_Rica";
    private const string SourceIp = "192.0.2.10";

    private sealed record DemoStudent(Guid PersonId, Guid? UserId, string First, string Last, string Cedula);

    public async Task<DemoReport?> RunAsync(CancellationToken ct = default)
    {
        // Permissions and the platform role first: the roles of the demo institution need them.
        await baseSeeder.SeedAsync(ct);

        await using var db = new AppDbContext(options, new PlatformTenant());

        if (await db.Institutions.AnyAsync(i => i.Slug == Slug, ct))
        {
            return null;
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        var nowUtc = time.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        var today = DateOnly.FromDateTime(localNow);
        var startToday = LocalTime.StartOfDay(zone, today);
        var minutesNow = localNow.Hour * 60 + localNow.Minute;

        DateTimeOffset At(DateOnly day, int hour, int minute) =>
            LocalTime.StartOfDay(zone, day).AddHours(hour).AddMinutes(minute);

        // Something that happened a while ago today never lands on yesterday.
        DateTimeOffset Ago(TimeSpan span)
        {
            var moment = nowUtc - span;

            return moment < startToday ? startToday.AddMinutes(1) : moment;
        }

        TimeOnly Clock(int minutes) =>
            TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 23 * 60 + 59)));

        // The five school days before today.
        var schoolDays = Enumerable.Range(1, 12)
            .Select(back => today.AddDays(-back))
            .Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Take(5)
            .ToList();
        var lastSchoolDay = schoolDays[0];

        var accounts = new List<DemoAccount>();
        var activations = new List<DemoActivation>();

        // The audit log is not filled with one entry per demo row: the few that matter are written by hand below.
        using (suppression.Begin())
        {
            var institution = new Institution
            {
                Id = Guid.NewGuid(),
                Name = "Liceo Demo",
                Slug = Slug,
                TimeZone = ZoneId,
                CreatedAt = nowUtc
            };

            db.Institutions.Add(institution);
            await db.SaveChangesAsync(ct);

            await InstitutionDefaults.EnsureAsync(db, institution.Id, ct);

            var inst = institution.Id;

            var settings = await db.InstitutionSettings.SingleAsync(s => s.InstitutionId == inst, ct);
            settings.DisplayName = "Liceo Demo";
            settings.PrimaryColor = "#1b6ca8";

            foreach (var module in await db.InstitutionModules.Where(m => m.InstitutionId == inst).ToListAsync(ct))
            {
                module.IsEnabled = true;
            }

            var roles = await db.Roles
                .Where(r => r.InstitutionId == inst)
                .ToDictionaryAsync(r => r.Key, r => r.Id, ct);

            // One hash for every demo account: they all share the same password.
            var passwordHash = passwordHasher.Hash(Password);

            User NewUser(string username, Guid? personId, string roleKey, string label)
            {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    InstitutionId = inst,
                    PersonId = personId,
                    Username = username,
                    PasswordHash = passwordHash,
                    IsActive = true,
                    CreatedAt = nowUtc.AddDays(-14)
                };
                user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roles[roleKey] });

                db.Users.Add(user);
                accounts.Add(new DemoAccount(username, label));

                return user;
            }

            // ---- Staff ----

            var director = NewUser(StaffUsernames.Build(Slug, "director"), null, RoleKeys.InstitutionAdmin, "Administrador");
            var teacher = NewUser(StaffUsernames.Build(Slug, "profe.rojas"), null, RoleKeys.Teacher, "Docente");
            var cafeteriaStaff = NewUser(StaffUsernames.Build(Slug, "comedor.1"), null, RoleKeys.CafeteriaStaff, "Personal de comedor");
            var gate = NewUser(StaffUsernames.Build(Slug, "porton.1"), null, RoleKeys.GateStaff, "Personal de portería");
            var vehicleStaff = NewUser(StaffUsernames.Build(Slug, "vehiculos.1"), null, RoleKeys.VehicleStaff, "Personal de vehículos");

            // ---- Students ----

            var groups = new[] { "10-1", "10-2", "11-1" }.ToDictionary(
                name => name,
                name => new Group { Id = Guid.NewGuid(), InstitutionId = inst, Name = name });
            db.Groups.AddRange(groups.Values);

            var roster = new (string First, string Last, string Group, bool Account)[]
            {
                ("Sofía", "Mora Vargas", "10-1", true),
                ("Mateo", "Rojas Solano", "10-1", true),
                ("Valeria", "Campos Núñez", "10-1", true),
                ("Daniel", "Chaves Ugalde", "10-2", true),
                ("Camila", "Jiménez Arce", "10-2", true),
                ("Esteban", "Soto Quirós", "10-2", true),
                ("Lucía", "Herrera Calvo", "11-1", true),
                ("Andrés", "Salas Rivera", "11-1", true),
                ("Isabella", "Vega Montero", "11-1", false),
                ("Gabriel", "Araya Peña", "10-1", false),
                ("Mariana", "Cordero Zúñiga", "10-2", false),
                ("Julián", "Barrantes Mena", "11-1", false)
            };

            var students = new List<DemoStudent>();

            for (var i = 0; i < roster.Length; i++)
            {
                var (first, last, group, hasAccount) = roster[i];
                var cedula = $"9000000{i + 1:00}";

                var person = new Person
                {
                    Id = Guid.NewGuid(),
                    InstitutionId = inst,
                    IdentificationNumber = cedula,
                    FirstName = first,
                    LastName = last,
                    IsActive = true,
                    CreatedAt = nowUtc.AddDays(-30)
                };

                db.People.Add(person);
                db.StudentProfiles.Add(new StudentProfile
                {
                    PersonId = person.Id,
                    InstitutionId = inst,
                    GroupId = groups[group].Id,
                    Status = StudentStatuses.Active
                });

                Guid? userId = null;

                if (hasAccount)
                {
                    userId = NewUser(
                        StudentUsernames.Build(Slug, cedula),
                        person.Id,
                        RoleKeys.Student,
                        $"Estudiante ({first} {last}, {group})").Id;
                }
                else
                {
                    var code = ActivationCodeGenerator.Generate();

                    db.ActivationCodes.Add(new ActivationCode
                    {
                        Id = Guid.NewGuid(),
                        InstitutionId = inst,
                        PersonId = person.Id,
                        CodeHash = codeHasher.Hash(code),
                        ExpiresAt = nowUtc.AddDays(30),
                        CreatedAt = nowUtc
                    });

                    activations.Add(new DemoActivation($"{first} {last}", cedula, code));
                }

                students.Add(new DemoStudent(person.Id, userId, first, last, cedula));
            }

            var sofia = students[0];
            var mateo = students[1];
            var valeria = students[2];
            var daniel = students[3];
            var camila = students[4];
            var esteban = students[5];
            var lucia = students[6];
            var andres = students[7];

            // Valeria sees the screen in her own way. Only display settings: never why.
            db.UserPreferences.Add(new UserPreference
            {
                UserId = valeria.UserId!.Value,
                UiMode = DisplayDefaults.Simple,
                FontScale = 1.5,
                ReadAloud = true,
                HighContrast = false,
                ReducedMotion = false
            });

            // ---- Cafeteria: no location yet, so it is set live ----

            var point = new AccessPoint
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                Name = "Comedor",
                Kind = AccessPointKinds.Cafeteria,
                RadiusMeters = 150,
                MaxAccuracyMeters = 100,
                IsActive = true,
                CreatedAt = nowUtc.AddDays(-14)
            };
            db.AccessPoints.Add(point);

            AccessService Service(string name, int fromHour, int toHour) => new()
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                AccessPointId = point.Id,
                Name = name,
                StartTime = new TimeOnly(fromHour, 0),
                EndTime = new TimeOnly(toHour, 0),
                IsActive = true
            };

            var breakfast = Service("Desayuno", 5, 10);
            var lunch = Service("Almuerzo", 10, 14);
            var snack = Service("Merienda", 14, 18);
            db.AccessServices.AddRange(breakfast, lunch, snack);

            // The same history every time. Nothing today: the demo adds the first registrations live.
            var random = new Random(2026);
            var diners = students.Where(s => s.UserId is not null).ToList();
            var plan = new[] { (breakfast, 7, 9, 4), (lunch, 11, 14, 7), (snack, 14, 16, 3) };

            foreach (var day in schoolDays)
            {
                foreach (var (service, fromHour, toHour, take) in plan)
                {
                    foreach (var diner in diners.OrderBy(_ => random.Next()).Take(take))
                    {
                        var byQr = random.Next(100) < 70;

                        db.AccessEvents.Add(new AccessEvent
                        {
                            Id = Guid.NewGuid(),
                            InstitutionId = inst,
                            AccessPointId = point.Id,
                            AccessServiceId = service.Id,
                            PersonId = diner.PersonId,
                            ServiceDate = day,
                            OccurredAt = At(day, fromHour, 0).AddMinutes(random.Next(0, (toHour - fromHour) * 60)),
                            Method = byQr ? AccessMethods.Qr : AccessMethods.Manual,
                            RecordedByUserId = byQr ? null : cafeteriaStaff.Id,
                            DistanceMeters = byQr ? random.Next(5, 90) : null
                        });
                    }
                }
            }

            // ---- Vehicles ----

            Vehicle Moto(DemoStudent owner, string plate, string make, string model, string color, string status, string? note, int daysAgo, bool reviewed) => new()
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                PersonId = owner.PersonId,
                Plate = plate,
                Make = make,
                Model = model,
                Color = color,
                Status = status,
                StatusNote = note,
                CreatedByUserId = owner.UserId!.Value,
                CreatedAt = nowUtc.AddDays(-daysAgo),
                ReviewedByUserId = reviewed ? vehicleStaff.Id : null,
                ReviewedAt = reviewed ? nowUtc.AddDays(-daysAgo).AddHours(5) : null
            };

            var dem101 = Moto(mateo, "DEM101", "Honda", "XR 150", "Rojo", VehicleStatuses.Approved, null, 10, true);
            var dem102 = Moto(daniel, "DEM102", "Yamaha", "FZ 150", "Negro", VehicleStatuses.Approved, null, 9, true);
            var dem103 = Moto(andres, "DEM103", "Suzuki", "GN 125", "Azul", VehicleStatuses.Approved, null, 8, true);
            var dem104 = Moto(andres, "DEM104", "Bajaj", "Pulsar 180", "Gris", VehicleStatuses.Suspended, "Documentos vencidos", 7, true);
            var dem105 = Moto(esteban, "DEM105", "Honda", "CB 125", "Blanco", VehicleStatuses.Pending, null, 1, false);
            var dem106 = Moto(lucia, "DEM106", "Yamaha", "YBR 125", "Verde", VehicleStatuses.Pending, null, 1, false);
            var dem107 = Moto(valeria, "DEM107", "Suzuki", "AX 100", "Rojo", VehicleStatuses.Rejected, "Falta la póliza", 6, true);
            db.Vehicles.AddRange(dem101, dem102, dem103, dem104, dem105, dem106, dem107);

            // ---- Parking: Mateo is inside now, Daniel already left, Andrés never closed yesterday's visit ----

            ParkingEvent Park(Vehicle vehicle, DemoStudent owner, DateOnly day, DateTimeOffset entered, DateTimeOffset? exited, bool autoClosed = false) => new()
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                VehicleId = vehicle.Id,
                PersonId = owner.PersonId,
                ServiceDate = day,
                EnteredAt = entered,
                EnteredByUserId = gate.Id,
                ExitedAt = exited,
                ExitedByUserId = exited is null ? null : gate.Id,
                AutoClosed = autoClosed
            };

            db.ParkingEvents.AddRange(
                Park(dem101, mateo, today, Ago(TimeSpan.FromHours(1)), null),
                Park(dem102, daniel, today, Ago(TimeSpan.FromHours(4)), Ago(TimeSpan.FromHours(2))),
                Park(dem103, andres, lastSchoolDay, At(lastSchoolDay, 7, 5), null, autoClosed: true));

            foreach (var day in schoolDays)
            {
                foreach (var (vehicle, owner) in new[] { (dem101, mateo), (dem102, daniel), (dem103, andres) })
                {
                    // That visit is the one left open.
                    if (day == lastSchoolDay && vehicle == dem103)
                    {
                        continue;
                    }

                    db.ParkingEvents.Add(Park(
                        vehicle,
                        owner,
                        day,
                        At(day, 6, 50 + random.Next(0, 25)),
                        At(day, 15, 15 + random.Next(0, 40))));
                }
            }

            // ---- Visits, with a drawn signature ----

            Visit Guest(string name, string document, string host, string reason, string status, DateOnly day, DateTimeOffset created, DateTimeOffset? entered, DateTimeOffset? exited, int seed) => new()
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                ServiceDate = day,
                FullName = name,
                IdentificationNumber = document,
                Phone = $"8888-{seed:0000}",
                Host = host,
                Reason = reason,
                SignaturePng = DemoSignature.Create(seed),
                Status = status,
                CreatedAt = created,
                HandledByUserId = status == VisitStatuses.Pending ? null : gate.Id,
                EnteredAt = entered,
                ExitedAt = exited,
                ExitedByUserId = exited is null ? null : gate.Id
            };

            var secondDay = schoolDays[1];

            db.Visits.AddRange(
                Guest("Roberto Vindas Castro", "V1002001", "Dirección", "Entrega de documentos", VisitStatuses.Pending, today, Ago(TimeSpan.FromMinutes(10)), null, null, 1),
                Guest("Ana Lorena Pérez Mora", "V1002002", "Orientación", "Reunión con la orientadora", VisitStatuses.Inside, today, Ago(TimeSpan.FromMinutes(50)), Ago(TimeSpan.FromMinutes(40)), null, 2),
                Guest("Carlos Mena Brenes", "V1002003", "Dirección", "Reunión", VisitStatuses.Exited, today, Ago(TimeSpan.FromHours(3)), Ago(TimeSpan.FromMinutes(170)), Ago(TimeSpan.FromHours(2)), 3),
                Guest("Marta Quesada León", "V1002004", "Comedor", "Entrega de víveres", VisitStatuses.Exited, lastSchoolDay, At(lastSchoolDay, 9, 0), At(lastSchoolDay, 9, 10), At(lastSchoolDay, 9, 45), 4),
                Guest("Pablo Zamora Ruiz", "V1002005", "Contabilidad", "Pago de matrícula", VisitStatuses.Exited, lastSchoolDay, At(lastSchoolDay, 10, 30), At(lastSchoolDay, 10, 35), At(lastSchoolDay, 11, 0), 5),
                Guest("Elena Rojas Brenes", "V1002006", "Dirección", "Reunión de padres", VisitStatuses.Exited, secondDay, At(secondDay, 14, 0), At(secondDay, 14, 10), At(secondDay, 15, 0), 6));

            // ---- Exit permits: Sofía has one valid now, Camila's was used, Valeria's is for tomorrow ----

            ExitPermit Permit(DemoStudent owner, DateOnly day, int fromMinutes, int toMinutes, string status, string reason, DateTimeOffset created, DateTimeOffset? used = null, DateTimeOffset? cancelled = null) => new()
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                PersonId = owner.PersonId,
                ServiceDate = day,
                FromTime = Clock(fromMinutes),
                ToTime = Clock(toMinutes),
                Reason = reason,
                Status = status,
                CreatedByUserId = teacher.Id,
                CreatedAt = created,
                UsedAt = used,
                UsedByUserId = used is null ? null : gate.Id,
                CancelledAt = cancelled,
                CancelledByUserId = cancelled is null ? null : teacher.Id
            };

            db.ExitPermits.AddRange(
                Permit(sofia, today, minutesNow - 30, minutesNow + 240, ExitPermitStatuses.Active, "Cita médica", Ago(TimeSpan.FromHours(2))),
                Permit(camila, today, minutesNow - 180, minutesNow - 60, ExitPermitStatuses.Used, "Trámite familiar", Ago(TimeSpan.FromHours(4)), used: Ago(TimeSpan.FromHours(2))),
                Permit(valeria, today.AddDays(1), 8 * 60, 10 * 60, ExitPermitStatuses.Active, "Cita médica", Ago(TimeSpan.FromHours(1))),
                Permit(mateo, lastSchoolDay, 10 * 60, 12 * 60, ExitPermitStatuses.Cancelled, "Trámite familiar", At(lastSchoolDay, 8, 0), cancelled: At(lastSchoolDay, 9, 0)));

            // ---- A few audit entries, written by hand, so the summary and the log have something to show ----

            AuditLog Log(string action, string entityType, string? entityId, Guid? userId, DateTimeOffset at, object? metadata = null) => new()
            {
                InstitutionId = inst,
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OccurredAt = at,
                IpAddress = SourceIp,
                Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata)
            };

            object LoginOk(User user) => new { username = user.Username, reason = (string?)null };
            object LoginFailed() => new { username = teacher.Username, reason = "wrong_password" };
            object StatusChange(string from, string to) => new { changes = new { Status = new { @from = from, to } } };

            db.AuditLogs.AddRange(
                Log("auth.login.succeeded", "User", director.Id.ToString(), director.Id, Ago(TimeSpan.FromHours(5)), LoginOk(director)),
                Log("auth.login.succeeded", "User", gate.Id.ToString(), gate.Id, Ago(TimeSpan.FromHours(4)), LoginOk(gate)),
                Log("auth.login.succeeded", "User", vehicleStaff.Id.ToString(), vehicleStaff.Id, Ago(TimeSpan.FromHours(3)), LoginOk(vehicleStaff)),
                Log("auth.login.failed", "User", teacher.Id.ToString(), teacher.Id, Ago(TimeSpan.FromMinutes(95)), LoginFailed()),
                Log("auth.login.failed", "User", teacher.Id.ToString(), teacher.Id, Ago(TimeSpan.FromMinutes(90)), LoginFailed()),
                Log("auth.login.failed", "User", teacher.Id.ToString(), teacher.Id, Ago(TimeSpan.FromMinutes(85)), LoginFailed()),
                Log("auth.login.failed", "User", teacher.Id.ToString(), teacher.Id, Ago(TimeSpan.FromMinutes(82)), LoginFailed()),
                Log("auth.account.locked", "User", teacher.Id.ToString(), teacher.Id, Ago(TimeSpan.FromMinutes(80)), new { username = teacher.Username, reason = "too_many_failures" }),
                Log("card.scan.reused", "Person", camila.PersonId.ToString(), gate.Id, Ago(TimeSpan.FromMinutes(20))),
                Log(
                    "AccessReport.exported",
                    "AccessReport",
                    null,
                    director.Id,
                    At(lastSchoolDay, 16, 30),
                    new { @from = schoolDays[^1].ToString("yyyy-MM-dd"), to = lastSchoolDay.ToString("yyyy-MM-dd"), rows = 63 }),
                Log("Vehicle.updated", "Vehicle", dem101.Id.ToString(), vehicleStaff.Id, dem101.ReviewedAt!.Value, StatusChange("pending", "approved")),
                Log("Vehicle.updated", "Vehicle", dem102.Id.ToString(), vehicleStaff.Id, dem102.ReviewedAt!.Value, StatusChange("pending", "approved")),
                Log("Vehicle.updated", "Vehicle", dem103.Id.ToString(), vehicleStaff.Id, dem103.ReviewedAt!.Value, StatusChange("pending", "approved")),
                Log("Vehicle.updated", "Vehicle", dem107.Id.ToString(), vehicleStaff.Id, dem107.ReviewedAt!.Value, StatusChange("pending", "rejected")));

            await db.SaveChangesAsync(ct);
        }

        return new DemoReport(Slug, Password, accounts, activations);
    }
}
