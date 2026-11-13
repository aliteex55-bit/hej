-- =====================================================================
-- Holiday Maker – testdata
-- =====================================================================
-- Kör efter 01_create_database.sql:
--
--     mysql -u root -p --default-character-set=utf8mb4 < 02_testdata.sql
--
-- Innehåll:
--     6 boenden på 3 orter
--    22 rum med 4 olika kapaciteter (1, 2, 4 och 6 personer)
--     5 kunder
--    25 bokningar i december 2026, mellan 2 och 8 nätter långa
--
-- Skriptet tömmer tabellerna först och går därför att köra om när som
-- helst för att få tillbaka utgångsläget.
--
-- Varje bokning passerar triggern i databasen när den läggs in. Att
-- skriptet går igenom är alltså i sig ett bevis på att ingen av de 25
-- bokningarna krockar med en annan eller har för många gäster.
-- =====================================================================

-- Gör att å, ä och ö kommer in rätt även om klienten har en annan
-- teckenuppsättning som standard.
SET NAMES utf8mb4;

USE holiday_maker;

SET FOREIGN_KEY_CHECKS = 0;
TRUNCATE TABLE booking;
TRUNCATE TABLE room;
TRUNCATE TABLE customer;
TRUNCATE TABLE accommodation;
SET FOREIGN_KEY_CHECKS = 1;

-- --------------------------------------------------------------- boenden
-- Priser, omdömen, avstånd och faciliteter varierar, så att varje filter
-- och båda sorteringarna ger olika resultat.
INSERT INTO accommodation
    (id, name, city, has_pool, has_restaurant, beach_distance_m, center_distance_m, rating)
VALUES
    (1, 'Hotell Strandkanten',      'Malmö',     TRUE,  TRUE,   150,  900, 5),
    (2, 'Pensionat Solgården',      'Malmö',     FALSE, TRUE,  1200,  300, 4),
    (3, 'Hotell Hamnkrogen',        'Göteborg',  TRUE,  TRUE,  2500,  150, 4),
    (4, 'Vandrarhem Lilla Bommen',  'Göteborg',  FALSE, FALSE, 3000,  700, 2),
    (5, 'Visby Strandhotell',       'Visby',     TRUE,  TRUE,    80, 1100, 5),
    (6, 'Pensionat Ringmuren',      'Visby',     FALSE, FALSE, 1800,  200, 3);

-- ------------------------------------------------------------------- rum
INSERT INTO room (id, accommodation_id, room_number, capacity, price_per_night) VALUES
    ( 1, 1, '101', 2, 1450.00),
    ( 2, 1, '102', 2, 1450.00),
    ( 3, 1, '103', 4, 2200.00),
    ( 4, 1, '104', 1,  950.00),
    ( 5, 2, '201', 1,  700.00),
    ( 6, 2, '202', 2, 1050.00),
    ( 7, 2, '203', 4, 1600.00),
    ( 8, 3, '301', 2, 1250.00),
    ( 9, 3, '302', 2, 1250.00),
    (10, 3, '303', 4, 1950.00),
    (11, 3, '304', 6, 2600.00),
    (12, 4, '401', 1,  450.00),
    (13, 4, '402', 2,  650.00),
    (14, 4, '403', 4,  900.00),
    (15, 4, '404', 4,  900.00),
    (16, 5, '501', 2, 1800.00),
    (17, 5, '502', 4, 2500.00),
    (18, 5, '503', 1, 1200.00),
    (19, 6, '601', 2,  890.00),
    (20, 6, '602', 2,  890.00),
    (21, 6, '603', 4, 1300.00),
    (22, 6, '604', 1,  620.00);

-- ----------------------------------------------------------------- kunder
INSERT INTO customer (id, first_name, last_name, email, phone) VALUES
    (1, 'Anna',   'Lindqvist', 'anna.lindqvist@example.com', '070-111 22 33'),
    (2, 'Björn',  'Ek',        'bjorn.ek@example.com',       '070-222 33 44'),
    (3, 'Carina', 'Nyberg',    'carina.nyberg@example.com',  '070-333 44 55'),
    (4, 'David',  'Olsson',    'david.olsson@example.com',   '070-444 55 66'),
    (5, 'Elin',   'Sandberg',  'elin.sandberg@example.com',  '070-555 66 77');

-- -------------------------------------------------------------- bokningar
-- Bokning 2 börjar samma dag som bokning 1 slutar (rum 101, den 5:e),
-- och bokning 12 samma dag som bokning 11 slutar (rum 301, den 9:e):
-- utcheckningsdagen upptar inte rummet.
INSERT INTO booking (id, room_id, customer_id, check_in, check_out, guests) VALUES
    ( 1,  1, 1, '2026-12-01', '2026-12-05', 2),
    ( 2,  1, 2, '2026-12-05', '2026-12-10', 2),
    ( 3,  1, 3, '2026-12-18', '2026-12-22', 1),
    ( 4,  2, 4, '2026-12-10', '2026-12-13', 2),
    ( 5,  3, 5, '2026-12-03', '2026-12-08', 4),
    ( 6,  3, 1, '2026-12-20', '2026-12-27', 3),
    ( 7,  4, 2, '2026-12-11', '2026-12-14', 1),
    ( 8,  5, 3, '2026-12-02', '2026-12-04', 1),
    ( 9,  6, 4, '2026-12-09', '2026-12-16', 2),
    (10,  7, 5, '2026-12-21', '2026-12-28', 4),
    (11,  8, 1, '2026-12-05', '2026-12-09', 2),
    (12,  8, 2, '2026-12-09', '2026-12-12', 2),
    (13,  9, 3, '2026-12-24', '2026-12-31', 2),
    (14, 10, 4, '2026-12-01', '2026-12-06', 4),
    (15, 10, 5, '2026-12-15', '2026-12-19', 3),
    (16, 11, 1, '2026-12-23', '2026-12-30', 6),
    (17, 12, 2, '2026-12-07', '2026-12-09', 1),
    (18, 13, 3, '2026-12-12', '2026-12-15', 2),
    (19, 14, 4, '2026-12-18', '2026-12-26', 4),
    (20, 16, 5, '2026-12-02', '2026-12-09', 2),
    (21, 16, 1, '2026-12-27', '2026-12-31', 2),
    (22, 17, 2, '2026-12-10', '2026-12-17', 4),
    (23, 19, 3, '2026-12-04', '2026-12-07', 2),
    (24, 21, 4, '2026-12-13', '2026-12-20', 4),
    (25, 22, 5, '2026-12-29', '2026-12-31', 1);

-- Kontroll: ska visa 6 boenden, 22 rum, 5 kunder och 25 bokningar.
SELECT
    (SELECT COUNT(*) FROM accommodation) AS boenden,
    (SELECT COUNT(*) FROM room)          AS rum,
    (SELECT COUNT(*) FROM customer)      AS kunder,
    (SELECT COUNT(*) FROM booking)       AS bokningar;
