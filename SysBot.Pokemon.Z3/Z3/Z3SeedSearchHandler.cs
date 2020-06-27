using PKHeX.Core;

namespace SysBot.Pokemon
{
    public class Z3SeedSearchHandler<T> : ISeedSearchHandler<T> where T : PKM, new()
    {
        private static int[] GetBlankIVTemplate() => new[] {-1, -1, -1, -1, -1, -1};

        public void CalculateAndNotify(T pkm, PokeTradeDetail<T> detail, SeedCheckSettings settings, PokeTradeBot bot)
        {
            var ec = pkm.EncryptionConstant;
            var pid = pkm.PID;
            var IVs = pkm.IVs.Length == 0 ? GetBlankIVTemplate() : PKX.ReorderSpeedLast((int[])pkm.IVs.Clone());
            var name = SpeciesName.GetSpeciesName(pkm.Species, 2);
            var ot = pkm.OT_Name;
            var abilityNo = pkm.AbilityNumber;
            var ability = pkm.Ability;
            var gender = pkm.GetSaneGender();
            var nature = pkm.Nature;
            if (settings.ShowAllZ3Results)
            {
                var matches = Z3Search.GetAllSeeds(ec, pid, IVs, name, ot, gender, abilityNo, ability, nature);
                foreach (var match in matches)
                {
                    var lump = new PokeTradeSummary("Calculated Seed:", match);
                    detail.SendNotification(bot, lump);
                }
            }
            else
            {
                var match = Z3Search.GetFirstSeed(ec, pid, IVs, name, ot, gender, abilityNo, ability, nature);
                var lump = new PokeTradeSummary("Calculated Seed:", match);
                detail.SendNotification(bot, lump);
            }
        }
    }
}