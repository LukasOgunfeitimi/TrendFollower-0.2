using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
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

        if (UptrendCounter > DowntrendCounter) return "BUY";
        if (DowntrendCounter > UptrendCounter) return "SELL";

        return "NEUTRAL";

        return " [" + UptrendCounter +
               " - " + DowntrendCounter +
               " - " + NeutralCounter + "]";
    }
}
namespace cAlgo.Robots
{
    public class SetupAnalyser {

        public static string CalculateSLTP(SupportResistance SR, double PipSize) {
            List<Bar> PreviousHighs = SR.PreviousHighs;
            List<Bar> PreviousLows = SR.PreviousLows;

            SLTP sltp = new();

            SRMetrics highs = new();
            SRMetrics lows = new();

            int Lookup = Math.Min(PreviousHighs.Count - 2, PreviousLows.Count - 2);

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
        }
}
}
