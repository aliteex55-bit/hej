using System.Text.Json;
using MySqlConnector;

namespace HolidayMaker;

/// <summary>
/// Hämtar anslutningssträngen och öppnar anslutningar.
/// Strängen ligger i config.json, inte hårdkodad i källkoden, så att
/// lösenordet kan bytas utan att bygga om programmet.
/// </summary>
public sealed class Db(string connectionString)
{
    private readonly string _connectionString = connectionString;

    public async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public static string LoadConnectionString()
    {
        // En miljövariabel vinner över filen - praktiskt vid demo.
        var fromEnv = Environment.GetEnvironmentVariable("HOLIDAYMAKER_CONNSTR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var path = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Hittar inte {path}. Kopiera config.json till programmappen.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var value = doc.RootElement.GetProperty("connectionString").GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("connectionString i config.json är tom.");

        return value;
    }
}
