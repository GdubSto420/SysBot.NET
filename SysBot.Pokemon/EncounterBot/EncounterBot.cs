using PKHeX.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Base.SwitchStick;
using static SysBot.Pokemon.PokeDataOffsets;

namespace SysBot.Pokemon
{
    public class EncounterBot : PokeRoutineExecutor
    {
        private readonly PokeTradeHub<PK8> hub;
        private readonly BotCompleteCounts Counts;
        private readonly string Ping;
        private readonly IDumper DumpSetting;
        private readonly Nature DesiredNature;
        private readonly int[] DesiredIVs = {-1, -1, -1, -1, -1, -1};
        private readonly byte[] BattleMenuReady = {0, 0, 0, 255};

        public EncounterBot(PokeBotConfig cfg, PokeTradeHub<PK8> Hub) : base(cfg)
        {
            hub = Hub;
            Counts = Hub.Counts;
            DumpSetting = Hub.Config.Folder;
            DesiredNature = Hub.Config.Encounter.DesiredNature;
            Ping = !Hub.Config.PingOnMatch.Equals(string.Empty) ? $"<@{Hub.Config.PingOnMatch}>\n" : "";

            /* Populate DesiredIVs array.  Bot matches 0 and 31 IVs.
             * Any other nonzero IV is treated as a minimum accepted value.
             * If they put "x", this is a wild card so we can leave -1. */
            string[] splitIVs = Hub.Config.Encounter.DesiredIVs.Split(new [] {'/'}, StringSplitOptions.RemoveEmptyEntries);

            // Only accept up to 6 values in case people can't count.
            for (int i = 0; i < 6 && i < splitIVs.Length; i++)
            {
                if (splitIVs[i] == "x" || splitIVs[i] == "X")
                    continue;
                DesiredIVs[i] = Convert.ToInt32(splitIVs[i]);
            }
        }

        private int encounterCount;
        private int catchCount;
        private byte[] pouchData = { 0 };

        private bool StopCondition(PK8 pk)
        {
            /*  Match species and look for a shiny. This is only checked for walking encounters.
             *  If they specified a species, it has to match. */
            if (hub.Config.Encounter.EncounteringType == EncounterMode.HorizontalLine || hub.Config.Encounter.EncounteringType == EncounterMode.VerticalLine)
            {
                if (!pk.IsShiny || (hub.Config.Encounter.StopOnSpecies != Species.None && hub.Config.Encounter.StopOnSpecies != (Species)pk.Species))
                    return false;
            }

            if (DesiredNature != Nature.Random && DesiredNature != (Nature)pk.Nature)
                return false;

            // Reorder the Pokemon's IVs to HP/Atk/Def/SpA/SpD/Spe
            int[] pkIVList = PKX.ReorderSpeedLast(pk.IVs);

            for (int i = 0; i < 6; i++)
            {
                // Match all 0's.
                if (DesiredIVs[i] == 0 && pk.IVs[i] != 0)
                    return false;
                // Wild cards should be -1, so they will always be less than the Pokemon's IVs.
                if (DesiredIVs[i] > pkIVList[i])
                    return false;
            }
            return true;
        }

        protected override async Task MainLoop(CancellationToken token)
        {
            Log("Identifying trainer data of the host console.");
            await IdentifyTrainer(token).ConfigureAwait(false);

            Log("Starting main EncounterBot loop.");
            Config.IterateNextRoutine();

            // Clear out any residual stick weirdness.
            await ResetStick(token).ConfigureAwait(false);

            var task = hub.Config.Encounter.EncounteringType switch
            {
                EncounterMode.VerticalLine => WalkInLine(token),
                EncounterMode.HorizontalLine => WalkInLine(token),
                EncounterMode.Eternatus => DoEternatusEncounter(token),
                EncounterMode.LegendaryDogs => DoDogEncounter(token),
                _ => WalkInLine(token),
            };
            await task.ConfigureAwait(false);

            await ResetStick(token).ConfigureAwait(false);
        }

        private async Task WalkInLine(CancellationToken token)
        {
            if (hub.Config.Encounter.CatchEncounter && !hub.Config.Encounter.StrongSpawn)
            {
                Log("Checking Poké Ball count...");
                pouchData = await Connection.ReadBytesAsync(PokeBallOffset, 116, token).ConfigureAwait(false);
                var counts = EncounterCount.GetBallCounts(pouchData);
                catchCount = counts.PossibleCatches(Ball.Master);

                if (catchCount == 0)
                {
                    Log("Insufficient Master Balls. Please obtain at least one before starting.");
                    return;
                }
            }

            while (!token.IsCancellationRequested)
            {
                if (hub.Config.Encounter.StrongSpawn)
                    await StrongSpawn(token).ConfigureAwait(false);

                var attempts = await StepUntilEncounter(token).ConfigureAwait(false);
                if (attempts < 0) // aborted
                    continue;

                Log($"Encounter found after {attempts} attempts! Checking details...");

                // Reset stick while we wait for the encounter to load.
                await ResetStick(token).ConfigureAwait(false);

                var pk = await ReadPokemon(WildPokemonOffset, token).ConfigureAwait(false);
                if (pk.Species == 0)
                {
                    Log("Invalid data detected. Restarting loop.");

                    // Flee and continue looping.
                    while (await IsInBattle(token).ConfigureAwait(false))
                        await FleeToOverworld(token).ConfigureAwait(false);
                    continue;
                }

                encounterCount++;
                Log($"Encounter: {encounterCount}{Environment.NewLine}{ShowdownSet.GetShowdownText(pk)}{Environment.NewLine}");
                Counts.AddCompletedEncounters();
                Counts.AddEncounteredSpecies(pk);

                if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
                    DumpPokemon(DumpSetting.DumpFolder, "encounters", pk);

                // Offsets are flickery so make sure we see it 3 times.
                for (int i = 0; i < 3; i++)
                    await ReadUntilChanged(BattleMenuOffset, BattleMenuReady, 5_000, 0_100, true, token).ConfigureAwait(false);

                if (StopCondition(pk))
                {
                    if (hub.Config.CaptureVideoClip)
                        await PressAndHold(CAPTURE, 2_000, 1_000, token).ConfigureAwait(false);

                    if (hub.Config.Encounter.CatchEncounter && !hub.Config.Encounter.StrongSpawn)
                    {
                        await SetLastUsedBall(Ball.Master, token).ConfigureAwait(false);
                        Log($"Result found! Attempting to catch...");
                        await CatchWildPokemon(pk, token).ConfigureAwait(false);

                        if (!hub.Config.Encounter.InjectPokeBalls && encounterCount != 0 && encounterCount % catchCount == 0)
                            return;

                        await WalkInLine(token).ConfigureAwait(false);
                    }

                    Log($"{Ping}Result found! Stopping routine execution; restart the bot(s) to search again.");
                    return;
                }

                Log("Running away...");
                while (await IsInBattle(token).ConfigureAwait(false) && !hub.Config.Encounter.StrongSpawn)
                    await FleeToOverworld(token).ConfigureAwait(false);
            }
        }

        private async Task DoEternatusEncounter(CancellationToken token)
        {
            while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.EncounterBot)
            {
                await SetStick(LEFT, 0, 20_000, 500, token).ConfigureAwait(false);
                await SetStick(LEFT, 0, 0, 1_000, token).ConfigureAwait(false);

                var pk = await ReadPokemon(RaidPokemonOffset, token).ConfigureAwait(false);
                if (pk.Species == 0)
                {
                    Connection.Log("Invalid data detected. Restarting loop.");
                    // add stuff for recovering
                    continue;
                }

                encounterCount++;
                Connection.Log($"Encounter: {encounterCount}:{Environment.NewLine}{ShowdownSet.GetShowdownText(pk)}{Environment.NewLine}{Environment.NewLine}");
                Counts.AddCompletedLegends();

                if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
                    DumpPokemon(DumpSetting.DumpFolder, "legends", pk);

                if (StopCondition(pk))
                {
                    Connection.Log($"{Ping}Result found! Stopping routine execution; restart the bot(s) to search again.");
                    return;
                }

                Connection.Log("Resetting raid by restarting the game");
                // Close out of the game
                await Click(HOME, 1_600, token).ConfigureAwait(false);
                await Click(X, 0_800, token).ConfigureAwait(false);
                await Click(A, 4_000, token).ConfigureAwait(false); // Closing software prompt
                Connection.Log("Closed out of the game!");

                // Open game and select profile
                await Click(A, 1_000, token).ConfigureAwait(false);
                await Click(A, 1_000, token).ConfigureAwait(false);
                Connection.Log("Restarting the game!");

                // Switch Logo lag, skip cutscene, game load screen
                await Task.Delay(14_000, token).ConfigureAwait(false);
                await Click(A, 1_000, token).ConfigureAwait(false);
                await Task.Delay(3_500, token).ConfigureAwait(false);
                Connection.Log("Back in the overworld!");
                await ResetStick(token).ConfigureAwait(false);
            }
        }

        private async Task DoDogEncounter(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Log("Looking for a new dog...");

                // At the start of each loop, an A press is needed to exit out of a prompt.
                await Click(A, 0_500, token).ConfigureAwait(false);
                await SetStick(LEFT, 0, 30000, 1_000, token).ConfigureAwait(false);

                // Encounters Zacian/Zamazenta
                await Click(A, 0_600, token).ConfigureAwait(false);

                // Cutscene loads
                await Click(A, 2_600, token).ConfigureAwait(false);

                // Click through all the menus.
                while (!await IsInBattle(token).ConfigureAwait(false))
                    await Click(A, 1_000, token).ConfigureAwait(false);

                Log("Encounter started! Checking details...");
                var pk = await ReadPokemon(LegendaryPokemonOffset, token).ConfigureAwait(false);
                if (pk.Species == 0)
                {
                    Log("Invalid data detected. Restarting loop.");
                    continue;
                }

                encounterCount++;
                Log($"Encounter: {encounterCount}{Environment.NewLine}{ShowdownSet.GetShowdownText(pk)}{Environment.NewLine}");
                Counts.AddCompletedLegends();

                if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
                    DumpPokemon(DumpSetting.DumpFolder, "legends", pk);

                if (StopCondition(pk))
                {
                    Log($"{Ping}Result found! Stopping routine execution; restart the bot(s) to search again.");
                    return;
                }

                // Get rid of any stick stuff left over so we can flee properly.
                await ResetStick(token).ConfigureAwait(false);

                // Wait for the entire cutscene.
                await Task.Delay(15_000, token).ConfigureAwait(false);

                // Offsets are flickery so make sure we see it 3 times.
                for (int i = 0; i < 3; i++)
                    await ReadUntilChanged(BattleMenuOffset, BattleMenuReady, 5_000, 0_100, true, token).ConfigureAwait(false);

                Log("Running away...");
                while (await IsInBattle(token).ConfigureAwait(false))
                    await FleeToOverworld(token).ConfigureAwait(false);

                // Extra delay to be sure we're fully out of the battle.
                await Task.Delay(0_250, token).ConfigureAwait(false);
            }
        }

        private async Task<int> StepUntilEncounter(CancellationToken token)
        {
            Log("Walking around until an encounter...");
            int attempts = 0;
            while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.EncounterBot)
            {
                if (!await IsInBattle(token).ConfigureAwait(false))
                {
                    switch (hub.Config.Encounter.EncounteringType)
                    {
                        case EncounterMode.VerticalLine:
                            await SetStick(LEFT, 0, -30000, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset

                            // Quit early if we found an encounter on first sweep.
                            if (await IsInBattle(token).ConfigureAwait(false))
                                break;

                            await SetStick(LEFT, 0, 30000, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset
                            break;
                        case EncounterMode.HorizontalLine:
                            await SetStick(LEFT, -30000, 0, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset

                            // Quit early if we found an encounter on first sweep.
                            if (await IsInBattle(token).ConfigureAwait(false))
                                break;

                            await SetStick(LEFT, 30000, 0, 2_400, token).ConfigureAwait(false);
                            await SetStick(LEFT, 0, 0, 0_100, token).ConfigureAwait(false); // reset
                            break;
                    }

                    attempts++;
                    if (attempts % 10 == 0)
                        Log($"Tried {attempts} times, still no encounters.");
                }

                if (await IsInBattle(token).ConfigureAwait(false))
                    return attempts;
            }

            return -1; // aborted
        }

        private async Task ResetStick(CancellationToken token)
        {
            // If aborting the sequence, we might have the stick set at some position. Clear it just in case.
            await SetStick(LEFT, 0, 0, 0_500, token).ConfigureAwait(false); // reset
        }

        private async Task FleeToOverworld(CancellationToken token)
        {
            // This routine will always escape a battle.
            await Click(DUP, 0_400, token).ConfigureAwait(false);
            await Click(A, 0_400, token).ConfigureAwait(false);
            await Click(B, 0_400, token).ConfigureAwait(false);
            await Click(B, 0_400, token).ConfigureAwait(false);
        }

        private async Task CatchWildPokemon(PK8 pk, CancellationToken token)
        {
            var check = await ReadPokemon(WildPokemonOffset, token).ConfigureAwait(false);
            if (encounterCount != 0 && encounterCount % catchCount == 0)
            {
                Log($"Ran out of Master Balls to catch {SpeciesName.GetSpeciesName(pk.Species, 2)}.");
                if (hub.Config.Encounter.InjectPokeBalls)
                {
                    Log("Restoring original pouch data.");
                    await Connection.WriteBytesAsync(pouchData, PokeBallOffset, token).ConfigureAwait(false);
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
                else
                {
                    Log("Restart the game and the bot(s) or set \"Inject Poké Balls\" to True in the config.");
                    return;
                }
            }

            await Click(B, 1_000, token).ConfigureAwait(false);
            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false); //Throw ball

            await Click(B, 1_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false); //Just in case we didn't

            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false); //Attempt again to be sure
            while (!await IsOnOverworld(hub.Config, token).ConfigureAwait(false) && check.Species != 0)
            {
                await Click(B, 0_400, token).ConfigureAwait(false);
            }

            if (await IsOnOverworld(hub.Config, token).ConfigureAwait(false) && !await IsInBattle(token).ConfigureAwait(false))
                Log($"{Ping}Caught {SpeciesName.GetSpeciesName(pk.Species, 2)} in a Master Ball! Resuming routine...");
        }

        private async Task StrongSpawn(CancellationToken token)
        {
            Log("Closing the game!");
            await Click(HOME, 1_500, token).ConfigureAwait(false);
            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(A, 4_000, token).ConfigureAwait(false); // Closing software prompt
            Log("Closed out of the game!");

            // Open game and select profile.
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            // If they have DLC on the system and can't use it, requires an UP + A to start the game.
            // Should be harmless otherwise since they'll be in loading screen.
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 0_600, token).ConfigureAwait(false);

            // Switch Logo lag, skip cutscene, game load screen
            await Task.Delay(11_000, token).ConfigureAwait(false);

            for (int i = 0; i < 5; i++)
                await Click(A, 1_000, token).ConfigureAwait(false);

            while (!await IsOnOverworld(hub.Config, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);

            Log("Restarted the game!");

            while (!await IsInBattle(token).ConfigureAwait(false))
                await Task.Delay(2_000, token).ConfigureAwait(false);
        }
    }
}