using System.Collections.Generic;

namespace LTESingleLinkAJM.Models
{
    public static class TestPlan
    {
        public static IReadOnlyList<TestItem> BuildDefault()
        {
            var raw = new (string Band, string Ch, int Earfcn)[]
            {
                ("B1","Low",18050),   ("B1","Mid",18300),   ("B1","High",18550),
                ("B2","Low",18650),   ("B2","Mid",18900),   ("B2","High",19150),
                ("B3","Low",19250),   ("B3","Mid",19575),   ("B3","High",19900),
                ("B4","Low",20000),   ("B4","Mid",20175),   ("B4","High",20350),
                ("B5","Low",20450),   ("B5","Mid",20525),   ("B5","High",20600),
                ("B7","Low",20800),   ("B7","Mid",21100),   ("B7","High",21400),
                ("B8","Low",21500),   ("B8","Mid",21625),   ("B8","High",21750),
                ("B12","Low",23060),  ("B12","Mid",23095),  ("B12","High",23130),
                ("B13","Mid",23230),
                ("B17","Low",23780),  ("B17","Mid",23790),  ("B17","High",23800),
                ("B18","Low",23900),  ("B18","Mid",23925),  ("B18","High",23950),
                ("B19","Low",24050),  ("B19","Mid",24075),  ("B19","High",24100),
                ("B20","Low",24200),  ("B20","Mid",24300),  ("B20","High",24400),
                ("B25","Low",26090),  ("B25","Mid",26365),  ("B25","High",26640),
                ("B26","Low",26740),  ("B26","Mid",26865),  ("B26","High",26990),
                ("B28","Low",27260),  ("B28","Mid",27435),  ("B28","High",27610),
                ("B66","Low",132022), ("B66","Mid",132322), ("B66","High",132622),
                ("B34","Low",36250),  ("B34","Mid",36275),  ("B34","High",36300),
                ("B38","Low",37800),  ("B38","Mid",38000),  ("B38","High",38200),
                ("B39","Low",38300),  ("B39","Mid",38450),  ("B39","High",38600),
                ("B40","Low",38700),  ("B40","Mid",39150),  ("B40","High",39600),
                ("B41","Low",39700),  ("B41","Mid",40620),  ("B41","High",41540),
            };

            var list = new List<TestItem>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                list.Add(new TestItem
                {
                    Index = i + 1,
                    Band = raw[i].Band,
                    ChannelType = raw[i].Ch,
                    Earfcn = raw[i].Earfcn,
                    TpcSetup = "MAX POWER",
                    TargetPower = 23,
                    UlRb = 50,
                    DlRb = 50,
                    Status = TestStatus.Pending
                });
            }
            return list;
        }
    }
}
