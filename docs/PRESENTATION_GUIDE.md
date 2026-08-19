# Presentationsguide

## TL;DR

Jag har byggt tre Azure Functions som jobbar tillsammans. Ingest tar emot JSON, Process mappar och validerar produkten och gör om den till XML, och Deliver skickar samma XML vidare till mottagaren. RabbitMQ kopplar isär stegen och gör att felaktiga produkter kan hanteras separat utan att stoppa resten av flödet.

## Så uppfyller jag kraven

| Krav | Min lösning |
|---|---|
| Azure Functions med .NET/C# | Jag använder .NET 10 med isolated worker |
| Minst tre Functions | Ingest, Process och Deliver |
| Minst en icke-HTTP-trigger | Process och Deliver använder RabbitMQ-trigger |
| Ta emot produktdata | Ingest tar emot `POST /api/products` |
| Validera data | JSON-kontroll, affärsvalidering och XSD-validering |
| Transformera data | Extern JSON → kanonisk modell → XML |
| Publicera resultatet | Deliver skickar XML till mottagarens API |
| Hantera felaktig data separat | Felaktiga produkter skickas som JSON till invalid-kön |
| Loggning | Jag loggar steg, status, korrelations-ID och produkt-ID |
| Köra lokalt | Azurite, RabbitMQ, Function App och mockmottagaren |

## Frågor och svar

### Varför delade du upp lösningen på detta sätt?

Jag ville frikoppla systemen så att det blir enkelt att koppla på fler källor senare. Om ett nytt system skickar samma JSON-format kan det använda den befintliga ingressen. Om det i stället skickar till exempel CSV eller använder ett annat protokoll kan jag lägga till en ny ingressadapter som översätter datan till det format som Process redan förväntar sig.

Sedan kan jag återanvända samma validering, XML-transformering och Deliver-funktion. Det är en stor fördel med ett kanoniskt format: flera olika inflöden kan använda samma väg till slutsystemet utan att jag behöver bygga om hela integrationen.

Jag valde att lägga alla tre Functions i samma Function App eftersom detta är en mindre uppgift och blir enklare att köra, konfigurera och deploya. I en större produktionslösning hade jag kunnat dela upp dem i tre Function Apps om jag behöver oberoende skalning, deployment, behörigheter eller bättre felisolering.

### Varför valde du dessa triggers?

Jag valde en HTTP-trigger för Ingest eftersom det externa systemet skickar datan till en endpoint. Där gör jag en snabb kontroll av att bodyn innehåller läsbar JSON. Felaktig eller tom JSON får HTTP 400, medan läsbar JSON accepteras med HTTP 202 och valideras vidare asynkront.

RabbitMQ-triggern känns naturlig för Process och Deliver eftersom jag har frikopplat stegen med köer. RabbitMQ lagrar meddelandena tills en Function kan hantera dem och triggar funktionen när ett meddelande kommer in. Det ger mig även buffring, acknowledgements och återförsök vid tekniska fel.

### Hur skulle du skala lösningen?

Lösningen kan köras med flera workers. Då ansluter flera consumers till samma RabbitMQ-kö och RabbitMQ delar ut meddelandena mellan dem.

I Azure hade jag valt en plan som stöder RabbitMQ-triggern, till exempel Elastic Premium, aktiverat Runtime Scale Monitoring och sedan övervakat ködjup och meddelandeålder. Då kan fler workers startas när belastningen ökar.

Just nu ligger alla Functions i samma Function App och delar workers och resurser. I produktion hade jag övervägt tre separata Function Apps eftersom stegen har olika belastning. Ingest hanterar snabba HTTP-anrop, Process gör mer CPU-arbete med mapping och XML, och Deliver måste begränsas efter vad mottagar-API:t klarar.

Med separata appar kan jag skala Process utan att samtidigt skala Deliver. Jag kan även deploya ett steg utan att påverka de andra, använda olika RabbitMQ-behörigheter och låta Ingest och Process fortsätta om Deliver har problem. Nackdelen är mer infrastruktur, fler inställningar, identiteter, pipelines och mer drift. Därför valde jag en gemensam Function App för uppgiften, men hade delat upp den när det finns ett faktiskt produktionsbehov.

### Hur skulle du monitorera den i produktion?

Jag skulle använda Application Insights för att följa hela flödet med korrelations-ID. Jag vill kunna se fel, valideringsresultat, exekveringstid, antal återförsök och hur lång tid mottagar-API:t tar på sig.

För RabbitMQ hade jag övervakat:

- hur många meddelanden som ligger i köerna;
- hur gamla de äldsta meddelandena är;
- Ready och Unacked;
- hur många consumers som är anslutna;
- disk- och minneslarm;
- hur många meddelanden som hamnar i en framtida DLQ.

Jag hade satt alerts på till exempel växande köer, många valideringsfel, återkommande leveransfel och meddelanden som ligger för länge. Jag hade främst loggat metadata och felinformation, inte alla kompletta payloads.

### Hur skulle du hantera högre volymer?

Jag hade skalat upp antalet workers och justerat concurrency och RabbitMQ prefetch utifrån hur mycket som ligger i köerna. Jag hade även lasttestat hela flödet, inte bara ingress-API:t.

Samtidigt vill jag inte starta hur många Deliver-workers som helst om mottagar-API:t är långsamt. Då använder jag backpressure, vilket betyder att jag begränsar hur snabbt Deliver får anropa mottagaren. Överskottet får ligga kvar säkert i RabbitMQ tills mottagaren hinner med.

Jag hade också gjort leveransen idempotent så att samma meddelande kan skickas igen utan att skapa dubbla produkter. Vid mycket högre publiceringsvolym hade jag även sett över fler separat ägda RabbitMQ-kanaler i stället för att låta allt gå genom en kanal per worker.

### Vilken del hjälpte AI dig med?

Jag använde AI som mitt huvudsakliga implementationsstöd och AI har skrivit merparten av grundkoden. Jag gav Codex uppgiftsfilen och beskrev hur jag ville bygga arkitekturen med Ingest, Process och Deliver, hur köerna skulle användas och var validering och felhantering skulle ske.

Jag styrde även om lösningen så att JSON transformeras till den kanoniska modellen och sedan till XSD-validerad XML redan i Process. Deliver ska bara skicka samma XML vidare.

AI hjälpte mig även att skapa Docker Compose-filen för RabbitMQ på min hemmaserver, felsöka, skriva tester och gå igenom dokumentationen. Jag har ändrat vissa delar själv och gått igenom, testat och sett till att jag förstår all kod.

### Vad skulle du förbättra med ytterligare en dags arbete?

Jag hade först satt upp en teknisk DLQ med felmetadata, loggning, alerts och en kontrollerad replay-funktion. Då försvinner inte meddelanden efter sista återförsöket och det blir möjligt att rätta felet och köra om dem.

Jag hade också gjort leveransen idempotent så att duplicerade meddelanden inte spelar någon roll. Om tiden räckte hade jag lagt till fler integrationstester, Key Vault-hantering och Azure Workbooks för datan i Application Insights.

## Demoplan

Jag använder ett eget `X-Correlation-Id` för varje anrop så att jag enkelt kan följa meddelandet genom loggarna.

### 1. Giltig produkt

Jag skickar denna produkt:

```json
{
  "productId": "DEMO-VALID-001",
  "name": "Hultafors Hammer",
  "price": 249.90,
  "currency": "SEK",
  "stockQuantity": 25,
  "category": "Tools"
}
```

Förväntat resultat:

```text
HTTP 202
→ JSON hamnar i received-kön
→ Process mappar och validerar produkten
→ Process skapar och XSD-validerar XML
→ rå XML hamnar i valid-kön
→ Deliver skickar samma XML till mottagaren
```

### 2. Affärsmässigt felaktig produkt

```json
{
  "productId": "DEMO-INVALID-001",
  "name": "Invalid Hammer",
  "price": -10,
  "currency": "GBP",
  "stockQuantity": -2,
  "category": "Tools"
}
```

Jag förväntar mig HTTP 202 eftersom detta fortfarande är läsbar JSON. Process hittar sedan tre affärsfel: negativt pris, fel valuta och negativt lagersaldo. Ett `INVALID_PRODUCT`-meddelande hamnar i invalid-kön och produkten skickas inte till mottagaren.

Efter detta skickar jag en giltig produkt igen för att visa att den felaktiga produkten inte blockerade resten av flödet.

### 3. Obligatoriska fält saknas

```json
{
  "productId": null,
  "name": "   ",
  "price": 99.50,
  "currency": "eur",
  "stockQuantity": 0,
  "category": null
}
```

Jag förväntar mig HTTP 202 och sedan valideringsfel för produkt-ID och namn i invalid-kön. Detta visar också normaliseringen: `eur` blir `EUR`, blanksteg blir null och lagersaldo noll är tillåtet.

### 4. Felaktigt formaterad JSON

Detta är avsiktligt inte giltig JSON eftersom det finns ett extra kommatecken:

```text
{
  "productId": "DEMO-BROKEN-001",
  "name": "Broken JSON",
}
```

Jag förväntar mig direkt HTTP 400 med `MALFORMED_JSON`. Ingenting ska läggas i RabbitMQ.

### 5. Tekniskt fel hos mottagaren

Jag stoppar mockmottagaren och skickar den giltiga produkten igen. Ingest svarar fortfarande HTTP 202 och Process skapar giltig XML, men Deliver misslyckas när den försöker anropa mottagaren.

RabbitMQ-triggern gör då återförsök. Efter den femte misslyckade exekveringen avvisas meddelandet. Eftersom lösningen ännu inte har en teknisk DLQ försvinner det då, vilket är den första saken jag hade förbättrat.

Efter demonstrationen startar jag mottagaren igen.

