# Changelog — RCG AI Companion

Cada versão tem uma tag no git (`git checkout v2.7`, por exemplo).

## v2.10 — 2026-10-06
- **F7 troca a parceira durante o jogo** (passa pelas personagens disponíveis; nunca a mesma do jogador; Kunio/Riki só depois de zerar). Cada personagem mantém o próprio progresso e a escolha fica salva em `PersonagemDaParceira`.
- Log explica por que uma troca foi recusada (no ar / caída).
- Testado no jogo: troca, progresso mantido, escolha mantida após reiniciar a fase.

## v2.9 — 2026-10-06
- **Corrigido: travamento no Game Over** quando a parceira morria depois do jogador (a tela só aceitava o controle de quem morreu por último). Com a IA ligada, a tela é sempre do Player 1. Bug reproduzido e correção verificada no jogo.
- Nova opção `PersonagemDaParceira` (Auto = dupla padrão do jogo: Misako↔Kyoko, Kunio↔Riki).
- Modo de teste para desenvolvimento (`ModoTeste`): comandos por arquivo e simulação de botões do Player 1 nos menus.

## v2.8 — 2026-10-05
- **Corrigido: jogador preso na loja** esperando a vez da parceira. Vez dela protegida contra erros + vigia que fecha a loja se a vez do Player 2 travar.
- Versão aparece no log ("vX carregado").

## v2.7 — 2026-10-04
- Corrigido o "pisca-pisca" ao virar de frente (toque de 1 frame não virava a personagem).
- Inimigo caído: começa direto com o ataque forte; não gasta golpe em inimigo levantando.

## v2.6 — 2026-10-04
- Registro de ações (`RCG_AICompanion_acoes.log`) com linha do tempo e resumos de eficiência.

## v2.5 — 2026-10-04
- Defesa ignora inimigos atordoados "piscando" no estado de ataque; pausa entre defesas.

## v2.4 — 2026-10-04
- Reviver com a tolerância real do jogo; vez visível da parceira na loja; posicionamento em batalha (pinça, contornar, espaçamento, sair do cerco).

## v2.3 — 2026-10-04
- Defesa só contra golpes reais, de frente, com tempo máximo; parry por golpe; armas só em combate.

## v2.2 — 2026-10-04
- Plataforma por imitação (refaz o caminho do jogador) e ataques aéreos.

## v2.1 — 2026-10-04
- Pegar armas pela área de interação; ataque em inimigo caído.

## v2.0 — 2026-10-04
- Armas, parry que aprende, esquiva, táticas de chefe, recrutas, acessórios, ordens (F10), falas.

## v1.0 — 2026-10-04
- Primeira versão: IA no Player 2 (luta, defende, revive, cura, compra), entra sozinha, segue pelas portas, progresso salvo pelo jogo.
