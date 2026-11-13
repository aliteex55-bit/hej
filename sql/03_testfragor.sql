-- =====================================================================
-- Holiday Maker – programmets SQL-frågor, körbara i en databasklient
-- =====================================================================
-- Här finns samma frågor som i src/HolidayMaker/Repository.cs, så att
-- de kan provas och förklaras utan att programmet är igång.
--
-- I C#-koden skickas värdena som parametrar: @city, @checkIn och så
-- vidare. MySQL har användarvariabler med samma skrivsätt. Därför sätts
-- värdena här med SET, och frågan under kan stå ordagrant som i koden.
--
-- Allt i filen är SELECT-frågor. Ingenting ändras i databasen.
-- Kör mot testdatat från 02_testdata.sql:
--
--     mysql -u root -p --default-character-set=utf8mb4 --table < 03_testfragor.sql
-- =====================================================================

-- Databasen använder svensk sorteringsordning (utf8mb4_swedish_ci), där
-- å, ä och ö är egna bokstäver och inte varianter av a och o. Variablerna
-- nedan måste ha samma ordning som kolumnerna de jämförs med – annars
-- svarar MySQL "Illegal mix of collations". Därför anges den här.
-- (I programmet behövs det inte: en parameter rättar sig efter kolumnen.)
SET NAMES utf8mb4 COLLATE utf8mb4_swedish_ci;
USE holiday_maker;

-- ---------------------------------------------------------------------
-- 1. Lediga rum: grundsökningen
--    Malmö, 14–16 december, 2 personer, inga filter, lägst pris först.
--    Väntat: 101 och 102 (1450), 203 (1600), 103 (2200).
--    Rum 202 saknas: det är bokat 9–16 december.
-- ---------------------------------------------------------------------
SELECT '1. Malmö 14–16 dec, 2 personer, lägst pris först' AS fraga;

SET @city = 'Malmö', @checkIn = '2026-12-14', @checkOut = '2026-12-16', @guests = 2,
    @requirePool = 0, @requireRestaurant = 0, @maxBeach = NULL, @maxCenter = NULL,
    @sortByPrice = 1;

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

-- ---------------------------------------------------------------------
-- 2. Samma sökning, sorterad på omdöme i stället (@sortByPrice = 0).
--    Väntat: Strandkantens tre rum (omdöme 5) före Solgården (omdöme 4).
-- ---------------------------------------------------------------------
SELECT '2. Samma sökning, högst omdöme först' AS fraga;

SET @sortByPrice = 0;

SELECT  r.id, a.name AS accommodation_name, r.room_number, r.capacity, r.price_per_night, a.rating
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

-- ---------------------------------------------------------------------
-- 3. Ett upptaget rum filtreras bort.
--    Rum 101 i Malmö är bokat 1–5 december. Rum 102 är ledigt då.
--    Väntat: 102 finns med, 101 gör det inte.
-- ---------------------------------------------------------------------
SELECT '3. Malmö 1–5 dec: 101 är upptaget, 102 är ledigt' AS fraga;

SET @city = 'Malmö', @checkIn = '2026-12-01', @checkOut = '2026-12-05', @guests = 2,
    @requirePool = 0, @requireRestaurant = 0, @maxBeach = NULL, @maxCenter = NULL,
    @sortByPrice = 1;

SELECT  a.name AS accommodation_name, r.room_number, r.capacity, r.price_per_night
  FROM  room r
  JOIN  accommodation a ON a.id = r.accommodation_id
 WHERE  a.city = @city
   AND  r.capacity >= @guests
   AND  NOT EXISTS (
            SELECT 1
              FROM booking b
             WHERE b.room_id   = r.id
               AND b.check_in  < @checkOut
               AND b.check_out > @checkIn)
 ORDER BY r.price_per_night, a.name, r.room_number;

-- ---------------------------------------------------------------------
-- 4. Utcheckningsdagen upptar inte rummet.
--    Rum 102 är bokat 10–13 december.
--    Väntat: ledigt 13–16 (ny gäst in samma dag som den förra ut),
--            ledigt 7–10 (ut samma dag som nästa in),
--            upptaget 12–14 (en enda gemensam natt räcker).
-- ---------------------------------------------------------------------
SELECT '4. Rum 102 är bokat 10–13 dec. Tre perioder runt bokningen:' AS fraga;

SELECT  p.period,
        CASE WHEN EXISTS (
                 SELECT 1
                   FROM booking b
                   JOIN room r          ON r.id = b.room_id
                   JOIN accommodation a ON a.id = r.accommodation_id
                  WHERE a.city = 'Malmö'
                    AND r.room_number = '102'
                    AND b.check_in  < p.check_out
                    AND b.check_out > p.check_in)
             THEN 'upptaget'
             ELSE 'ledigt'
        END AS rum_102
  FROM (
        SELECT '13–16 dec' AS period, DATE '2026-12-13' AS check_in, DATE '2026-12-16' AS check_out
        UNION ALL
        SELECT ' 7–10 dec',           DATE '2026-12-07',             DATE '2026-12-10'
        UNION ALL
        SELECT '12–14 dec',           DATE '2026-12-12',             DATE '2026-12-14'
       ) AS p;

-- ---------------------------------------------------------------------
-- 5. Kombinerade filter.
--    Göteborg, 10–13 december, 2 personer.
--    a) Kräver pool: bara Hotell Hamnkrogen (302, 303, 304).
--    b) Pool och högst 100 m till strand: inga träffar, eftersom
--       Hamnkrogen ligger 2500 m från stranden.
-- ---------------------------------------------------------------------
SELECT '5a. Göteborg 10–13 dec, 2 personer, kräver pool' AS fraga;

SET @city = 'Göteborg', @checkIn = '2026-12-10', @checkOut = '2026-12-13', @guests = 2,
    @requirePool = 1, @requireRestaurant = 0, @maxBeach = NULL, @maxCenter = NULL,
    @sortByPrice = 1;

SELECT  a.name AS accommodation_name, r.room_number, r.price_per_night, a.has_pool, a.beach_distance_m
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
 ORDER BY r.price_per_night, a.name, r.room_number;

SELECT '5b. Samma, plus högst 100 m till strand (väntat: inga rader)' AS fraga;

SET @maxBeach = 100;

SELECT  a.name AS accommodation_name, r.room_number, r.price_per_night, a.has_pool, a.beach_distance_m
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
 ORDER BY r.price_per_night, a.name, r.room_number;

-- ---------------------------------------------------------------------
-- 6. För stort sällskap.
--    Visby har inget rum för 8 personer. Väntat: inga rader.
-- ---------------------------------------------------------------------
SELECT '6. Visby 10–12 dec, 8 personer (väntat: inga rader)' AS fraga;

SET @city = 'Visby', @checkIn = '2026-12-10', @checkOut = '2026-12-12', @guests = 8;

SELECT  a.name AS accommodation_name, r.room_number, r.capacity
  FROM  room r
  JOIN  accommodation a ON a.id = r.accommodation_id
 WHERE  a.city = @city
   AND  r.capacity >= @guests
   AND  NOT EXISTS (
            SELECT 1
              FROM booking b
             WHERE b.room_id   = r.id
               AND b.check_in  < @checkOut
               AND b.check_out > @checkIn);

-- ---------------------------------------------------------------------
-- 7. Kundens bokningar (samma fråga som i GetBookingsForCustomerAsync).
--    De två sista kolumnerna är en kontroll: databasen räknar själv ut
--    antal nätter och totalpris, på samma sätt som programmet gör.
--    Väntat för kund 1: fem bokningar, sammanlagt 51 600 kr.
-- ---------------------------------------------------------------------
SELECT '7. Bokningar för kund 1 (Anna Lindqvist)' AS fraga;

SET @customerId = 1;

SELECT  b.id,
        a.name AS accommodation_name,
        a.city,
        r.room_number,
        r.capacity,
        b.check_in,
        b.check_out,
        b.guests,
        r.price_per_night,
        DATEDIFF(b.check_out, b.check_in)                     AS natter,
        DATEDIFF(b.check_out, b.check_in) * r.price_per_night AS totalpris
  FROM  booking b
  JOIN  room r          ON r.id = b.room_id
  JOIN  accommodation a ON a.id = r.accommodation_id
 WHERE  b.customer_id = @customerId
 ORDER BY b.check_in, b.id;

-- ---------------------------------------------------------------------
-- 8. Kontrollen som görs en gång till när en bokning sparas
--    (samma villkor som i sökningen, mot ett enda rum).
--    Rum 3 (103 i Malmö) är bokat 20–27 december.
--    Väntat: 1 krock för 22–24 december, 0 krockar för 27–29 december.
-- ---------------------------------------------------------------------
SELECT '8. Krockar för rum 3: först 22–24 dec, sedan 27–29 dec' AS fraga;

SET @roomId = 3, @checkIn = '2026-12-22', @checkOut = '2026-12-24';

SELECT COUNT(*) AS krockar
  FROM booking
 WHERE room_id   = @roomId
   AND check_in  < @checkOut
   AND check_out > @checkIn;

SET @checkIn = '2026-12-27', @checkOut = '2026-12-29';

SELECT COUNT(*) AS krockar
  FROM booking
 WHERE room_id   = @roomId
   AND check_in  < @checkOut
   AND check_out > @checkIn;

-- ---------------------------------------------------------------------
-- 9. Två kontroller av hela databasen. Båda ska ge 0.
-- ---------------------------------------------------------------------
SELECT '9. Kontroller av hela databasen (båda ska vara 0)' AS fraga;

SELECT COUNT(*) AS bokningar_som_krockar
  FROM booking x
  JOIN booking y
    ON x.room_id   = y.room_id
   AND x.id        < y.id
   AND x.check_in  < y.check_out
   AND x.check_out > y.check_in;

SELECT COUNT(*) AS bokningar_med_for_manga_gaster
  FROM booking b
  JOIN room r ON r.id = b.room_id
 WHERE b.guests > r.capacity;

-- =====================================================================
-- Vill du se att databasen själv säger nej? Kör en av raderna nedan i
-- taget (ta bort de två strecken först). Alla tre ska ge ett fel, och
-- ingenting sparas.
--
-- Krockar med bokningen 20–27 december i rum 3:
--   INSERT INTO booking (room_id, customer_id, check_in, check_out, guests)
--   VALUES (3, 1, '2026-12-22', '2026-12-24', 2);
--   -- ERROR 1644: Rummet har redan en bokning under perioden
--
-- Fyra personer i rum 1, som rymmer två:
--   INSERT INTO booking (room_id, customer_id, check_in, check_out, guests)
--   VALUES (1, 1, '2026-12-11', '2026-12-13', 4);
--   -- ERROR 1644: Antal personer ryms inte i rummet
--
-- Utcheckning före incheckning:
--   INSERT INTO booking (room_id, customer_id, check_in, check_out, guests)
--   VALUES (15, 1, '2026-12-14', '2026-12-12', 2);
--   -- ERROR 3819: Check constraint 'chk_booking_dates' is violated.
-- =====================================================================
