using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SysBot.Pokemon.TradeModule
{
    public static class ErrorTranslator
    {
        public static (string message, string? solution) TranslateALMError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return ("Error desconocido.", null);

            var rx = new Regex(
                @"Type\s*=\s*([A-Za-z_]+).*?Value\s*=\s*([^\}\r\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            var matches = rx.Matches(raw);
            if (matches.Count > 0)
            {
                var lines = new List<string>();
                var solutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in matches)
                {
                    var type = m.Groups[1].Value.Trim();
                    var value = m.Groups[2].Value.Trim().TrimEnd('}', ' ');

                    switch (type.ToLowerInvariant())
                    {
                        case "moveunrecognized":
                            lines.Add($"• El movimiento **{value}** no existe o está mal escrito.");
                            solutions.Add("✔ Usa el nombre oficial en inglés (ej: `Thunderbolt`, no `Trueno`).");
                            break;

                        case "itemunrecognized":
                            lines.Add($"• El objeto **{value}** no existe o está mal escrito.");
                            solutions.Add("✔ Usa el nombre oficial en inglés (ej: `Leftovers`, no `Restos`).");
                            break;

                        case "natureunrecognized":
                            lines.Add($"• La naturaleza **{value}** no existe o está mal escrita.");
                            solutions.Add("✔ Ejemplos válidos: `Adamant`, `Jolly`, `Modest`.");
                            break;

                        case "abilityunrecognized":
                            lines.Add($"• La habilidad **{value}** no existe o está mal escrita.");
                            solutions.Add("✔ Usa el nombre oficial en inglés (ej: `Intimidate`, `Levitate`).");
                            break;

                        default:
                            lines.Add($"• Error de formato en **{value}** ({type}).");
                            solutions.Add("✔ Revisa la sintaxis del set (EVs/IVs y stats `HP/Atk/Def/SpA/SpD/Spe`).");
                            break;
                    }
                }

                if (IsShinyLockText(raw))
                {
                    solutions.Clear();
                    solutions.Add("✔ Quita `Shiny: Yes` del set o solicita otro Pokémon.");
                }

                string msg = string.Join("\n", lines);
                string? sol = solutions.Count > 0 ? string.Join("\n", solutions) : null;
                return (msg, sol);
            }

            if (raw.Contains("TokenFailParse", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("Unable to parse Showdown Set", StringComparison.OrdinalIgnoreCase))
            {
                return ("El set contiene una o más líneas con formato inválido.",
                        "✔ Revisa EVs/IVs (0–252, múltiplos de 4) y nombres de stats: `HP`, `Atk`, `Def`, `SpA`, `SpD`, `Spe`.");
            }

            if (IsShinyLockText(raw))
            {
                return ("Este Pokémon tiene **Shiny lock** y no puede intercambiarse como shiny.",
                        "✔ Quita `Shiny: Yes` del set o solicita otro Pokémon.");
            }

            return (raw, null);
        }

        public static string ExtractValue(string raw)
        {
            int idx = raw.IndexOf("Value =", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string part = raw[(idx + 7)..].Trim();
                int end = part.IndexOfAny(new[] { '}', '\r', '\n' });
                if (end >= 0) part = part[..end].Trim();
                return part;
            }
            return "?";
        }

        public static bool IsShinyLockText(string raw)
        {
            return raw.Contains("Shiny lock", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("Shiny-locked", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("cannot be shiny", StringComparison.OrdinalIgnoreCase);
        }

    }
}
