using System;
using PKHeX.Core;

namespace SysBot.Pokemon
{
    class EncounterCount
    {
        private int Master, Poke, Beast, Dive, Dream, Dusk, Fast, Friend, Great, Heal, Heavy, Level, Love, Lure, Luxury, Moon, Nest, Net, Premier, Quick, Repeat, Timer, Ultra;


        internal static readonly ushort[] Pouch_Ball_SWSH =
        {
            001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012, 013, 014, 015, 016,
            492, 493, 494, 495, 496, 497, 498, 499, 500,
            576,
            851,
        };

        private static InventoryPouch8 GetBallPouch(byte[] ballBlock)
        {
            var pouch = new InventoryPouch8(InventoryType.Balls, Pouch_Ball_SWSH, 999, 0, 28);
            pouch.GetPouch(ballBlock);
            return pouch;
        }

        public static EncounterCount GetBallCounts(byte[] ballBlock)
        {
            var pouch = GetBallPouch(ballBlock);
            return ReadCounts(pouch);
        }

        private static EncounterCount ReadCounts(InventoryPouch pouch)
        {
            var counts = new EncounterCount();
            foreach (var ball in pouch.Items)
                counts.SetCount(ball.Index, ball.Count);
            return counts;
        }

        private void SetCount(int ball, int count)
        {
            if (ball == 1)
                Master = count; //Regular balls
            if (ball == 2)
                Ultra = count;
            if (ball == 3)
                Great = count;
            if (ball == 4)
                Poke = count;
            if (ball == 6)
                Net = count;
            if (ball == 7)
                Dive = count;
            if (ball == 8)
                Nest = count;
            if (ball == 9)
                Repeat = count;
            if (ball == 10)
                Timer = count;
            if (ball == 11)
                Luxury = count;
            if (ball == 12)
                Premier = count;
            if (ball == 13)
                Dusk = count;
            if (ball == 14)
                Heal = count;
            if (ball == 15)
                Quick = count;
            if (ball == 492)
                Fast = count; //Apriballs
            if (ball == 493)
                Level = count;
            if (ball == 494)
                Lure = count;
            if (ball == 495)
                Heavy = count;
            if (ball == 496)
                Love = count;
            if (ball == 497)
                Friend = count;
            if (ball == 498)
                Moon = count;
            if (ball == 576)
                Dream = count;
            if (ball == 851)
                Beast = count;
        }

        public int PossibleCatches(Ball ball)
        {
            return ball switch
            {
                Ball.Master => Master, Ball.Poke => Poke, Ball.Beast => Beast,
                Ball.Dive => Dive, Ball.Dream => Dream, Ball.Dusk => Dusk,
                Ball.Fast => Fast, Ball.Friend => Friend, Ball.Great => Great,
                Ball.Heal => Heal, Ball.Heavy => Heavy, Ball.Level => Level,
                Ball.Love => Love, Ball.Lure => Lure, Ball.Luxury => Luxury,
                Ball.Moon => Moon, Ball.Nest => Nest, Ball.Net => Net,
                Ball.Premier => Premier, Ball.Quick => Quick, Ball.Repeat => Repeat, 
                Ball.Timer => Timer, Ball.Ultra => Ultra,
                _ => throw new ArgumentOutOfRangeException(nameof(Ball))
            };
        }

        public int BallIndex(Ball ball, out int result)
        {
            result = (int)ball;
            if (ball == Ball.Fast)
                result = 492;
            if (ball == Ball.Level)
                result = 493;
            if (ball == Ball.Lure)
                result = 494;
            if (ball == Ball.Heavy)
                result = 495;
            if (ball == Ball.Love)
                result = 496;
            if (ball == Ball.Friend)
                result = 497;
            if (ball == Ball.Moon)
                result = 498;
            if (ball == Ball.Dream)
                result = 576;
            if (ball == Ball.Beast)
                result = 851;

            return result;
        }
    }
}
