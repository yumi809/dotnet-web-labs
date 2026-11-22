using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Orders.Dal;

/// <summary>
/// Розгортає OrdersDb скриптами з db/p1 (schema → procedures → seed). Скрипти ідемпотентні,
/// тому ініціалізацію можна безпечно запускати при кожному старті у Development.
/// </summary>
public sealed partial class DbInitializer(IConfiguration configuration, ILogger<DbInitializer> logger)
{
    private static readonly string[] Scripts = ["schema.sql", "procedures.sql", "seed.sql"];

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString(UnitOfWork.UnitOfWork.ConnectionStringName));
        builder.InitialCatalog = "master"; // schema.sql сам створює OrdersDb, якщо її немає

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);

        foreach (var script in Scripts)
        {
            var sql = ReadScript(script);
            foreach (var batch in GoSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync(ct);
            }
            logger.LogInformation("Executed database script {Script}", script);
        }
    }

    private static string ReadScript(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
