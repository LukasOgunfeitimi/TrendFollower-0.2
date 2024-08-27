using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using cAlgo.API;
public struct SLTP {
    public double SL;
    public double TP;
    public string comment;
}
public struct SRMetrics {
    public int UptrendCounter = 0;
    public int DowntrendCounter = 0;
    public int NeutralCounter = 0;
    public SRMetrics() { }
    public override string ToString() {
        int max = Math.Max(Math.Max(UptrendCounter, DowntrendCounter), NeutralCounter);

        //return max == UptrendCounter ? "BUY" : (max == DowntrendCounter ? "SELL" : "NEUTRAL");


        if (UptrendCounter > DowntrendCounter) return "BUY";
        if (UptrendCounter < DowntrendCounter) return "SELL";

        return "NEUTRAL";

        return " [" + UptrendCounter +
               " - " + DowntrendCounter +
               " - " + NeutralCounter + "]";
    }
}
namespace cAlgo.Robots {
    public class SetupAnalyser {
        private readonly TrendFollower bot;

        private Bars Bars => this.bot.Bars;
        private double PipSize => this.bot.Symbol.PipSize;
        private void Log(object msg) { if (this.bot.Debug) this.bot.Print(msg?.ToString()); }

        public SetupAnalyser(TrendFollower bot) {
            this.bot = bot;
        }
        private int GetMATrend(double MA, int index, SupportResistance SR) {
            if (SR.LevelsMAUptrend[index] == MA) return 0;
            if (SR.LevelsMADowntrend[index] == MA) return 1;
            if (SR.LevelsMANeutral[index] == MA) return 2;
            new Exception("MA not in any trend");
            return -1;
        }
        public SLTP AnalyseTrade(TradeType direction, SupportResistance SR, double PipSize, double EntryPrice) {
            SLTP sltp = new();
            SRMetrics MAs = new();

            /*
             * Identify Trend
             */
            int latestIndex = Bars.Count - 1;
            int lookback = Math.Min(latestIndex - 1, 500);

            for (int i = latestIndex; i > (latestIndex - lookback); i--) {
                double diff = Math.Round(SR.LevelsMA[i] - SR.LevelsMA[i - 1], 2);

                int Trend = GetMATrend(SR.LevelsMA[i], i, SR);

                if (Trend == 0) MAs.UptrendCounter++;
                else if (Trend == 1) MAs.DowntrendCounter++;
                else if (Trend == 2) MAs.NeutralCounter++;
            }

            sltp.comment = MAs.ToString();


            /*
             * Calculate TP
             */
            List<SupportResistance.Level> PreviousHighs = SR.PreviousHighLevels;
            List<SupportResistance.Level> PreviousLows = SR.PreviousLowLevels;

            double HighestLevel = SR.PreviousHighLevels.Any()
            ? SR.PreviousHighLevels.Max(level => level.Max.High)
            : double.NaN;

            double LowestLevel = SR.PreviousLowLevels.Any()
            ? PreviousLows.Min(l => l.Min.Low)
            : double.NaN;

            DateTime LookbackTime = bot.Server.Time.AddDays(-5);

            double MaxTPPipSize = 500 / PipSize;
            sltp.TP = 1000;
            sltp.comment += " needs normal tp";
            if (direction == TradeType.Buy) {
                foreach (var High in SR.PreviousHighs) {
                    //if (High.OpenTime < LookbackTime) break;
                    //Log(High.High + " " + EntryPrice);
                    // In range
                    double DifferenceInPips = (High.High - EntryPrice) / PipSize;
                    if (DifferenceInPips > 1000 && DifferenceInPips < MaxTPPipSize) {
                        sltp.comment += " TP: " + High.High;
                        sltp.TP = High.High;
                        break;
                    }
                }
            } else {
                foreach (var Low in SR.PreviousLows) {
                    double DifferenceInPips = (EntryPrice - Low.Low) / PipSize;
                    if (DifferenceInPips > 100 && DifferenceInPips < MaxTPPipSize) {
                        sltp.comment += " TP:" + Low.Low;
                        sltp.TP = Low.Low;
                        break;
                    }
                }
            }

            return sltp;


        }
    }
}



/*
 *             int Lookup = Math.Min(PreviousHighs.Count - 2, PreviousLows.Count - 2);

            for (int i = 0; i < Math.Min(Lookup, 10); i++) {
                double diffHighs = (PreviousHighs[i].High - PreviousHighs[i + 1].High) / PipSize;
                if (diffHighs >= 1000) highs.UptrendCounter++;
                else if (diffHighs <= -1000) highs.DowntrendCounter++;
                else highs.NeutralCounter++;

                double diffLows = (PreviousLows[i].Low - PreviousLows[i + 1].Low) / PipSize;
                if (diffLows >= 1000) lows.UptrendCounter++;
                else if (diffLows <= -1000) lows.DowntrendCounter++;
                else lows.NeutralCounter++;
            }
        
            
            //Log(highs.ToString());
            //Log(lows.ToString());
            sltp.comment = highs.ToString() + " " + lows.ToString();

            return highs.ToString() + " " + lows.ToString();
            // score based system?

            // once trend has been idenitified
            // trend = direction full risk
            // trend = neutral  half risk
            // trend != direction low risk

            // if there has been an impulse candle previously (max5 bars ago) = full risk

            // Calculating TP
            // if price is past (5?) relative range use an aggresive position (eg trail stops)
            // if price within relative range set normal tp and a final tp at the range

            // Calculating SL   
            // normal SL?

            /*
            List<SupportResistance.Level> PreviousHighs = SR.PreviousHighLevels;
            List<SupportResistance.Level> PreviousLows = SR.PreviousLowLevels;

            double HighestLevel = SR.PreviousHighLevels.Any()
            ? SR.PreviousHighLevels.Max(level => level.Max.High)
            : double.NaN;
            double LowestLevel = SR.PreviousLowLevels.Any()
            ? PreviousLows.Min(l => l.Min.Low)
            : double.NaN;


            if (direction == TradeType.Buy) {
                if (HighestLevel != double.NaN) {
                    if (Ask > HighestLevel) Comment += " outside of range";
                    else Comment += " in the range";
                }
            } else {
                if (LowestLevel != double.NaN) {
                    if (Bid < LowestLevel) Comment += "outside of range";
                    else Comment += " in the range";
                }
            }
            */