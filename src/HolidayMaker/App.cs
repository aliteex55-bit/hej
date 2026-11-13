using System.Globalization;
using MySqlConnector;

namespace HolidayMaker;

/// <summary>
/// Konsolmenyn. Menyvalen 1–6 motsvarar ett för ett de sex saker som
/// operatören ska kunna göra enligt uppgiften.
///
/// All SQL ligger i Repository. Den här klassen ställer frågor, visar
/// svar och ser till att felaktig inmatning inte avslutar programmet.
/// </summary>
public sealed class App(Repository repo)
{
    // "Alla rum kan bokas under december 2026." Första och sista natten
    // som går att boka. Eftersom utcheckningsdagen inte upptar rummet kan
    // utcheckningen som senast ske dagen efter den sista natten.
    private static readonly DateOnly FirstNight = new(2026, 12, 1);
    private static readonly DateOnly LastNight = new(2026, 12, 31);
    private static readonly DateOnly LatestCheckOut = LastNight.AddDays(1);

    // Rimlighetsgräns för inmatningen. Om sällskapet ryms i ett visst rum
    // avgörs av rummets kapacitet i databasen, inte av den här siffran.
    private const int MaxGuestsInput = 99;

    private const string DateFormat = "yyyy-MM-dd";

    private readonly Repository _repo = repo;

    // Kunden som operatören just nu hjälper.
    private Customer? _current;

    // Villkoren från den senaste sökningen som gav träffar. Val 3 kan då
    // boka utan att ort och datum behöver skrivas in en gång till.
    private SearchCriteria? _lastSearch;

    public async Task RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("===========================================");
        Console.WriteLine("  HOLIDAY MAKER - bokning via telefon");
        Console.WriteLine("===========================================");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"Vald kund: {(_current is null ? "ingen" : $"{_current.FullName} (nr {_current.Id})")}");
            Console.WriteLine("-------------------------------------------");
            Console.WriteLine("  1. Välj eller registrera kund");
            Console.WriteLine("  2. Sök lediga rum");
            Console.WriteLine("  3. Skapa bokning");
            Console.WriteLine("  4. Visa kundens bokningar");
            Console.WriteLine("  5. Ändra antal personer i en bokning");
            Console.WriteLine("  6. Avboka");
            Console.WriteLine("  0. Avsluta");
            Console.WriteLine();

            var choice = Input.ReadInt("Val: ", 0, 6);
            if (choice is null or 0)
            {
                Console.WriteLine("Hej då.");
                return;
            }

            try
            {
                switch (choice)
                {
                    case 1: await ChooseCustomerAsync(); break;
                    case 2: await SearchAsync(); break;
                    case 3: await CreateBookingAsync(); break;
                    case 4: await ShowBookingsAsync(); break;
                    case 5: await ChangeGuestsAsync(); break;
                    case 6: await CancelAsync(); break;
                }
            }
            catch (MySqlException ex)
            {
                // Till exempel om databasen stängs av medan programmet kör.
                Console.WriteLine();
                Console.WriteLine($"  Databasen svarade med ett fel: {ex.Message}");
                Console.WriteLine("  Åtgärden genomfördes inte. Försök igen.");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"  Ett oväntat fel inträffade: {ex.Message}");
                Console.WriteLine("  Åtgärden genomfördes inte. Försök igen.");
            }

            Input.Pause();
        }
    }

    // =================================================================
    // 1. Registrera och välja kund
    // =================================================================
    private async Task ChooseCustomerAsync()
    {
        var customers = await _repo.GetCustomersAsync();

        Console.WriteLine();
        Console.WriteLine("KUNDER");
        Console.WriteLine();

        if (customers.Count == 0)
        {
            Console.WriteLine("  Inga kunder är registrerade än.");
        }
        else
        {
            Console.WriteLine($"  {"Nr",4}  {"Namn",-30}{"E-post",-34}Telefon");
            Console.WriteLine($"  {new string('-', 82)}");
            foreach (var c in customers)
            {
                Console.WriteLine(
                    $"  {c.Id,4}  {Shorten($"{c.LastName}, {c.FirstName}", 29),-30}{Shorten(c.Email, 33),-34}{c.Phone}");
            }
        }

        Console.WriteLine();
        var allowed = customers.Select(c => c.Id).Append(0).ToList();
        var id = Input.ReadChoice(
            "Kundnummer (0 = registrera ny kund, Enter = tillbaka): ",
            allowed,
            "  Det numret finns inte i listan. Skriv ett kundnummer, eller 0 för en ny kund.");

        if (id is null) return;

        if (id == 0)
        {
            await RegisterCustomerAsync();
            return;
        }

        _current = customers.First(c => c.Id == id);
        Console.WriteLine($"  Vald kund: {_current.FullName}.");
    }

    private async Task RegisterCustomerAsync()
    {
        Console.WriteLine();
        Console.WriteLine("REGISTRERA NY KUND");
        Console.WriteLine("  Skriv \"avbryt\" för att gå tillbaka utan att spara.");
        Console.WriteLine();

        // Längderna är desamma som kolumnernas i tabellen customer.
        var first = Input.ReadText("Förnamn: ", 60);
        if (first is null) return;

        var last = Input.ReadText("Efternamn: ", 60);
        if (last is null) return;

        var email = ReadEmail();
        if (email is null) return;

        var phone = ReadPhone();
        if (phone is null) return;

        try
        {
            _current = await _repo.CreateCustomerAsync(first, last, email, phone);
            Console.WriteLine($"  Kund nr {_current.Id} är registrerad och vald: {_current.FullName}.");
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // UNIQUE-villkoret på customer.email slog till.
            Console.WriteLine("  Det finns redan en kund med den e-postadressen. Ingen ny kund sparades.");
        }
    }

    private static string? ReadEmail()
    {
        while (true)
        {
            var email = Input.ReadText("E-post: ", 120);
            if (email is null) return null;

            var at = email.IndexOf('@');
            var looksRight = at > 0
                             && at < email.Length - 1
                             && at == email.LastIndexOf('@')
                             && !email.Any(char.IsWhiteSpace);
            if (looksRight) return email;

            Console.WriteLine("  En e-postadress ska ha formen namn@adress, utan mellanslag.");
        }
    }

    private static string? ReadPhone()
    {
        while (true)
        {
            var phone = Input.ReadText("Telefon: ", 30);
            if (phone is null) return null;

            var looksRight = phone.Any(char.IsAsciiDigit)
                             && phone.All(ch => char.IsAsciiDigit(ch) || ch is ' ' or '+' or '-' or '(' or ')');
            if (looksRight) return phone;

            Console.WriteLine("  Ett telefonnummer får bara innehålla siffror, mellanslag och tecknen + - ( ).");
        }
    }

    // =================================================================
    // 2. Söka efter lediga rum
    // =================================================================
    private async Task SearchAsync()
    {
        var criteria = await AskSearchCriteriaAsync();
        if (criteria is null) return;

        var hits = await _repo.SearchAvailableRoomsAsync(criteria);
        ShowHits(hits, criteria);

        if (hits.Count == 0)
        {
            _lastSearch = null;
            return;
        }

        _lastSearch = criteria;
        Console.WriteLine(_current is null
            ? "  För att boka ett av rummen: välj en kund med 1 och sedan 3 i menyn."
            : "  Välj 3 i menyn för att boka ett av rummen.");
    }

    /// <summary>
    /// Ställer följdfrågorna som sökningen behöver: ort, datum och antal
    /// personer, och därefter de valfria filtren och sorteringen.
    /// Null betyder att operatören avbröt.
    /// </summary>
    private async Task<SearchCriteria?> AskSearchCriteriaAsync()
    {
        var cities = await _repo.GetCitiesAsync();

        Console.WriteLine();
        Console.WriteLine("SÖK LEDIGA RUM");
        Console.WriteLine();
        Console.WriteLine("  Svara på frågorna, så visas de rum som är lediga under hela perioden.");
        Console.WriteLine("  Skriv \"avbryt\" för att gå tillbaka till menyn.");
        Console.WriteLine();

        if (cities.Count == 0)
        {
            Console.WriteLine("  Det finns inga boenden i databasen. Kör sql/02_testdata.sql.");
            return null;
        }

        // Orterna visas först, så att operatören ser vad som går att välja.
        Console.WriteLine("  Orter med boenden:");
        for (var i = 0; i < cities.Count; i++)
            Console.WriteLine($"    {i + 1}. {cities[i]}");
        Console.WriteLine();

        var city = ReadCity(cities);
        if (city is null) return null;

        Console.WriteLine($"  Bokningsbar period: {FirstNight.ToString(DateFormat)} till {LastNight.ToString(DateFormat)}.");

        var checkIn = ReadCheckIn();
        if (checkIn is null) return null;

        var checkOut = ReadCheckOut(checkIn.Value);
        if (checkOut is null) return null;

        var guests = Input.ReadInt("Antal personer: ", 1, MaxGuestsInput);
        if (guests is null) return null;

        Console.WriteLine();
        Console.WriteLine("  Valfria filter. Tryck bara Enter för att hoppa över ett filter.");

        var pool = Input.ReadYesNo("Måste boendet ha pool? (j/n): ", defaultValue: false);
        if (pool is null) return null;

        var restaurant = Input.ReadYesNo("Måste boendet ha restaurang? (j/n): ", defaultValue: false);
        if (restaurant is null) return null;

        if (!Input.TryReadOptionalInt("Högst antal meter till strand: ", 0, 1_000_000, out var maxBeach)) return null;
        if (!Input.TryReadOptionalInt("Högst antal meter till centrum: ", 0, 1_000_000, out var maxCenter)) return null;

        Console.WriteLine();
        Console.WriteLine("  Sortering: 1 = lägst pris först, 2 = högst omdöme först.");
        if (!Input.TryReadOptionalInt("Sortering (Enter = 1): ", 1, 2, out var sort, "för lägst pris först")) return null;

        return new SearchCriteria(
            city, checkIn.Value, checkOut.Value, guests.Value,
            pool.Value, restaurant.Value, maxBeach, maxCenter,
            sort == 2 ? SortOrder.RatingHighestFirst : SortOrder.PriceLowestFirst);
    }

    /// <summary>
    /// Orten väljs med sitt nummer i listan eller genom att skriva namnet.
    /// Det som skickas vidare till SQL-frågan är alltid ortens namn så som
    /// det står i databasen.
    /// </summary>
    private static string? ReadCity(List<string> cities)
    {
        while (true)
        {
            var text = Input.ReadText("Ort (namn eller nummer i listan): ", 60);
            if (text is null) return null;

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                if (number >= 1 && number <= cities.Count)
                    return cities[number - 1];

                Console.WriteLine($"  Listan har nummer 1 till {cities.Count}.");
                continue;
            }

            var match = cities.FirstOrDefault(c => string.Equals(c, text, StringComparison.CurrentCultureIgnoreCase));
            if (match is not null)
                return match;

            Console.WriteLine($"  Det finns inga boenden på orten \"{text}\". Välj en ort ur listan ovan.");
        }
    }

    private static DateOnly? ReadCheckIn()
    {
        while (true)
        {
            var date = Input.ReadDate("Incheckning (ÅÅÅÅ-MM-DD): ");
            if (date is null) return null;

            if (date >= FirstNight && date <= LastNight)
                return date;

            Console.WriteLine($"  Rum kan bara bokas under december 2026. Välj en dag mellan {FirstNight.ToString(DateFormat)} och {LastNight.ToString(DateFormat)}.");
        }
    }

    private static DateOnly? ReadCheckOut(DateOnly checkIn)
    {
        while (true)
        {
            var date = Input.ReadDate("Utcheckning (ÅÅÅÅ-MM-DD): ");
            if (date is null) return null;

            if (date <= checkIn)
            {
                Console.WriteLine("  Utcheckning måste ligga efter incheckning.");
                continue;
            }
            if (date > LatestCheckOut)
            {
                Console.WriteLine($"  Sista natten som går att boka är {LastNight.ToString(DateFormat)}, så utcheckning kan senast vara {LatestCheckOut.ToString(DateFormat)}.");
                continue;
            }
            return date;
        }
    }

    private static void ShowHits(List<RoomHit> hits, SearchCriteria c)
    {
        Console.WriteLine();
        Console.WriteLine($"LEDIGA RUM I {c.City.ToUpper(CultureInfo.CurrentCulture)}");
        Console.WriteLine($"  Period    : {c.CheckIn.ToString(DateFormat)} till {c.CheckOut.ToString(DateFormat)} ({Count(c.Nights, "natt", "nätter")})");
        Console.WriteLine($"  Sällskap  : {Count(c.Guests, "person", "personer")}");
        Console.WriteLine($"  Filter    : {DescribeFilters(c)}");
        Console.WriteLine($"  Sortering : {(c.Sort == SortOrder.PriceLowestFirst ? "lägst pris först" : "högst omdöme först")}");
        Console.WriteLine();

        if (hits.Count == 0)
        {
            Console.WriteLine("  Inga lediga rum matchar sökningen.");
            return;
        }

        Console.WriteLine(
            $"  {"Rum-id",6}  {"Boende",-26} {"Ort",-11} {"Rum",-6} {"Pers",4}  {"Pris/natt",9}  {"Omdöme",6}  {"Pool",-5} {"Rest.",-6} {"Strand",7}  {"Centrum",7}");
        Console.WriteLine($"  {new string('-', 108)}");

        foreach (var h in hits)
        {
            Console.WriteLine(
                $"  {h.RoomId,6}  {Shorten(h.AccommodationName, 25),-26} {Shorten(h.City, 10),-11} {h.RoomNumber,-6} {h.Capacity,4}  " +
                $"{h.PricePerNight,9:N2}  {h.Rating,6}  {YesNo(h.HasPool),-5} {YesNo(h.HasRestaurant),-6} {h.BeachDistance,5} m  {h.CenterDistance,5} m");
        }

        Console.WriteLine();
        Console.WriteLine($"  {Count(hits.Count, "ledigt rum", "lediga rum")}. Totalpris för vistelsen = {Count(c.Nights, "natt", "nätter")} x pris per natt.");
    }

    private static string DescribeFilters(SearchCriteria c)
    {
        var parts = new List<string>();
        if (c.RequirePool) parts.Add("pool");
        if (c.RequireRestaurant) parts.Add("restaurang");
        if (c.MaxBeachDistance is not null) parts.Add($"högst {c.MaxBeachDistance} m till strand");
        if (c.MaxCenterDistance is not null) parts.Add($"högst {c.MaxCenterDistance} m till centrum");
        return parts.Count == 0 ? "inga" : string.Join(", ", parts);
    }

    // =================================================================
    // 3. Skapa en bokning
    // =================================================================
    private async Task CreateBookingAsync()
    {
        if (_current is null)
        {
            Console.WriteLine("  Välj en kund först (val 1).");
            return;
        }

        SearchCriteria? criteria = null;

        if (_lastSearch is not null)
        {
            var s = _lastSearch;
            Console.WriteLine();
            Console.WriteLine($"  Senaste sökningen: {s.City}, {s.CheckIn.ToString(DateFormat)} till {s.CheckOut.ToString(DateFormat)}, {Count(s.Guests, "person", "personer")}.");

            var reuse = Input.ReadYesNo("Boka ett rum från den sökningen? (j/n, Enter = j): ", defaultValue: true);
            if (reuse is null) return;
            if (reuse.Value) criteria = s;
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("  Ingen sökning är gjord än, så vi börjar med att söka fram ett ledigt rum.");
        }

        criteria ??= await AskSearchCriteriaAsync();
        if (criteria is null) return;

        // Sökningen körs alltid om mot databasen. Listan visar därför vad
        // som är ledigt just nu, inte vad som var ledigt vid förra sökningen.
        var hits = await _repo.SearchAvailableRoomsAsync(criteria);
        ShowHits(hits, criteria);

        if (hits.Count == 0)
        {
            _lastSearch = null;
            return;
        }
        _lastSearch = criteria;

        Console.WriteLine();
        var roomId = Input.ReadChoice(
            $"Rum-id att boka åt {_current.FullName} (Enter = avbryt): ",
            hits.Select(h => h.RoomId).ToList(),
            "  Skriv ett rum-id ur listan ovan, eller tryck bara Enter för att avbryta.");

        if (roomId is null)
        {
            Console.WriteLine("  Ingen bokning gjord.");
            return;
        }

        var room = hits.First(h => h.RoomId == roomId);

        // Tillgängligheten kontrolleras en gång till inne i den här metoden,
        // i samma transaktion som sparar bokningen.
        var result = await _repo.CreateBookingAsync(
            room.RoomId, _current.Id, criteria.CheckIn, criteria.CheckOut, criteria.Guests);

        Console.WriteLine();
        switch (result.Outcome)
        {
            case BookingOutcome.Created:
                Console.WriteLine("  BOKNINGSBEKRÄFTELSE");
                Console.WriteLine($"    Bokningsnummer : {result.BookingId}");
                Console.WriteLine($"    Kund           : {_current.FullName}");
                Console.WriteLine($"    Boende         : {room.AccommodationName}, {room.City}");
                Console.WriteLine($"    Rum            : {room.RoomNumber}");
                Console.WriteLine($"    Incheckning    : {criteria.CheckIn.ToString(DateFormat)}");
                Console.WriteLine($"    Utcheckning    : {criteria.CheckOut.ToString(DateFormat)}");
                Console.WriteLine($"    Antal personer : {criteria.Guests}");
                Console.WriteLine($"    Totalpris      : {Count(result.Nights, "natt", "nätter")} x {result.PricePerNight:N2} kr = {result.Total:N2} kr");
                break;

            case BookingOutcome.RoomTaken:
                Console.WriteLine("  Rummet har hunnit bli bokat sedan sökningen gjordes.");
                Console.WriteLine("  Ingen bokning gjord. Välj 3 igen för att se vad som är ledigt nu.");
                break;

            case BookingOutcome.TooManyGuests:
                Console.WriteLine("  Sällskapet ryms inte i rummet. Ingen bokning gjord.");
                break;

            case BookingOutcome.RoomMissing:
                Console.WriteLine("  Rummet finns inte längre. Ingen bokning gjord.");
                break;
        }
    }

    // =================================================================
    // 4. Visa kundens bokningar
    // =================================================================
    private async Task ShowBookingsAsync()
    {
        if (_current is null)
        {
            Console.WriteLine("  Välj en kund först (val 1).");
            return;
        }

        var rows = await _repo.GetBookingsForCustomerAsync(_current.Id);
        ShowBookingRows(rows, _current);
    }

    private static void ShowBookingRows(List<BookingRow> rows, Customer customer)
    {
        Console.WriteLine();
        Console.WriteLine($"BOKNINGAR FÖR {customer.FullName.ToUpper(CultureInfo.CurrentCulture)}");
        Console.WriteLine();

        if (rows.Count == 0)
        {
            Console.WriteLine("  Kunden har inga bokningar.");
            return;
        }

        Console.WriteLine(
            $"  {"Nr",6}  {"Boende",-26} {"Ort",-11} {"Rum",-6} {"Incheckning",-13}{"Utcheckning",-13}{"Nätter",6}  {"Pers",4}  {"Totalpris",13}");
        Console.WriteLine($"  {new string('-', 107)}");

        foreach (var b in rows)
        {
            Console.WriteLine(
                $"  {b.BookingId,6}  {Shorten(b.AccommodationName, 25),-26} {Shorten(b.City, 10),-11} {b.RoomNumber,-6} " +
                $"{b.CheckIn.ToString(DateFormat)}   {b.CheckOut.ToString(DateFormat)}   {b.Nights,6}  {b.Guests,4}  {b.Total,10:N2} kr");
        }

        Console.WriteLine();
        Console.WriteLine($"  {Count(rows.Count, "bokning", "bokningar")}, sammanlagt {rows.Sum(b => b.Total):N2} kr.");
    }

    /// <summary>
    /// Visar den valda kundens bokningar och låter operatören peka ut en.
    /// Att valet görs ur listan innebär dels att numret inte behöver
    /// kommas ihåg, dels att en felskrivning aldrig kan träffa en annan
    /// kunds bokning.
    /// </summary>
    private async Task<BookingRow?> PickBookingAsync(string prompt)
    {
        if (_current is null)
        {
            Console.WriteLine("  Välj en kund först (val 1).");
            return null;
        }

        var rows = await _repo.GetBookingsForCustomerAsync(_current.Id);
        ShowBookingRows(rows, _current);
        if (rows.Count == 0) return null;

        Console.WriteLine();
        var id = Input.ReadChoice(
            prompt,
            rows.Select(b => b.BookingId).ToList(),
            $"  {_current.FullName} har ingen bokning med det numret. Skriv ett nummer ur listan, eller tryck bara Enter för att avbryta.");

        return id is null ? null : rows.First(b => b.BookingId == id);
    }

    // =================================================================
    // 5. Ändra antal personer i en bokning
    // =================================================================
    private async Task ChangeGuestsAsync()
    {
        var booking = await PickBookingAsync("Bokningsnummer att ändra (Enter = avbryt): ");
        if (booking is null) return;

        Console.WriteLine();
        Console.WriteLine($"  Bokning {booking.BookingId}: {booking.AccommodationName}, rum {booking.RoomNumber}, " +
                          $"{booking.CheckIn.ToString(DateFormat)} till {booking.CheckOut.ToString(DateFormat)}.");
        Console.WriteLine($"  Gäller nu {Count(booking.Guests, "person", "personer")}. Rummet tar högst {booking.Capacity}.");

        var guests = Input.ReadInt("Nytt antal personer: ", 1, MaxGuestsInput);
        if (guests is null) return;

        // Det är databasen som avgör om sällskapet ryms, inte den här klassen.
        var outcome = await _repo.UpdateGuestsAsync(booking.BookingId, guests.Value);

        switch (outcome)
        {
            case GuestUpdateOutcome.Updated:
                Console.WriteLine($"  Bokning {booking.BookingId} gäller nu {Count(guests.Value, "person", "personer")}.");
                break;

            case GuestUpdateOutcome.TooManyGuests:
                Console.WriteLine($"  Går inte: rum {booking.RoomNumber} tar högst {Count(booking.Capacity, "person", "personer")}. Bokningen är oförändrad.");
                break;

            case GuestUpdateOutcome.BookingMissing:
                Console.WriteLine("  Bokningen finns inte längre.");
                break;
        }
    }

    // =================================================================
    // 6. Avboka
    // =================================================================
    private async Task CancelAsync()
    {
        var booking = await PickBookingAsync("Bokningsnummer att avboka (Enter = avbryt): ");
        if (booking is null) return;

        Console.WriteLine();
        Console.WriteLine($"  Bokning {booking.BookingId}: {booking.AccommodationName}, rum {booking.RoomNumber}, " +
                          $"{booking.CheckIn.ToString(DateFormat)} till {booking.CheckOut.ToString(DateFormat)}, " +
                          $"{Count(booking.Guests, "person", "personer")}, {booking.Total:N2} kr.");

        var confirmed = Input.ReadYesNo("Avboka den här bokningen? (j/n, Enter = n): ", defaultValue: false);
        if (confirmed is not true)
        {
            Console.WriteLine("  Inget avbokat.");
            return;
        }

        var removed = await _repo.CancelBookingAsync(booking.BookingId);
        Console.WriteLine(removed
            ? $"  Bokning {booking.BookingId} är avbokad. Rum {booking.RoomNumber} är ledigt igen {booking.CheckIn.ToString(DateFormat)} till {booking.CheckOut.ToString(DateFormat)}."
            : "  Bokningen fanns inte längre.");
    }

    // =================================================================
    // Små hjälpare för utskrifterna
    // =================================================================

    /// <summary>Kortar av en text som annars skulle spräcka kolumnen.</summary>
    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + ".";

    /// <summary>"1 natt" men "3 nätter".</summary>
    private static string Count(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    private static string YesNo(bool value) => value ? "ja" : "nej";
}
