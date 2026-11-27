using VarinsEdu.Infrastructure.Seeding;

namespace VarinsEdu.Api.Seeding;

// Development only (see Program.cs): creates the fictional school once, when "Demo:Enabled" is on.
// The accounts and the activation codes are written to the log, because the codes are shown only this once.
public class DemoDataHostedService(IServiceScopeFactory scopes, ILogger<DemoDataHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();

            var report = await scope.ServiceProvider
                .GetRequiredService<DemoDataSeeder>()
                .RunAsync(cancellationToken);

            if (report is null)
            {
                logger.LogInformation(
                    "Demo data: the institution '{Slug}' already exists, nothing was created.",
                    DemoDataSeeder.Slug);

                return;
            }

            logger.LogWarning(
                "DATOS DE DEMOSTRACIÓN creados (solo para desarrollo). Colegio: {Slug}. Contraseña de todas las cuentas: {Password}",
                report.Slug,
                report.Password);

            foreach (var account in report.Accounts)
            {
                logger.LogInformation("Cuenta {Username}: {Role}", account.Username, account.Role);
            }

            foreach (var activation in report.Activations)
            {
                logger.LogInformation(
                    "Sin cuenta: {Student}, cédula {Identification}, código de activación {Code}",
                    activation.Student,
                    activation.Identification,
                    activation.Code);
            }
        }
        catch (Exception ex)
        {
            // The demo is a convenience: it must never stop the API from starting.
            logger.LogError(ex, "The demo data could not be created.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
