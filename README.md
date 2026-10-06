# RCG AI Companion (River City Girls 1)

Mod que transforma o **Player 2 em uma parceira controlada por IA** no River City Girls 1 (Steam/PC).
Feito com [BepInEx 5](https://github.com/BepInEx/BepInEx) + Harmony (o jogo é Unity 2018.2 / Mono).
Nenhum arquivo original do jogo é alterado.

Versão atual: **v2.10** — veja o [CHANGELOG](CHANGELOG.md).

## O que ela faz
- **Entra sozinha** no jogo e **segue você** (inclusive pelas portas, na hora).
- **Luta junto**: prioriza quem está te batendo, faz combos, ataca inimigos caídos, usa especial e **ataques aéreos** (malabarismo, entrada pulando).
- **Posicionamento**: ataca pelo lado oposto ao seu (pinça), evita o meio da multidão, contorna inimigos, sai do cerco e recua entre combos.
- **Parry que aprende**: mede o tempo de cada golpe de cada inimigo e defende no instante certo; o aprendizado fica salvo em `BepInEx\config\rcg.aicompanion.parry.txt`.
- **Esquiva** de golpes indefensáveis e de chefes; **chefes**: bate e recua, cai matando quando ele fica atordoado.
- **Armas** do chão (só em combate), **recrutas**, **te revive**, **se cura** e pega comida do chão.
- **Compras**: na loja ela tem a vez dela (visível): golpes do dojo, comidas com bônus permanente, comidas de reserva e acessórios (nota para os 35 efeitos; equipa os 2 melhores).
- **Plataforma por imitação**: grava o seu caminho (pulos, pulos na parede, escadas) e refaz quando você está numa altura que ela não alcança.
- **Personalidade**: frases sobre vitórias, reviver, nível, parry, chefes...
- **Progresso salvo** junto com o seu save (o jogo salva tudo por personagem).

## Teclas
| Tecla | Ação |
|---|---|
| **F7** | Troca a parceira (passa pelas disponíveis; a escolha fica salva) |
| **F8** | Liga/desliga a IA (desligada, um amigo pode usar o controle 2) |
| **F9** | Chama a parceira para perto |
| **F10** | Ordens: Normal → Agressiva → Defensiva → Fica aqui |

## Estrutura do repositório
```
src/                      código do mod (C# 5, compilado com o csc do .NET Framework)
  CompanionPlugin.cs      plugin BepInEx: configuração, teclas, entrada automática, cura, teleporte, troca de parceira, vigia da loja
  CompanionBrain.cs       IA de combate: alvos, combos, defesa/parry, posicionamento, reviver, armas, aéreos
  CompanionNavigator.cs   plataforma por imitação (grava e refaz o caminho do jogador)
  CompanionShopper.cs     compras e acessórios
  CompanionPatches.cs     patches Harmony (entrada do P2, loja, portas, Game Over, eventos de dano)
  AttackLearner.cs        aprendizado do tempo dos golpes (parry)
  CompanionSpeech.cs      falas
  CompanionTelemetry.cs   registro de ações + resumos de eficiência
  CompanionTestHarness.cs ferramentas de teste (só com ModoTeste)
docs/                     guias para testers (inglês e português)
build.ps1                 compila e instala a DLL no jogo
```

## Compilar
Requisitos: River City Girls instalado pela Steam + BepInEx 5.4.23.5 (x64) na pasta do jogo.
```
powershell -ExecutionPolicy Bypass -File build.ps1
```
Feche o jogo antes (a DLL fica travada enquanto ele está aberto). O script compila contra as DLLs do próprio jogo e copia o resultado para `BepInEx\plugins`.
Se o jogo estiver em outro lugar: `build.ps1 -GameDir "D:\...\River City Girls"`.

## Testes
- Logs: `BepInEx\LogOutput.log` e `BepInEx\RCG_AICompanion_acoes.log` (linha do tempo + resumos de eficiência a cada 60s).
- Modo de teste (`[Debug] ModoTeste = true`): comandos em `BepInEx\teste_comando.txt` — `status`, `gameover_auto`, `ui:confirmar`/`ui:baixo`/..., `parceira:<nome>`, `parceira_ciclo`. **Desligue depois** (F11 provoca Game Over).
- Faça backup do save antes de testar: `%USERPROFILE%\AppData\LocalLow\WayForward Technologies\River City Girls\_savedata`.

## Distribuição para testers
O pacote leva o BepInEx + a DLL + os guias de `docs/`. Os pacotes gerados ficam em `dist/` (fora do git).

## Desinstalar
Apague da pasta do jogo: `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`.

## Aviso
Mod de fãs, **não oficial** e sem vínculo com a WayForward ou a Arc System Works.
*River City Girls* e seus personagens pertencem aos respectivos donos. Este repositório contém
apenas o código do mod — nenhum arquivo, asset ou código do jogo. É preciso ter o jogo original (Steam).
