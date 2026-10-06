==============================================================================
            RCG AI COMPANION v2.10 - MOD FOR RIVER CITY GIRLS 1
            AI-controlled partner in place of Player 2 (co-op)
                           >>> TEST BUILD <<<
==============================================================================

WHAT'S NEW IN v2.10
-------------------
  - NEW: press F7 in-game to switch your partner on the spot. It cycles
    through the available characters (never the same as yours; Kunio and
    Riki only after beating the game). Each character keeps her/his own
    progress (level, moves, items, money), and your choice is remembered
    for the next sessions and stages. (Tested in-game: switching, progress
    kept, and the choice survives a stage restart.)

  From v2.9:
  - FIXED: GAME OVER softlock. If your partner went down AFTER you, the
    "Continue / Quit" screen waited for controller 2 (the AI), so you
    couldn't toggle or confirm anything. Now the screen is always yours
    while the AI is on. (Tested in-game: the original bug was reproduced
    first, then the fix was confirmed - toggle, confirm and restart all
    work.)
  - NEW SETTING: PersonagemDaParceira (partner character). By default the
    game pairs Misako <-> Kyoko and Kunio <-> Riki - that's the game's own
    rule, so picking Riki gives you Kunio and vice versa. You can now
    choose your partner (see SETTINGS below).

  From v2.8:
  - FIXED: you could get stuck in a store, waiting for your partner to
    finish her turn. A safety watchdog now closes the store by itself if
    Player 2's turn ever stalls.

  If you already have an older version installed, just copy the new files
  over it (choose "Replace").


WHAT THIS MOD DOES
------------------
Player 2 (Misako/Kyoko) is controlled by an AI. She:
  - joins the game by herself and follows you (including through doors);
  - fights alongside you: combos, ground attacks, air attacks, specials;
  - blocks, learns each enemy's attack timing (parry) and dodges;
  - revives you when you go down;
  - heals with food from her backpack and picks up food dropped by enemies;
  - picks up weapons from the ground during fights;
  - does her own shopping at stores and at the dojo (moves, food,
    accessories);
  - levels up, and ALL of her progress is saved together with your save;
  - follows you across platforms and ladders (she copies the path you took);
  - says short lines during combat.

No original game file is modified.

This test build already includes:
  - a config file ready for testing (action report and detailed log
    turned on);
  - the attack timings the AI has already learned for 10 enemy types,
    so her parry works against those enemies right from the start.


WHAT IS IN THIS FOLDER
----------------------
  README.txt                <- this file, in English
                               (do NOT copy it into the game)
  LEIA-ME.txt               <- the same guide, in Portuguese
  COPY INTO GAME FOLDER\    <- the mod. The CONTENTS of this folder go
                               into the game folder.


REQUIREMENTS
------------
  - River City Girls (the FIRST game) on Steam, for Windows.
  - Nothing else to install: the mod loader (BepInEx) is already included.


------------------------------------------------------------------------------
 INSTALLATION, STEP BY STEP
------------------------------------------------------------------------------

0) IF YOU DOWNLOADED A .ZIP (e.g. from Google Drive)
   Right-click the downloaded file > "Extract All..." and open the
   extracted folder.

1) (RECOMMENDED) BACK UP YOUR SAVE
   Press  Windows + R , paste the path below and press Enter:

     %USERPROFILE%\AppData\LocalLow\WayForward Technologies\River City Girls

   Copy the  _savedata  folder somewhere safe (e.g. your Desktop).

2) CLOSE THE GAME if it is running.

3) OPEN THE GAME FOLDER
   In Steam: Library > right-click "River City Girls" > Manage >
   Browse local files.
   This opens the folder that contains  RiverCityGirls.exe .
   (Usually: C:\Program Files (x86)\Steam\steamapps\common\River City Girls)

4) COPY AND PASTE
   a) Open the  "COPY INTO GAME FOLDER"  folder (inside this test folder).
   b) Select everything:  Ctrl + A
   c) Copy:               Ctrl + C
   d) Go to the game folder (step 3) and paste:  Ctrl + V
      If Windows asks, choose "Replace the files in the destination".
      If it asks for administrator permission, click "Continue".

   After pasting, the game folder should look like this:

     River City Girls\
       RiverCityGirls.exe          (already there)
       RiverCityGirls_Data\        (already there)
       winhttp.dll                 <- from the mod
       doorstop_config.ini         <- from the mod
       BepInEx\                    <- from the mod
         core\
         config\
         plugins\
           RCG_AICompanion.dll

   WARNING: the most common mistake is pasting the whole
   "COPY INTO GAME FOLDER" folder inside the game folder. winhttp.dll and
   the BepInEx folder must sit DIRECTLY next to RiverCityGirls.exe, not
   inside another folder.

   (The files ".doorstop_version" and "changelog.txt" are optional; if one
   of them is missing after downloading, that's fine.)

5) START THE GAME FROM STEAM as usual.
   The first launch may take a few extra seconds (this is normal).

6) CHECK THAT THE MOD LOADED
   In the game folder, open  BepInEx\LogOutput.log  with Notepad.
   It should contain a line similar to:

     RCG AI Companion v2.10 carregado. F7 troca a parceira, F8 liga/desliga...

   If that line is there, everything is working!


------------------------------------------------------------------------------
 HOW TO PLAY
------------------------------------------------------------------------------

  - Load your save (or start a new game) as usual, in single player.
  - A few seconds later your partner joins by herself as Player 2.
  - Just play. She will fight, follow you, shop, heal, etc.

  KEYS (keyboard):
    F7  = switch your partner (cycles through the available characters;
          the choice is remembered). If she is in the air or down, try
          again in a moment.
    F8  = turn the AI on/off. When it is off, a friend can use controller 2
          normally (the AI hands the character back).
    F9  = call your partner over to you (if she gets stuck).
    F10 = change her orders:
            Normal  >  Aggressive  >  Defensive  >  Stay here
          (the mode is shown above her head, in Portuguese:
           "Modo: Normal", "Modo: Agressiva!", "Modo: Defensiva",
           "Fico aqui!")

  STORES: when you leave a store, the game gives Player 2 a turn. You will
  see her doing her own shopping, and then the store closes.

  PLATFORMS: she learns by COPYING you. If you climb onto a platform, she
  repeats your jumps/climbs. If she can't make it, she is teleported next
  to you after a few attempts.

  NOTE: her speech lines, the log messages and the config file are in
  Portuguese for now. Everything works the same.


------------------------------------------------------------------------------
 SETTINGS (OPTIONAL)
------------------------------------------------------------------------------

File (in the game folder):  BepInEx\config\rcg.aicompanion.cfg
Open it with Notepad while the game is CLOSED. The setting names are in
Portuguese; here are the most useful ones:

  PersonagemDaParceira = Auto Partner character: Auto, Misako, Kyoko,
                              Kunio or Riki. Auto = the game's default pair.
                              Kunio/Riki only after beating the game; it
                              can't be the same character as yours.
                              Easier: just press F7 in-game.
  Agressividade = 0.7         Aggressiveness (0 = careful ... 1 = very
                              aggressive)
  ChanceDeDefender = 0.55     Chance to block an incoming attack (0 to 1)
  VidaParaCurar = 0.35        Eats food when her health drops below 35%
  UsarArmas = true            Use weapons from the ground
  UsarParry = true            Learn attack timings and parry
  AtaquesAereos = true        Air attacks and juggles
  Plataforma = true           Follow you across platforms/ladders
  UsarRecrutas = true         Use her recruit (assist)
  FazerCompras = true         Automatic shopping at stores
  DinheiroReserva = 10        Money she tries to keep
  MaxComidasNaMochila = 6     Max healing food she carries
  ComprarAcessorios = true    Buy/equip the best accessories
  Falas = true                Speech lines above her head
  RegistroDeAcoes = true      Action report for testing (see below)
  LogDetalhado = true         Extra detail in LogOutput.log

  Use true/false for on/off settings.


------------------------------------------------------------------------------
 FOR TESTERS: WHAT TO TEST AND HOW TO SEND FEEDBACK
------------------------------------------------------------------------------

The mod records a report of everything your partner does. After playing,
send these 2 files (they are in the BepInEx folder, inside the game folder):

  1) BepInEx\RCG_AICompanion_acoes.log
       Timeline of everything she decided and did, plus efficiency
       summaries every 60 seconds (hit rate, idle time, etc.).
       IMPORTANT: this file is recreated every time the game starts. The
       previous session is kept in  RCG_AICompanion_acoes_anterior.log .
       So send the file BEFORE starting the game again (or send the
       "_anterior" one).

  2) BepInEx\LogOutput.log
       General log (errors, warnings, purchases, healing).

Along with the files, describe in your own words:
  - What she did that looked strange/wrong, and roughly WHEN (start of a
    stage, boss fight, inside a store...).
  - Which stage/area you were in.
  - What you expected her to do instead.

Things worth testing:
  [ ] Does she join by herself after you load your save?
  [ ] Does she fight well? Does she stand around? Get hit from behind a lot?
  [ ] Does she hit enemies lying on the ground?
  [ ] Does she block / parry / dodge at the right time?
  [ ] When you go down, does she come and revive you?
  [ ] Does she grab weapons during fights and drop heavy objects afterwards?
  [ ] Stores: does she take her turn and buy things? Does her money go down?
  [ ] Does she level up, and is her progress still there after you save
      and reload?
  [ ] Platforms/ladders: can she follow you?
  [ ] Bosses: does she back off from dangerous attacks?
  [ ] Do F8, F9 and F10 work?


------------------------------------------------------------------------------
 TROUBLESHOOTING
------------------------------------------------------------------------------

* My partner doesn't show up / nothing changed:
    - Check step 4: winhttp.dll and the BepInEx folder must be DIRECTLY
      next to RiverCityGirls.exe.
    - Check that BepInEx\LogOutput.log exists after starting the game. If it
      does NOT, BepInEx didn't load:
        - some antivirus programs block winhttp.dll: add the game folder to
          your antivirus exceptions and copy the files again.
    - Wait a few seconds after loading your save; she joins by herself.
    - Press F8 (the AI may have been turned off).

* Steam Deck / Linux (Proton):
    In Steam: right-click the game > Properties > General > Launch Options,
    and enter:
        WINEDLLOVERRIDES="winhttp=n,b" %command%

* The game updated and the mod stopped working:
    Copy the mod files again. If it still doesn't work, let us know: the
    update may have changed the game's code.

* Game Over screen doesn't respond:
    Fixed in v2.9. Check that BepInEx\LogOutput.log says "v2.9 carregado".
    If it still happens, send both log files.

* Stuck in a store, on your partner's turn:
    This should no longer happen since v2.8: the store closes by itself
    after a few seconds. If it still happens, send both log files - the action
    log records every step of her store turn (lines starting with LOJA).

* She got stuck somewhere:
    Press F9 to call her. If it always happens in the same place, let us
    know (send the logs).


------------------------------------------------------------------------------
 HOW TO UNINSTALL
------------------------------------------------------------------------------

With the game closed, delete these from the game folder:
    BepInEx\   winhttp.dll   doorstop_config.ini
    .doorstop_version   changelog.txt   (if present)

The game goes back to normal. Your partner's progress stays in your save
(she is the game's own Player 2 character).

==============================================================================
