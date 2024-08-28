using cAlgo.API;
using cAlgo.API.Internals;
using System;
using System.Collections.Generic;

namespace cAlgo.Robots {
    public class PositionTracker {
        private readonly TrendFollower bot;

        private double Balance => this.bot.Account.Balance;
        private double Equity => this.bot.Account.Equity;
        private Timer Timer => this.bot.Timer;
        private IServer Server => this.bot.Server;
        private Symbol Symbol => this.bot.Symbol;

        private void Log(object msg) { if (this.bot.Debug) this.bot.Print(msg?.ToString()); }

        private double TrailStopPips = 500;
        public struct PositionManager {
            public Position Position;
            public bool ReachedProfitThreshold = false;
            public bool ClosedHalf = false;
            public double MaxDrawdown = double.PositiveInfinity;
            public PositionManager(Position p, bool R, bool C) {
                Position = p;
                ReachedProfitThreshold = R;
                ClosedHalf = C;
            }
        }
        public readonly Dictionary<int, PositionManager> LocalPositions = new();

        public PositionTracker(TrendFollower bot) {
            this.bot = bot;
        }

        public void CheckPositions() {
            bot.AllPositions((pos) => {
                double ProfitPips = ProfitInPips(pos.TradeType, pos.EntryPrice, pos.CurrentPrice);

                if (!LocalPositions.ContainsKey(pos.Id)) {
                    LocalPositions[pos.Id] = new PositionManager(pos, false, false);
                }
                PositionManager P = LocalPositions[pos.Id];

                // if we go into half of total target mark it
                if (!P.ReachedProfitThreshold && ProfitPips >= this.bot.SLTPInPipsHalf) {
                    //Log(pos.EntryPrice + " " + pos.CurrentPrice + " " + ProfitPips + " " + bot.SLTPInPipsHalf);
                    //ClosePartial(pos, 0.5);
                    //BreakEven(pos);
                    P.ReachedProfitThreshold = true;
                }
                // if we got into drawdown without seeing profit close early
                else if (!P.ReachedProfitThreshold && ProfitPips <= -this.bot.SLTPInPipsHalf) {
                    //this.bot.ClosePositionAsync(pos);

                }

                P.MaxDrawdown = Math.Min(P.MaxDrawdown, pos.Pips);

                LocalPositions[pos.Id] = P;
            });

        }
        public double CalculateLotsByDesiredMargin(double marginPercentage) {
            double RequiredMargin = Equity / (marginPercentage / 100);
            double volume = (RequiredMargin * Symbol.DynamicLeverage[0].Leverage) / Symbol.Bid;

            return Symbol.VolumeInUnitsToQuantity(volume);
        }
        private double GetUpdatedDrawdown(double num, double percentage) {
            return Math.Round(num * (1 - percentage), 2);
        }
        public void ClosePartial(Position pos, double ClosePercentage) {
            double Volume = Symbol.NormalizeVolumeInUnits(pos.VolumeInUnits / 2);
            bot.ClosePositionAsync(pos, Volume);
        }
        public void BreakEven(Position pos) {
            pos.ModifyStopLossPips(-250);
        }
        private double ProfitInPips(TradeType Direction, double Entry, double Current) {
            double pips = (Current - Entry) / Symbol.PipSize;
            return Direction == TradeType.Sell ? -pips : pips;
        }
    }
}


/*
 * Trailstop
 *                 if (P.ReachedProfitThreshold) {
                    if (pos.TradeType == TradeType.Buy) {
                        if ((Symbol.Bid - pos.EntryPrice) / Symbol.PipSize > TrailStopPips) {
                            double StopLossPips = Symbol.Bid - (double)pos.StopLoss / Symbol.PipSize;
                            if (StopLossPips > TrailStopPips) {
                                Log("Trailed stop " + pos.NetProfit);
                                pos.ModifyStopLossPrice((Symbol.Bid - (TrailStopPips * Symbol.PipSize)));
                            }
                        }
                    } else {
                        if ((pos.EntryPrice - Symbol.Ask) / Symbol.PipSize > TrailStopPips) {
                            double StopLossPips = ((double)pos.StopLoss - Symbol.Ask) / Symbol.PipSize;
                            if (StopLossPips > TrailStopPips) {
                                Log("Trailed stop " + pos.NetProfit);
                                pos.ModifyStopLossPrice((Symbol.Ask + (TrailStopPips * Symbol.PipSize)));
                            }
                        }
                    }
                }
*/