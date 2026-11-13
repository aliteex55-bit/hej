using MySqlConnector;

namespace HolidayMaker;

/// <summary>
/// All SQL i programmet ligger här, en metod per fråga.
///
/// Varje värde som kommer från operatören skickas som en parameter
/// (@namn) och aldrig som en del av SQL-texten. PrepareAsync gör dessutom
/// frågan till en förberedd sats: servern får först frågan med
/// platshållare och därefter värdena för sig. Ett värde kan därför aldrig
/// tolkas som SQL-kod, vad operatören än skriver.
///
/// Sökning, filtrering och sortering görs av databasen. C#-koden tar
/// bara emot de färdiga raderna.
/// </summary>
public sealed class Repository(Db db)
{
    private readonly Db _db = db;

    // -----------------------------------------------------------------
    // 1. Kunder
    // -----------------------------------------------------------------
    public async Task<List<Customer>> GetCustomersAsync()
    {
        const string sql = """
            SELECT id, first_name, last_name, email, phone
              FROM customer
             ORDER BY last_name, first_name, id;
            """;

        await using var conn = await _db.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var list = new List<Customer>();
        while (await reader.ReadAsync())
        {
            list.Add(new Customer(
                reader.GetInt32("id"),
                reader.GetString("first_name"),
                reader.GetString("last_name"),
                reader.GetString("email"),
                reader.GetString("phone")));
        }
        return list;
    }

    public async Task<Customer> CreateCustomerAsync(string firstName, string lastName, string email, string phone)
    {
        const string sql = """
            INSERT INTO customer (first_name, last_name, email, phone)
            VALUES (@firstName, @lastName, @email, @phone);
            """;

        await using var conn = await _db.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@firstName", firstName);
        cmd.Parameters.AddWithValue("@lastName", lastName);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@phone", phone);
        await cmd.PrepareAsync();
        await cmd.ExecuteNonQueryAsync();

        return new Customer((int)cmd.LastInsertedId, firstName, lastName, email, phone);
    }

    // -----------------------------------------------------------------
    // 2. Sök lediga rum
    //
    // Hjärtat i uppgiften. NOT EXISTS-blocket säger: ta med rummet bara
    // om det INTE finns någon bokning som krockar med perioden.
    // En befintlig bokning krockar när
    //
    //     den börjar före den nya periodens slut   (b.check_in  < @checkOut)
    //     OCH slutar efter den nya periodens början (b.check_out > @checkIn)
    //
    // Jämförelserna är strikta. Det gör att utcheckningsdagen är ledig
    // för nästa gäst, samtidigt som alla sätt att överlappa fångas – även
    // när den ena perioden helt omsluter den andra.
    //
    // Varje valfritt filter är skrivet som (filtret är avstängt OR
    // villkoret är uppfyllt). Samma fråga klarar därför alla kombinationer
    // av filter, utan att SQL-texten behöver byggas ihop av strängar.
    //
    // Sorteringen styrs av en siffra. Operatörens val hamnar aldrig som
    // text i ORDER BY.
    // -----------------------------------------------------------------
    public async Task<List<RoomHit>> SearchAvailableRoomsAsync(SearchCriteria c)
    {
        const string sql = """
            SELECT  r.id,
                    a.name AS accommodation_name,
                    a.city,
                    r.room_number,
                    r.capacity,
                    r.price_per_night,
                    a.rating,
                    a.has_pool,
                    a.has_restaurant,
                    a.beach_distance_m,
                    a.center_distance_m
              FROM  room r
              JOIN  accommodation a ON a.id = r.accommodation_id
             WHERE  a.city = @city
               AND  r.capacity >= @guests
               AND (@requirePool       = 0 OR a.has_pool       = 1)
               AND (@requireRestaurant = 0 OR a.has_restaurant = 1)
               AND (@maxBeach  IS NULL OR a.beach_distance_m  <= @maxBeach)
               AND (@maxCenter IS NULL OR a.center_distance_m <= @maxCenter)
               AND  NOT EXISTS (
                        SELECT 1
                          FROM booking b
                         WHERE b.room_id   = r.id
                           AND b.check_in  < @checkOut
                           AND b.check_out > @checkIn)
             ORDER BY
                    CASE WHEN @sortByPrice = 1 THEN r.price_per_night END ASC,
                    CASE WHEN @sortByPrice = 0 THEN a.rating          END DESC,
                    r.price_per_night ASC,
                    a.name ASC,
                    r.room_number ASC;
            """;

        await using var conn = await _db.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@city", c.City);
        cmd.Parameters.AddWithValue("@guests", c.Guests);
        cmd.Parameters.AddWithValue("@checkIn", ToDb(c.CheckIn));
        cmd.Parameters.AddWithValue("@checkOut", ToDb(c.CheckOut));
        cmd.Parameters.AddWithValue("@requirePool", c.RequirePool ? 1 : 0);
        cmd.Parameters.AddWithValue("@requireRestaurant", c.RequireRestaurant ? 1 : 0);
        cmd.Parameters.AddWithValue("@maxBeach", (object?)c.MaxBeachDistance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@maxCenter", (object?)c.MaxCenterDistance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sortByPrice", c.Sort == SortOrder.PriceLowestFirst ? 1 : 0);
        await cmd.PrepareAsync();

        await using var reader = await cmd.ExecuteReaderAsync();

        var list = new List<RoomHit>();
        while (await reader.ReadAsync())
        {
            list.Add(new RoomHit(
                reader.GetInt32("id"),
                reader.GetString("accommodation_name"),
                reader.GetString("city"),
                reader.GetString("room_number"),
                reader.GetInt32("capacity"),
                reader.GetDecimal("price_per_night"),
                reader.GetInt32("rating"),
                reader.GetBoolean("has_pool"),
                reader.GetBoolean("has_restaurant"),
                reader.GetInt32("beach_distance_m"),
                reader.GetInt32("center_distance_m")));
        }
        return list;
    }

    /// <summary>Orterna som har boenden, för listan som visas före sökningen.</summary>
    public async Task<List<string>> GetCitiesAsync()
    {
        const string sql = """
            SELECT DISTINCT city
              FROM accommodation
             ORDER BY city;
            """;

        await using var conn = await _db.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var list = new List<string>();
        while (await reader.ReadAsync())
            list.Add(reader.GetString("city"));
        return list;
    }

    // -----------------------------------------------------------------
    // 3. Skapa bokning
    //
    // Uppgiften kräver att tillgängligheten kontrolleras EN GÅNG TILL när
    // bokningen sparas. Det görs här, i en transaktion:
    //
    //   1. Rummets rad låses (FOR UPDATE). Skulle två bokningar av samma
    //      rum komma samtidigt får den andra vänta tills den första är klar.
    //   2. Kapaciteten kontrolleras.
    //   3. Samma krockvillkor som i sökningen körs igen.
    //   4. Först därefter sparas bokningen, och transaktionen bekräftas.
    //
    // Går något av stegen fel rullas allt tillbaka och ingenting sparas.
    // -----------------------------------------------------------------
    public async Task<BookingResult> CreateBookingAsync(
        int roomId, int customerId, DateOnly checkIn, DateOnly checkOut, int guests)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string lockSql = """
            SELECT capacity, price_per_night
              FROM room
             WHERE id = @roomId
               FOR UPDATE;
            """;

        int capacity;
        decimal price;

        await using (var lockCmd = new MySqlCommand(lockSql, conn, tx))
        {
            lockCmd.Parameters.AddWithValue("@roomId", roomId);
            await lockCmd.PrepareAsync();

            await using var reader = await lockCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await reader.CloseAsync();
                await tx.RollbackAsync();
                return new BookingResult(BookingOutcome.RoomMissing);
            }
            capacity = reader.GetInt32("capacity");
            price = reader.GetDecimal("price_per_night");
        }

        if (guests > capacity)
        {
            await tx.RollbackAsync();
            return new BookingResult(BookingOutcome.TooManyGuests);
        }

        const string conflictSql = """
            SELECT COUNT(*)
              FROM booking
             WHERE room_id   = @roomId
               AND check_in  < @checkOut
               AND check_out > @checkIn;
            """;

        await using (var conflictCmd = new MySqlCommand(conflictSql, conn, tx))
        {
            conflictCmd.Parameters.AddWithValue("@roomId", roomId);
            conflictCmd.Parameters.AddWithValue("@checkIn", ToDb(checkIn));
            conflictCmd.Parameters.AddWithValue("@checkOut", ToDb(checkOut));
            await conflictCmd.PrepareAsync();

            var conflicts = Convert.ToInt32(await conflictCmd.ExecuteScalarAsync());
            if (conflicts > 0)
            {
                await tx.RollbackAsync();
                return new BookingResult(BookingOutcome.RoomTaken);
            }
        }

        const string insertSql = """
            INSERT INTO booking (room_id, customer_id, check_in, check_out, guests)
            VALUES (@roomId, @customerId, @checkIn, @checkOut, @guests);
            """;

        int newId;
        await using (var insertCmd = new MySqlCommand(insertSql, conn, tx))
        {
            insertCmd.Parameters.AddWithValue("@roomId", roomId);
            insertCmd.Parameters.AddWithValue("@customerId", customerId);
            insertCmd.Parameters.AddWithValue("@checkIn", ToDb(checkIn));
            insertCmd.Parameters.AddWithValue("@checkOut", ToDb(checkOut));
            insertCmd.Parameters.AddWithValue("@guests", guests);
            await insertCmd.PrepareAsync();
            await insertCmd.ExecuteNonQueryAsync();
            newId = (int)insertCmd.LastInsertedId;
        }

        await tx.CommitAsync();

        // Totalpris = antal nätter × pris per natt.
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        return new BookingResult(BookingOutcome.Created, newId, nights, price, nights * price);
    }

    // -----------------------------------------------------------------
    // 4. Kundens bokningar
    // -----------------------------------------------------------------
    public async Task<List<BookingRow>> GetBookingsForCustomerAsync(int customerId)
    {
        const string sql = """
            SELECT  b.id,
                    a.name AS accommodation_name,
                    a.city,
                    r.room_number,
                    r.capacity,
                    b.check_in,
                    b.check_out,
                    b.guests,
                    r.price_per_night
              FROM  booking b
              JOIN  room r          ON r.id = b.room_id
              JOIN  accommodation a ON a.id = r.accommodation_id
             WHERE  b.customer_id = @customerId
             ORDER BY b.check_in, b.id;
            """;

        await using var conn = await _db.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@customerId", customerId);
        await cmd.PrepareAsync();

        await using var reader = await cmd.ExecuteReaderAsync();

        var list = new List<BookingRow>();
        while (await reader.ReadAsync())
        {
            list.Add(new BookingRow(
                reader.GetInt32("id"),
                reader.GetString("accommodation_name"),
                reader.GetString("city"),
                reader.GetString("room_number"),
                reader.GetInt32("capacity"),
                DateOnly.FromDateTime(reader.GetDateTime("check_in")),
                DateOnly.FromDateTime(reader.GetDateTime("check_out")),
                reader.GetInt32("guests"),
                reader.GetDecimal("price_per_night")));
        }
        return list;
    }

    // -----------------------------------------------------------------
    // 5. Ändra antal personer
    //
    // Rummets kapacitet läses ut och jämförs innan något ändras. Raden
    // låses under tiden, så att kontrollen och ändringen hör ihop.
    // Triggern i databasen vaktar samma regel om någon skriver direkt
    // i en databasklient.
    // -----------------------------------------------------------------
    public async Task<GuestUpdateOutcome> UpdateGuestsAsync(int bookingId, int guests)
    {
        await using var conn = await _db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string capacitySql = """
            SELECT r.capacity
              FROM booking b
              JOIN room r ON r.id = b.room_id
             WHERE b.id = @bookingId
               FOR UPDATE;
            """;

        int capacity;
        await using (var capacityCmd = new MySqlCommand(capacitySql, conn, tx))
        {
            capacityCmd.Parameters.AddWithValue("@bookingId", bookingId);
            await capacityCmd.PrepareAsync();

            var value = await capacityCmd.ExecuteScalarAsync();
            if (value is null)
            {
                await tx.RollbackAsync();
                return GuestUpdateOutcome.BookingMissing;
            }
            capacity = Convert.ToInt32(value);
        }

        if (guests > capacity)
        {
            await tx.RollbackAsync();
            return GuestUpdateOutcome.TooManyGuests;
        }

        const string updateSql = """
            UPDATE booking
               SET guests = @guests
             WHERE id = @bookingId;
            """;

        await using (var updateCmd = new MySqlCommand(updateSql, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("@guests", guests);
            updateCmd.Parameters.AddWithValue("@bookingId", bookingId);
            await updateCmd.PrepareAsync();
            await updateCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return GuestUpdateOutcome.Updated;
    }

    // -----------------------------------------------------------------
    // 6. Avboka
    //
    // Raden tas bort ur databasen. Eftersom sökningen bara letar efter
    // bokningar som finns, är rummet därmed ledigt igen för perioden.
    // -----------------------------------------------------------------
    public async Task<bool> CancelBookingAsync(int bookingId)
    {
        const string sql = """
            DELETE FROM booking
             WHERE id = @bookingId;
            """;

        await using var conn = await _db.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bookingId", bookingId);
        await cmd.PrepareAsync();

        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    /// <summary>DATE-kolumner tar emot DateTime, så DateOnly översätts här.</summary>
    private static DateTime ToDb(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}
