# Ashes of the Throne — Action RPG Dark Fantasy co-op (stile PS2 / Souls-like)

Unity **6000.3 LTS** · URP 17 (Forward+) · **Netcode for GameObjects 2.13** + Unity Transport 2.7 · Input System 1.20

---

## 1. Avvio rapido

1. Apri il progetto `D:\DarkFantasy` in Unity 6.3.
2. Menu **Dark Fantasy ▸ Build Everything (playable scene)**.
   Genera in `Assets/DarkFantasy/Generated/` texture, materiali, mesh, Animator, prefab e la scena
   `Assets/DarkFantasy/Scenes/DarkFantasy.unity` (aperta e messa per prima nelle Build Settings).
   Si può rieseguire quando vuoi: rigenera tutto da zero.
3. **Play** → nel menu clicca **Create Lobby → Open the Lobby → Begin the Journey** (tutto il gioco è in inglese). Sei al falò; le rovine e i guardiani sono davanti, il castello in fondo.

## 2. Test multiplayer (LAN / IP diretto)

| Scenario | Come |
|---|---|
| **Online (consigliato)** | *Create Lobby* con **Visibility: Online (everyone)** → *Open the Lobby*. Chiunque abbia il gioco la vede in **Find Lobbies** e entra con un clic. Passa da Unity Relay: **nessuna porta da aprire**, nessun IP da scambiare. |
| Due PC in LAN | Come sopra, oppure Visibility *LAN / IP only*: la lobby appare in *Find Lobbies* sulla stessa rete (beacon UDP 47777). |
| IP diretto (riserva) | Visibility *LAN / IP only*; gli amici usano *Join by IP* con IP pubblico + porta della stanza, e l'host inoltra quella porta **UDP** sul router. |
| Due istanze sullo stesso PC | **Dark Fantasy ▸ Build Windows Player (multiplayer test)**, avvia `DarkFantasy.exe` e crea una lobby online; nell'Editor *Find Lobbies* e cliccala. Da riga di comando: `DarkFantasy.exe -online "Nome"` apre subito una lobby online. |

Le lobby online usano Unity Gaming Services (pacchetto `com.unity.services.multiplayer`, progetto cloud già collegato:
Lobby + Relay + login anonimo). Il codice è in `Scripts/Network/OnlineLobby.cs`.

**Lobby**: chi crea sceglie nome, numero di cavalieri (1-4) e porta. Nella stanza si vedono i membri ([Host], "(you)"),
le info da condividere e il pulsante **Begin the Journey** (solo l'host). Chi entra dopo l'inizio viene messo
direttamente in partita.

**Avvio rapido senza menu**: `DarkFantasy.exe -host` oppure `DarkFantasy.exe -join 192.168.1.10` (opzionale `-port 7777`).
Nella build c'è anche `autohost.txt` accanto all'eseguibile: finché esiste, l'exe parte direttamente come host
(comodo per testare con l'Editor come client). Cancellalo per tornare al menu normale.

Massimo 4 giocatori (modificabile su `NetworkManager ▸ NetworkBootstrap ▸ Max Players`).
La finestra della build è ridimensionabile e gira anche in background, così puoi tenere due istanze affiancate.

## 3. Comandi

| Azione | Tastiera / Mouse | Gamepad |
|---|---|---|
| Muovi | WASD / frecce | Levetta sinistra |
| Camera | Mouse | Levetta destra |
| Corsa (tieni) / Schivata (tocca) | Shift / Spazio | B / Cerchio |
| Attacco leggero (combo da 3) | Tasto sinistro | R1 |
| Attacco pesante (tieni = carica) | F | R2 |
| Parata con lo spadone (tieni) | Tasto destro | L1 |
| Parry (con lo spadone) | Q | L2 |
| Lock-on / cambio bersaglio | Rotellina (click) / scatto del mouse a sx-dx | R3 / levetta dx |
| Classifica (tieni) | Tab | Select + L1 |
| Rianima un alleato a terra | E vicino a lui | A / Croce |
| A terra: arrenditi e torna al fuoco (tieni) | Spazio | Y / Triangolo |
| Fiaschetta (4 cariche, +50% vita) | R | X / Quadrato |
| Interagisci (cancello di nebbia) | E | A / Croce |
| Inventario (personaggio, armi, armature, pozioni) | I | Select |
| Albero delle competenze | K | — |
| Accovacciati / alzati | Ctrl o C | L3 |
| Pozioni (Iron Draught / Vigor Tonic / Soul Salve) | 2 / 3 / 4 | — |
| Ultimate (non bardo) | 1 | Croce su |
| Pausa · Guida comandi | Esc · F1 | Start |
| Schermo intero | F11 | — |

**Arciere** (stessi tasti, ruoli diversi): tasto sinistro / R1 = *Quick Shot* (spammabile, la catena salta il gesto
verso la faretra) · F / R2 tenuto = *Power Shot* caricato (fino a +110% danni, più spinta e postura) · tasto destro / L1
tenuto = **mira** sopra la spalla (camera ravvicinata, cammina piano, il primo tiro parte già incoccato e più rapido).
L'arciere non para e non fa parry (Q non fa nulla): si difende con distanza e capriole.

**Bardo** (mago di supporto, usa il **mana** — barra blu sotto la stamina, si ricarica da sola): tasto sinistro / R1 =
*Arcane Bolt* (dardo magico che insegue, cadenza più lenta dell'arciere) · F / R2 = *Triade* (3 dardi a ventaglio).
Incantesimi sui tasti **1 2 3 4** (tastierino o croce direzionale sul gamepad), visibili in basso al centro con icona,
nome, effetto, costo e ricarica:

| Tasto | Incantesimo | Effetto | Mana · Ricarica |
|---|---|---|---|
| 1 | Mending Hymn | cura te e gli alleati entro 9 m (30% vita), note verdi | 35 · 14 s |
| 2 | War Anthem | aura dorata per 15 s su te e alleati entro 10 m: +25% danni, −18% danni subiti, stamina più rapida | 40 · 24 s |
| 3 | Dirge of Ruin | sfera viola a ricerca: maledice il bersaglio 12 s (+30% danni subiti, −30% velocità), anche i boss | 25 · 14 s |
| 4 | Crescendo | onda sonora sul punto mirato (16 m): 46 danni ad area + tanta postura | 45 · 16 s |

Gli effetti attivi si vedono su tutti: aura dorata e icona sotto la barra degli alleati, teschio viola sopra il
nemico maledetto (e accanto alla barra del boss). Reliquia del bardo: *Voice of the Pyre* (mana −20%, dardi più veloci).

## 4. Cosa c'è (mappa delle richieste)

**Classi giocabili: Cavaliere o Arciere** — scelte nel menu (*Class*, ricordata tra le sessioni) e modificabili nella
stanza della lobby (il pulsante *Class* e la colonna accanto a ogni nome). All'avvio il server crea per ognuno il
personaggio della sua classe (`NetworkBootstrap.ServerSpawnPlayer`, classe replicata in `LobbyState`); chi entra a
partita iniziata entra con la propria classe.
- *Modello* (`Art/Archer.fbx`, `Tools/archer.blend`, `Tools/archer_anim.py`, texture `Tools/archer_texture.py`):
  il cavaliere Solus senza cresta e spallacci, con cappuccio e maschera di stoffa (si vede solo la fessura degli occhi),
  mantellina, mantello sfrangiato, faretra con frecce sulla schiena, bracciale sul braccio dell'arco.
- *Arco ricurvo* nella mano sinistra; ossa `BowSocket/BowTop/BowBottom` (arco) e `NockSocket` (mano destra) esportate
  con le animazioni. La corda (`ArcherBow`) è tesa tra le punte e segue la mano destra quando tende; la freccia incoccata
  compare solo tra l'incocco e il rilascio. Scricchiolio della tensione e schiocco del rilascio sono sincronizzati con il clip.
- *Animazioni* (`BW_*`): guardia con l'arco basso, camminata, corsa, `BW_Shot` (alza, incocca al frame 8, tende fino alla
  mascella, rilascia al 15), `BW_Charged` (tensione piena al frame 24, tenuta mentre carichi, rilascio al 26),
  `BW_Ready` (arco alzato e freccia incoccata, livello superiore mentre miri camminando). Colpito/caduta dal pacchetto.
- *Frecce* (`Arrow`): proiettili locali con leggera gravità e scia, sweep con raycast (niente tunnel). Ogni client vola
  la stessa freccia (stessa origine e velocità); solo quella di chi tira applica il danno (`EnemyBase.TakeRangedHitRpc`,
  stesso flusso dei colpi di spada: reazione immediata locale, danno/postura/knockback sul server). Passano attraverso i
  compagni, si conficcano nei muri. Il mirino (`PlayerController.PredictAimPoint`) mostra dove andrà davvero la freccia:
  bersaglio agganciato, altrimenti un leggero aiuto alla mira vicino al mirino, altrimenti ciò che tocca il centro.
  In tiro "dall'anca" la freccia parte qualche grado sopra la linea della camera (che guarda un po' in basso).
- *I guardiani* colpiti da lontano vanno dall'arciere che li ha colpiti (non dal giocatore più vicino) e non
  contrattaccano a vuoto se sei lontano.
- Bilanciamento: 110 HP (cavaliere 130), Quick Shot 12 danni ~0,42 s, Power Shot 30→63 danni, armatura più leggera.

**Ricompensa del boss: forziere e reliquie** — `Core/RewardChest.cs`, `Player/Inventory.cs`, `Core/RelicDisplay.cs`,
modelli in Blender `Tools/relic_models.py` / `Tools/relics.blend` → `Art/Relics/*.json` (letti dal builder).
- Qualche secondo dopo la morte di The Ashen King un forziere emerge dal pavimento davanti al trono (server, di rete).
  Chiunque lo apre con **E**: il coperchio si alza con un bagliore e ne escono **una reliquia per giocatore, secondo la
  classe**: tante *Cinder Oath* quanti cavalieri e tante *Thornwraith* quanti arcieri (1 cavaliere → 1 spada;
  2 cavalieri + 1 arciere → 2 spade + 1 arco). Ognuno riceve la reliquia della propria classe.
- *Cinder Oath*: flamberge annerita con un canale di brace incandescente lungo la lama e guardia d'oro a corna.
  +35% danni, +25% postura, scia della lama color brace.
- *Thornwraith*: arco di legno nero e osso con spine, rune azzurre luminose; +35% danni, frecce più veloci, frecce
  con scia infuocata. La corda resta agganciata alle punte (stessi punti di incocco dell'arco base).
- **Inventario: tasto I** (Select sul gamepad): le armi della tua classe, *Equip* per cambiarle. Le reliquie restano
  salvate su questo PC tra una sessione e l'altra; l'arma equipaggiata è replicata, quindi tutti la vedono.
- Test in editor: `msg:Boss(Clone),DevKill,x` (dopo `msg:BossArena(Clone),DevStartFight,x` con il giocatore nell'arena),
  `msg:RewardChest(Clone),DevOpen,x`, `msg:Player_0 (local),DevEquip,1`, `msg:GameUI,DevInventory,1`.

**Animazioni dei compagni sincronizzate** — quando un giocatore finisce o annulla un'azione, gli altri ricevono
`Recover`; in più, se lo stato replicato dice che non sta più attaccando ma sullo schermo degli altri è ancora in un
attacco (messaggio in ritardo, Whirlwind in loop), il personaggio torna alla locomozione da solo dopo 0,35 s.

**Tempo dei clip indipendente dalla velocità** — `CharacterAnimator.GetStateTime` ora usa la durata reale del clip:
`AnimatorStateInfo.length` è divisa per la velocità di riproduzione, quindi durante la carica (0,03×), l'hit-stop (0,02×)
o le pause dei colpi ritardati dei nemici il tempo letto saltava avanti di 30-50 volte. Effetti corretti: il Power Shot
partiva all'inizio della carica; i colpi "trattenuti" dei nemici finivano subito e il controllo di riserva colpiva senza
che la lama si vedesse calare.

Test in editor: `msg:Player_0 (local),DevPose,Attack1;0.46` congela il personaggio su un frame (render delle pose),
`msg:Bow,DevBow,x` scrive lo stato dell'arco.

**Visual PS2 / VHS** — `Scripts/Rendering/ScreenPresenter.cs` + `Shaders/VHS_PS2_Screen.shader`
La camera renderizza in una RenderTexture a 448 righe (filtro Point, niente MSAA) mostrata con lo shader VHS:
wobble del nastro, banda di tracking, aberrazione cromatica, chroma bleed, profondità colore ridotta con
**dither grid Bayer 4×4**, scanlines regolabili, grana, vignettatura CRT e color grading cupo (grigio/marrone/verde palude).
In più un Volume URP (tonemapping, desaturazione −38, split toning verde/marrone, grana, vignetta) e nebbia esponenziale.
VHS e bassa risoluzione si attivano/disattivano dal menu di pausa.

**Camera Souls-like** — `Scripts/Camera/SoulsCamera.cs` (script dedicato, nessun Cinemachine)
Orbita libera 360° con smoothing regolabile · lock-on che inquadra giocatore e nemico · cambio bersaglio con scatto
laterale di mouse/levetta · sblocco automatico se il bersaglio muore, esce di portata o resta fuori vista per 2 s ·
collisione con SphereCast che stringe la distanza in fretta e la rilascia morbida.

**Rete** — `Scripts/Network/NetworkBootstrap.cs` (NGO, host autoritativo)
Lobby (crea / trova in LAN / entra via IP) con `LobbyState` replicato e `LanDiscovery` · approvazione connessioni · IP locale e pubblico nel menu · i nemici, il boss e l'arena sono
autoritativi sul server · il movimento del giocatore usa `NetworkTransform` in modalità Owner (reattivo) ·
animazioni: locomozione via NetworkVariable, azioni via RPC · vita/stamina/nome via NetworkVariable (barre del gruppo in alto a destra).
Le schivate/parate/parry sono risolti dal proprietario (come nei Souls) e il risultato è replicato a tutti.

**Combattimento** — `Scripts/Player/PlayerController.cs`, `Scripts/Combat/*`
**Spadone a due mani** (niente scudo: parata e parry si fanno con la lama).
Combo leggera *Rising Slash → Sweep → Whirlwind* con buffer d'input + finestra di grazia di 0.45 s: anche premendo
in ritardo (spam) la catena prosegue invece di ripartire dal primo colpo; i colpi si concatenano dal frame di
recupero con cross-fade 0.12 s · pesante *Cleave* caricabile (l'animazione si ferma al caricamento, +80% danni, iper-armatura) · schivata con i-frame e stamina ·
parata a stamina con guard break · **parry** con finestra di 0.22 s che manda in stagger (danni ×2 come riposte) ·
postura/stagger su nemici e boss · flinch/atterramento sul giocatore · hitbox a **capsula sull'arma** con sweep tra i frame,
attivabile a tempo o da Animation Event (`HitboxOn`/`HitboxOff`, vedi `AnimationEventRelay`).
Inerzia dell'armatura con `armorWeight`. Movimento a 8 direzioni in lock-on, libero verso la camera altrimenti.

**Sincronizzazione colpi ↔ animazione** — `Editor/Builder/AnimationAnalysis.cs` analizza ogni clip d'attacco
(velocità reale della punta della lama, frame per frame) e ricava inizio/picco/fine del fendente; la presa dello spadone è
scelta da un solver che massimizza il movimento *di taglio* (penalizza le stoccate). A runtime tutto è letto dal tempo
REALE dell'Animator (`CharacterAnimator.GetStateTime`): finestra di danno, scia della lama (`BladeTrail`) e suono.
Chi attacca fa reagire il nemico nello stesso frame del contatto (flinch del busto + hit-stop + sangue/scintille);
gli altri client ricevono l'effetto dal server.

**Animazioni dello spadone (fatte su misura in Blender)** — `Tools/gs_anim.py`, `Tools/knight_greatsword.blend` → `Art/Knight_GS.fbx`
Il pacchetto non aveva un'arma: le sue animazioni "heavy weapon" impugnavano un oggetto invisibile. Il nuovo set è
generato fotogramma per fotogramma su un rig Solus identico:
- presa misurata dai pugni del rig (asse dell'elsa = linea nocca indice → nocca mignolo, filo allineato alle nocche),
  mano destra sotto la guardia, sinistra 19 cm più in basso; braccia e gambe con IK analitico a due ossa;
- i colpi seguono archi di taglio reali attorno al busto (lama radiale, filo che guida il movimento) con
  anticipo, accelerazione, accompagnamento e recupero; anche, busto e testa caricano e scaricano il colpo,
  il piede avanza nei colpi e la testa resta sul bersaglio;
- clip: `GS_Idle` (guardia a due mani), `GS_Walk` (guardia), `GS_Run` (spadone appoggiato sulla spalla destra, piatto sulla spalliera, braccio sinistro libero che oscilla con le gambe), `GS_Attack1`
  (diagonale ascendente), `GS_Attack2` (orizzontale sinistra→destra, si concatena al primo), `GS_Spin` (giro completo,
  in loop per il tornado del boss), `GS_Heavy` (fendente dall'alto: la carica tiene la spada sopra la testa),
  `GS_Parry`, `GS_Block`, `GS_Roar`. Valgono per giocatore, guardiani e boss.
- camminata e corsa non vengono mai mescolate a metà (le braccia finirebbero in pose ibride): sotto ~2,6 m/s si cammina
  in guardia (ciclo accelerato), sopra si corre con la spada in spalla; le velocità sono divise per la taglia del
  personaggio (il boss alto il doppio cammina a grandi passi); da agganciati al bersaglio si resta sempre in guardia.
- la spada in Unity si aggancia all'osso `SwordSocket` esportato con le animazioni; le finestre di colpo sono
  ricalcolate dalla velocità reale della punta. Per rigenerare: apri il .blend, esegui `gs_anim.py`
  (`make_sword()`, `build_all()`, `export(...)`), poi *Build Everything*.

**Audio** — `Scripts/Core/Sfx.cs`, `MusicPlayer.cs`. Rimossi i suoni sintetizzati e il vento di fondo.
Il tuo `a-simple-sword-slash` è avviato in anticipo in modo che il suo picco cada esattamente sul frame del colpo,
con pitch/velocità che seguono la velocità del fendente + variazione casuale (boss più grave, guardiani medi).
Impatti, parry, schivata, grugniti, ruggito, schianti, nebbia, fiaschetta, UI e fuoco dei bracieri dal *Free Sounds Pack*.
`bossfight.mp3` parte (fade-in) all'inizio della bossfight e sfuma alla vittoria/reset.

**Combattimento leggibile (anti-spam)**
- *Knockback*: ogni colpo spinge indietro il bersaglio (pesante caricato = spinta forte; il boss quasi non si sposta).
  Anche il giocatore viene sbalzato quando è colpito, con screen shake proporzionale.
- *Fendenti visibili*: scia della lama più lunga e luminosa (bianca/dorata per il giocatore, rossa per i nemici)
  + striscia di taglio e lampo nel punto d'impatto, orientati nella direzione del colpo.
- *Telegrafi*: ~0.4 s prima del colpo la lama del nemico brilla — **oro = parabile (Q)**, **rosso = non parabile**.
- *Postura*: dopo uno stagger il nemico ha alcuni secondi di "stance" (niente stun-lock infinito); il danno ×2
  vale solo per la **riposte dopo un parry**. Durante i propri attacchi i nemici hanno armatura parziale (scambio colpi).
- *Punizione dello spam*: se lo colpisci più volte di fila il guardiano risponde con un contrattacco a iper-armatura.
  Gli attacchi costano più stamina; senza stamina i fendenti diventano lenti e deboli.
- I guardiani attaccano a turno; dopo un colpo subito hai un attimo di invulnerabilità (niente morte in catena).

**Opzioni** (menu principale e pausa → *Options*): volume generale / musica / effetti, schermo intero (anche **F11**),
VHS, risoluzione PS2, luminosità, intensità dello screen shake, sensibilità, inverti Y. Salvate in PlayerPrefs.
La sala del trono ha luci di riempimento, rim light dalla vetrata ed esposizione automatica all'interno.

**Boss: The Ashen King** — `Scripts/Enemies/BossAI.cs`, `BossVisuals.cs`, `Core/BossFx.cs`, `Combat/AshCrescent.cs`
Le mosse sono *sequenze* di colpi; ogni colpo ha tracking proprio, una carica minima garantita (leggibile)
e, se previsto, un **colpo ritardato** (la lama si ferma in alto, brilla, poi cala veloce) e un evento d'impatto.
- *Fase 1 (Cavaliere di cenere)*: Ashen String (2–3 colpi), Delayed Verdict, Lunging Cleave (accorcia le distanze),
  **Leaping Slam** (salta su di te: un cerchio rosso a terra mostra dove atterra), **Ash Wave** (onda a mezzaluna
  che viaggia a terra: rotolaci attraverso), Turning Sweep (punisce chi gli sta alle spalle).
- *Trasformazione (55% HP)*: barcolla, ruggisce, la spada **prende fuoco**, braci e armatura incandescente, nova di fuoco.
- *Fase 2 (Inferno)*: Inferno String (4 colpi, l'ultimo ritardato ed esplosivo), Triple Fire Wave, Double Leap,
  **Eruption** (colonne di fuoco sotto ogni giocatore, annunciate da cerchi che si riempiono), Inferno Whirlwind
  (tornado di fuoco che insegue + esplosione finale), Earthquake (3 anelli di fuoco consecutivi).
- *Disperazione (<25%)*: pause più brevi, più eruzioni. Scaling HP/postura invariato: base × (1 + (N−1) × 0.6).
**IA "che ragiona"**: legge lo stato replicato del giocatore (attacca, rolla, para, beve, …) e ricorda le abitudini:
se bevi la fiasca ti punisce (carica o combo), se pari spesso usa colpi pesanti/ritardati, se rolli tanto ritarda i fendenti,
se stai lontano a lungo accorcia la distanza. Tra un attacco e l'altro sceglie un ritmo (avvicinarsi, girarti intorno,
arretrare, osservare) e dopo 2-3 attacchi di fila si prende una pausa vera; a volte arretra apposta e poi punisce
la distanza con onde, salti o carica. Se lo martelli fa un balzo indietro (Evade).
**Knockback**: un "flinch meter" si riempie coi danni (≈3 colpi); quando è pieno, e non sta già colpendo, il boss
viene respinto (animazione di dolore + polvere), con 6 s di recupero, quindi capita a volte ma non sempre.
**Carica telegrafata**: prima di caricare ruggisce (glint, polvere, scossa, suono) per ~1 s, poi corre lasciando
polvere e colpisce solo da vicino: niente più "teletrasporti" senza preavviso.
Test in editor: `msg:Boss(Clone),DevHealth,0.5` (vai alla fase 2), `msg:Player_0 (local),DevGod,1` (invulnerabile).

**Livello** — rovine esterne con 5 guardiani corrotti, colonnato, lapidi, alberi morti, erba secca, falò (respawn);
castello con facciata, torri, sala del trono a navata con colonne, torce tremolanti, bracieri, vetrate rotte, stendardi
laceri, tetto crollato da cui filtra la luna.

**UI** — kit *Ravenhollow Lite* (Brunka Games): finestra fleur, pulsanti, campi, slider, checkbox, barre 3-slice,
icone, keycap e cursore, tutti con i 9-slice presi da `manifest.json`. Font Jacquard 12 (titoli) e Jersey 10 (testo).

### Novità: capitolo del castello (aggiornamento)

- **Fuochi / checkpoint** — nella Hall of Chains (e alla Grande Scala, al trono, al falò) c'è un braciere: premi **E**
  per riposare. Recuperi vita, stamina, mana e fiaschette, il punto diventa il tuo respawn e **tutti i nemici normali
  rinascono** (come in Dark Souls; i boss sconfitti restano sconfitti).
- **Riprendi da lì** — ogni fuoco toccato si sblocca in modo permanente sul PC: nella schermata *Create Lobby* il
  pulsante **Start at** permette di partire dalla Hall of Chains o dalla Grande Scala (solo dopo averle raggiunte,
  quindi solo dopo aver battuto il primo boss). Le porte già aperte e il primo boss risultano già superati.
- **Terrazza** — tramonto con cielo dipinto, sole basso che illumina di arancio, braci nell'aria, vento; tutto attorno
  il castello: torri rotonde con guglie e pennoni, il grande mastio a nord-est, il palazzo a ovest, il campanile a est,
  mura merlate, ponti ad archi e un ponte verso due torri di guardia tra cui tramonta il sole; finestre accese.
- **Drago** — la *carica* ora è leggibile: si accuccia e ringhia, poi sul pavimento compare la **corsia** (bordi rossi,
  frecce e dischi che si riempiono quando arriva la testa) e corre dritto senza più correggere la traiettoria; la
  hitbox è tutto il corpo (larga come la corsia). Se sbatte contro un muro resta stordito. Nuovi effetti: il soffio
  lascia **terreno in fiamme** che brucia per qualche secondo, la codata solleva un arco di polvere, gli schianti
  lanciano detriti, in fase 2 lo schianto apre una **fila di eruzioni** verso di te, fumo e braci dalle narici,
  respiro/petto che si muove a riposo, andatura più animale in corsa.
- **Prigioni e tunnel** — l'acqua è a pozzanghere scure e lucide (bordi irregolari, riflessi delle torce con sonde di
  riflessione) visibili anche da lontano; celle aperte senza sbarre davanti alla porta, gabbie appese, catene,
  candele, stendardi, casse; **eco** (Audio Reverb Zone) in tunnel, prigioni e castello.
- **Passi** — suoni del *Free Footsteps Pack* scelti in base al terreno (pietra, terra/erba, ghiaia) e splash nelle
  pozzanghere, sincronizzati con l'appoggio reale dei piedi delle animazioni (velocità e pitch variano con il passo).

### Co-op: a terra, rianimazioni, spettatore, classifica

- **A terra**: se hai alleati in piedi, a vita zero non muori ma cadi a terra per **10 s** (cerchio rosso, timer
  sopra la testa e nel riquadro centrale). Un alleato preme **E** vicino a te: appare un **cerchio di cura**; finché
  un alleato ci resta dentro il timer si ferma e la barra di rianimazione sale (4 s): ti rialzi con il 35% della vita.
  Se esce dal cerchio il timer riprende da dove era. Se sei l'ultimo in piedi muori subito.
- **Tieni Spazio** (1 s) per arrenderti: torni subito all'ultimo fuoco. Se eri nell'arena di un boss durante lo
  scontro, la nebbia **non ti fa più rientrare** finché la battaglia non finisce.
- Se muori dissanguato **nell'arena del boss**, fai da **spettatore** ai compagni ancora vivi (click = cambia alleato)
  finché non vincono o cadono anche loro; poi rinasci al fuoco.
- **Tab (tieni)**: la classifica con nome, classe, arma equipaggiata, uccisioni, morti e rianimazioni.

**Bardo, aggiornamento**: suona un vecchio mandolino (al posto del bastone) e lancia note musicali; le animazioni
sono pizzicati e accordi. I suoni di attacchi e incantesimi usano il mandolino con velocità/intonazione diverse
(nota acuta e veloce per il dardo, accordo più grave per la Triade, cupo e lento per la Dirge…). Icone degli
incantesimi ridisegnate in stile medievale (calice, spada nell'alloro, teschio, campana). Il drago ha la sua musica.

### Armi del drago, temi delle armi, PvP, bilanciamento

- **Il tesoro del drago**: quando Vaelthrax muore, sulla terrazza sorge un forziere bruciato (*Open the wyrm's hoard*):
  una terza arma per classe, da equipaggiare con **I**.
  - *Wyrmfang* (cavaliere): zanna d'ossidiana seghettata con il filo rosso sangue, guardia a teschio di drago con ali
    d'osso, pomolo ad artiglio con gemma di brace. +50% danni, +35% postura; scia della lama rosso fuoco più lunga,
    esplosioni di fuoco dove colpisce, braci che seguono ogni fendente.
  - *Wyrmwing* (arciere): ossa d'ala con membrana cremisi, testa di drago sull'impugnatura. +50% danni, frecce più
    veloci e **infuocate** (scia di fuoco, fiamme, scoppio di fuoco all'impatto).
  - *Vaelthrax's Lament* (bardo): mandolino a scaglie cremisi, manico d'ossidiana, corde di brace, corna e spine.
    +50% danni degli incantesimi, incantesimi −30% mana, **note di fuoco**.
  Ogni arma del drago ha braci e calore attorno a sé finché è equipaggiata.
- **Ogni arma ha il suo tema**: frecce normali → runiche azzurre (Thornwraith) → infuocate (Wyrmwing); note blu →
  dorate (Voice of the Pyre) → di fuoco (Lament); scia della spada bianca → color brace → rosso fuoco lunga.
- **PvP**: nel menu di pausa (**Esc → PvP: OFF/ON**). Solo due giocatori entrambi su ON possono colpirsi (spada,
  frecce, dardi del bardo; danni dimezzati tra giocatori, la parata funziona anche in PvP). Chi è su ON ha il badge
  *PvP ON* sotto le barre, `[PvP]` accanto al nome nel gruppo e nella classifica; le uccisioni PvP contano nella
  colonna KILLS. Un giocatore ucciso in PvP muore subito (niente "a terra").
- **Bilanciamento**: cavaliere = più vita (130), para e fa parry, il DPS più alto ma in mischia; arciere = 110 vita,
  il più mobile, danno costante a distanza (Quick Shot 13, Power Shot 32); bardo = 105 vita, supporto: danno proprio
  più basso (nota 13, Triade 3×9 con più stamina), cure (30%, ogni 14 s) e buff. Le armi relic (+35%) e del drago
  (+50%) danno lo stesso vantaggio a tutte le classi.
- **Filtri VHS/PS2**: non scuriscono più la scena (tinta a luminanza costante, contrasto che non schiaccia i neri,
  scanline compensate): con o senza filtri la luce è la stessa.
- **Tramonto**: il cielo dipinto non viene più "bruciato" dall'esposizione e dalla color grading del castello: sulla
  terrazza c'è una correzione colore dedicata (colori caldi e saturi, bloom arancio) e il disco del sole è netto,
  tra le due torri di guardia (all'inizio il drago, appollaiato, ci sta proprio davanti).
- **Pozzanghere**: bordi morbidi che sfumano nella pietra (niente più quadrati).

Dev menu (F9): *Bard*, *Archer*, *Knight* cambiano classe; *Terrace* porta al drago; *Unlock relic + dragon weapons* sblocca tutte le armi.

### Oltre il ponte: il Bosco di Spine, il Tempio di Vetro e lo Specchio

- **Dopo il drago** si apre il cancello a nord della terrazza: il *Ponte della Rocca*, le due torri di guardia e poi il
  **Ponte Lungo** (circa 100 m) nel tramonto, con le torri del castello tutt'intorno. In fondo c'è la **Torre della Scala**:
  una scala a chiocciola che scende di 32 m (3 giri e mezzo, torce lungo il muro) fino al burrone.
- **Il Bosco di Spine** (*Thornwood*): alberi contorti e spinati, schegge di vetro piantate nel terreno (alcune brillano),
  nebbia viola, fuochi fatui lungo il sentiero. Nemici nuovi:
  - **Shard Wraith**: spettro incappucciato che fluttua, tiene le distanze e lancia ventagli di schegge (il vetro si
    illumina prima della raffica: schiva di lato); da vicino graffia. Fragile.
  - **Glass Revenant**: cavaliere di vetro con schegge che gli orbitano attorno, più resistente dei carcerieri.
- **Il Tempio di Vetro**: pavimento di vetro nero che riflette, colonne spezzate, colonne capovolte che galleggiano,
  vetrate frantumate e schegge sospese dalla magia oscura, particelle che salgono, distorsione leggera dell'immagine:
  un sogno storto. Cancello di nebbia come le altre arene.
- **Shattered Mirror of Malice** (boss): cornice gotica di ferro nero con teschi e spuntoni, vetro incrinato con un
  vortice, due enormi braccia demoniache con crepe di lava, catene e artigli.
  - **Movimento**: fluttua e si trascina sulle mani (le usa come zampe, gomiti piegati in fuori come un ragno), ruota a
    360° sul suo asse magico; negli scatti (*Glide*) le schegge vibrano e lascia fumo.
  - **Attacchi fisici**: *Dark Slam* (mano dall'alto sul punto dove sei, onda d'urto e detriti; avviso a terra),
    *Crushing Embrace* (le mani si chiudono a tenaglia davanti al vetro), *Circular Sweep* (gira su se stesso con una
    mano rasoterra: punisce chi gli gira attorno).
  - **Attacchi magici**: *Reflection of Malice* (spettri pallidi che inseguono: ti **rallentano** o ti **invertono i
    comandi** per qualche secondo), *Shard Hail* (pioggia di schegge a media/lunga distanza, ogni impatto è segnato a
    terra), *Soul Vortex* (solo fase 2: un portale nel vetro risucchia tutti, bisogna correre via; poi implode).
  - **Fase 1**: lento, soprattutto mani e grandine; dopo i colpi pesanti appoggia le mani a terra: è il momento di
    colpire la cornice. **Fase 2 (50%)**: il vetro si frantuma del tutto, più fumo, il doppio delle schegge che gli
    girano attorno, mosse più rapide (×1.22), scie di fuoco nero dalle mani, vortice sbloccato.
  - **Musica**: `mirrorboss.mp3`. In fase 1 ripete il coro cupo (0–64 s), al 50% salta al crescendo orchestrale
    (66 s) e lo ripete fino alla fine dello scontro.
  - Battuto lo Specchio, se il forziere radioso non è già stato trovato, appare nel tempio.
- **Nuovi fuochi**: *The Long Bridge*, *The Thornwood* (ai piedi della torre), *The Glass Temple* (davanti al tempio);
  si possono scegliere come punto di partenza di una nuova lobby, come gli altri.

### Segreti nelle prigioni, pozioni, armi radiose

- **Muri illusori**: la parete ovest dell'*Ossario* e quella est della *Caserma* non esistono. Ci si cammina dentro.
  - A ovest, la **Cripta Dimenticata**: un'iscrizione e, **se i dadi lo vogliono**, il **forziere radioso**.
  - A est, la stanza del **Last Minstrel**, il fantasma di un menestrello (suona quando gli parli).
- **Oswin the Unreflected**: un prigioniero nella cella chiusa del blocco ovest (quella centrale a nord). Avvicinati
  alle sbarre e premi **E**: racconta la storia di Caer Vaelmoor, del re Aldric, della regina Ysolde e dello Specchio
  (e dà due consigli utili). Lore originale, non Dark Souls.
- **Easter egg**: un'anatra di legno nella Cappella Sommersa; una scheggia "che ricorda" nel bosco.
- **Ember Flask** (fiaschette di brace, arancioni e luminose): ogni fiaschetta raccolta aggiunge **+1 carica massima** per il resto della partita
  alle cure. Sono su un sarcofago dell'Ossario, sull'altare sommerso, su una branda della Caserma, sul tavolo della
  stanza delle torture, nelle due stanze segrete e due nel bosco.
- **Armi radiose** (*Dawnbreaker*, *Starwoven*, *Seraph's Citole*): argento lucido e oro, scintille, luce; danno +65%.
  - Il **forziere radioso** ha una **probabilità del 35%**, tirata **quando si crea la lobby**, di trovarsi nella
    Cripta Dimenticata. Come gli altri forzieri dà **un'arma a testa** (quella della propria classe).
  - **F9 → Spawn radiant chest (host)**: comando admin, fa comparire il forziere davanti a te.
- **Persistenza**: le armi ottenute restano tue in ogni lobby (solo o con altri), salvate per computer. Però è
  persistente solo l'**aspetto** e la possibilità di equipaggiarle: il **bonus** si attiva solo dopo aver battuto
  il boss di quel livello **nella lobby corrente** (Re → reliquie, drago → armi del drago, forziere radioso aperto →
  armi radiose). Nell'inventario (I) un'arma "dormiente" lo dice; nella classifica compare *(dormant)*.

### Chat, ping, attacchi annullabili, note più grandi

- **Chat**: **Invio** apre, scrivi, **Invio** invia (Esc chiude). I messaggi compaiono a destra per tutti, anche a chi
  non ha la chat aperta, e svaniscono dopo circa 4 secondi; con la chat aperta si vedono le ultime righe.
- **Ping**: nuova colonna nella classifica (TAB), per te e per gli altri (*host* per chi ospita).
- **Annullare l'attacco**: durante un fendente puoi premere la capriola e il colpo viene interrotto subito.
- **Bardo**: le note musicali sono molto più grandi e visibili.

Dev menu (F9): *Mirror: the long bridge*, *Mirror: the Thornwood*, *Mirror: start the fight*,
*Spawn radiant chest (host)*, *Unlock every weapon*.

### Quarta classe: il Reaper (falce) e le Ultimate

- **Reaper**: il rig del cavaliere in armatura annerita, cappuccio profondo, maschera d'osso con occhi viola, mantello
  a brandelli e tunica lacera. Arma: la **falce**. Animazioni proprie (fatte in Blender, `Tools/reaper_anim.py` →
  `Art/Reaper.fbx`): mietitura orizzontale, fendente ascendente di rovescio, piroetta completa, colpo pesante dall'alto.
  - È più rapido del cavaliere (corsa 4.6 contro 4.1, capriola più lunga e meno costosa, attacchi più veloci) ma ogni
    colpo fa meno danno (14 / 15 / 19, pesante 34 contro 22 / 24 / 30 / 46) e ha meno vita (112 contro 130).
  - Falci per ogni livello: *Gravewarden's Scythe* (base), *Widow of the Pyre* (reliquia del Re: manico nero con fasce
    d'oro, teschio, filo di brace), *Wyrmreaver* (drago: zanna seghettata su un manico di vertebre, braci),
    *Dawnsickle* (radiosa: mezzaluna d'argento su manico dorato, contro-lama, scintille). Escono anche dai forzieri.
  - Nel menu del lobby si sceglie come le altre classi (il bottone della classe ora cicla tra 4).
- **Ultimate (tasto 1 / croce su)** per cavaliere, arciere e reaper (il bardo ha già le sue magie). L'indicatore si
  riempie piano da solo e in fretta facendo danni; quando è pieno l'icona in basso al centro si accende.
  Durante l'ultimate non si viene interrotti (armatura) e si subisce il 40% di danno in meno.
  - **Cavaliere, Cinder Judgement** (icona: spada piantata tra le fiamme): fendente che spacca il terreno, un muro di
    fuoco corre in avanti per 10 m (danno alto e barcollamento).
  - **Arciere, Storm of Arrows** (icona: frecce che cadono su un cerchio): una freccia al cielo, poi una pioggia di
    frecce sul punto mirato per 2 secondi.
  - **Reaper, Harvest of Souls** (icona: falce sopra un vortice di anime): due giri vorticosi che feriscono tutto
    intorno, un salto e una mietitura che schiaccia il terreno. Ogni anima presa cura il reaper (fino al 25%).
- **Drago più giusto con chi combatte da vicino**: il primo morso e la parte vicina della coda ora fanno solo
  barcollare (non ti scaraventano più via); la coda arriva meno spesso (ogni 9 s al massimo, e non subito);
  il pestone scaraventa solo sotto la zampa (fuori si barcolla) ed è meno frequente; dopo morsi, pestoni e colpi
  di coda il drago resta fermo più a lungo, il momento per colpirlo. La spinta quando si viene atterrati è più corta.

### Nomi, PvP, inventario, armature dei boss, competenze, capriola e accovacciamento

- **Nickname sopra i giocatori**: sopra ogni altro giocatore c'è il suo nome (nel colore della classe, rosso se potete
  combattervi), una barretta della vita e la scritta `PvP` quando il duello tra voi due è possibile. Oltre i 45 m sparisce.
- **PvP più giusto**: i colpi tra giocatori fanno il 95% del danno (prima molto meno), ogni colpo fa barcollare per
  un attimo (abbastanza per continuare la combo) con 0.18 s di invulnerabilità per evitare raffiche, e un piccolo
  spintone. Il lock-on (rotellina) ora aggancia anche i giocatori ostili e la mira assistita li considera.
- **Inventario nuovo (I)**, tutto in inglese, in tre colonne:
  - *a sinistra* il tuo personaggio in 3D con l'arma e l'armatura che indossi: **trascina per girarlo, rotellina per
    avvicinarti**; sotto, vita / stamina / mana e danno subito;
  - *al centro* gli slot **Helmet, Armour, Legs, Boots, Weapon** (clic su uno slot = filtra i pezzi di quello slot);
    le anime possedute e il bottone **Skills (K)**;
  - *a destra* la scorta, in categorie: **Usables** (fiaschetta e pozioni), **Weapons**, **Armour**. Ogni casella mostra
    l'oggetto vero (è disegnato dal gioco stesso con il modello che porti), e passandoci sopra col mouse esce una
    scheda con nome, tipo, descrizione ed effetto.
- **Pozioni** (tasti **2 / 3 / 4**, il bardo le beve cliccandole nell'inventario): *Iron Draught* (−30% danni subiti per
  30 s), *Vigor Tonic* (stamina +60% più veloce per 30 s), *Soul Salve* (rigenera il 4.5% di vita al secondo per 10 s).
  Si parte con una per tipo; i nemici ne lasciano a volte, i boss sempre due.
- **Armature dei boss**: ogni boss sconfitto regala a tutto il gruppo un set di 4 pezzi a tema, da indossare e mostrare
  (si vedono su tutti i client): *Regalia of the Ashen King* (piastre annerite con bordi d'oro e corona di brace),
  *Wyrmscale* (scaglie cremisi, corna d'osso, giunture che covano fuoco), *Mirrorborn* (argento lunare e oro, cristalli
  di vetro luminosi, aureola fluttuante e scintille: il più bello), *Deathshroud* (cappuccio d'ombra con teschio dagli
  occhi viola, costole d'osso, catene). Un pezzo protegge davvero (dal 3.5% al 6.5% di danno in meno, max 35%)
  solo se quel boss è stato sconfitto nella lobby in cui sei, altrimenti è solo estetico ("dormant").
  Le piastre sono **generate dalla pelle stessa di ogni personaggio** (la superficie del corpo attorno alle ossa coperte,
  gonfiata di 1–2 cm, con gli stessi pesi delle ossa): combaciano col modello e seguono ogni animazione. Sopra ci sono
  solo piccoli ornamenti (corona, corna, aureola, spallacci, cappuccio).
- **Albero delle competenze (K)**: ogni nemico e ogni boss lascia **anime** (numero in alto a destra, con "+N souls").
  Si spendono in 5 rami: *Vitality* (vita), *Endurance* (stamina e recupero), *Strength* (danno fisico), *Mind* (mana e
  carica dell'ultimate), *Arcana* (danno magico). Il costo sale a ogni livello.
  **Le competenze e le anime tornano a 0** quando entri in una lobby il cui host ha appena iniziato dal falò iniziale;
  armi e armature restano sempre tue.
- **Armi radiose ridisegnate** (più belle di quelle del drago, non più la reliquia ricolorata): *Dawnbreaker* (spadone
  con ali dorate, cuore di vetro luminoso lungo la lama e due aureole che la circondano), *Starwoven* (arco di costole
  d'argento, piume d'oro, stelle di cristallo sulle punte e corda di luce), *Dawnsickle* (mezzaluna di vetro luminoso su
  dorso d'argento, ali dorate e aureola sulla testa della falce), il bastone del bardo porta una corona di luce.
- **Capriola vera**: il personaggio si raccoglie a palla (clip fatto in Blender) e fa un giro completo di 360° a terra
  attorno al proprio centro, poi si rialza e **accovacciamento**
  con **Ctrl** o **C** (L3 sul gamepad): posa da accovacciato, camminata da accovacciato, più lenta; da accovacciato i
  nemici ti notano solo a metà distanza. Correre, attaccare, parare o bere ti fa rialzare.

### Personaggi (4 slot)

- All'avvio il gioco chiede **quale personaggio giocare**: 4 slot. Uno slot vuoto → **Create** (nome + classe; la classe
  resta quella del personaggio). **Play** lo sceglie, **Delete** (due clic) lo cancella.
- Ogni personaggio ha la **sua progressione**: armi e set di armatura ottenuti, armatura equipaggiata, anime e
  competenze, fuochi sbloccati (punto di partenza della lobby). "Change Character" nel menu torna alla scelta.
- Il salvataggio di prima degli slot diventa automaticamente il primo personaggio ("Wanderer", rinominabile nel menu).

### Controlli personalizzabili e icone del joystick

- **F1** (o Pausa → Controls) apre la finestra **Controls**: ogni azione con il suo tasto (tastiera/mouse) e il suo
  tasto del gamepad. **Clicca una casella e premi il nuovo tasto o bottone** (Esc / Start annulla); se il tasto era già
  usato da un'altra azione le due si scambiano. "Reset to defaults" rimette tutto com'era. Si salva subito.
- **Icone del joystick automatiche**: appena tocchi il gamepad, i prompt a schermo (interagisci, ultimate, incantesimi
  del bardo, finestra controlli) mostrano i bottoni veri (A/B/X/Y colorati, LB/RB/LT/RT, L3/R3, croce direzionale; con un
  pad PlayStation diventano ✕ ○ □ △, L1/R1…). Tornando a mouse e tastiera tornano i tasti.
- Arciere: la carica della freccia usa il suono `bow_draw_fast`.

### Il Regno della Luna Rossa e la Morte

- Dopo aver sconfitto lo **Specchio**, la sua cornice vuota diventa un **portale** (vortice rosso dove c'era il
  vetro): avvicinati e premi **E** per attraversarlo. Si arriva nel **Regno della Luna Rossa**, un mondo d'ombra sotto
  un'enorme luna rossa: una piana di cenere, lapidi storte, alberi morti, lanterne di anime azzurre, una cappella in
  rovina. Accanto all'arrivo c'è un nuovo fuoco (**The Red Moon**, nuovo punto di partenza della lobby) e un portale per
  tornare al Tempio di Vetro.
- **Nuovi nemici**: *Mourning Shade* (vedova di fumo che fluttua, lancia anime che ti inseguono, graffia da vicino) e
  *Deathless Sentinel* (cavaliere color osso con un anello di anime, più resistente).
- **Boss finale: Death, the Last Reaper**, il Mietitore alato della cartella `Assets/grim reaper` (l'Alembic è stato
  cotto in Blender in 8 pose del suo volo stazionario, corpo + falce, riprodotte come flipbook; per i colpi tutta la figura
  ruota, si inclina e gira su sé stessa):
  - *movimento*: non tocca mai terra, scivola lasciando una scia di nebbia e anime; si dissolve in fumo e ricompare
    alle tue spalle; si alza in aria per le magie più forti.
  - *Reaper's Slash*: fendente lentissimo e devastante (orizzontale ad ampio arco o verticale lungo una linea) con una
    lama spettrale che allunga la portata; è danno oscuro: **la parata ne ferma solo una parte**.
  - *Infernal Crescent*: giro rapido che libera un **anello di energia oscura che si allarga**: va saltato con una
    capriola al momento giusto (non si para). In fase 2 gli anelli sono due.
  - *Eclipse Lunge*: sparisce e attraversa il bersaglio in un lampo, ricomparendo oltre.
  - *Call of Souls*: una mano scheletrica di nebbia vola verso di te; se ti prende ti solleva, ti prosciuga la vita,
    ti scaglia a terra e ti lascia la maledizione della tomba (rallentato).
  - *Rain of Spectral Scythes*: si alza e fa piovere decine di falci spettrali a schemi (anelli intorno a lui, linee
    verso i giocatori, poi a caccia): i cerchi a terra mostrano dove cadranno.
  - *Veil of Darkness*: ogni tanto svanisce nella nebbia che si infittisce; lo tradiscono solo il **ticchettio di un
    orologio** e il rumore della lama trascinata. Poi riappare alle spalle di qualcuno.
  - *Fase 1, il Mietitore silenzioso*: tattico, fendenti ampi, brevi teletrasporti; **se bevi da lontano ti punisce**
    con una mezzaluna di luce d'anima.
  - *Fase 2, il Giudizio finale (sotto il 40%)*: il mantello brucia e diventa ectoplasma, la falce prende fuoco fatuo
    blu-viola, è più veloce, fa combo più lunghe ed evoca spettri minori.
  - Vincere dà il set **Deathshroud** e chiude la storia ("DEATH IS DEAD").
  - Musica: se metti un file `Assets/reaperboss.mp3` viene usato per lo scontro (altrimenti il tema del Re).

### Build per macOS (da Windows)

1. **Unity Hub ▸ Installs** ▸ sulla 6000.3 l'ingranaggio ▸ **Add modules** ▸ spunta **Mac Build Support (Mono)** e installa.
2. In Unity: **Dark Fantasy ▸ Build macOS Player (from Windows)**. Esce `Builds/macOS/AshesOfTheThrone.app`
   (universale: Mac Intel e Apple Silicon). Da Windows si può usare solo il backend **Mono** (IL2CPP per Mac richiede
   un Mac): il menu lo imposta da solo per la build e poi rimette quello di prima. La cartella
   `..._BurstDebugInformation_DoNotShip` accanto all'app non va distribuita.
3. **Portarla sul Mac**: comprimi la cartella `.app` in **.zip** (o meglio `.tar.gz`, che conserva i permessi).
   Lo zip fatto da Windows perde il permesso di esecuzione: sul Mac, nel Terminale, una volta sola:
   `chmod +x AshesOfTheThrone.app/Contents/MacOS/*`
4. **Prima apertura**: l'app non è firmata, quindi macOS la blocca. Clic destro sull'app ▸ **Apri** ▸ Apri
   (oppure Impostazioni di Sistema ▸ Privacy e sicurezza ▸ "Apri comunque"). Se dice che è "danneggiata":
   `xattr -cr AshesOfTheThrone.app`
5. Multiplayer: Mac e PC giocano insieme con le lobby online (Relay), in LAN o con IP diretto, come due PC; serve solo
   che entrambe le build vengano dallo stesso progetto aggiornato.

### Aree secondarie, arene dei boss uniche, nuovi mostri

- **Aree secondarie** (tunnel nelle pareti della gola, con oggetti `LootPickup`: anime, pozioni, fiasche; ognuno li
  raccoglie una volta per sessione):
  - *Tumulo delle Radici* (parete ovest del Bosco di Spine): nicchie funerarie, radici dal soffitto, una Shard Wraith
    di guardia e **Maelis la Radicata**, la giardiniera della regina cresciuta dentro il muro.
  - *Grotta del Soffiatore* (parete est, vicino al tempio): fornace a fuoco viola, specchi falliti, **Sorella Calyx**,
    la vetraia che creò lo Specchio (dà il suggerimento per il cuore del boss).
  - *Ossario dei Nomi* (Luna Rossa, a est del campo): cripta di teschi, pozza nera, **il Traghettatore che Rimase**
    (lore sul Reaper e su come schivarne gli attacchi).
- **Sala dei Riflessi** (arena dello Specchio): anello di specchi ad arco veri (riflettono la scena), la stessa sala
  capovolta sospesa in alto, oculo da cui cade una colonna di luce, prismi che la spezzano in raggi tra gli specchi,
  anelli di luce incisi nel pavimento di vetro nero.
- **Corte dell'Ultima Ora** (arena del Reaper): quadrante di pietra sospeso su un abisso, ponte d'ossa con
  ringhiere, 11 pietre delle ore con le ore incise in luce rossa, lancette luminose che girano nel pavimento,
  un orologio colossale nel cielo incatenato alla luna, anime che salgono a spirale dall'abisso illuminato di rosso.
- **Nuovi mostri della Luna Rossa** (`RealmBeastAI`, animazione procedurale, attacchi telegrafati):
  - *Gravecrawler*: gabbia toracica su sei zampe d'osso con teschio cornuto; si impenna prima del balzo, morso rapido.
  - *Lantern Warden*: gigante incappucciato con lanterna su un'asta; porta la lanterna dietro la spalla prima della
    spazzata, la solleva (si illumina + cerchio a terra) prima del rintocco ad area.
- **Drago**: la carica in avanti è sostituita dal *Balzo* (si acquatta, salta ad arco, ricade con onda d'urto:
  il punto di caduta è segnato a terra). Morsi, codate e pestoni hanno ora un segnale visivo (bagliore delle fauci,
  archi a terra) e la telecamera in lock-on si allontana sui bersagli grandi.
- **Ultimate in PvP**: le ultimate di Cavaliere e Reaper colpiscono anche i giocatori con PvP attivo (danno ridotto).
- **Armature**: piastre con texture incise/scaglie/filigrana/sudario con normal map, colori ed emissioni attenuati.

### Quinta classe: lo Smoker · attacco tenendo premuto

- **Tenere premuto il clic sinistro** (o il tasto attacco del pad) continua ad attaccare: combo complete per le
  classi da mischia, raffica continua per arciere, bardo e smoker. Un clic che apre/chiude un menu non diventa un
  attacco tenuto.
- **Smoker** (rig e animazioni del bardo, `SmokerCaster` + `SmokeFx`): vagabondo con cappello a tesa larga e una
  pipa churchwarden lunga quanto un bastone (procedurale sulla mano destra, brace nel fornello che fuma e sbuffi
  dalla bocca). Usa il **mana**:
  - *Clic sinistro* — **Sbuffo**: palla di fumo a ricerca con brace al centro.
  - *Clic destro tenuto* — **Tirare dalla pipa**: il mana diventa fiato (fino a 3, la brace si accende);
    rilasciando **espira** uno spirito di fumo per ogni fiato (max 4 vivi, durano 16 s): occhi di brace,
    cacciano il nemico più vicino e lo artigliano.
  - *F* — **Folata Soffocante**: cono di fumo nero (danno, poise) e tutti gli spiriti si lanciano sul primo
    bersaglio colpito (colpo potenziato).
  - *1* — Ultimate **Il Grande Respiro**: un colosso di fumo alto come una torre si alza dietro di lui per ~9 s,
    colpisce col pugno i nemici vicini (il punto d'impatto è segnato a terra) e chiude con uno schianto a due mani.
  - Armi: Old Churchwarden → The King's Last Pipe (reliquia) → Wyrmflue (drago) → Censer of Dawn (radiosa):
    fumo più economico e più forte. Funziona anche in PvP. HUD: fiato e spiriti sotto la barra del mana.
  - **Posa della pipa** (`SmokerPose`, IK a due ossa sul braccio destro sopra le clip del bardo): mentre carica
    tiene il bocchino alle labbra e inspira finché il tasto è premuto; per sbuffo, folata, espirazione e ultimate
    porta la pipa alla bocca, tira e la abbassa mentre il fumo/gli spiriti escono dalla bocca.
  - **Carica al centro dello schermo**: pannello "DRAWING SMOKE" con 3 barre (una per spirito) che si riempiono,
    "LUNGS FULL" quando è pieno; in alto a sinistra suggerimento del tasto, ricariche e spiriti.
  - **Bilanciamento**: max 3 spiriti (12 s, danno ridotto, colpo ogni 1,1 s), carica più lenta e costosa,
    4 s di recupero dopo ogni espirazione, Folata 30 mana + 5 s di recupero, mana 100 con rigenerazione 4/s,
    sbuffo più lento, colosso meno forte.
- **Musiche**: `MECHANICAL GOD.mp3` per il Reaper, `boss fight.mp3` per lo Specchio (in loop semplice).
- **Audio senza scatti**: le musiche sono caricate compresse in memoria (non più in streaming da disco), buffer
  DSP a 1024 ("Good latency") e 48 voci reali; gli effetti hanno un budget (max 26 insieme, lo stesso suono al
  massimo 3 volte e non due volte entro 40 ms) e priorità più bassa della musica, che non viene mai rubata.

## 5. Struttura

```
Assets/DarkFantasy/
  Scripts/  Camera · Characters · Combat · Core · Enemies · Network · Player · Procedural · Rendering · UI
  Shaders/VHS_PS2_Screen.shader
  Editor/Builder/      DarkFantasyBuilder.cs (one-click), TextureGen.cs (texture PS2 procedurali)
  Editor/Diagnostics/  DevConsoleBridge.cs (log su file + comandi di test, eliminabile)
  Generated/           tutto ciò che crea il builder (non modificare a mano: viene sovrascritto)
  Docs/PipelineAsset_PS2.md · Tools/blender_ps2_export.py
```

## 6. Limiti noti / prossimi passi

- Capriola e accovacciamento sono clip proprie (`Tools/moves_anim.py` → `Art/Moves.fbx`) su un layer "Body" che
  muove bacino, busto e gambe: le braccia restano quelle della classe (arma in mano). Lo strafe in lock-on usa ancora
  la camminata.
- Lo spadone è una mesh procedurale agganciata a `hand_R` con l'orientamento calcolato dal solver: se cambi modello,
  rilancia *Build Everything* (o *Dark Fantasy ▸ Debug ▸ Render Attack Sheets* per controllare i fendenti).
- Il log `[DF] attack ...` in `PlayerController.StartAttack` è solo Editor: utile per verificare le finestre, rimovibile.
- I nemici usano steering con evitamento ostacoli (niente NavMesh): per livelli più complessi conviene un NavMeshSurface.
- La texture del cavaliere è ridotta a 256 px per il look PS2 (import settings della texture originale).

Crediti: UI "Gothic Manor Pixel UI Kit – Ravenhollow Lite" © Brunka Games (licenza nel kit: non ridistribuire gli sprite
separatamente). Font Jacquard 12 / Jersey 10 sotto SIL OFL. Modello "Solus the Knight" dal pacchetto Low Poly Knights.
