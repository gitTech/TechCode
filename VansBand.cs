using cAlgo.API;
using CarbonFx.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
// namespace is like a folder containing more files. the first word "CarbonFX. " is the folder then cAlgo. then Robots
//Namespaces implicitly have public access
//namespace can be used more than once in the project code
// access data by namespace.class.myFunction();
//NET Framework uses namespaces to organize its many classes
//They organize large code projects.
//They are delimited by using the . operator.
//The global namespace is the "root" namespace: global::System will always refer to the .NET Framework namespace System.
// class == BaseBot ( does order management ) 
namespace CarbonFx.cAlgo.Robots
{
    //-- can use class,interface,struct,enum,delegate within the namespace
    public class VansBand : EnchancedRobot
    {

        #region Settings


        private int MaxLot
        {
            get { return _fxSettings.Get<int>("MaxLot"); }
        }

        private int MinTakeProfit
        {
            get { return _fxSettings.Get<int>("MinTakeProfit"); }
        }

        private double TakeProfitMulti
        {
            get { return _fxSettings.Get<double>("TakeProfitMulti"); }
        }

        private int MinStopLoss
        {
            get { return _fxSettings.Get<int>("MinStopLoss"); }
        }

        private double StopLossMulti
        {
            get { return _fxSettings.Get<double>("StopLossMulti"); }
        }

        private int MinRenkoSize
        {
            get { return _fxSettings.Get<int>("MinRenkoSize"); }
        }

        private double RenkoSizeMulti
        {
            get { return _fxSettings.Get<double>("RenkoSizeMulti"); }
        }

        private int LotSize
        {
            get { return _fxSettings.Get<int>("LotSize"); }
        }


        private int MaxBuyOrders
        {
            get { return _fxSettings.Get<int>("MaxBuyOrders"); }
        }


        private int MaxSellOrders
        {
            get { return _fxSettings.Get<int>("MaxSellOrders"); }
        }


        private int MinMaxLookback
        {
            get { return _fxSettings.Get<int>("MinMaxLookback"); }
        }


        private int PipsFromMax
        {
            get { return _fxSettings.Get<int>("PipsFromMax"); }
        }


        private int PipsFromMin
        {
            get { return _fxSettings.Get<int>("PipsFromMin"); }
        }


        private int OrderTimeSpacing
        {
            get { return _fxSettings.Get<int>("OrderTimeSpacing"); }
        }


        private int ReCalcTakeProfit
        {
            get { return _fxSettings.Get<int>("ReCalcTakeProfit"); }
        }

        private int ReCalcRenkos
        {
            get { return _fxSettings.Get<int>("ReCalcRenkos"); }
        }

        private double[] SpacingMultiplierLevel
        {
            get { return _fxSettings.Get<string>("OrderSpacingMultipliers").Split(new char[] {','}).Select(c => double.Parse(c)).ToArray(); }
        }

        private double LotMultiplier
        {
            get { return _fxSettings.Get<double>("LotMultiplier"); }
        }

        /// <summary>
        /// When large losses are experienced, they are broken down into sized chunks
        /// </summary>
        private double BatchLossAmount
        {
            get { return _fxSettings.Get<double>("BatchLossAmount") * -1; }
        }

        private string LossesSettingKey
        {
            get { return "martingrid:" + this.RobotName + ":losses"; }
        }

        double _basePerPip = 0;
        private double BasePerPip
        {
            get
            {
                if (_basePerPip != 0)
                {
                    return _basePerPip;
                }
                return _basePerPip = _fxSettings.GetShared<double>(string.Format("{0}:baseperpip", this.Symbol.Code));
            }
            set { _fxSettings.SetShared(string.Format("{0}:baseperpip", this.Symbol.Code), value.ToString("0.###")); }
        }

        long _baseVolume = 0;
        private long BaseVolume
        {
            get
            {
                if (_baseVolume != 0)
                {
                    return _baseVolume;
                }
                _baseVolume = _fxSettings.GetShared<long>(string.Format("{0}:basevolume", this.Symbol.Code));
                if (_baseVolume == 0)
                {
                    return LotSize;
                }
                else
                {
                    return _baseVolume;
                }
            }
            set { _fxSettings.SetShared(string.Format("{0}:basevolume", this.Symbol.Code), value.ToString()); }
        }

        #endregion

        int CurTakeProfit, CurRenkoSize;
        DateTime lastReCalc, lastPlaceOrderCall;

        RemoteSettings _fxSettings;

        BollingerBands channel;
        //WellesWilderSmoothing ma;

        public int ChannelPeriod = 20;
        // public int MAPeriod = 200;

        protected override void OnStart()
        {
            base.OnStart();
            //-- Getting settings from the Server for init function
            _fxSettings = RemoteSettings.GetSettings(this.RobotName, this.SettingKeyName, base.Label);

            this.Print("Start --- {0} {1} {2} {3} {4}", this.RobotName, this.SettingKeyName, base.Label, this.LotSize, this.MaxLot);

            this.CurRenkoSize = this.MinRenkoSize;
            this.CurTakeProfit = this.MinTakeProfit;
            lastPlaceOrderCall = lastReCalc = this.Server.Time.Subtract(new TimeSpan(0, 1, 0));

            channel = Indicators.BollingerBands(this.MarketSeries.Close, ChannelPeriod, 2.0, MovingAverageType.Simple);


        }

        protected override void OnBar()
        {
            int last = MarketSeries.Close.Count - 1;
            // -- Main logic 
            if (this.Orders.Count() == 0)
            {
                if (MarketSeries.Close[last - 2] < channel.Top[last - 2] && MarketSeries.Close[last - 1] > channel.Top[last - 1])
                {
                    //-- check candle bar patterns before opening order

                    this.PlaceOrder(TradeType.Sell, GetLotSize(), MinTakeProfit, MinStopLoss);
                }

                if (MarketSeries.Close[last - 2] > channel.Bottom[last - 2] && MarketSeries.Close[last - 1] < channel.Bottom[last - 1])
                {
                    //-- check candle bar patterns before opening order

                    this.PlaceOrder(TradeType.Buy, GetLotSize(), MinTakeProfit, MinStopLoss);
                }
            }
        }

        private int GetLotSize()
        {
            // -- if last trade closed at a $ or Pip Loss - GetLotSizeMultiplier
            // -- Martingale Function
            if (lossLots > 0)
            {
                int Loss = 1;
                if (lossCount == 1)
                    Loss = 2;
                // 2x -60


                if (lossCount == 2)
                    Loss = 3;
                // 2x -60
                if (lossCount == 3)
                    Loss = 6;
                //3x - 120 + -60 = -180
                if (lossCount == 4)
                    Loss = 12;
                //6x - 120 + -60 + -180 = -360
                if (lossCount == 5)
                    Loss = 24;
                if (lossCount == 6)
                    Loss = 48;
                if (lossCount == 7)
                    Loss = 96;
                if (lossCount == 8)
                    Loss = 192;
                if (lossCount == 9)
                    Loss = 384;
                if (lossCount == 10)
                    Loss = 768;
                // 38.4 lot
                if (lossCount == 11)
                    Loss = 1536;
                // 76.8 lot
                if (lossCount == 12)
                    Loss = 3072;
                //
                if (lossCount == 13)
                    Loss = 6144;
                //
                if (lossCount == 14)
                    Loss = 12288;
                // 
                return LotSize * Loss;
            }
            else
                return LotSize;
        }

        int lossLots = 0;
        int lossCount = 0;
        double lossAmount = 0;
        protected override void OnPositionClosed(Position position, PositionClosedReason reason)
        {
            if (reason == PositionClosedReason.StopLossHit)
            {
                lossAmount += position.GrossProfit;
                lossCount++;
                lossLots += (int)position.Volume;
            }
            if (reason == PositionClosedReason.TakeProfitHit)
            {
                lossAmount = 0;
                lossCount = 0;
                lossLots = 0;
            }
        }

        private void PlaceOrder(TradeType direction, int lot)
        {
            PlaceOrder(direction, lot, string.Empty);
        }

        private void PlaceOrder(TradeType direction, int lot, string note)
        {
            if (lastPlaceOrderCall.AddMinutes(1) < this.Server.Time)
            {
                lastPlaceOrderCall = this.Server.Time;
                this.SendOrder(direction, lot, note);
            }
        }

        private void PlaceOrder(TradeType direction, int lot, int tp, int sl)
        {
            if (lastPlaceOrderCall.AddMinutes(1) < this.Server.Time)
            {
                this.SendOrder(direction, lot, sl, tp, null);
            }
        }

        private int LotMartingaleCalc(double dd)
        {
            try
            {
                if (dd < 0)
                {
                    var tempTP = CurTakeProfit;
                    // +(this.Symbol.Spread * this.Symbol.PipSize);
                    // What's the price per pip we're looking for to recoup that
                    var targetPerPip = Math.Abs(dd) / tempTP;


                    // Find lot size that meets that condition    $0.15/10,000 = targetPerPip/ X
                    var lot = RoundLot((targetPerPip * BaseVolume) / BasePerPip);

                    double commission = GetCommissionAmount(lot);

                    // recalc with commission included
                    targetPerPip = (Math.Abs(dd) + commission) / tempTP;

                    lot = RoundLot((targetPerPip * BaseVolume) / BasePerPip);

                    return Math.Max(LotSize, RoundLot(lot * LotMultiplier));

                }
                else
                {
                    return LotSize;
                }
            } catch (Exception ex)
            {
                this.Print("Error calculating lot size");
                throw ex;
            }
        }

        protected override void OnError(Error error)
        {
            this.Print("Error: {0}", error.Code);
            base.OnError(error);
        }
    }
}
