using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace SysBot.Pokemon
{
    public class SeedSearchResult
    {
        public static readonly SeedSearchResult None = new SeedSearchResult(Z3SearchResult.SeedNone, default, 0, new int[6], string.Empty, string.Empty, 0, 0, 0, 0);

        public readonly Z3SearchResult Type;
        public readonly ulong Seed;
        public readonly int FlawlessIVCount;
        private readonly int[] IVs;
        private readonly string Name;
        private readonly string OT;
        private readonly int Gender;
        private readonly int AbilityNo;
        private readonly int Ability;
        private readonly int Nature;

        public SeedSearchResult(Z3SearchResult type, ulong seed, int ivCount, int[] ivs, string name, string ot, int gender, int abilityNo, int ability, int nature)
        {
            Type = type;
            Seed = seed;
            FlawlessIVCount = ivCount;
            IVs = ivs;
            Name = name;
            OT = ot;
            Gender = gender;
            AbilityNo = abilityNo;
            Ability = ability;
            Nature = nature;
        }

        public override string ToString()
        {
            return Type switch
            {
                Z3SearchResult.SeedMismatch => $"Seed found, but not an exact match {Seed:X16}",
                Z3SearchResult.Success => string.Join(Environment.NewLine, GetLines()),
                _ => "The Pokémon is not a raid Pokémon!"
            };
        }

        private IEnumerable<string> GetLines()
        {
            if (Name == string.Empty)
                yield return "";
            else
            {
                yield return $"Species: {Name}  ({(Gender)Gender})";
                yield return $"Trainer: {OT}";
                yield return $"Nature: {(Nature)Nature}";
                if (AbilityNo > 2)
                    yield return $"Ability: {(Ability)Ability} (HA)";
                else yield return $"Ability: {(Ability)Ability} ({AbilityNo})";
            }

            var first = $"Seed: {Seed:X16}";
            if (FlawlessIVCount >= 1)
                first += $", IVCount: {FlawlessIVCount}";
            yield return first;

            if (IVs[0] == 0 && IVs[1] == 0 && IVs[2] == 0 && IVs[3] == 0 && IVs[4] == 0 &&IVs[5] == 0)
                yield return "";
            else yield return $"IVs: {IVs[0]} HP **/** {IVs[1]} Atk **/** {IVs[2]} Def **/** {IVs[3]} SpA **/** {IVs[4]} SpD **/** {IVs[5]} Spe\n";

            SeedSearchUtil.GetNextShinyFrame(Seed, out var _, out var star, out var square);

            if (star == -1)
                yield return $"Next Star Shiny Frame: >100,000";
            else yield return $"Next Star Shiny Frame: {star}";

            if (square == -1)
                yield return $"Next Square Shiny Frame: >100,000";
            else yield return $"Next Square Shiny Frame: {square}";
        }

        public Shiny GetShinyType()
        {
            SeedSearchUtil.GetNextShinyFrame(Seed, out var type, out var star, out var square);
            if (star == -1 && square != -1)
                return type == 2 ? Shiny.AlwaysSquare : Shiny.AlwaysSquare;
            else if (star != -1 && square == -1)
                return type == 1 ? Shiny.AlwaysStar : Shiny.AlwaysStar;
            else if (star != -1 && square != -1)
            {
                if (star - square > 0)
                    return type == 2 ? Shiny.AlwaysSquare : Shiny.AlwaysSquare;
                else return type == 1 ? Shiny.AlwaysStar : Shiny.AlwaysStar;
            }
            else return type == 1 ? Shiny.AlwaysStar : Shiny.AlwaysSquare;
        }
    }
}