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
    public class TrendFollower: Robot {
        /*
        TODO:
        Better tpsl placements?
        */
        [Parameter("Setups per day", DefaultValue = 1)]
        public int SetupsPerDay {get; set;}
        
        [Parameter("ADX Period", DefaultValue = 17)]
        public int ADXPeriod {get; set;}
        
        [Parameter("ADX Threshold", DefaultValue = 16)]
        public int ADXThreshold {get; set;}

        [Parameter("EMA Period", DefaultValue = 180)]
        public int EMAPeriod {get; set;}

        [Parameter("EMA Threshold", DefaultValue = 35)]
        public int EMAThreshold {get; set;}

        [Parameter("PropFirm Version", DefaultValue = true)]
        public bool PropFirmVersion {get; set;}
        
        [Parameter("Risk per trade (%)", DefaultValue = 0.02, Step = 0.005)]
        public double RiskPerTrade {get; set;}
        
        [Parameter("SL/TP in Price Difference", DefaultValue = 50)]
        public double SLTPInPriceDifference {get; set;}
        
        // set to false when backtesting for performance
        [Parameter("Debug", DefaultValue = true)]
        public bool Debug {get; set;}
        
        [Parameter("Webhook", DefaultValue = false)]
        public bool WebhookActivated {get; set;}
        
        // Exponential Moving Average: Shows which direction the trend is
        private IndicatorDataSeries EMA {get; set;}
        // Average Directional Index: Shows how strong the trend is
        private IndicatorDataSeries ADX {get; set;}
        
        private SupportResistance SR;
       
        public double SLTPInPips;
        public double SLTPInPipsHalf { get => SLTPInPips / 2; }
        
        private struct SLTP {
            public double SL;
            public double TP;
            public string comment;
        }
        
        private struct SRMetrics {
            public int UptrendCounter = 0;
            public int DowntrendCounter = 0;
            public int NeutralCounter = 0;
            public override string ToString() {
                return " [" + UptrendCounter +
                       " - "+ DowntrendCounter +
                       " - " + NeutralCounter + "]";
            }
        }
        
        private MetricTracker MetricTracker;
        public Webhook Webhook;
                
        private double DailySetupsTaken = 0;
        private DateTime LastTradeClose {get; set;}
        
        private void Log(string msg) { if (Debug) Print(msg); Webhook.SendWebhookMessage(msg); }
        private void CloseAll() { foreach(var pos in Positions) ClosePositionAsync(pos);  }
        public void AllPositions(Action<Position> c) { foreach(var pos in Positions) if (pos.Comment != null && pos.Comment.Contains("algo")) c(pos); }
        
        protected override void OnStart() {
            //var result = System.Diagnostics.Debugger.Launch();

            LastTradeClose = DateTime.MinValue;
            Positions.Closed += OnPositionClosed;
            
            SLTPInPips = SLTPInPriceDifference / Symbol.PipSize;

            MetricTracker = new(this);
            Webhook = new(WebhookActivated);
            
            MetricTracker.TotalDrawdownActivated = PropFirmVersion;
            MetricTracker.DailyDrawdownActivated = PropFirmVersion;
            
            ADX = Indicators.AverageDirectionalMovementIndexRating(ADXPeriod).ADX;
            EMA = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EMAPeriod).Result;
            SR  = Indicators.GetIndicator<SupportResistance>(15);
            Log("Started"); 
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

            MetricTracker.CheckMetrics((message) => {
                Log(message);
                CloseAll();
                Stop();
            });

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
            
            double PosMaxDrawdown = MetricTracker.LocalPositions[pos.Id].MaxDrawdown;

            Webhook.SendWebhookMessage(pos.Id + " closed with " + pos.NetProfit + " max drawdown " + PosMaxDrawdown);
            
            MetricTracker.LocalPositions.Remove(pos.Id);
             
        }
        private SLTP CalculateSLTP(TradeType direction) {
            List<Bar> PreviousHighs = SR.PreviousHighs;
            List<Bar> PreviousLows = SR.PreviousLows;
            
            SLTP sltp = new();
            
            SRMetrics highs = new();
            SRMetrics lows = new();
            
            int Lookup = Math.Min(PreviousHighs.Count - 2, PreviousLows.Count - 2);
            
            for (int i = 0; i < Math.Min(Lookup, 10); i++) {
                double diffHighs = (PreviousHighs[i].High - PreviousHighs[i + 1].High) / Symbol.PipSize;
                if (diffHighs >= 1000) highs.UptrendCounter++;
                else if (diffHighs <= -1000) highs.DowntrendCounter++;
                else highs.NeutralCounter++;
                Log(PreviousLows[i].Low.ToString());
                Log(PreviousLows[i].Low.ToString());
                double diffLows = (PreviousLows[i].Low - PreviousLows[i + 1].Low) / Symbol.PipSize;
                if (diffLows >= 1000) lows.UptrendCounter++;
                else if (diffLows <= -1000) lows.DowntrendCounter++;
                else lows.NeutralCounter++;
            }
            
            Log(highs.ToString());
            Log(lows.ToString());
            sltp.comment = highs.ToString() + " " + lows.ToString();
            // Calculate TP
            // If the latest pivot point is reachable use that
            // if not use 100 pip

            if (direction == TradeType.Buy) {
                double diff = (PreviousHighs.First().High - Ask) / Symbol.PipSize;
                if (diff > 1000) sltp.TP = diff;
                else sltp.TP = 1000;
            } else {
                double diff = (Bid - PreviousLows.First().Low) / Symbol.PipSize;
                if (diff > 1000) sltp.TP = diff;
                else sltp.TP = 1000;
            }
            
            // Calculate SL
            // If the latest pivot point is under 100 pip use that
            // if not use 100 pip
            if (direction == TradeType.Buy) {
                double diff = (Bid - PreviousLows.First().Low) / Symbol.PipSize;
                if (diff < 1000) sltp.SL = diff;
                else sltp.SL = 1000;
            } else {
                double diff = (PreviousHighs.First().High - Ask) / Symbol.PipSize;
                if (diff < 1000) sltp.SL = diff;
                else sltp.SL = 1000;
            }
            
            return sltp;
            
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
                
                SR.AnalyzeHistoricalData(1000);
                
                SLTP sltp = CalculateSLTP(direction);
                
                double RiskAmount = MetricTracker.CanTradeLossAmount / SetupsPerDay;
                if (MetricTracker.ReducedRiskActive) RiskAmount *= MetricTracker.ReducedRiskMultipler;
                
                double Volume = Symbol.VolumeForFixedRisk(RiskAmount, sltp.SL);

                //double Volume = RiskAmount / (SLTPInPips * Symbol.PipSize);
                
                //Comment += " ADX: " + Math.Round(ADX[latestIndex], 2) + " " + Math.Abs(Math.Round(diff,2));
                Comment += sltp.comment;
                while (Volume > 0) {
                    double OrderVolume = Math.Min(Volume, Symbol.VolumeInUnitsMax);
                    ExecuteMarketOrder(direction, SymbolName, OrderVolume, null, sltp.SL, sltp.TP, Comment);
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

    public class MetricTracker {
        public TrendFollower bot;     
        
        private double Balance       => this.bot.Account.Balance;
        private double Equity        => this.bot.Account.Equity;
        private Timer Timer          => this.bot.Timer;
        private IServer Server       => this.bot.Server;
        private Symbol Symbol        => this.bot.Symbol;
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
        public int    DailyTradesTaken = 0;

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
        public bool   DailyDrawdownActivated = true;
        
        // Prop firm 6%, so let do 4%
        public double StartingBalance;
        public double TotalDrawdown;
        public double TotalDrawdownPercentage = 0.04;
        public bool   TotalDrawdownActivated = true;
        
        // if we go under 1% equity under the starting balance reduce risk
        public double ReduceRiskDrawdownPercentage = 0.01;
        public double ReduceRiskDrawdownAmount;
        public double ReducedRiskMultipler = 0.5; // Reduce risk in half
        public bool   ReducedRiskActive = false;
        
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
            this.bot.AllPositions((pos) => {
                double ProfitPips = ProfitInPips(pos.TradeType, pos.EntryPrice, pos.CurrentPrice);
                
                if (!LocalPositions.ContainsKey(pos.Id)) {
                    LocalPositions[pos.Id] = new PositionTracker(pos, false, false);
                }
                PositionTracker P = LocalPositions[pos.Id];
                
                // if we go into half of total target mark it
                if (!P.ReachedProfitThreshold && ProfitPips >= this.bot.SLTPInPipsHalf) {
                    P.ReachedProfitThreshold = true;
                } 
                // if we got into drawdown without seeing profit close early
                else if (!P.ReachedProfitThreshold && ProfitPips <= -this.bot.SLTPInPipsHalf){
                    this.bot.ClosePositionAsync(pos);
                }
                
                P.MaxDrawdown = Math.Min(P.MaxDrawdown, pos.NetProfit);
                
                LocalPositions[pos.Id] = P;
            });
            
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
            return Direction == TradeType.Sell ? -pips: pips;
        }
    }
    
    public class Webhook {
        private readonly bool Activated;
        private readonly HttpClient _httpClient;
        private const string _webhookUrl = "https://discord.com/api/webhooks/1259628968508788908/Fu60NYaw7JMIeKYpSn0Q-kFVkPQyrE5vFlUjt0Ozt2aAetvAiofBfZaeRbxzHTU9kau9";
        public Webhook(bool Activated) {
            this.Activated = Activated;
            _httpClient = new HttpClient();
        }
        public void SendWebhookMessage(double Volume, TradeType direction, string comment, string message) {
            SendWebhookMessage(direction + " " + message + " Volume " + Volume + " Info " + comment); 
        }
        public async void SendWebhookMessage(string message) {
            if(!Activated) return;
            
            string jsonPayload = "{\"username\":\"TrendFollower\",\"avatar_url\":\"\",\"content\":\"<@109213512366071808> " + message + "\"}";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(_webhookUrl, content);
        }
    }
}

/*
        // Token: 0x06000A3E RID: 2622 RVA: 0x0001B308 File Offset: 0x00019508
        public decimal? VolumeForFixedRisk(string quoteAssetName, decimal amount, decimal stopLossInPips, decimal pipSize)
        {
            decimal? num = this._smallAssetConverter.Convert(amount, this._smallEnvironment.Account.Currency, quoteAssetName, false);
            if (num == null)
            {
                return null;
            }
            return new decimal?(num.Value / (Math.Abs(stopLossInPips) * pipSize));
        }
        // Token: 0x06000A40 RID: 2624 RVA: 0x0001B37C File Offset: 0x0001957C
        public decimal? AmountRisked(decimal volume, decimal stopLossInPips, decimal pipSize, string quoteAssetName)
        {
            decimal value = volume * Math.Abs(stopLossInPips) * pipSize;
            return this._smallAssetConverter.Convert(value, quoteAssetName, this._smallEnvironment.Account.Currency, false).Round(this._smallEnvironment.Account.Digits);
        }

*/