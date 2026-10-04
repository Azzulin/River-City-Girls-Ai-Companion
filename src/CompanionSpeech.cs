using System.Collections.Generic;
using UnityEngine;

namespace RCGCompanion
{
    // Falas da parceira (aparecem como texto flutuante em cima dela).
    internal static class CompanionSpeech
    {
        private static readonly Dictionary<string, string[]> Lines = new Dictionary<string, string[]>
        {
            { "kill", new[] { "Toma!", "Proximo!", "Facil!", "Quem e o proximo?", "Fica no chao!", "Hehe!" } },
            { "vitoria", new[] { "Limpo!", "Mandamos bem!", "Toca aqui!", "Somos imparaveis!", "Ja acabou?" } },
            { "parceiraCaiu", new[] { "Aguenta ai, ja vou!", "Nao desiste!", "To indo!" } },
            { "revivida", new[] { "De pe! Bora!", "Levanta, guerreira!", "Te peguei!" } },
            { "vidaBaixa", new[] { "To mal...", "Me cobre!", "Ai, isso doeu!" } },
            { "comer", new[] { "Nham!", "Delicia!", "Energia!" } },
            { "nivel", new[] { "Subi de nivel!", "To ficando forte!", "Level up!" } },
            { "parry", new[] { "Parry!", "Li seu golpe!", "Previsivel!" } },
            { "arma", new[] { "Olha o que eu achei!", "Isso vai doer!", "Arma na mao!" } },
            { "chefe", new[] { "Esse e dos grandes!", "Hora do chefe!", "Juntas a gente vence!" } },
            { "recruta", new[] { "Ajuda aqui!", "Vem, reforco!", "Pega eles!" } },
            { "comida", new[] { "Opa, comida!", "Vou guardar essa!" } },
            { "parado", new[] { "Vamos logo?", "To ficando com sono...", "Bora explorar!" } },
            { "esquiva", new[] { "Errou!", "Quase!", "Muito lenta!" } },
        };

        private static readonly Dictionary<string, float> NextByKey = new Dictionary<string, float>();
        private static float _nextAny;

        // chance: probabilidade de falar quando o evento acontece.
        public static void Say(string key, float chance = 1f)
        {
            if (!CompanionPlugin.Talk.Value)
            {
                return;
            }
            float now = Time.time;
            float next;
            if (now < _nextAny || (NextByKey.TryGetValue(key, out next) && now < next))
            {
                return;
            }
            if (Random.value > chance * CompanionPlugin.TalkFrequency.Value)
            {
                return;
            }
            string[] pool;
            if (!Lines.TryGetValue(key, out pool) || pool.Length == 0)
            {
                return;
            }
            _nextAny = now + 2.5f;
            NextByKey[key] = now + 8f;
            CompanionPlugin.Say(pool[Random.Range(0, pool.Length)], false);
        }
    }
}
