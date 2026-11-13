using System.Globalization;
using System.Text;
using HolidayMaker;
using MySqlConnector;

// Svenska tecken ut på skärmen.
Console.OutputEncoding = Encoding.UTF8;

// Svenska tecken in från tangentbordet. Med UTF-16 läser .NET tangentbordet
// direkt från konsolen i stället för via konsolens teckentabell. Då blir
// alla bokstäver rätt – även sådana som ø och č, som saknas i de gamla
// teckentabellerna. Görs bara när någon faktiskt sitter vid tangentbordet,
// inte när inmatningen kommer från en fil eller ett annat program.
if (!Console.IsInputRedirected)
{
    try
    {
        Console.InputEncoding = Encoding.Unicode;
    }
    catch (IOException)
    {
        // Går det inte att ställa om behålls konsolens egen inställning.
    }
}

// Svensk formatering av tal och belopp: 1 450,00.
var swedish = new CultureInfo("sv-SE");
CultureInfo.CurrentCulture = swedish;
CultureInfo.DefaultThreadCurrentCulture = swedish;

try
{
    var db = new Db(Db.LoadConnectionString());
    var repo = new Repository(db);

    // Ett tidigt anrop ger ett begripligt felmeddelande om databasen
    // inte går att nå, i stället för ett fel mitt i menyn.
    await repo.GetCitiesAsync();

    await new App(repo).RunAsync();
}
catch (MySqlException ex)
{
    Console.WriteLine();
    Console.WriteLine("Kunde inte ansluta till databasen.");
    Console.WriteLine($"  {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Kontrollera att MySQL är igång, att databasen holiday_maker finns");
    Console.WriteLine("(kör sql/01_create_database.sql och sql/02_testdata.sql) och att");
    Console.WriteLine("användare och lösenord i config.json stämmer.");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Programmet kunde inte starta: {ex.Message}");
    return 1;
}

return 0;
