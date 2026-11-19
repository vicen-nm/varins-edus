using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Visits;
using Xunit;

namespace VarinsEdu.Tests;

public class VisitServiceTests
{
    // 10:00 in an institution whose time zone is UTC.
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 18);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? IpAddress => "203.0.113.7";
    }

    private sealed record World(
        DbContextOptions<AppDbContext> Options,
        FakeClock Clock,
        Guid InstitutionId,
        string Slug,
        Guid StaffId);

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    // One institution (time zone UTC) and a staff member. active: false makes an inactive institution.
    private static async Task<World> SeedAsync(
        DbContextOptions<AppDbContext>? options = null,
        FakeClock? clock = null,
        string slug = "demo-a",
        Guid? institutionId = null,
        bool active = true)
    {
        options ??= NewOptions();
        clock ??= new FakeClock(Start);

        var inst = institutionId ?? Guid.NewGuid();
        var staff = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio " + slug, Slug = slug, TimeZone = "UTC", IsActive = active });
        db.Users.Add(new User
        {
            Id = staff,
            InstitutionId = inst,
            Username = $"staff@{slug}",
            PasswordHash = "x",
            CreatedAt = Start
        });

        await db.SaveChangesAsync();

        return new World(options, clock, inst, slug, staff);
    }

    // A fake but well-formed signature: the 8 bytes of a PNG and some more.
    private static string Png(int extra = 200) =>
        "data:image/png;base64," + Convert.ToBase64String(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[extra]).ToArray());

    private static VisitInput Valid(string id = "1-1111-1111", string name = "Carlos Visita") =>
        new(name, id, "8888-0001", "Dirección", "Reunión", Png());

    private static PublicVisitService PublicFor(World w) => new(w.Options, w.Clock);

    private static VisitService ServiceFor(World w, Guid? institutionId, Guid? userId)
    {
        var tenant = new FakeTenant(institutionId);

        return new VisitService(new AppDbContext(w.Options, tenant), tenant, new FakeUser(userId), w.Clock);
    }

    // A new service every time: each one has its own database context, like each request does.
    private static VisitService StaffOf(World w) => ServiceFor(w, w.InstitutionId, w.StaffId);

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    private static async Task<Guid> IdOfAsync(World w, string number)
    {
        using var db = Reader(w.Options);

        return await db.Visits.Where(v => v.IdentificationNumber == number).Select(v => v.Id).FirstAsync();
    }

    private static Visit NewVisit(World w, string id, string status, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        InstitutionId = w.InstitutionId,
        ServiceDate = date,
        FullName = "Visita " + id,
        IdentificationNumber = id,
        Host = "Dirección",
        Reason = "Reunión",
        SignaturePng = [1, 2, 3],
        Status = status,
        CreatedAt = Start
    };

    [Fact]
    public async Task A_public_submission_creates_a_pending_visit_with_clean_data()
    {
        var w = await SeedAsync();

        var result = await PublicFor(w).SubmitAsync(
            " Demo-A ",
            new VisitInput("  Carlos Visita ", "a-123 45", " 8888-0001 ", " Dirección ", " Reunión ", Png()));

        Assert.Null(result.Error);

        using var db = Reader(w.Options);
        var visit = await db.Visits.SingleAsync();

        Assert.Equal(w.InstitutionId, visit.InstitutionId);
        Assert.Equal(Today, visit.ServiceDate);
        Assert.Equal("Carlos Visita", visit.FullName);
        Assert.Equal("A12345", visit.IdentificationNumber);
        Assert.Equal("8888-0001", visit.Phone);
        Assert.Equal("Dirección", visit.Host);
        Assert.Equal("Reunión", visit.Reason);
        Assert.Equal(VisitStatuses.Pending, visit.Status);
        Assert.Equal(Start, visit.CreatedAt);
        Assert.Equal(208, visit.SignaturePng.Length);
        Assert.Null(visit.HandledByUserId);

        // A blank phone is stored as none.
        var blank = await PublicFor(w).SubmitAsync("demo-a", Valid("2-2222-2222") with { Phone = "  " });

        Assert.Null(blank.Error);
        Assert.Null((await db.Visits.SingleAsync(v => v.IdentificationNumber == "222222222")).Phone);
    }

    [Fact]
    public async Task Invalid_submissions_are_rejected_and_nothing_is_saved()
    {
        var w = await SeedAsync();
        var service = PublicFor(w);

        string Prefixed(byte[] bytes) => "data:image/png;base64," + Convert.ToBase64String(bytes);
        var magic = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        Assert.Equal("invalid_visit_data", (await service.SubmitAsync("demo-a", Valid(name: "ab"))).Error);
        Assert.Equal("invalid_visit_data", (await service.SubmitAsync("demo-a", Valid(name: new string('x', 121)))).Error);
        Assert.Equal("invalid_visit_data", (await service.SubmitAsync("demo-a", Valid() with { Host = "x" })).Error);
        Assert.Equal("invalid_visit_data", (await service.SubmitAsync("demo-a", Valid() with { Reason = " " })).Error);
        Assert.Equal("invalid_visit_data", (await service.SubmitAsync("demo-a", Valid() with { Phone = new string('1', 31) })).Error);

        Assert.Equal("invalid_visitor_id", (await service.SubmitAsync("demo-a", Valid(id: "123"))).Error);
        Assert.Equal("invalid_visitor_id", (await service.SubmitAsync("demo-a", Valid(id: "--"))).Error);
        Assert.Equal("invalid_visitor_id", (await service.SubmitAsync("demo-a", Valid(id: new string('1', 21)))).Error);

        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = null })).Error);
        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = "hola" })).Error);
        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = "data:image/jpeg;base64,AAAA" })).Error);
        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = "data:image/png;base64,@@@@" })).Error);
        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = Prefixed(new byte[200]) })).Error);
        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = Prefixed(magic.Concat(new byte[10]).ToArray()) })).Error);
        Assert.Equal("invalid_signature", (await service.SubmitAsync("demo-a", Valid() with { Signature = Prefixed(magic.Concat(new byte[70 * 1024]).ToArray()) })).Error);

        using var db = Reader(w.Options);
        Assert.Empty(await db.Visits.ToListAsync());
    }

    [Fact]
    public async Task Unknown_and_inactive_institutions_are_refused()
    {
        var w = await SeedAsync();
        await SeedAsync(w.Options, w.Clock, "demo-b", active: false);

        Assert.Equal("institution_not_found", (await PublicFor(w).SubmitAsync("otro-colegio", Valid())).Error);
        Assert.Equal("institution_not_found", (await PublicFor(w).SubmitAsync("demo-b", Valid())).Error);
        Assert.Equal("institution_not_found", (await PublicFor(w).SubmitAsync(null, Valid())).Error);

        using var db = Reader(w.Options);
        Assert.Empty(await db.Visits.ToListAsync());
    }

    [Fact]
    public async Task A_repeated_open_visit_creates_no_duplicate_but_a_finished_one_may_come_again()
    {
        var w = await SeedAsync();
        var service = PublicFor(w);

        Assert.Null((await service.SubmitAsync("demo-a", Valid("1-1111-1111"))).Error);

        // The same document while the visit is open: the answer is the same and nothing is created.
        Assert.Null((await service.SubmitAsync("demo-a", Valid("111111111", "Otro Nombre"))).Error);

        using (var db = Reader(w.Options))
        {
            Assert.Equal(1, await db.Visits.CountAsync());
        }

        Assert.Null((await service.SubmitAsync("demo-a", Valid("2-2222-2222"))).Error);

        using (var db = Reader(w.Options))
        {
            Assert.Equal(2, await db.Visits.CountAsync());
        }

        // After the visit is finished, the same person may come again.
        var first = await IdOfAsync(w, "111111111");
        Assert.Null((await StaffOf(w).AdmitAsync(first)).Error);
        Assert.Null((await StaffOf(w).ExitAsync(first)).Error);

        Assert.Null((await service.SubmitAsync("demo-a", Valid("1-1111-1111"))).Error);

        using var check = Reader(w.Options);
        Assert.Equal(3, await check.Visits.CountAsync());
    }

    [Fact]
    public async Task An_institution_takes_a_limited_number_of_requests_a_day()
    {
        var w = await SeedAsync();

        using (var db = Reader(w.Options))
        {
            for (var i = 0; i < PublicVisitService.MaxPerDay; i++)
            {
                db.Visits.Add(NewVisit(w, $"ID{i:0000}", VisitStatuses.Exited, Today));
            }

            // Yesterday's visits do not count.
            db.Visits.Add(NewVisit(w, "OLD0001", VisitStatuses.Exited, Today.AddDays(-1)));

            await db.SaveChangesAsync();
        }

        Assert.Equal("visit_limit_reached", (await PublicFor(w).SubmitAsync("demo-a", Valid("9-9999-9999"))).Error);

        using var check = Reader(w.Options);
        Assert.Equal(PublicVisitService.MaxPerDay + 1, await check.Visits.CountAsync());
    }

    [Fact]
    public async Task The_guard_admits_rejects_and_registers_exits_only_in_the_allowed_order()
    {
        var w = await SeedAsync();

        await PublicFor(w).SubmitAsync("demo-a", Valid("1-1111-1111", "Carlos Visita"));
        await PublicFor(w).SubmitAsync("demo-a", Valid("2-2222-2222", "Dora Visita"));

        var carlos = await IdOfAsync(w, "111111111");
        var dora = await IdOfAsync(w, "222222222");

        Assert.Null((await StaffOf(w).AdmitAsync(carlos)).Error);

        using (var db = Reader(w.Options))
        {
            var visit = await db.Visits.SingleAsync(v => v.Id == carlos);

            Assert.Equal(VisitStatuses.Inside, visit.Status);
            Assert.Equal(Start, visit.EnteredAt);
            Assert.Equal(w.StaffId, visit.HandledByUserId);
        }

        // Out of order or repeated.
        Assert.Equal("invalid_visit_status", (await StaffOf(w).AdmitAsync(carlos)).Error);
        Assert.Equal("invalid_visit_status", (await StaffOf(w).RejectAsync(carlos)).Error);
        Assert.Equal("invalid_visit_status", (await StaffOf(w).ExitAsync(dora)).Error);

        Assert.Null((await StaffOf(w).RejectAsync(dora)).Error);
        Assert.Equal("invalid_visit_status", (await StaffOf(w).AdmitAsync(dora)).Error);

        w.Clock.Now = Start.AddHours(2);

        Assert.Null((await StaffOf(w).ExitAsync(carlos)).Error);
        Assert.Equal("invalid_visit_status", (await StaffOf(w).ExitAsync(carlos)).Error);
        Assert.Equal("visit_not_found", (await StaffOf(w).AdmitAsync(Guid.NewGuid())).Error);

        using var check = Reader(w.Options);
        var exited = await check.Visits.SingleAsync(v => v.Id == carlos);

        Assert.Equal(VisitStatuses.Exited, exited.Status);
        Assert.Equal(Start.AddHours(2), exited.ExitedAt);
        Assert.Equal(w.StaffId, exited.ExitedByUserId);
        Assert.Equal(VisitStatuses.Rejected, (await check.Visits.SingleAsync(v => v.Id == dora)).Status);
    }

    [Fact]
    public async Task Visits_of_a_previous_day_are_expired_or_without_exit_and_cannot_move()
    {
        var w = await SeedAsync();
        var yesterday = Today.AddDays(-1);

        var pending = NewVisit(w, "PEND0001", VisitStatuses.Pending, yesterday);
        var inside = NewVisit(w, "INSD0001", VisitStatuses.Inside, yesterday);

        using (var db = Reader(w.Options))
        {
            db.Visits.AddRange(pending, inside);
            await db.SaveChangesAsync();
        }

        Assert.Equal("invalid_visit_status", (await StaffOf(w).AdmitAsync(pending.Id)).Error);
        Assert.Equal("invalid_visit_status", (await StaffOf(w).ExitAsync(inside.Id)).Error);

        var all = await StaffOf(w).ListAsync(yesterday, null, null, 0, 50);

        Assert.Equal(2, all.Total);
        Assert.Equal(new[] { "expired", "no_exit" }, all.Items.Select(i => i.Status).Order().ToArray());

        Assert.Equal(pending.Id, Assert.Single((await StaffOf(w).ListAsync(yesterday, "expired", null, 0, 50)).Items).Id);
        Assert.Equal(inside.Id, Assert.Single((await StaffOf(w).ListAsync(yesterday, "no_exit", null, 0, 50)).Items).Id);
        Assert.Empty((await StaffOf(w).ListAsync(yesterday, "pending", null, 0, 50)).Items);

        // Today has nothing, and the counters are about today.
        var today = await StaffOf(w).ListAsync(null, null, null, 0, 50);

        Assert.Equal(0, today.Total);
        Assert.Equal(0, today.Inside);
        Assert.Equal(0, today.Pending);
    }

    [Fact]
    public async Task The_list_filters_counts_today_and_shows_only_this_institution()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, w.Clock, "demo-b");
        var service = PublicFor(w);

        await service.SubmitAsync("demo-a", Valid("1-1111-1111", "Carlos Visita"));
        await service.SubmitAsync("demo-a", Valid("2-2222-2222", "Dora Visita"));
        await service.SubmitAsync("demo-a", Valid("3-3333-3333", "Eva Visita"));
        await service.SubmitAsync("demo-a", Valid("4-4444-4444", "Fito Visita"));
        await service.SubmitAsync("demo-b", Valid("1-1111-1111", "Gina Visita"));

        await StaffOf(w).AdmitAsync(await IdOfAsync(w, "222222222"));
        var eva = await IdOfAsync(w, "333333333");
        await StaffOf(w).AdmitAsync(eva);
        await StaffOf(w).ExitAsync(eva);
        await StaffOf(w).RejectAsync(await IdOfAsync(w, "444444444"));

        var staff = StaffOf(w);
        var all = await staff.ListAsync(null, null, null, 0, 50);

        Assert.Equal(4, all.Total);
        Assert.Equal(1, all.Inside);
        Assert.Equal(1, all.Pending);

        Assert.Equal("Carlos Visita", Assert.Single((await staff.ListAsync(null, "pending", null, 0, 50)).Items).FullName);

        var inside = Assert.Single((await StaffOf(w).ListAsync(null, "inside", null, 0, 50)).Items);

        Assert.Equal("Dora Visita", inside.FullName);
        Assert.Equal("222222222", inside.IdentificationNumber);
        Assert.Equal("staff@demo-a", inside.HandledBy);
        Assert.Equal(Start, inside.EnteredAt);

        var exited = Assert.Single((await StaffOf(w).ListAsync(null, "exited", null, 0, 50)).Items);

        Assert.Equal("Eva Visita", exited.FullName);
        Assert.Equal("staff@demo-a", exited.ExitedBy);

        Assert.Equal("Fito Visita", Assert.Single((await StaffOf(w).ListAsync(null, "rejected", null, 0, 50)).Items).FullName);
        Assert.Equal("Dora Visita", Assert.Single((await StaffOf(w).ListAsync(null, null, "dora", 0, 50)).Items).FullName);
        Assert.Equal("Carlos Visita", Assert.Single((await StaffOf(w).ListAsync(null, null, "1-1111", 0, 50)).Items).FullName);
        Assert.Equal(0, (await StaffOf(w).ListAsync(Today.AddDays(-1), null, null, 0, 50)).Total);

        var others = await ServiceFor(other, other.InstitutionId, other.StaffId).ListAsync(null, null, null, 0, 50);

        Assert.Equal("Gina Visita", Assert.Single(others.Items).FullName);
    }

    [Fact]
    public async Task The_signature_is_given_apart_and_only_to_its_own_institution()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, w.Clock, "demo-b");

        await PublicFor(w).SubmitAsync("demo-a", Valid());

        var id = await IdOfAsync(w, "111111111");
        var signature = await StaffOf(w).SignatureAsync(id);

        Assert.Null(signature.Error);
        Assert.Equal(208, signature.Value!.Length);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, signature.Value.Take(4).ToArray());

        Assert.Equal("visit_not_found", (await StaffOf(w).SignatureAsync(Guid.NewGuid())).Error);
        Assert.Equal("visit_not_found", (await ServiceFor(other, other.InstitutionId, other.StaffId).SignatureAsync(id)).Error);
    }

    [Fact]
    public async Task Platform_users_cannot_manage_visits()
    {
        var w = await SeedAsync();
        var service = ServiceFor(w, null, w.StaffId);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListAsync(null, null, null, 0, 50));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.AdmitAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.RejectAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.ExitAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.SignatureAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Visit_changes_are_audited_without_the_personal_data_or_the_signature()
    {
        var institutionId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new FakeTenant(institutionId),
                new FakeUser(null),
                TimeProvider.System,
                new AuditSuppression()))
            .Options;

        var w = await SeedAsync(options, null, "demo-a", institutionId);

        var signature = Png();
        var input = new VisitInput("Carlos Visita Secreto", "A1234567", "8888-0001", "Doña Marta", "Entrevista confidencial", signature);

        Assert.Null((await PublicFor(w).SubmitAsync("demo-a", input)).Error);
        Assert.Null((await StaffOf(w).AdmitAsync(await IdOfAsync(w, "A1234567"))).Error);

        using var db = Reader(options);
        var logs = await db.AuditLogs.Where(a => a.EntityType == "Visit").ToListAsync();

        Assert.Equal(2, logs.Count);

        foreach (var secret in new[] { "Secreto", "A1234567", "8888-0001", "Marta", "confidencial", signature["data:image/png;base64,".Length..] })
        {
            Assert.All(logs, a => Assert.DoesNotContain(secret, a.Metadata));
        }

        Assert.Contains(logs, a => a.Action == "Visit.updated" && a.Metadata!.Contains("inside"));
        Assert.Contains(logs, a => a.Metadata!.Contains("[redacted]"));
    }
}
