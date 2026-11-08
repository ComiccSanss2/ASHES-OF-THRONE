# Pipeline asset & texture — look PS2 dark fantasy

## Modellazione in Blender

**Budget poligonale (era PS2)**
| Oggetto | Triangoli |
|---|---|
| Personaggio giocabile / nemico | 2.000 – 4.500 |
| Boss | 5.000 – 8.000 |
| Prop piccolo (torcia, lapide) | 50 – 300 |
| Modulo architettonico (muro 8 m, colonna) | 200 – 1.200 |

**Forme**: niente primitive "nude". Ogni cubo va smussato (bevel a 1 segmento), i bordi superiori dei muri spezzati,
le colonne a 6–8 lati con plinto e capitello, i tronchi piegati. Suddividi le superfici grandi (griglia ~0.5–1 m) e
sposta leggermente i vertici (*Randomize* 1–3 cm) per dare peso: pietra consumata, legno marcio, metallo ammaccato.
Shading **flat** o con *Auto Smooth* a 30° — mai smooth integrale su pietra.

**Vietato**: neon, colori saturi, materiali PBR lucidi, elementi moderni/sci-fi.

**Scala e orientamento**: 1 unità = 1 m, personaggi alti ~1.8 m, origine ai piedi, fronte verso −Y in Blender
(diventa +Z in Unity con l'export sotto). Applica scala e rotazione (Ctrl+A) prima di esportare.

**Export FBX** — usa `Tools/blender_ps2_export.py` (o manualmente):
- Apply Scalings: *FBX Units Scale* · Forward: *−Z Forward* · Up: *Y Up* · Apply Transform ✓
- Solo Selected ✓ · Mesh: Smoothing *Face* · Armature: *Add Leaf Bones* ✗
- Animazioni: *Bake Animation* ✓, NLA strips o All Actions; chiama le azioni come gli stati
  (es. `attack_light_1`, `roll`, `strafe_left`) per mapparle nel controller.

**In Unity (Import Settings del modello)**: Rig *Generic* (o *Humanoid* se vuoi retargeting), Animation Type coerente,
*Bake Into Pose* per la rotazione/Y delle clip "in place", Materials *None* (usa i materiali del progetto).

## Texture

**Risoluzione**: 128–256 px per prop e moduli, 256–512 px per personaggi e boss. Mai 2K/4K.

**Palette desaturata** (saturazione < 35%): grigi pietra `#4a4744 → #6e6a62`, marroni `#33281f → #5a4632`,
verde palude `#2f3a28 → #4a5236`, ruggine `#5a3a22`, ombre mai nere pure (`#151312`). Gli accenti caldi
(fuoco `#c46a2a`) solo su fonti di luce.

**Regole di pittura** (come fa `Editor/Builder/TextureGen.cs`):
1. Base a rumore a bassa frequenza (variazione di tono per blocco/asse).
2. **Ambient occlusion dipinta**: giunti e fessure scuri al 40–55%, bordi smussati scuriti verso l'interno.
3. Dettaglio ad alta frequenza leggero (grana 10–20%), crepe sottili, muschio dal basso, macchie d'umidità.
4. Quantizza i colori (~24 livelli per canale) per la sensazione 16-bit.
5. **Tiling**: rumore periodico (wrap) o offset 50% + timbro clone sui giunti.
6. Niente foto: se parti da una foto, riducila a 64–128 px, posterizza, ridipingi le ombre.

**Import in Unity**: Filter *Bilinear* (autentico PS2) o *Point* (più "crunchy"), Mip Maps ✓, Compression *None* o *High
Quality*, Max Size 256/512. Usa il materiale `Universal Render Pipeline/Simple Lit` con riflessi ambientali spenti, smoothness ≤ 0.1.

**Generare nuove texture**: aggiungi una funzione in `TextureGen.cs` (vedi `StoneBricks`, `Flagstones`, `RustMetal`)
e un `LitMat(...)` nel builder; rilancia *Costruisci Tutto*.

## Post-processing (riepilogo)

Risoluzione interna 448p → VHS shader (aberrazione 1.2 px, chroma bleed 0.6, 32 livelli/canale + Bayer 4×4,
scanline 0.35, vignetta 0.35) → Volume URP (Neutral tonemapping, saturazione −38, contrasto +16, split toning
ombre verde palude / luci marrone, grana Medium3). Tutti i parametri sono sul materiale `Generated/Materials/VHS_Screen`
e sul profilo `Generated/Rendering/DarkFantasyVolume`.
