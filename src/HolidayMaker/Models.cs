namespace HolidayMaker;

/// <summary>En kund i registret.</summary>
public record Customer(int Id, string FirstName, string LastName, string Email, string Phone)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>Ett ledigt rum som sökningen hittade.</summary>
public record RoomHit(
    int RoomId,
    string AccommodationName,
    string City,
    string RoomNumber,
    int Capacity,
    decimal PricePerNight,
    int Rating,
    bool HasPool,
    bool HasRestaurant,
    int BeachDistance,
    int CenterDistance);

/// <summary>En rad i "kundens bokningar".</summary>
public record BookingRow(
    int BookingId,
    string AccommodationName,
    string City,
    string RoomNumber,
    int Capacity,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Guests,
    decimal PricePerNight)
{
    /// <summary>10–13 december är tre nätter: utcheckningsdagen räknas inte.</summary>
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    /// <summary>Totalpris = antal nätter × pris per natt. Lagras aldrig, räknas alltid ut.</summary>
    public decimal Total => Nights * PricePerNight;
}

/// <summary>Sökvillkoren som operatören matar in.</summary>
public record SearchCriteria(
    string City,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Guests,
    bool RequirePool,
    bool RequireRestaurant,
    int? MaxBeachDistance,
    int? MaxCenterDistance,
    SortOrder Sort)
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
}

/// <summary>De två tillåtna sorteringarna. Fri text hamnar aldrig i ORDER BY.</summary>
public enum SortOrder
{
    PriceLowestFirst,
    RatingHighestFirst
}

/// <summary>Utfallet av ett försök att boka.</summary>
public enum BookingOutcome
{
    Created,
    RoomTaken,
    TooManyGuests,
    RoomMissing
}

/// <summary>Det som bokningsbekräftelsen behöver.</summary>
public record BookingResult(
    BookingOutcome Outcome,
    int BookingId = 0,
    int Nights = 0,
    decimal PricePerNight = 0m,
    decimal Total = 0m);

/// <summary>Utfallet av ett försök att ändra antal personer.</summary>
public enum GuestUpdateOutcome
{
    Updated,
    TooManyGuests,
    BookingMissing
}
