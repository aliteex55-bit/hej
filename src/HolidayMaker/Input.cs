using System.Globalization;

namespace HolidayMaker;

/// <summary>
/// Inläsning från tangentbordet som inte kraschar på felaktig inmatning.
///
/// Varje metod frågar om igen tills svaret går att tolka. Skriver
/// operatören "avbryt" (eller trycker Ctrl+Z) returneras null, och den
/// som anropade backar då tillbaka till menyn.
/// </summary>
public static class Input
{
    private const string CancelWord = "avbryt";

    /// <summary>
    /// Läser en rad och tar bort blanktecken i början och slutet.
    /// Null betyder att operatören vill avbryta.
    /// </summary>
    private static string? ReadLineOrCancel(string prompt)
    {
        Console.Write(prompt);
        var line = Console.ReadLine();

        if (line is null) return null;                  // Ctrl+Z, eller inmatningen tog slut
        line = line.Trim();
        return line.Equals(CancelWord, StringComparison.OrdinalIgnoreCase) ? null : line;
    }

    /// <summary>Text som inte får vara tom och inte längre än kolumnen i databasen.</summary>
    public static string? ReadText(string prompt, int maxLength)
    {
        while (true)
        {
            var line = ReadLineOrCancel(prompt);
            if (line is null) return null;

            if (line.Length == 0)
            {
                Console.WriteLine("  Fältet får inte vara tomt.");
                continue;
            }
            if (line.Length > maxLength)
            {
                Console.WriteLine($"  Högst {maxLength} tecken (du skrev {line.Length}).");
                continue;
            }
            return line;
        }
    }

    /// <summary>Ett heltal mellan min och max. Bara siffror godtas.</summary>
    public static int? ReadInt(string prompt, int min, int max)
    {
        while (true)
        {
            var line = ReadLineOrCancel(prompt);
            if (line is null) return null;

            if (TryParseDigits(line, out var value) && value >= min && value <= max)
                return value;

            Console.WriteLine($"  Skriv ett heltal mellan {min} och {max}.");
        }
    }

    /// <summary>
    /// Ett valfritt heltal. Tom rad ger value = null, och vad det betyder
    /// för operatören beskrivs av emptyMeans ("för att hoppa över").
    /// Returnerar false bara om operatören avbryter.
    /// </summary>
    public static bool TryReadOptionalInt(string prompt, int min, int max, out int? value,
        string emptyMeans = "för att hoppa över")
    {
        value = null;
        while (true)
        {
            var line = ReadLineOrCancel(prompt);
            if (line is null) return false;
            if (line.Length == 0) return true;

            if (TryParseDigits(line, out var number) && number >= min && number <= max)
            {
                value = number;
                return true;
            }

            Console.WriteLine($"  Skriv ett heltal mellan {min} och {max}, eller tryck bara Enter {emptyMeans}.");
        }
    }

    /// <summary>
    /// Låter operatören välja ett av numren i en lista som just har visats.
    /// Tom rad eller "avbryt" ger null.
    /// </summary>
    public static int? ReadChoice(string prompt, IReadOnlyCollection<int> allowed, string errorMessage)
    {
        while (true)
        {
            var line = ReadLineOrCancel(prompt);
            if (string.IsNullOrEmpty(line)) return null;

            if (TryParseDigits(line, out var value) && allowed.Contains(value))
                return value;

            Console.WriteLine(errorMessage);
        }
    }

    /// <summary>Ett datum på formen ÅÅÅÅ-MM-DD. Andra skrivsätt godtas inte, så att inget kan misstolkas.</summary>
    public static DateOnly? ReadDate(string prompt)
    {
        while (true)
        {
            var line = ReadLineOrCancel(prompt);
            if (line is null) return null;

            if (DateOnly.TryParseExact(line, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;

            Console.WriteLine("  Skriv datumet som ÅÅÅÅ-MM-DD, till exempel 2026-12-10.");
        }
    }

    /// <summary>
    /// Ja eller nej. Tom rad ger standardsvaret. Null betyder avbryt.
    /// </summary>
    public static bool? ReadYesNo(string prompt, bool defaultValue)
    {
        while (true)
        {
            var line = ReadLineOrCancel(prompt);
            if (line is null) return null;

            switch (line.ToLowerInvariant())
            {
                case "": return defaultValue;
                case "j" or "ja": return true;
                case "n" or "nej": return false;
            }

            Console.WriteLine("  Svara j för ja eller n för nej.");
        }
    }

    public static void Pause()
    {
        Console.WriteLine();
        Console.Write("Tryck Enter för att fortsätta...");
        Console.ReadLine();
    }

    /// <summary>Tolkar en sträng som bara består av siffror. Tecken som minus och punkt godtas inte.</summary>
    private static bool TryParseDigits(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
