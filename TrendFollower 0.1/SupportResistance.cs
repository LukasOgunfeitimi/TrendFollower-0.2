using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using System;
using System.Collections.Generic;
using System.Linq;
namespace cAlgo.Robots {
    [Indicator(AccessRights = AccessRights.FullAccess, AutoRescale = false, ScalePrecision = 2, IsOverlay = true)]
    public class SupportResistance: Indicator {

        [Parameter("Historical bars amount", DefaultValue = 15)]
        public int BarsLookup { get; set; }
        
        [Parameter("Midpoint EMA Period", DefaultValue = 100)]
        public int MidPointEMAPeriod {get; set;}

        [Output("PreviousHighs")]
        public List<Bar> PreviousHighs;
        
        [Output("PreviousLows")]
        public List<Bar> PreviousLows;
        
        [Output("PreviousHighLevels")]
        public List<Level> PreviousHighLevels;
        
        [Output("PreviousLowLevels")]
        public List<Level> PreviousLowLevels;

        [Output("LevelsMA", LineColor = "Blue")]
        public IndicatorDataSeries LevelsMA { get; set; }

        [Output("LevelsMAUptrend", LineColor = "Green", PlotType = PlotType.DiscontinuousLine)]
        public IndicatorDataSeries LevelsMAUptrend { get; set; }

        [Output("LevelsMADowntrend", LineColor = "Red", PlotType = PlotType.DiscontinuousLine)]
        public IndicatorDataSeries LevelsMADowntrend { get; set; }

        [Output("LevelsMANeutral", LineColor = "Yellow", PlotType = PlotType.DiscontinuousLine)]
        public IndicatorDataSeries LevelsMANeutral { get; set; }

        private double MomentumThreshold = 0.5;
        private double Alpha;

        public struct Level {
            public Bar Min = new();
            public Bar Max = new();
            public Bar Earilest = new();
            public Bar Latest = new();
            public List<Bar> Bars = new();
            public Level() { }
            public void Add(Bar bar) {
                Bars.Add(bar);
                if (bar.High < Min.High || Min.OpenTime == DateTime.MinValue) Min = bar;
                if (bar.High > Max.High) Max = bar;
                if (bar.OpenTime < Earilest.OpenTime || Earilest.OpenTime == DateTime.MinValue) Earilest = bar;
                if (bar.OpenTime > Latest.OpenTime || Latest.OpenTime == DateTime.MinValue) Latest = bar;
                
            }
        }
                
        public override void Calculate(int index) {}

        protected override void Initialize() {
            //System.Diagnostics.Debugger.Launch();
            Alpha = 2.0 / (double)checked(MidPointEMAPeriod + 1);
            AnalyzeHistoricalData(5000);
            CalculateLevelMA(5000);
        }

        public void GetTrend(double Current, double Previous, int index) {
            LevelsMAUptrend[index] = double.NaN;
            LevelsMADowntrend[index] = double.NaN;
            LevelsMANeutral[index] = double.NaN;

            double Momentum = Math.Abs(Previous - Current);

            if (Previous > Current && Momentum > MomentumThreshold)
                LevelsMAUptrend[index] = Current;
            else if (Previous < Current && Momentum > MomentumThreshold)
                LevelsMADowntrend[index] = Current;
            else
                LevelsMANeutral[index] = Current;

        }
        // Same calculations as EMA
        public void CalculateLevelMA(int BarsLookBack) {
            int HighCounter = 0;
            int LowCounter = 0;

            int latestIndex = Bars.Count - 1;

            for (int i = latestIndex; i > (latestIndex - BarsLookBack); i--) {
                Bar high = PreviousHighs[HighCounter];
                Bar low = PreviousLows[LowCounter];
                Bar CurrentBar = Bars[i];

                double MidPoint = Math.Round((high.High + low.Low) / 2, 2);

                double PreviousMA = i == latestIndex ? Bars[latestIndex].Close : LevelsMA[i + 1];
                double NewMA = MidPoint * Alpha + PreviousMA * (1 - Alpha);

                LevelsMA[i] = NewMA;

                GetTrend(NewMA, PreviousMA, i);
                
                if (high == CurrentBar && PreviousHighs.Count - 1 > HighCounter + 1)
                        HighCounter++;
                
                if (low == CurrentBar && PreviousLows.Count - 1 > LowCounter + 1)
                        LowCounter++;

            }
        }

        public void AnalyzeHistoricalData(int BarsLookBack) {
            PreviousHighs = new();
            PreviousLows = new();
            
            PreviousHighLevels = new();
            PreviousLowLevels = new();

            int latestIndex = Bars.Count - 1;
            for (int i = latestIndex; i > (latestIndex - BarsLookBack); i--) {

                if (GetPivot(Bars.HighPrices, i, BarsLookup, true)) {
                    PreviousHighs.Add(Bars[i]);
                    DrawLine("Resistance_Historical_" + i, Bars[i], Color.Green);
                }

                if (GetPivot(Bars.LowPrices, i, BarsLookup, false)) {
                    PreviousLows.Add(Bars[i]);
                    DrawLine("Support_Historical_" + i, Bars[i], Color.Red);
                }
            }
            double LevelThreshold = 1000 * Symbol.PipSize; // how many pips far away can it be before it cant be in the level

            for (int i = 0; i < PreviousHighs.Count - 1; i++) {
                double High = PreviousHighs[i].High;
                double NextHigh = PreviousHighs[i + 1].High;
                
                double HighDiff = High - NextHigh;
                if (Math.Abs(HighDiff) <= LevelThreshold) {
                    Level NewLevel = new();
                    NewLevel.Add(PreviousHighs[i]);
                    NewLevel.Add(PreviousHighs[i + 1]);
                    PreviousHighLevels.Add(NewLevel);
                }
                
            }

            for (int i = 0; i < PreviousLows.Count - 1; i++) {
                double Low = PreviousLows[i].Low;
                double NextLow = PreviousLows[i + 1].Low;

                double LowDiff = Low - NextLow;
                if (Math.Abs(LowDiff) <= LevelThreshold) {
                    Level NewLevel = new();
                    NewLevel.Add(PreviousLows[i]);
                    NewLevel.Add(PreviousLows[i + 1]);
                    PreviousLowLevels.Add(NewLevel);
                }
            }

            int counter = 0;
            foreach (var Level in PreviousHighLevels) {
                //Chart.DrawRectangle(counter++.ToString(), Level.Earilest.OpenTime, Level.Min.High, Level.Latest.OpenTime, Level.Max.High, Color.Green);
            }

            foreach (var Level in PreviousLowLevels) {
                //Chart.DrawRectangle(counter++.ToString(), Level.Earilest.OpenTime, Level.Min.Low, Level.Latest.OpenTime, Level.Max.Low, Color.Red);
            }

            CalculateLevelMA(BarsLookup);
        }

        private bool GetPivot(DataSeries series, int index, int barsAmount, bool findHigh) {
            if (index < barsAmount || index >= series.Count - barsAmount)
                return false;

            double currentValue = series[index];

            for (int i = 1; i <= barsAmount; i++) {
                if (
                    (series[index - i] - currentValue) > 0 == findHigh ||
                    (series[index + i] - currentValue) > 0 == findHigh
                ) return false;
            }

            return true;
        }

        private void DrawLine(string id, Bar bar, Color color) {
            double Price = id.Contains("Resistance") ? bar.High : bar.Low;
            Chart.DrawTrendLine(id, bar.OpenTime, Price, bar.OpenTime.AddMinutes(60), Price, color, 5);
        }
    }
}



/*
                for (int j = 0; j < PreviousHighLevels.Count; j++) {
                    Level level = PreviousHighLevels[j];
                    
                    if ( 
                         (High >= level.Min.High && High <= level.Max.High) || // if its in the level
                         (
                            (High >= (level.Min.High - LevelThreshold)) &&  // if its outside the level but
                            (High <= (level.Max.High + LevelThreshold))     // in good range then we'll add it 
                          )    
                       )  {
                        Print("Added");
                       level.Add(PreviousHighs[i]);
                       }
                    
                }
                        /*
        private double _alpha;
        protected override void Initialize() => this._alpha = 2.0 / (double)checked(this.Periods + 1);

        public override void Calculate(int index) {
            int index1 = checked(index + this.Shift);
            double d = this.Result[checked(index1 - 1)];
            if (double.IsNaN(d))
                this.Result[index1] = this.Source[index];
            else
                this.Result[index1] = this.Source[index] * this._alpha + d * (1.0 - this._alpha);
        }

        ChartIndicator StandardIndicator.AddToChart() => this.AddToChart();
        }
        */
                