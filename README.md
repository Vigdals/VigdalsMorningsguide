# 🦌 Vigdals Mørningsguide

> Berekn døgngrader for mørning av hjort og anna vilt ved hjelp av
> historiske temperaturdata frå Meteorologisk institutt.

![.NET](https://img.shields.io/badge/.NET-10-blueviolet) ![ASP.NET
Core](https://img.shields.io/badge/ASP.NET_Core-MVC-blue)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED) ![Raspberry
Pi](https://img.shields.io/badge/Raspberry_Pi-Hosted-C51A4A)
![License](https://img.shields.io/badge/License-MIT-green)

---

## Funksjonar

- 🦌 Reknar ut døgngrader for optimal mørning
- Hentar historiske temperaturar frå Frost API
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
dotnet run
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

- [x] Lokal temperatur og luftfuktigheit frå Shelly eller ESP32 (delvis implementert)
- [x] Valg om kjøleskapsmørning
- [ ] Infoside om mørningsguide med anbefalingar
- [ ] Varsling når kjøtet er ferdigmørna via e-post
- [ ] PWA/mobilstøtte
- [ ] Plan om korleis skalere ut målarar til lokale jegrar

---

# Lisens

MIT License

---
