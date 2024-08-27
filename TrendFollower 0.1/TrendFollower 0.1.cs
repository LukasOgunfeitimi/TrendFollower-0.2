using System;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;

namespace cAlgo.Robots {
    /*
        Name: TrendFollower
        Author: Lukas Ogunfeitimi
        Description: Uses ADX and EMA to look gfor trade
    */
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class TrendFollower : Robot {
        /*
        TODO:
        Better tpsl placements?
        */
        [Parameter("Setups per day", DefaultValue = 1)]
        public int SetupsPerDay { get; set; }

        [Parameter("ADX Period", DefaultValue = 17)]
        public int ADXPeriod { get; set; }

        [Parameter("ADX Threshold", DefaultValue = 16)]
        public int ADXThreshold { get; set; }

        [Parameter("EMA Period", DefaultValue = 180)]
        public int EMAPeriod { get; set; }

        [Parameter("EMA Threshold", DefaultValue = 35)]
        public int EMAThreshold { get; set; }

        [Parameter("PropFirm Version", DefaultValue = true)]
        public bool PropFirmVersion { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 0.02, Step = 0.005)]
        public double RiskPerTrade { get; set; }

        [Parameter("SL/TP in Price Difference", DefaultValue = 50)]
        public double SLTPInPriceDifference { get; set; }

        // set to false when backtesting for performance
        [Parameter("Debug", DefaultValue = true)]
        public bool Debug { get; set; }

        [Parameter("Webhook", DefaultValue = false)]
        public bool WebhookActivated { get; set; }

        // Exponential Moving Average: Shows which direction the trend is
        private IndicatorDataSeries EMA { get; set; }
        // Average Directional Index: Shows how strong the trend is
        private IndicatorDataSeries ADX { get; set; }

        private SupportResistance SR;

        public double SLTPInPips;
        public double SLTPInPipsHalf { get => SLTPInPips / 2; }

        public MetricTracker MetricTracker;
        public PositionTracker PositionTracker;
        public SetupAnalyser SetupAnalyser;
        public Webhook Webhook;

        private double DailySetupsTaken = 0;
        private DateTime LastTradeClose { get; set; }

        private void Log(string msg) { if (Debug) Print(msg); Webhook.SendWebhookMessage(msg); }
        private void CloseAll() { foreach (var pos in Positions) ClosePositionAsync(pos); }
        public void AllPositions(Action<Position> c) { foreach (var pos in Positions) if (pos.Comment != null && pos.Comment.Contains("algo")) c(pos); }

        protected override void OnStart() {
            //var result = System.Diagnostics.Debugger.Launch();

            LastTradeClose = DateTime.MinValue;
            Positions.Closed += OnPositionClosed;

            SLTPInPips = SLTPInPriceDifference / Symbol.PipSize;

            MetricTracker = new(this);
            PositionTracker = new(this);
            SetupAnalyser = new(this);
            Webhook = new(WebhookActivated);

            MetricTracker.TotalDrawdownActivated = PropFirmVersion;
            MetricTracker.DailyDrawdownActivated = PropFirmVersion;

            ADX = Indicators.AverageDirectionalMovementIndexRating(ADXPeriod).ADX;
            EMA = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EMAPeriod).Result;
            SR = Indicators.GetIndicator<SupportResistance>(15, 100);
            Log("Started");
        }
        public void Terminate(string TerminationReason) {
            Log(TerminationReason);
            CloseAll();
            Stop();
        }
        protected override void OnTick() {
            // 2 minutes before market closes
            // close all trades due to market gaps
            TimeSpan TimeTillClose = Symbol.MarketHours.TimeTillClose();
            if (TimeTillClose <= TimeSpan.FromMinutes(2) && TimeTillClose > TimeSpan.Zero) {
                if (Positions.Where(pos => pos.Comment.Contains("algo")).Count() > 0) {
                    Log("Closing all positions for market close");
                    CloseAll(); // maybe if its a good broker and they dont have gaps we dont need to do this
                }
                DailySetupsTaken = 0;
            }

            MetricTracker.CheckMetrics(Terminate);
            PositionTracker.CheckPositions();


            int latestIndex = Bars.Count - 1;
            // if the price reverses 
            // back to the ema then close the pos early
            AllPositions((pos) =>{
                TradeType direction = pos.TradeType;
                double EMAValue = EMA[latestIndex];
                if (
                    direction == TradeType.Buy  && Bid < EMAValue || 
                    direction == TradeType.Sell && Ask > EMAValue
                   ) {
                        Webhook.SendWebhookMessage("Position " + pos.Id + " reversed back to EMA " + EMAValue);
                        ClosePositionAsync(pos);
                   }
            });
        }
        
        protected void OnPositionClosed(PositionClosedEventArgs args) {
            Position pos = args.Position;
            if (pos.Comment != null && !pos.Comment.Contains("algo")) return;
            
            LastTradeClose = Server.Time;
            
            double PosMaxDrawdown = PositionTracker.LocalPositions[pos.Id].MaxDrawdown;

            Webhook.SendWebhookMessage(pos.Id + " closed with " + pos.NetProfit + " max drawdown " + PosMaxDrawdown);
            
            PositionTracker.LocalPositions.Remove(pos.Id);
             
        }
        protected override void OnBar() {
            int latestIndex = Bars.Count - 1;

            // going from the latest ADX value
            int decreasingCounter = 0;
            for (int i = latestIndex; i > (latestIndex - 20); i--) {
                // if the ADX value before is greater than
                // current then say the trend is dying
                if (ADX[i] < ADX[i - 1]) decreasingCounter++;
                // if theres 5 occurencies then ignore
                if (decreasingCounter == 5) return;
            }
            
            double diff = Bars[latestIndex].Close - EMA[latestIndex];
            
            if (
                ADX[latestIndex] >= ADXThreshold && 
                Math.Abs(diff) > EMAThreshold    && 
                DailySetupsTaken < SetupsPerDay  &&
                Positions.Where(pos => pos.Comment.Contains("algo")).Count() == 0 &&
                (Bars[latestIndex].OpenTime - LastTradeClose).TotalMinutes > 60
               ) {                
                string Comment = "algo";
                
                DailySetupsTaken++;
                
                TradeType direction = diff > 0 ? TradeType.Buy : TradeType.Sell; 
                
                SR.AnalyzeHistoricalData(Bars.Count - 1);
                SLTP sltp = SetupAnalyser.AnalyseTrade(direction, SR, Symbol.PipSize, direction == TradeType.Buy ? Ask : Bid);
                Comment += sltp.comment;
                double RiskAmount = MetricTracker.CanTradeLossAmount / SetupsPerDay;
                if (MetricTracker.ReducedRiskActive) RiskAmount *= MetricTracker.ReducedRiskMultipler;
                
                double Volume = Symbol.VolumeForFixedRisk(RiskAmount, 1000);

                //double Volume = RiskAmount / (SLTPInPips * Symbol.PipSize);
                
                //Comment += " ADX: " + Math.Round(ADX[latestIndex], 2) + " " + Math.Abs(Math.Round(diff,2));
                //Comment += sltp.comment;

                while (Volume > 0) {
                    double OrderVolume = Math.Min(Volume, Symbol.VolumeInUnitsMax);
                    ExecuteMarketOrder(direction, SymbolName, OrderVolume, null, 1000, sltp.TP, Comment);
                    Volume -= OrderVolume;
                }
                
                Webhook.SendWebhookMessage(Volume, direction, Comment, "Order placed");

                MetricTracker.DailyTradesTaken++;
            }
        }
        protected override void OnStop() {
            Log("Bot stopped");
        }
        protected override void OnError(Error err) {
            Log("Errored out " + err);
        }
    }

   
}

/*
        // Token: 0x06000A3E RID: 2622 RVA: 0x0001B308 File Offset: 0x00019508
        public decimal? VolumeForFixedRisk(string quoteAssetName, decimal amount, decimal stopLossInPips, decimal pipSize) {
            decimal? num = this._smallAssetConverter.Convert(amount, this._smallEnvironment.Account.Currency, quoteAssetName, false);
            if (num == null) {
                return null;
            }
            return new decimal?(num.Value / (Math.Abs(stopLossInPips) * pipSize));
        }
        // Token: 0x06000A40 RID: 2624 RVA: 0x0001B37C File Offset: 0x0001957C
        public decimal? AmountRisked(decimal volume, decimal stopLossInPips, decimal pipSize, string quoteAssetName) {
            decimal value = volume * Math.Abs(stopLossInPips) * pipSize;
            return this._smallAssetConverter.Convert(value, quoteAssetName, this._smallEnvironment.Account.Currency, false).Round(this._smallEnvironment.Account.Digits);
        }

*/  