# Vigdals mørningsguide

Ei enkel ASP.NET Core MVC-løysing som reknar ut døgngrader for mørning av viltkjøt.

Løysinga brukar temperaturmålingar frå [Frost API](https://frost.met.no/) og tek utgangspunkt i målestasjonen **RV5 Loftesnes** (`SN55709:0`).

## Funksjonar

- Registrering av dato og klokkeslett for oppheng
- Norsk datoformat og 24-timarsklokke
- Valfritt mål for døgngrader
- 80 døgngrader som standard
- Utrekning basert på historiske temperaturmålingar
- Estimert tid att til valt døgngradmål
- Oversikt over temperatur og døgngrader per dag
- Varsling dersom datagrunnlaget er mangelfullt
