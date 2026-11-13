# Testprotokoll

Testerna är körda 2026-11-11 med MySQL 8.4.9, .NET 10 och Windows 11,
mot testdatat i `sql/02_testdata.sql`. **Alla tester gav väntat resultat.**

Varje punkt i uppgiftens testlista är kontrollerad på två sätt:

- **Genom programmet**, så som operatören använder det.
- **Direkt i databasen**, med SQL i en databasklient. Det visar att det är
  databasen som upprätthåller reglerna, inte bara C#-koden.

Alla tester utgår från nyinläst testdata. Kör `sql/02_testdata.sql` igen
för att komma tillbaka dit. Frågorna som används direkt i databasen finns
samlade i `sql/03_testfragor.sql`.

I testdatat är rum 102 på Hotell Strandkanten i Malmö (rum-id 2, två
bäddar) bokat **10–13 december** och inga andra dagar. De flesta testerna
nedan använder det rummet.

---

## 1. Ett ledigt rum visas och ett upptaget filtreras bort

Rum 101 är bokat 1–5 december. Rum 102 är ledigt de dagarna.

| Steg | Väntat | Utfall |
| --- | --- | --- |
| Val 2: Malmö, 2026-12-01 till 2026-12-05, 2 personer, inga filter | 102 visas, 101 visas inte | Visade 202, **102** och 203. 101 saknades |

## 2. Ett rum kan bokas samma dag som en tidigare kund checkar ut

| Steg | Väntat | Utfall |
| --- | --- | --- |
| Val 2: Malmö, 2026-12-13 till 2026-12-16, 2 personer | 102 visas, fast en gäst checkar ut ur rummet den 13:e | 102 visades |
| Val 3: boka rum-id 2 för samma period | Bokningen går igenom | Bokning 26, 3 nätter × 1 450,00 kr = 4 350,00 kr |
| Val 2: Malmö, 2026-12-07 till 2026-12-10, 2 personer | 102 visas, fast nästa gäst checkar in den 10:e | 102 visades |
| Val 3: boka rum-id 2 för samma period | Bokningen går igenom | Bokning 27, 3 nätter × 1 450,00 kr = 4 350,00 kr |

Gränsen fungerar alltså åt båda hållen. Efter testet var rum 102 bokat
7–10, 10–13 och 13–16 december, utan att någon bokning krockade.

## 3. Överlappande bokningar nekas

**Genom programmet.** Med rum 102 bokat enligt test 2 söktes fem perioder
som var och en överlappar en bokning på sitt eget sätt. Rummet ska inte
erbjudas för någon av dem.

| Sökt period | Typ av överlapp | Väntat | Utfall |
| --- | --- | --- | --- |
| 8–11 december | börjar före en bokning, slutar inuti den | 102 visas inte | Visades inte |
| 12–15 december | börjar inuti, slutar efter | 102 visas inte | Visades inte |
| 11–12 december | ligger helt inuti | 102 visas inte | Visades inte |
| 6–17 december | **omsluter** bokningarna helt | 102 visas inte | Visades inte |
| 10–13 december | exakt samma period | 102 visas inte | Visades inte |
| 16–18 december | börjar när sista bokningen slutar | 102 visas | Visades |
| 5–7 december | slutar när första bokningen börjar | 102 visas | Visades |

**Kontrollen när bokningen sparas.** Uppgiften kräver att tillgängligheten
kontrolleras en gång till vid sparandet. Det testades så här: en sökning
i Visby 10–13 december visade rum 602 som ledigt. Medan listan stod kvar
på skärmen lades en bokning av rum 602 in direkt i databasen, som om en
annan operatör hade hunnit före. Därefter valdes rum 602 i programmet.

| Väntat | Utfall |
| --- | --- |
| Bokningen nekas och ingenting sparas | "Rummet har hunnit bli bokat sedan sökningen gjordes. Ingen bokning gjord." Ingen bokning sparades för kunden |
| Nästa sökning visar inte rummet | Rum 602 saknades i listan |

**Direkt i databasen.** Samma fem överlapp provades som `INSERT` mot
rum 102, bokat 10–13 december. Triggern ska stoppa dem.

| Period | Väntat | Utfall |
| --- | --- | --- |
| 8–11 december | nekas | `ERROR 1644: Rummet har redan en bokning under perioden` |
| 12–15 december | nekas | `ERROR 1644`, samma meddelande |
| 11–12 december | nekas | `ERROR 1644`, samma meddelande |
| 8–15 december (omsluter) | nekas | `ERROR 1644`, samma meddelande |
| 10–13 december | nekas | `ERROR 1644`, samma meddelande |
| 13–16 december | godkänns | Raden sparades |
| 7–10 december | godkänns | Raden sparades |

De två sista raderna är lika viktiga som de fem första: de visar att
villkoret inte är för strängt.

## 4. Ett för stort sällskap kan inte boka rummet

| Steg | Väntat | Utfall |
| --- | --- | --- |
| Val 2: Visby, 2026-12-10 till 2026-12-12, 8 personer | Inga rum – det största rummet i Visby tar 4 | "Inga lediga rum matchar sökningen." |
| Val 2: Göteborg, 2026-12-10 till 2026-12-13, 2 personer | Bara rum med plats för minst 2 | Fem rum, alla med kapacitet 2 eller mer |
| Direkt i databasen: `INSERT` med 4 gäster i rum 1, som rymmer 2 | nekas | `ERROR 1644: Antal personer ryms inte i rummet` |

Programmet erbjuder alltså aldrig ett för litet rum, och databasen säger
nej om någon försöker gå förbi programmet.

## 5. Kombinerade sökfilter och båda sorteringsalternativen

Alla sökningar gäller 2 personer.

| Sökning | Väntat | Utfall |
| --- | --- | --- |
| Göteborg 10–13 dec, pool | Bara Hotell Hamnkrogen | 302, 303, 304 |
| Göteborg 10–13 dec, pool + högst 100 m till strand | Inga rum (Hamnkrogen ligger 2 500 m bort) | Inga lediga rum |
| Göteborg 10–13 dec, pool + restaurang + högst 200 m till centrum | Hamnkrogen (150 m) | 302, 303, 304 |
| Göteborg 10–13 dec, högst 500 m till centrum | Hamnkrogen, inte vandrarhemmet (700 m) | 302, 303, 304 |
| Malmö 14–16 dec, restaurang + högst 500 m till strand | Bara Strandkanten (150 m) | 101, 102, 103 |
| Visby 10–13 dec, högst 300 m till centrum | Bara Pensionat Ringmuren (200 m) | 601, 602, 603 |
| Malmö 14–16 dec, **lägst pris först** | Stigande pris | 1 450, 1 450, 1 600, 2 200 kr |
| Malmö 14–16 dec, **högst omdöme först** | Omdöme 5 före omdöme 4 | Strandkantens tre rum (5), sedan Solgården (4) |

Vid samma omdöme kommer det billigaste rummet först.

## 6. Ändring av antal personer följer rummets kapacitet

Bokning 26 i rum 102, som rymmer 2 personer.

| Steg | Väntat | Utfall |
| --- | --- | --- |
| Val 5: bokning 26, nytt antal 4 | nekas | "Går inte: rum 102 tar högst 2 personer. Bokningen är oförändrad." |
| Val 5: bokning 26, nytt antal 1 | går igenom | "Bokning 26 gäller nu 1 person." |
| Val 4 | Bokningen visar 1 person | 1 person |
| Direkt i databasen: `UPDATE booking SET guests = 9 WHERE id = 1` | nekas | `ERROR 1644: Antal personer ryms inte i rummet` |

## 7. Avbokning frigör rummet

| Steg | Väntat | Utfall |
| --- | --- | --- |
| Val 3: boka rum 102 för 13–16 december | Bokning skapas | Bokning 26 |
| Val 2: Malmö 13–16 dec, 2 personer | 102 visas inte | Visades inte |
| Val 6: avboka bokning 26, svara j | Bekräftelse | "Bokning 26 är avbokad. Rum 102 är ledigt igen 2026-12-13 till 2026-12-16." |
| Val 2: samma sökning igen | 102 visas | Visades |

## 8. Uppgifterna finns kvar efter att applikationen startats om

| Steg | Väntat | Utfall |
| --- | --- | --- |
| Välj kund 5, boka rum 602 i Visby 10–13 december, avsluta med val 0 | Bokningsnummer visas | Bokning 26, 2 670,00 kr |
| Starta programmet igen, välj kund 5, val 4 | Bokning 26 finns kvar | Fanns kvar, samma datum och pris |
| Stäng av och starta om hela MySQL-servern, starta programmet, val 4 | Bokning 26 finns kvar | Fanns kvar |

Det sista steget går utöver uppgiften. Det visar att uppgifterna ligger
på disk och inte bara i minnet hos ett program som råkar vara igång.

---

## Felaktig inmatning avslutar inte programmet

Allt nedan skrevs in i programmet. Varje gång kom ett meddelande och
frågan ställdes om, utan att programmet avslutades.

| Inmatning | Programmets svar |
| --- | --- |
| Menyval `9`, `abc`, `-1` | "Skriv ett heltal mellan 0 och 6." |
| Val 3, 4, 5 eller 6 utan vald kund | "Välj en kund först (val 1)." |
| Ort `Stockholm` | "Det finns inga boenden på orten "Stockholm"." |
| Ort nummer `7` | "Listan har nummer 1 till 3." |
| Incheckning `2026-11-30` eller `2027-01-01` | "Rum kan bara bokas under december 2026." |
| Datum `2026-12-32` eller `10/12/2026` | "Skriv datumet som ÅÅÅÅ-MM-DD." |
| Utcheckning samma dag som eller före incheckning | "Utcheckning måste ligga efter incheckning." |
| Utcheckning `2027-01-02` | "…utcheckning kan senast vara 2027-01-01." |
| Antal personer `0`, `abc`, `100` | "Skriv ett heltal mellan 1 och 99." |
| `kanske` på en ja/nej-fråga | "Svara j för ja eller n för nej." |
| Avstånd `-5` eller `abc` | "Skriv ett heltal mellan 0 och 1000000, eller tryck bara Enter…" |
| Rum-id som inte finns i träfflistan | "Skriv ett rum-id ur listan ovan…" |
| Bokningsnummer som tillhör en annan kund | "…har ingen bokning med det numret." |
| Ny kund utan förnamn | "Fältet får inte vara tomt." |
| Förnamn på 500 tecken | "Högst 60 tecken (du skrev 500)." |
| E-post utan @, med mellanslag eller med två @ | "En e-postadress ska ha formen namn@adress…" |
| Telefon `abc` | "Ett telefonnummer får bara innehålla siffror…" |
| Ny kund med en e-post som redan finns | "Det finns redan en kund med den e-postadressen. Ingen ny kund sparades." |
| `avbryt` mitt i en sökning | Tillbaka till menyn |

Dessutom stängdes MySQL-servern av medan programmet stod i menyn.
Programmet svarade "Databasen svarade med ett fel … Åtgärden genomfördes
inte" och fortsatte köra. När servern startats igen fungerade samma
menyval, med samma kund vald.

## Försök till SQL-injektion

| Inmatning | Väntat | Utfall |
| --- | --- | --- |
| Förnamn `Robert'); DROP TABLE booking;--` | Sparas som ett vanligt namn | Kunden sparades med exakt den texten som förnamn. Tabellen `booking` fanns kvar med alla 25 rader |

Serverns logg (`general_log`) visar varför det inte går. För en sökning
i Visby tog servern emot två anrop:

```
Prepare  SELECT ... WHERE a.city = ? AND r.capacity >= ? AND (? = 0 OR a.has_pool = 1) ...
Execute  SELECT ... WHERE a.city = 'Visby' AND r.capacity >= 2 AND (1 = 0 OR a.has_pool = 1) ...
```

I det första anropet kommer frågan med frågetecken som platshållare. Då
tolkar servern den färdigt. Värdena kommer först i det andra anropet, och
fylls i som data. (Loggen skriver ut satsen med värdena insatta för att
den ska gå att läsa.) Det som operatören skriver kan därför aldrig bli en
del av själva SQL-koden.

## Reglerna i databasen

Varje rad nedan kördes som en egen SQL-sats direkt i databasen. Alla
nekades, och antalet rader var oförändrat efteråt.

| Försök | Regel som stoppade det |
| --- | --- |
| Utcheckning före incheckning | `CHECK chk_booking_dates` |
| Bokning med 0 gäster | `CHECK chk_booking_guests` |
| Rum med kapacitet 0 | `CHECK chk_room_capacity` |
| Rum med pris 0 | `CHECK chk_room_price` |
| Boende med omdöme 0 eller 7 | `CHECK chk_accommodation_rating` |
| Negativt avstånd till strand | Datatypen `INT UNSIGNED` |
| Bokning utan utcheckningsdatum | `NOT NULL` |
| Två rum med samma nummer på samma boende | `UNIQUE uq_room_number` |
| Två boenden med samma namn på samma ort | `UNIQUE uq_accommodation_name_city` |
| Två kunder med samma e-post, även med versaler | `UNIQUE uq_customer_email` |
| Ta bort ett rum som har bokningar | Främmande nyckeln `fk_booking_room` (RESTRICT) |
| Ta bort ett boende vars rum har bokningar | Samma främmande nyckel |
| Bokning för en kund som inte finns | Främmande nyckeln `fk_booking_customer` |
| Flytta en bokning så att den krockar med en annan | Triggern `trg_booking_before_update` |
| `DROP TABLE` som programmets användare `holiday_app` | Behörigheten: `ERROR 1142, DROP command denied` |

## E/R-diagrammet mot databasen

Diagrammets 25 kolumner jämfördes med databasens `information_schema`,
kolumn för kolumn. Namn, ordning och datatyp stämde för alla 25, liksom
de fyra primärnycklarna och de tre främmande nycklarna.
