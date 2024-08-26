using cAlgo.API.Internals;
using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cAlgo.Robots
{
    public class MetricTracker {
        private readonly TrendFollower bot;

        private double Balance => this.bot.Account.Balance;
        private double Equity => this.bot.Account.Equity;
        private Timer Timer => this.bot.Timer;
        private IServer Server => this.bot.Server;
        private Symbol Symbol => this.bot.Symbol;
        private void Log(object msg) { if (this.bot.Debug) this.bot.Print(msg?.ToString()); }

        public struct PositionTracker {
            public Position Position;
            public bool ReachedProfitThreshold = false;
            public bool ClosedHalf = false;
            public double MaxDrawdown = 0;
            public PositionTracker(Position p, bool R, bool C) {
                Position = p;
                ReachedProfitThreshold = R;
                ClosedHalf = C;
            }
        }
        public readonly Dictionary<int, PositionTracker> LocalPositions = new();

        // the lowest equity recorded for the day
        // will be used for risk managment
        public double DailyStartEquity;
        public double DailyLowestEquity;
        public int DailyTradesTaken = 0;

        // 21:00 UTC 1of1funding
        // this will be for tracking metrics
        // need to change for different propfirms
        public TimeSpan ResetTime = new(21, 0, 0);

        // risking 2% per day
        public double CanTradeDrawdown;
        public double CanTradeEquityPercentage;
        public double CanTradeLossAmount; // how much left, update this after each trade

        // Prop firm 4%, so lets do 3%
        public double DailyDrawdown;
        public double DailyDrawdownPercentage = 0.03;
        public bool DailyDrawdownActivated = true;

        // Prop firm 6%, so let do 4%
        public double StartingBalance;
        public double TotalDrawdown;
        public double TotalDrawdownPercentage = 0.04;
        public bool TotalDrawdownActivated = true;

        // if we go under 1% equity under the starting balance reduce risk
        public double ReduceRiskDrawdownPercentage = 0.01;
        public double ReduceRiskDrawdownAmount;
        public double ReducedRiskMultipler = 0.5; // Reduce risk in half
        public bool ReducedRiskActive = false;

        public MetricTracker(TrendFollower bot) {
            this.bot = bot;

            CanTradeEquityPercentage = bot.RiskPerTrade;

            Timer.Start(ResetTimeInMS());
            Timer.TimerTick += NewDay;

            StartingBalance = Balance;
            TotalDrawdown = GetUpdatedDrawdown(StartingBalance, TotalDrawdownPercentage);
            ReduceRiskDrawdownAmount = StartingBalance * ReduceRiskDrawdownPercentage;

            UpdateDailydrawdrown();
        }

        public void CheckMetrics(Action<string> StopTradingCallback) {
            DailyLowestEquity = Math.Min(DailyLowestEquity, Equity);

            if (DailyDrawdownActivated && Equity <= DailyDrawdown)
                StopTradingCallback("daily drawdown breached. DailyDrawdown: " + DailyDrawdown + " Equity: " + Equity);
            else if (TotalDrawdownActivated && Equity <= TotalDrawdown)
                StopTradingCallback("total drawdown breached. TotalDrawdown: " + TotalDrawdown + " Equity: " + Equity);
            else if (!ReducedRiskActive && Equity <= (StartingBalance - ReduceRiskDrawdownAmount)) {
                Log("Reducing risk " + Equity);
                ReducedRiskActive = true;
            } else if (ReducedRiskActive && Equity >= (StartingBalance + ReduceRiskDrawdownAmount)) {
                Log("Resetting risk " + Equity);
                ReducedRiskActive = false;
            }

        }
        protected void NewDay() {
            Timer.Stop();

            double HighestEquityDrawdownPercentage = Math.Round(((DailyStartEquity - DailyLowestEquity) / DailyStartEquity) * 100, 2);
            Log("-------------------------");
            Log("Daily start equity: " + DailyStartEquity);
            Log("Daily lowest equity: " + DailyLowestEquity + " -" + HighestEquityDrawdownPercentage + "%");
            Log("Daily end equity: " + Equity);
            Log("Daily trades taken: " + DailyTradesTaken);
            UpdateDailydrawdrown();
            DailyTradesTaken = 0;

            Timer.Start(ResetTimeInMS());
        }

        private void UpdateDailydrawdrown() {
            DailyDrawdown = GetUpdatedDrawdown(Equity, DailyDrawdownPercentage);

            CanTradeDrawdown = GetUpdatedDrawdown(Equity, CanTradeEquityPercentage);
            CanTradeLossAmount = Balance - CanTradeDrawdown;

            DailyStartEquity = Equity;
            DailyLowestEquity = Equity;
        }
        private int ResetTimeInMS() {
            // Get the current time
            DateTime CT = Server.TimeInUtc;
            // Make a new DateTime with the current date but the time is when the next reset time is
            DateTime NewResetTime = new(CT.Year, CT.Month, CT.Day, ResetTime.Hours, ResetTime.Minutes, ResetTime.Seconds);
            // if the curent time already passed the reset time then its the next day
            // this should only occur once when we start the bot
            if (CT.TimeOfDay >= ResetTime)
                NewResetTime = NewResetTime.AddDays(1);

            // the difference between current date/time and the current date and reset time is how many
            // seconds until the next reset date/time is
            return (int)(NewResetTime - CT).TotalSeconds + 1;
        }
        public double CalculateLotsByDesiredMargin(double marginPercentage) {
            double RequiredMargin = Equity / (marginPercentage / 100);
            double volume = (RequiredMargin * Symbol.DynamicLeverage[0].Leverage) / Symbol.Bid;

            return Symbol.VolumeInUnitsToQuantity(volume);
        }
        private double GetUpdatedDrawdown(double num, double percentage) {
            return Math.Round(num * (1 - percentage), 2);
        }
        public void BreakEven(Position pos) {
            if (pos.EntryPrice != pos.StopLoss)
                pos.ModifyStopLossPrice(pos.EntryPrice);
        }
        private double ProfitInPips(TradeType Direction, double Entry, double Current) {
            double pips = (Current - Entry) / Symbol.PipSize;
            return Direction == TradeType.Sell ? -pips : pips;
        }
    }
}
