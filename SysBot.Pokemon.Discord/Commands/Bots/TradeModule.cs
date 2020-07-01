using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using PKHeX.Core;

namespace SysBot.Pokemon.Discord
{
    [Summary("Queues new Link Code trades")]
    public class TradeModule : ModuleBase<SocketCommandContext>
    {
        private static TradeQueueInfo<PK8> Info => SysCordInstance.Self.Hub.Queues.Info;

        [Command("tradeList")]
        [Alias("tl")]
        [Summary("Prints the users in the trade queues.")]
        [RequireSudo]
        public async Task GetTradeListAsync()
        {
            string msg = Info.GetTradeList(PokeRoutineType.LinkTrade);
            var embed = new EmbedBuilder();
            embed.AddField(x =>
            {
                x.Name = "Pending Trades";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you the provided Pokémon file.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsyncAttach([Summary("Trade Code")]int code)
        {
            var sudo = Context.User.GetIsSudo();

            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("No attachment provided!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
            if (!att.Success || !(att.Data is PK8 pk8))
            {
                await ReplyAsync("No PK8 attachment provided!").ConfigureAwait(false);
                return;
            }

            await AddTradeToQueueAsync(code, Context.User.Username, pk8, sudo).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsync([Summary("Trade Code")]int code, [Summary("Showdown Set")][Remainder]string content)
        {
            const int gen = 8;
            content = ReusableActions.StripCodeBlock(content);
            var set = new ShowdownSet(content);
            var template = AutoLegalityWrapper.GetTemplate(set);
            if (Info.Hub.Config.Trade.Memes)
            {
                if (await TrollAsync(content, template).ConfigureAwait(false))
                    return;
            }

            if (set.InvalidLines.Count != 0)
            {
                var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
                await ReplyAsync(msg).ConfigureAwait(false);
                return;
            }

            var sav = AutoLegalityWrapper.GetTrainerInfo(gen);

            var pkm = sav.GetLegal(template, out _);
            var la = new LegalityAnalysis(pkm);
            var spec = GameInfo.Strings.Species[template.Species];
            var invalid = !(pkm is PK8) || (!la.Valid && SysCordInstance.Self.Hub.Config.Legality.VerifyLegality);
            if (invalid)
            {
                var imsg = $"Oops! I wasn't able to create something from that. Here's my best attempt for that {spec}!";
                await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
                return;
            }

            pkm.ResetPartyStats();
            var sudo = Context.User.GetIsSudo();
            await AddTradeToQueueAsync(code, Context.User.Username, (PK8) pkm, sudo).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsync([Summary("Showdown Set")][Remainder]string content)
        {
            var code = Info.GetRandomTradeCode();
            await TradeAsync(code, content).ConfigureAwait(false);
        }

        [Command("trade")]
        [Alias("t")]
        [Summary("Makes the bot trade you the attached file.")]
        [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
        public async Task TradeAsyncAttach()
        {
            var code = Info.GetRandomTradeCode();
            await TradeAsyncAttach(code).ConfigureAwait(false);
        }

        private async Task AddTradeToQueueAsync(int code, string trainerName, PK8 pk8, bool sudo)
        {
            if (!pk8.CanBeTraded() || !IsItemMule(pk8))
            {
                if (Info.Hub.Config.Trade.ItemMuleCustomMessage == string.Empty || IsItemMule(pk8))
                    Info.Hub.Config.Trade.ItemMuleCustomMessage = "Provided Pokémon content is blocked from trading!";
                await ReplyAsync($"{Info.Hub.Config.Trade.ItemMuleCustomMessage}").ConfigureAwait(false);
                return;
            }

            if (Info.Hub.Config.Trade.DittoTrade)
                DittoTrade(pk8);

            if (Info.Hub.Config.Trade.EggTrade)
                EggTrade(pk8);

            var la = new LegalityAnalysis(pk8);
            if (!la.Valid && SysCordInstance.Self.Hub.Config.Legality.VerifyLegality)
            {
                await ReplyAsync("PK8 attachment is not legal, and cannot be traded!").ConfigureAwait(false);
                return;
            }

            await Context.AddToQueueAsync(code, trainerName, sudo, pk8, PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);
        }

        private bool IsItemMule(PK8 pk8)
        {
            if (Info.Hub.Config.Trade.ItemMuleSpecies == Species.None || pk8.Species == 132 && Info.Hub.Config.Trade.DittoTrade || Info.Hub.Config.Trade.EggTrade && pk8.Nickname == "Egg")
                return true;
            return !(pk8.Species != SpeciesName.GetSpeciesID(Info.Hub.Config.Trade.ItemMuleSpecies.ToString()) || pk8.IsShiny);
        }

        private async Task<bool> TrollAsync(string content, IBattleTemplate set)
        {
            var defaultMeme = "https://i.imgur.com/qaCwr09.png";
            var path = Info.Hub.Config.Trade.MemeFileNames.Split(',');
            bool web = false;
            bool memeEmpty = false;

            if (path.Length < 6)
            {
                path = new string[] { defaultMeme, defaultMeme, defaultMeme, defaultMeme, defaultMeme, defaultMeme };
                memeEmpty = true;
            }

            if (Info.Hub.Config.Trade.MemeFileNames.Contains(".com") || memeEmpty)
                web = true;

            if (set.HeldItem == 16)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[0]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[0]).ConfigureAwait(false);
                return true;
            }
            else if (set.HeldItem == 500)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[1]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[1]).ConfigureAwait(false);
                return true;
            }
            else if (content.Contains($"★"))
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[2]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[2]).ConfigureAwait(false);
                return true;
            }
            else if (Info.Hub.Config.Trade.ItemMuleSpecies != Species.None && set.Shiny)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[3]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[3]).ConfigureAwait(false);
                return true;
            }
            else if (set.Nickname == "Egg" && set.Species >= 888 && set.Species <= 893)
            {
                if (web)
                    await Context.Channel.SendMessageAsync($"{path[4]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[4]).ConfigureAwait(false);
                return true;
            }
            else if (Info.Hub.Config.Trade.ItemMuleSpecies != Species.None && set.Species != SpeciesName.GetSpeciesID(Info.Hub.Config.Trade.ItemMuleSpecies.ToString()))
            {
                if (Info.Hub.Config.Trade.DittoTrade && set.Species == 132 || Info.Hub.Config.Trade.EggTrade && set.Nickname == "Egg")
                    return false;

                if (web)
                    await Context.Channel.SendMessageAsync($"{path[5]}").ConfigureAwait(false);
                else await Context.Channel.SendFileAsync(path[5]).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private void DittoTrade(PKM pk8)
        {
            if (pk8.Species != 132)
                return;

            var dittoLang = new string[] { "JPN", "ENG", "FRE", "ITA", "GER", "ESP", "KOR", "CHS", "CHT" };
            var dittoStats = new string[] { "ATK", "SPE" };

            if (!(pk8.Nickname.Contains(dittoLang[0]) || pk8.Nickname.Contains(dittoLang[1]) || pk8.Nickname.Contains(dittoLang[2]) || pk8.Nickname.Contains(dittoLang[3]) || pk8.Nickname.Contains(dittoLang[4])
                || pk8.Nickname.Contains(dittoLang[5]) || pk8.Nickname.Contains(dittoLang[6]) || pk8.Nickname.Contains(dittoLang[7]) || pk8.Nickname.Contains(dittoLang[8])))
            {
                pk8.Nickname = "KOR";
                pk8.IsNicknamed = true;
            }

            if (pk8.Nickname.Contains(dittoLang[0]))
                pk8.Language = (int)LanguageID.Japanese;
            else if (pk8.Nickname.Contains(dittoLang[1]))
                pk8.Language = (int)LanguageID.English;
            else if (pk8.Nickname.Contains(dittoLang[2]))
                pk8.Language = (int)LanguageID.French;
            else if (pk8.Nickname.Contains(dittoLang[3]))
                pk8.Language = (int)LanguageID.Italian;
            else if (pk8.Nickname.Contains(dittoLang[4]))
                pk8.Language = (int)LanguageID.German;
            else if (pk8.Nickname.Contains(dittoLang[5]))
                pk8.Language = (int)LanguageID.Spanish;
            else if (pk8.Nickname.Contains(dittoLang[6]))
                pk8.Language = (int)LanguageID.Korean;
            else if (pk8.Nickname.Contains(dittoLang[7]))
                pk8.Language = (int)LanguageID.ChineseS;
            else if (pk8.Nickname.Contains(dittoLang[8]))
                pk8.Language = (int)LanguageID.ChineseT;

            if (!(pk8.Nickname.Contains(dittoStats[0]) || pk8.Nickname.Contains(dittoStats[1])))
                pk8.IVs = new int[] { 31, 31, 31, 31, 31, 31 };
            else if (pk8.Nickname.Contains(dittoStats[0]))
                pk8.IVs = new int[] { 31, 0, 31, 31, 31, 31 };
            else if (pk8.Nickname.Contains(dittoStats[1]))
                pk8.IVs = new int[] { 31, 31, 31, 0, 31, 31 };

            pk8.StatNature = pk8.Nature;
            pk8.SetAbility(150);
            pk8.Met_Level = 40;
            pk8.Move1 = 144;
            pk8.Move1_PP = 0;
            pk8.Met_Location = 162;
            pk8.Ball = 21;
            pk8.SetSuggestedHyperTrainingData();

            if (pk8.Nickname.Contains(dittoStats[0]) && pk8.Nickname.Contains(dittoStats[1]))
                pk8.IVs = new int[] { 31, 0, 31, 0, 31, 31 };

            return;
        }

        private void EggTrade(PK8 pk8)
        {
            if (!Info.Hub.Config.Trade.EggTrade || pk8.Nickname != "Egg")
                return;

            pk8.IsEgg = true;
            pk8.Egg_Location = 60002;
            pk8.HeldItem = 0;
            pk8.CurrentLevel = 1;
            pk8.EXP = 0;
            pk8.DynamaxLevel = 0;
            pk8.Met_Level = 1;
            pk8.Met_Location = 0;
            pk8.CurrentHandler = 0;
            pk8.OT_Friendship = 1;
            pk8.HT_Name = "";
            pk8.HT_Friendship = 0;
            pk8.HT_Language = 0;
            pk8.HT_Gender = 0;
            pk8.HT_Memory = 0;
            pk8.HT_Feeling = 0;
            pk8.HT_Intensity = 0;
            pk8.EVs = new int[] { 0, 0, 0, 0, 0, 0 };
            pk8.Markings = new int[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            pk8.ClearRecordFlags();
            pk8.FixMoves();
            pk8.GetSuggestedRelearnMoves();
            pk8.SetSuggestedHyperTrainingData();

            return;
        }
    }
}
