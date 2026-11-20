# 🦌 Vigdals Mørningsguide

> Berekn døgngrader for mørning av hjort og anna vilt ved hjelp av
> historiske temperaturdata frå Meteorologisk institutt.

![.NET](https://img.shields.io/badge/.NET-10-blueviolet) ![ASP.NET
Core](https://img.shields.io/badge/ASP.NET_Core-MVC-blue)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED) ![Raspberry
Pi](https://img.shields.io/badge/Raspberry_Pi-Hosted-C51A4A)
![License](https://img.shields.io/badge/License-MIT-green)

---

## Design

- Haustpalett med dempa rustraud, varm grå og skogsgrøn/oliven som aksent.
- Flate kort med tydelege kantlinjer og meir firkanta hjørne, utan gradientar,
  glass-effektar eller store skuggar.
- Mørk brun footer med god kontrast, kompakt oppsett på mobil.
- Fargeval og spacing ligg i `wwwroot/css/site.css`; ingen endringar i
  utrekningar eller JavaScript.

## Funksjonar

- 🦌 Reknar ut døgngrader for optimal mørning
- Hentar historiske temperaturar frå Frost API
- Brukar historiske temperaturar frå Shelly som valbar lokal målar
- Raudt frostvarsel ved fersk Shelly-måling under 0 °C eller minusgrader
  i MET-prognosen dei neste 48 timane (30 minutt prognosecache).
  Når ein vel ein målestasjon, blir prognosen henta for koordinatane til
  den valde stasjonen. Ved Shelly blir Loftesnes brukt som utandørsindikasjon,
  ikkje som ei måling inne i skålen.
- Eiga kjelde «Kjøleskap» reknar heile perioden ved standard 4 °C, eller
  ein annan temperatur mellom 0 og 15 °C (strengt større enn 0).
  Det eksisterande valet «Lagt i kjøleskap» for overgang frå utandørs
  mørning blir verande og brukar framleis fast 4 °C.
- 🐳 Docker-basert drift
- HTTPS med Let's Encrypt
- Automatisk deploy frå GitHub
- Health endpoint (`/healthz`)

---

# Arkitektur

```text
                    GitHub
                       │
                       ▼
           git push til main
                       │
                       ▼
      Cron (kvart 5. minutt på Pi)
                       │
                       ▼
      deploy-vigdals-morningsguide.sh
                       │
      ┌────────────────┼────────────────┐
      │                │                │
 git fetch      dotnet publish   docker compose
      │                │                │
      └────────────────┴────────────────┘
                       │
                       ▼
             Docker Container
                       │
                       ▼
                  ASP.NET Core
                       │
                       ▼
             Nginx Reverse Proxy
                       │
         HTTP → HTTPS (Let's Encrypt)
                       │
                       ▼
 https://morningsguide.duckdns.org
```

---

# Teknologistakk

Teknologi Bruk

---

ASP.NET Core MVC (.NET 10) Webapplikasjon
Docker & Docker Compose Containerisering
Raspberry Pi Hosting
Nginx Reverse Proxy
DuckDNS Dynamisk DNS
Let's Encrypt Gratis SSL
Frost API Historiske temperaturdata
Shelly lokal tempmåling

---

# Lokal utvikling

```bash
git clone https://github.com/<brukar>/VigdalsMorningsguide.git
cd VigdalsMorningsguide

docker compose up --build
```

Eller:

```bash
dotnet user-secrets set "Frost:ClientId" "<client-id>"
dotnet user-secrets set "Shelly:BaseUrl" "https://shelly-xx-eu.shelly.cloud"
dotnet user-secrets set "Shelly:DeviceId" "<device-id>"
dotnet user-secrets set "Shelly:AuthKey" "<auth-key>"
dotnet run
```

Ikkje legg Shelly-nøkkelen i `appsettings.json` eller i Git.

## Utrekningsmodell

- Døgngrader blir integrerte med faktisk tid i UTC, slik at norske
  sommar- og vintertidsdøgn blir rekna som høvesvis 23 og 25 timar.
- Temperaturen blir halden i det nominelle måleintervallet. Små avvik i
  rapporteringstid blir tolererte, men manglande punkt fyller ikkje lange
  hol i historikken.
- Periodar med lågare datadekning enn kjelda sitt minstekrav blir viste,
  men blir ikkje lagde til døgngradtotalen.
- Etter det registrerte kjøleskapstidspunktet blir fast 4 °C brukt.
  Dagen blir delt i ein målt periode før tidspunktet og ein
  kjøleskapsperiode etter tidspunktet.
- Temperatur under 0 °C gir 0 døgngrader. Den målte
  gjennomsnittstemperaturen blir likevel vist.
- Dersom Shelly berre leverer minimum og maksimum for eit intervall,
  blir middelverdien brukt som eit uttrykkeleg merkt estimat.

Køyr dei frittståande regresjonstestane utan ekstra testpakkar:

```bash
dotnet run --project CalculationTests/VigdalsMorningsguide.CalculationTests.csproj
```

---

# Produksjon

Det er ingen manuelle deploy-steg.

```bash
git add .
git commit -m "Ny funksjon"
git push
```

Pi-en oppdagar nye commits automatisk og:

1.  Hentar siste kode
2.  Publiserer med `dotnet publish`
3.  Byggjer Docker-image
4.  Startar containeren på nytt
5.  Verifiserer `/healthz`

---

# 🔐 HTTPS

Prosjektet brukar:

- DuckDNS
- Let's Encrypt (Certbot)
- Nginx Reverse Proxy
- Automatisk HTTP → HTTPS

---

# Health Check

    GET /healthz

Respons:

```json
{
  "status": "healthy",
  "service": "VigdalsMorningsguide"
}
```

---

# Prosjektstruktur

```text
VigdalsMorningsguide/
│
├── Controllers/
├── Models/
├── Services/
├── Views/
├── wwwroot/
├── docker-compose.yml
├── Dockerfile
└── README.md
```

---

# Planlagde funksjonar

- [x] Lokal temperatur og luftfuktigheit frå Shelly
- [x] Shelly som temperaturkjelde i mørningskalkulatoren
- [x] Valg om kjøleskapsmørning
- [ ] Infoside om mørningsguide med anbefalingar
- [ ] Varsling når kjøtet er ferdigmørna via e-post
- [ ] PWA/mobilstøtte
- [ ] Plan om korleis skalere ut målarar til lokale jegrar

---

# Lisens

MIT License

---
