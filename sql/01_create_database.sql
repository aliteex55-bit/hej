-- =====================================================================
-- Holiday Maker – skapar databasen
-- =====================================================================
-- Kör hela skriptet i MySQL Workbench, eller från en terminal:
--
--     mysql -u root -p --default-character-set=utf8mb4 < 01_create_database.sql
--
-- Kör därefter 02_testdata.sql.
--
-- VARNING: skriptet börjar med att ta bort en befintlig databas som
-- heter holiday_maker, så att det alltid går att börja om från början.
--
-- Ordningen är: 1. tabeller, 2. triggers, 3. användare. Användaren
-- skapas sist eftersom det är det enda steget som beror på hur just
-- din MySQL-server är inställd.
-- =====================================================================

SET NAMES utf8mb4;

DROP DATABASE IF EXISTS holiday_maker;
CREATE DATABASE holiday_maker
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_swedish_ci;
USE holiday_maker;

-- =====================================================================
-- 1. TABELLER
-- =====================================================================

-- ---------------------------------------------------------------------
-- Boende. Pool, restaurang, avstånd och omdöme hör till boendet och
-- inte till det enskilda rummet – annars skulle samma värden upprepas
-- på varje rum i huset.
-- ---------------------------------------------------------------------
CREATE TABLE accommodation (
    id                INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    name              VARCHAR(100)     NOT NULL,
    city              VARCHAR(60)      NOT NULL,
    has_pool          BOOLEAN          NOT NULL DEFAULT FALSE,
    has_restaurant    BOOLEAN          NOT NULL DEFAULT FALSE,
    beach_distance_m  INT UNSIGNED     NOT NULL,
    center_distance_m INT UNSIGNED     NOT NULL,
    rating            TINYINT UNSIGNED NOT NULL,
    PRIMARY KEY (id),
    INDEX ix_accommodation_city (city),
    CONSTRAINT uq_accommodation_name_city UNIQUE (name, city),
    CONSTRAINT chk_accommodation_rating   CHECK (rating BETWEEN 1 AND 5)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- Rum. Kapacitet och pris per natt hör till rummet. Rumsnumret är unikt
-- inom boendet, men samma nummer får finnas på olika boenden.
-- Tas ett boende bort följer dess rum med (CASCADE) – men bara om inget
-- av rummen har bokningar, se booking nedan.
-- ---------------------------------------------------------------------
CREATE TABLE room (
    id               INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    accommodation_id INT UNSIGNED     NOT NULL,
    room_number      VARCHAR(10)      NOT NULL,
    capacity         TINYINT UNSIGNED NOT NULL,
    price_per_night  DECIMAL(8,2)     NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT uq_room_number        UNIQUE (accommodation_id, room_number),
    CONSTRAINT fk_room_accommodation FOREIGN KEY (accommodation_id)
        REFERENCES accommodation (id) ON DELETE CASCADE,
    CONSTRAINT chk_room_capacity     CHECK (capacity >= 1),
    CONSTRAINT chk_room_price        CHECK (price_per_night > 0)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- Kund. E-postadressen är unik, så samma kund kan inte registreras två
-- gånger av misstag.
-- ---------------------------------------------------------------------
CREATE TABLE customer (
    id         INT UNSIGNED NOT NULL AUTO_INCREMENT,
    first_name VARCHAR(60)  NOT NULL,
    last_name  VARCHAR(60)  NOT NULL,
    email      VARCHAR(120) NOT NULL,
    phone      VARCHAR(30)  NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT uq_customer_email UNIQUE (email)
) ENGINE = InnoDB;

-- ---------------------------------------------------------------------
-- Bokning. En bokning gäller en kund och ett rum.
--
-- Totalpris lagras INTE. Det räknas ut som antal nätter × rummets pris
-- per natt när det visas, så att det aldrig kan säga emot datumen.
--
-- Ett rum som har bokningar kan inte tas bort (RESTRICT). Tas en kund
-- bort försvinner kundens bokningar (CASCADE) och rummen blir lediga.
--
-- Indexet på (room_id, check_in, check_out) är det som sökningen efter
-- lediga rum använder.
-- ---------------------------------------------------------------------
CREATE TABLE booking (
    id          INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    room_id     INT UNSIGNED     NOT NULL,
    customer_id INT UNSIGNED     NOT NULL,
    check_in    DATE             NOT NULL,
    check_out   DATE             NOT NULL,
    guests      TINYINT UNSIGNED NOT NULL,
    created_at  DATETIME         NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX ix_booking_room_dates (room_id, check_in, check_out),
    CONSTRAINT fk_booking_room     FOREIGN KEY (room_id)
        REFERENCES room (id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_customer FOREIGN KEY (customer_id)
        REFERENCES customer (id) ON DELETE CASCADE,
    CONSTRAINT chk_booking_dates   CHECK (check_out > check_in),
    CONSTRAINT chk_booking_guests  CHECK (guests >= 1)
) ENGINE = InnoDB;

-- =====================================================================
-- 2. TRIGGERS
--
-- Två regler går inte att skriva som CHECK, eftersom ett CHECK-villkor
-- bara ser den egna raden:
--
--   a) antalet personer måste rymmas i rummet   (en annan tabell: room)
--   b) perioden får inte krocka med en annan bokning  (andra rader)
--
-- De ligger därför som triggers. Då håller databasen ihop även om någon
-- skriver direkt i en databasklient. Applikationen kontrollerar samma
-- två saker innan den sparar, och kan då ge ett vänligare meddelande.
--
-- Två perioder krockar när den ena börjar före den andras slut OCH
-- slutar efter den andras början. Jämförelserna är strikta (< och >),
-- så en gäst kan checka in samma dag som den förra checkar ut.
-- =====================================================================
DELIMITER $$

CREATE TRIGGER trg_booking_before_insert
BEFORE INSERT ON booking
FOR EACH ROW
BEGIN
    DECLARE v_capacity  TINYINT UNSIGNED;
    DECLARE v_conflicts INT;

    SELECT capacity INTO v_capacity FROM room WHERE id = NEW.room_id;
    IF NEW.guests > v_capacity THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Antal personer ryms inte i rummet';
    END IF;

    SELECT COUNT(*) INTO v_conflicts
      FROM booking
     WHERE room_id   = NEW.room_id
       AND check_in  < NEW.check_out
       AND check_out > NEW.check_in;
    IF v_conflicts > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Rummet har redan en bokning under perioden';
    END IF;
END$$

CREATE TRIGGER trg_booking_before_update
BEFORE UPDATE ON booking
FOR EACH ROW
BEGIN
    DECLARE v_capacity  TINYINT UNSIGNED;
    DECLARE v_conflicts INT;

    SELECT capacity INTO v_capacity FROM room WHERE id = NEW.room_id;
    IF NEW.guests > v_capacity THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Antal personer ryms inte i rummet';
    END IF;

    -- Samma kontroll som vid INSERT, men den egna raden räknas inte.
    SELECT COUNT(*) INTO v_conflicts
      FROM booking
     WHERE room_id   = NEW.room_id
       AND id       <> NEW.id
       AND check_in  < NEW.check_out
       AND check_out > NEW.check_in;
    IF v_conflicts > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Rummet har redan en bokning under perioden';
    END IF;
END$$

DELIMITER ;

-- =====================================================================
-- 3. ANVÄNDARE FÖR APPLIKATIONEN
--
-- Programmet ska inte koppla upp som root. Det får en egen användare
-- som bara kan läsa och ändra rader i den här databasen – inte skapa
-- eller ta bort tabeller.
--
-- Lösenordet nedan är ett exempel. Byter du det här måste du byta det
-- i src/HolidayMaker/config.json också.
--
-- ALTER USER gör att lösenordet gäller även om användaren redan fanns
-- från en tidigare körning.
--
-- Får du ett fel just här (till exempel för att ditt konto inte får
-- skapa användare) är databasen ändå färdig. Ange då ett eget konto
-- i config.json.
-- =====================================================================
CREATE USER IF NOT EXISTS 'holiday_app'@'localhost' IDENTIFIED BY 'Byt_mig_123!';
ALTER USER 'holiday_app'@'localhost' IDENTIFIED BY 'Byt_mig_123!';
GRANT SELECT, INSERT, UPDATE, DELETE ON holiday_maker.* TO 'holiday_app'@'localhost';
