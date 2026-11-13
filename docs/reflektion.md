# Reflektion

> **Utkast – ta bort den här rutan före inlämning.** Reflektionen bedöms
> individuellt och ska vara dina egna ord och slutsatser. Texten nedan
> beskriver de val som faktiskt är gjorda i lösningen, och varje
> sakpåstående är kontrollerat mot koden och databasen. Läs igenom, stryk
> det du inte håller med om och skriv om resten med dina egna ord.
> Det sista avsnittet, om AI, måste du skriva själv.

## Datamodellen

Databasen har fyra tabeller: `accommodation`, `room`, `customer` och
`booking`. Ett boende har många rum, ett rum har många bokningar och en
kund har många bokningar.

Den viktigaste uppdelningen är den mellan boende och rum. Pool,
restaurang, avstånd och omdöme är egenskaper hos boendet. Hade de legat
på rummet skulle samma uppgifter ha upprepats på varje rum i huset, och
en ändring av omdömet hade behövt göras på flera rader som då kunde
hamna i otakt. Kapacitet och pris per natt hör däremot till rummet,
eftersom de skiljer sig mellan rum i samma hus.

Totalpriset lagras inte. Det räknas ut som antal nätter gånger rummets
pris per natt varje gång det visas. Ett lagrat totalpris hade kunnat
säga emot datumen, och då hade databasen innehållit två svar på samma
fråga.

Orten är en kolumn på boendet och ingen egen tabell. Uppgiften beskriver
inga egna uppgifter om en ort, så en ort-tabell hade bara innehållit ett
namn. Priset för det valet är att samma ort kan stavas olika på två
boenden. Behövdes fler uppgifter om orten skulle jag bryta ut den.

Omdömet är ett heltal mellan 1 och 5. Uppgiften säger "ett värde mellan
1 och 5", och ett heltal räcker för att sortera på. Med decimaler hade
kolumnen behövt vara `DECIMAL(2,1)`.

Villkor som bara gäller den egna raden ligger som `CHECK`: omdöme 1–5,
kapacitet minst 1, pris över 0, minst en gäst och utcheckning efter
incheckning. `UNIQUE` finns på kundens e-post, på boendets namn inom en
ort och på rumsnumret inom ett boende. Samma rumsnummer får alltså
finnas på två olika hotell, men inte två gånger i samma hus.

Två regler går inte att skriva som `CHECK`, eftersom de gäller andra
rader eller en annan tabell: att sällskapet ryms i rummet och att
perioden inte krockar med en annan bokning. De ligger som triggers. Jag
ville att databasen skulle hålla ihop även om någon skriver direkt i en
databasklient. Nackdelen är att samma två regler nu finns på två ställen,
i triggern och i C#-koden, och måste ändras på båda om de ändras.

Raderingsreglerna följer samma tanke. Ett rum som har bokningar kan inte
tas bort (`RESTRICT`), och därmed inte heller dess boende. Tas en kund
bort följer kundens bokningar med (`CASCADE`), eftersom en bokning utan
kund inte betyder något.

## Tolkningar av uppgiften

"Alla rum kan bokas under december 2026" har jag tolkat som att
bokningsbara nätter är 1–31 december 2026. Programmet tar därför bara
emot incheckning de dagarna. Eftersom utcheckningsdagen inte upptar
rummet kan utcheckningen som senast vara den 1 januari 2027. Perioden
står som konstanter på ett ställe i `App.cs`. I databasen finns ingen
sådan gräns, eftersom den är en uppgift om säsongen och inte om hur en
bokning är uppbyggd.

Val 3–6 i menyn gäller den kund som är vald. Vid ändring och avbokning
visas kundens bokningar, och operatören väljer ur den listan. Då behöver
bokningsnumret inte kommas ihåg, och en felskrivning kan inte träffa en
annan kunds bokning.

## Tillgänglighetssökningen

Ett rum är ledigt om det inte finns någon bokning av rummet som krockar
med den sökta perioden. En bokning krockar när den börjar före periodens
slut och slutar efter periodens början:

```sql
b.check_in < @checkOut AND b.check_out > @checkIn
```

Jämförelserna är strikta, och det är hela poängen. En gäst som checkar ut
den 13:e hindrar inte den som checkar in den 13:e. Samtidigt fångar det
enda villkoret alla sätt som två perioder kan överlappa på, även när den
ena helt omsluter den andra.

Kontrollen görs två gånger: i sökningen, och en gång till i samma
transaktion som sparar bokningen. Rummets rad låses först med
`FOR UPDATE`. Kommer två bokningar av samma rum samtidigt får den andra
vänta, och ser sedan den förstas bokning. Uppgiften kräver bara en
operatör åt gången, men den andra kontrollen krävs uttryckligen, och utan
låset hade den inte varit värd något.

Filtren är skrivna som `(@requirePool = 0 OR a.has_pool = 1)`. Samma
fråga klarar därför alla kombinationer av filter, och SQL-texten behöver
aldrig byggas ihop av strängar.

## Säkerhet

Allt som operatören skriver skickas till databasen som parametrar, aldrig
som en del av SQL-texten. Varje fråga med parametrar förbereds dessutom
med `PrepareAsync`. Servern får då först frågan med platshållare och
tolkar den färdigt, och därefter värdena för sig. Ett värde kan därför
inte bli SQL-kod, vad som än skrivs in. Det är kontrollerat i serverns
logg, och genom att registrera en kund med förnamnet
`Robert'); DROP TABLE booking;--`. Namnet sparades som text och tabellen
fanns kvar.

Sorteringen går inte att skicka som parameter på vanligt sätt, eftersom
`ORDER BY` är en del av frågans uppbyggnad. Operatörens val översätts
därför till en siffra som styr ett `CASE`-uttryck i `ORDER BY`. Text från
operatören hamnar aldrig där.

Programmet kopplar upp som `holiday_app` och inte som root. Den
användaren får läsa och ändra rader i den här databasen men inte skapa
eller ta bort tabeller. Ett `DROP TABLE` som den användaren nekas av
servern.

Anslutningen är krypterad med TLS, vilket är drivrutinens standardläge
när servern erbjuder det. Det som återstår är att serverns certifikat
inte kontrolleras och att lösenordet står i klartext i `config.json`.
För en lokal skoluppgift är det rimligt. I skarp drift hade lösenordet
legat utanför projektmappen och certifikatet kontrollerats.

## Resultat

Alla sex funktionerna i uppgiften fungerar, och sökning, filtrering och
sortering görs av databasen. Felaktig inmatning leder till att frågan
ställs om, inte till att programmet avslutas. Programmet klarar också att
databasen stängs av medan det kör.

Samtliga punkter i uppgiftens testlista är kontrollerade, både genom
programmet och direkt i databasen. Resultaten står i
`docs/testprotokoll.md`.

Testningen hittade fel som inte syntes när man bara läste koden:

- Om en ändring av antal personer lyckats avgjordes först av hur många
  rader `UPDATE`-satsen ändrade. Men MySQL räknar inte en rad som ändrad
  när det nya värdet är samma som det gamla. Nu läses rummets kapacitet
  ut och jämförs uttryckligen.
- Å, ä och ö i testdatat blev fel när skripten kördes med `mysql < fil`
  på Windows. Skripten inleds därför med `SET NAMES utf8mb4`.
- Lösenordet till programmets användare klarade inte MySQL:s
  lösenordspolicy. På en server där den är påslagen hade skriptet stannat
  mitt i.
- Bokstäver som ø och č ändrades tyst till o och c när ett kundnamn
  skrevs in i ett vanligt konsolfönster, eftersom fönstrets teckentabell
  saknar dem. "Søren" sparades som "Soren". Programmet läser nu
  tangentbordet som Unicode, och namnet sparas som det skrevs.

## Möjliga förbättringar

- **Byta rum och datum i en bokning.** Det ingår inte i grunduppgiften,
  men är det en operatör skulle be om först. Krockkontrollen finns redan
  och går att återanvända.
- **Bokningsbar period i databasen.** Nu står december 2026 i koden. En
  tabell med säsonger hade gjort att perioden kan ändras utan att
  programmet byggs om.
- **Pris per säsong.** Priset per natt är detsamma hela perioden. En
  pristabell per rum och datumintervall vore mer verklighetsnära, men gör
  uträkningen av totalpriset svårare.
- **Historik.** En avbokad bokning försvinner helt. Ett statusfält hade
  bevarat historiken, men då måste varje fråga om lediga rum komma ihåg
  att bortse från avbokade rader.
- **Automatiska tester.** Testerna i protokollet körs för hand. Just
  överlappen är sådant som borde kontrolleras automatiskt varje gång
  koden ändras.

## Hur jag har använt AI och hur jag har kontrollerat resultatet

> **Skriv det här avsnittet själv, och ta sedan bort rutan.** Det är ett
> krav i uppgiften, och det ska beskriva vad som faktiskt hände.
>
> Fakta att utgå från – så här har AI-assistenten (Claude) använts i det
> här projektet:
>
> - Den föreslog datamodellen och skrev SQL-skripten, C#-koden, README,
>   testprotokollet och utkastet till den här reflektionen.
> - Den körde testerna: genom programmet, direkt i databasen och med ett
>   eget testprogram som jämförde sökningen mot ett facit.
> - Den hittade och rättade felen som räknas upp under Resultat.
>
> Det du själv behöver göra, och sedan beskriva här:
>
> - Kör igenom testprotokollet på egen hand och se att du får samma svar.
> - Läs `Repository.cs` tills du kan förklara varje fråga utan att läsa
>   innantill – särskilt krockvillkoret och varför jämförelserna är strikta.
> - Kör `sql/03_testfragor.sql` i en databasklient och ändra i värdena.
> - Skriv vad du ändrade, vad du inte förstod först, och vad du lärde dig.
>
> Vid redovisningen ska du kunna förklara relationerna,
> tillgänglighetssökningen och de parameteriserade anropen.
